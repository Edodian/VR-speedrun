// DesktopXRGrabInteractor.cs
// Author: companion to DesktopXROriginMouseLook / DesktopMovementScript
//
// Purpose
//   A drop-in XR Interaction Toolkit interactor that lets a desktop player
//   (mouse + keyboard) grab and carry XRGrabInteractables exactly the way a
//   VR controller does, while VR controllers remain the sole owners whenever
//   the HMD is rendering.
//
//   Because this is a real XRBaseInteractor, every XRGrabInteractable hook
//   you've already wired up (selectEntered / selectExited / Activate /
//   throwOnDetach / attach easing / movement types / etc.) keeps working
//   unchanged on desktop.
//
// Default bindings (auto-created if you leave the InputActionProperty fields empty)
//   Grab               : hold <Keyboard>/e
//   Push / Pull        : <Mouse>/scroll  (Y axis)
//   Rotate modifier    : hold <Keyboard>/r
//   Rotate delta       : <Mouse>/delta  (only while modifier is held)
//
// Setup
//   1. Add this component anywhere under your XROrigin (a child of the XROrigin
//      GameObject is fine, but it can also live on the XROrigin itself).
//   2. References auto-assign in Reset/Awake. Override them in the Inspector if
//      your rig has a non-standard layout.
//   3. Leave "Hold Anchor" empty to have one created automatically. The created
//      anchor is repositioned every frame in front of the camera and is what
//      XRGrabInteractable uses as its attach point.
//   4. Make sure your XRGrabInteractables can be selected by this interactor.
//      If you use Interaction Layers, add this interactor's layer to their list.
//   5. Leave "Allow Desktop Grab While XR Is Active" OFF unless you specifically
//      want both desktop and VR grabbing live at the same time.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class DesktopXRGrabInteractor : XRBaseInteractor
{
    [Header("XR / Rig References")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Camera targetCamera;
    [Tooltip("Where held objects are anchored. Auto-created as a child of this GameObject if left empty.")]
    [SerializeField] private Transform holdAnchor;

    [Header("Pick-Ray Detection")]
    [SerializeField, Min(0.1f)] private float maxGrabDistance = 4f;
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
    [Tooltip("When the desktop cursor is locked (FPS look-mode), shoot the pick ray from screen center instead of the cursor.")]
    [SerializeField] private bool useScreenCenterWhenCursorLocked = true;

    [Header("Hold Behaviour")]
    [SerializeField, Min(0.05f)] private float defaultHoldDistance = 0.7f;
    [SerializeField, Min(0.05f)] private float minHoldDistance = 0.25f;
    [SerializeField, Min(0.1f)] private float maxHoldDistance = 3.0f;
    [SerializeField, Min(0f)] private float scrollSensitivity = 0.15f;
    [SerializeField, Min(0f)] private float rotationSensitivity = 0.4f;

    [Header("Input Actions (New Input System)")]
    [Tooltip("Hold to grab, release to drop.")]
    [SerializeField] private InputActionProperty grabAction;
    [Tooltip("Vector2; Y axis pushes/pulls held object. Default: mouse scroll.")]
    [SerializeField] private InputActionProperty distanceAction;
    [Tooltip("Hold to switch mouse from look to rotating the held object.")]
    [SerializeField] private InputActionProperty rotateModifierAction;
    [Tooltip("Vector2 delta consumed while rotateModifier is held. Default: mouse delta.")]
    [SerializeField] private InputActionProperty rotateAction;

    [Header("XR Safety")]
    [Tooltip("Keep OFF so HMD-driven controllers remain the only grab owner while XR is rendering.")]
    [SerializeField] private bool allowDesktopGrabWhileXRIsActive = false;

    // ---- Private state ----
    private bool ownsGrabAction;
    private bool ownsDistanceAction;
    private bool ownsRotateModifierAction;
    private bool ownsRotateAction;

    private float currentHoldDistance;
    private IXRSelectInteractable hoverCandidate;
    private bool autoCreatedHoldAnchor;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    protected override void Reset()
    {
        base.Reset();
        AutoAssignReferences();
        CreateDefaultActionsIfMissing();
    }

    protected override void Awake()
    {
        base.Awake();
        AutoAssignReferences();
        EnsureHoldAnchor();
        CreateDefaultActionsIfMissing();

        currentHoldDistance = Mathf.Clamp(defaultHoldDistance, minHoldDistance, maxHoldDistance);
        attachTransform = holdAnchor;
        keepSelectedTargetValid = true;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EnableAction(grabAction);
        EnableAction(distanceAction);
        EnableAction(rotateModifierAction);
        EnableAction(rotateAction);
    }

    protected override void OnDisable()
    {
        // Release any held object cleanly (fires selectExited events) before the
        // base class deregisters us from the XRInteractionManager.
        ForceReleaseSelection();

        DisableAction(grabAction);
        DisableAction(distanceAction);
        DisableAction(rotateModifierAction);
        DisableAction(rotateAction);

        base.OnDisable();
    }

    private void OnDestroy()
    {
        DisposeOwnedActions();
    }

    // -------------------------------------------------------------------------
    // XRBaseInteractor overrides
    // -------------------------------------------------------------------------

    public override void GetValidTargets(List<IXRInteractable> targets)
    {
        targets.Clear();

        if (hasSelection)
        {
            for (int i = 0; i < interactablesSelected.Count; i++)
                targets.Add(interactablesSelected[i]);
            return;
        }

        if (hoverCandidate != null)
            targets.Add(hoverCandidate);
    }

    public override bool isSelectActive
    {
        get
        {
            if (!CanRunDesktopGrab())
                return false;

            bool pressed = ReadButton(grabAction);

            // Already carrying something? Stay active while the button is held.
            if (hasSelection) return pressed;

            // Otherwise only become active when we actually have something under the cursor.
            return pressed && hoverCandidate != null;
        }
    }

    public override bool isHoverActive => CanRunDesktopGrab();

    public override void PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase updatePhase)
    {
        base.PreprocessInteractor(updatePhase);

        if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic)
            return;

        if (!CanRunDesktopGrab())
        {
            if (hasSelection)
                ForceReleaseSelection();
            hoverCandidate = null;
            return;
        }

        UpdateHoldDistanceFromScroll();
        PositionHoldAnchor();
        UpdateRotationInput();

        if (hasSelection)
            hoverCandidate = null;
        else
            UpdateHoverCandidate();
    }

    public override bool CanSelect(IXRSelectInteractable interactable)
    {
        if (!base.CanSelect(interactable)) return false;

        // Permit the current hover candidate or anything we are already selecting
        // (so XRI doesn't accidentally drop the held object during a hover refresh).
        if (hoverCandidate == interactable) return true;
        return IsSelecting(interactable);
    }

    // -------------------------------------------------------------------------
    // Per-frame logic
    // -------------------------------------------------------------------------

    private bool CanRunDesktopGrab()
    {
        if (targetCamera == null) return false;
        if (Mouse.current == null && Keyboard.current == null) return false;
        if (!allowDesktopGrabWhileXRIsActive && IsXRCurrentlyActive()) return false;
        return true;
    }

    private bool IsXRCurrentlyActive()
    {
        return targetCamera != null && targetCamera.stereoEnabled;
    }

    private void UpdateHoldDistanceFromScroll()
    {
        Vector2 scroll = ReadVector2(distanceAction);
        if (Mathf.Abs(scroll.y) < 0.001f) return;

        // Mouse scroll on the New Input System reports raw notch values (e.g. +/-120 on Windows),
        // so we scale aggressively. Tune via scrollSensitivity.
        float delta = scroll.y * scrollSensitivity * 0.01f;
        currentHoldDistance = Mathf.Clamp(currentHoldDistance + delta, minHoldDistance, maxHoldDistance);
    }

    private void PositionHoldAnchor()
    {
        if (holdAnchor == null || targetCamera == null) return;

        Transform camT = targetCamera.transform;
        holdAnchor.position = camT.position + camT.forward * currentHoldDistance;

        // While idle, snap rotation to the camera so newly grabbed objects start out facing the player.
        // While carrying, the user owns rotation via UpdateRotationInput().
        if (!hasSelection)
            holdAnchor.rotation = camT.rotation;
    }

    private void UpdateRotationInput()
    {
        if (holdAnchor == null || targetCamera == null) return;
        if (!hasSelection) return;
        if (!ReadButton(rotateModifierAction)) return;

        Vector2 delta = ReadVector2(rotateAction);
        if (delta.sqrMagnitude < 0.0001f) return;

        Transform camT = targetCamera.transform;
        // Camera-relative tumbling: feels like "spin the object relative to my view".
        holdAnchor.Rotate(camT.up,     delta.x * rotationSensitivity, Space.World);
        holdAnchor.Rotate(camT.right, -delta.y * rotationSensitivity, Space.World);
    }

    private void UpdateHoverCandidate()
    {
        hoverCandidate = null;
        if (targetCamera == null || interactionManager == null) return;

        Ray ray = BuildPickRay();
        if (!Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance, raycastMask, triggerInteraction))
            return;

        // Resolve through the XRInteractionManager so we pick up whatever
        // IXRSelectInteractable owns this collider (children, compound colliders, etc.).
        if (interactionManager.TryGetInteractableForCollider(hit.collider, out IXRInteractable interactable) &&
            interactable is IXRSelectInteractable selectable)
        {
            hoverCandidate = selectable;
        }
    }

    private Ray BuildPickRay()
    {
        bool useCenter = useScreenCenterWhenCursorLocked && Cursor.lockState == CursorLockMode.Locked;

        if (!useCenter && Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            return targetCamera.ScreenPointToRay(mousePos);
        }

        return targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
    }

    private void ForceReleaseSelection()
    {
        if (interactionManager == null || !hasSelection) return;

        var copy = new List<IXRSelectInteractable>(interactablesSelected);
        for (int i = 0; i < copy.Count; i++)
            interactionManager.SelectExit(this, copy[i]);
    }

    // -------------------------------------------------------------------------
    // Setup helpers
    // -------------------------------------------------------------------------

    private void AutoAssignReferences()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponentInParent<XROrigin>();

        if (targetCamera == null)
        {
            if (xrOrigin != null && xrOrigin.Camera != null)
                targetCamera = xrOrigin.Camera;
            else if (Camera.main != null)
                targetCamera = Camera.main;
            else
                targetCamera = GetComponentInChildren<Camera>(true);
        }
    }

    private void EnsureHoldAnchor()
    {
        if (holdAnchor != null) return;

        var go = new GameObject("DesktopHoldAnchor");
        go.transform.SetParent(transform, false);
        holdAnchor = go.transform;
        autoCreatedHoldAnchor = true;
    }

    private void CreateDefaultActionsIfMissing()
    {
        if (grabAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopGrab",
                type: InputActionType.Button,
                expectedControlType: "Button");
            action.AddBinding("<Keyboard>/e");
            grabAction = new InputActionProperty(action);
            ownsGrabAction = true;
        }

        if (distanceAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopHoldDistance",
                type: InputActionType.Value,
                expectedControlType: "Vector2");
            action.AddBinding("<Mouse>/scroll");
            distanceAction = new InputActionProperty(action);
            ownsDistanceAction = true;
        }

        if (rotateModifierAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopRotateModifier",
                type: InputActionType.Button,
                expectedControlType: "Button");
            action.AddBinding("<Keyboard>/r");
            rotateModifierAction = new InputActionProperty(action);
            ownsRotateModifierAction = true;
        }

        if (rotateAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopRotateDelta",
                type: InputActionType.Value,
                expectedControlType: "Vector2");
            action.AddBinding("<Mouse>/delta");
            rotateAction = new InputActionProperty(action);
            ownsRotateAction = true;
        }
    }

    private static void EnableAction(InputActionProperty property)
    {
        if (property.action != null && !property.action.enabled)
            property.action.Enable();
    }

    private static void DisableAction(InputActionProperty property)
    {
        if (property.action != null && property.action.enabled)
            property.action.Disable();
    }

    private void DisposeOwnedActions()
    {
        if (ownsGrabAction && grabAction.action != null)
            grabAction.action.Dispose();
        if (ownsDistanceAction && distanceAction.action != null)
            distanceAction.action.Dispose();
        if (ownsRotateModifierAction && rotateModifierAction.action != null)
            rotateModifierAction.action.Dispose();
        if (ownsRotateAction && rotateAction.action != null)
            rotateAction.action.Dispose();
    }

    private static Vector2 ReadVector2(InputActionProperty property)
    {
        return property.action != null ? property.action.ReadValue<Vector2>() : Vector2.zero;
    }

    private static bool ReadButton(InputActionProperty property)
    {
        return property.action != null && property.action.IsPressed();
    }
}
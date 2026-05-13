using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

[DisallowMultipleComponent]
public sealed class DesktopXROriginMouseLook : MonoBehaviour
{
    [Header("XR / Rig References")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform yawRoot;
    [SerializeField] private Transform pitchTarget;
    [SerializeField] private Camera targetCamera;

    [Header("Input Actions (New Input System)")]
    [SerializeField] private InputActionProperty lookAction;
    [SerializeField] private InputActionProperty lookHoldAction;

    [Header("Mouse Look")]
    [SerializeField, Min(0.001f)] private float sensitivity = 0.12f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private bool requireRightMouseButton = true;
    [SerializeField] private bool lockCursorWhileLooking = true;

    [Header("Pitch Limits")]
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    [Header("XR Safety")]
    [Tooltip("Keep this OFF if you do not want desktop mouse-look to fight active HMD tracking.")]
    [SerializeField] private bool allowDesktopLookWhileXRIsActive = false;

    private bool ownsLookAction;
    private bool ownsLookHoldAction;
    private bool cursorLockedByScript;
    private float currentPitch;

    private void Reset()
    {
        AutoAssignReferences();
        CreateDefaultActionsIfMissing();
    }

    private void Awake()
    {
        AutoAssignReferences();
        CreateDefaultActionsIfMissing();
        CacheInitialPitch();
    }

    private void OnEnable()
    {
        EnableAction(lookAction);
        EnableAction(lookHoldAction);
    }

    private void OnDisable()
    {
        DisableAction(lookAction);
        DisableAction(lookHoldAction);
        ReleaseCursor();
    }

    private void OnDestroy()
    {
        DisposeOwnedActions();
    }

    private void Update()
    {
        if (!CanRunDesktopLook())
        {
            ReleaseCursor();
            return;
        }

        bool isLookHeld = !requireRightMouseButton || ReadButton(lookHoldAction);

        if (!isLookHeld)
        {
            ReleaseCursor();
            return;
        }

        if (lockCursorWhileLooking)
            LockCursor();

        Vector2 delta = ReadVector2(lookAction);
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        float yawDelta = delta.x * sensitivity;
        float pitchDelta = delta.y * sensitivity * (invertY ? 1f : -1f);

        if (yawRoot != null)
            yawRoot.Rotate(Vector3.up, yawDelta, Space.World);

        if (pitchTarget != null)
        {
            currentPitch = Mathf.Clamp(currentPitch + pitchDelta, minPitch, maxPitch);
            pitchTarget.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
        }
    }

    private bool CanRunDesktopLook()
    {
        if (Mouse.current == null)
            return false;

        // Safest default:
        // If XR stereo rendering is active, let HMD tracking own view direction.
        if (!allowDesktopLookWhileXRIsActive && IsXRCurrentlyActive())
            return false;

        return true;
    }

    private bool IsXRCurrentlyActive()
    {
        return targetCamera != null && targetCamera.stereoEnabled;
    }

    private void AutoAssignReferences()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();

        if (targetCamera == null)
            targetCamera = GetComponentInChildren<Camera>(true);

        if (yawRoot == null)
            yawRoot = transform;

        // For XR Origin rigs, this should usually be the Camera Offset / Floor Offset parent.
        if (pitchTarget == null && targetCamera != null)
            pitchTarget = targetCamera.transform.parent != null
                ? targetCamera.transform.parent
                : targetCamera.transform;
    }

    private void CacheInitialPitch()
    {
        if (pitchTarget == null)
        {
            currentPitch = 0f;
            return;
        }

        currentPitch = NormalizeAngle(pitchTarget.localEulerAngles.x);
        currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
    }

    private void CreateDefaultActionsIfMissing()
    {
        if (lookAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopLook",
                type: InputActionType.Value,
                expectedControlType: "Vector2");

            action.AddBinding("<Mouse>/delta");

            lookAction = new InputActionProperty(action);
            ownsLookAction = true;
        }

        if (lookHoldAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopLookHold",
                type: InputActionType.Button,
                expectedControlType: "Button");

            action.AddBinding("<Mouse>/rightButton");

            lookHoldAction = new InputActionProperty(action);
            ownsLookHoldAction = true;
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
        if (ownsLookAction && lookAction.action != null)
            lookAction.action.Dispose();

        if (ownsLookHoldAction && lookHoldAction.action != null)
            lookHoldAction.action.Dispose();
    }

    private static Vector2 ReadVector2(InputActionProperty property)
    {
        return property.action != null ? property.action.ReadValue<Vector2>() : Vector2.zero;
    }

    private static bool ReadButton(InputActionProperty property)
    {
        return property.action != null && property.action.IsPressed();
    }

    private void LockCursor()
    {
        if (cursorLockedByScript)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursorLockedByScript = true;
    }

    private void ReleaseCursor()
    {
        if (!cursorLockedByScript)
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorLockedByScript = false;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}
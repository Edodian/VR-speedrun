using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

[DisallowMultipleComponent]
public sealed class DesktopMovementScript : MonoBehaviour
{
    [Header("XR / Rig References")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform moveForwardSource;

    [Header("Input Actions (New Input System)")]
    [SerializeField] private InputActionProperty moveAction;
    [SerializeField] private InputActionProperty sprintAction;
    [SerializeField] private InputActionProperty jumpAction;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 2.5f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.75f;
    [SerializeField, Min(0f)] private float acceleration = 18f;
    [SerializeField, Min(0f)] private float deceleration = 24f;

    [Header("Gravity / Jump")]
    [SerializeField] private bool useGravity = true;
    [SerializeField] private bool allowJump = true;
    [SerializeField] private float gravity = -19.62f;
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float groundedStickForce = -2f;

    [Header("Character Controller Sync")]
    [SerializeField] private bool syncControllerToCameraHeight = true;
    [SerializeField] private float minControllerHeight = 1.0f;
    [SerializeField] private float maxControllerHeight = 2.2f;

    [Header("XR Safety")]
    [Tooltip("Keep this OFF if VR locomotion should remain the sole movement owner while the HMD is active.")]
    [SerializeField] private bool allowDesktopMoveWhileXRIsActive = false;

    private bool ownsMoveAction;
    private bool ownsSprintAction;
    private bool ownsJumpAction;

    private Vector3 planarVelocity;
    private float verticalVelocity;

    private void Reset()
    {
        AutoAssignReferences();
        CreateDefaultActionsIfMissing();
    }

    private void Awake()
    {
        AutoAssignReferences();
        CreateDefaultActionsIfMissing();
    }

    private void OnEnable()
    {
        EnableAction(moveAction);
        EnableAction(sprintAction);
        EnableAction(jumpAction);
    }

    private void OnDisable()
    {
        DisableAction(moveAction);
        DisableAction(sprintAction);
        DisableAction(jumpAction);
        planarVelocity = Vector3.zero;
    }

    private void OnDestroy()
    {
        DisposeOwnedActions();
    }

    private void Update()
    {
        if (syncControllerToCameraHeight)
            SyncCharacterControllerToCamera();

        if (!CanRunDesktopMove())
        {
            DampenVelocityWhenInactive();
            return;
        }

        Vector2 input = ReadVector2(moveAction);
        float targetSpeed = walkSpeed;

        if (ReadButton(sprintAction))
            targetSpeed *= sprintMultiplier;

        Vector3 desiredPlanarVelocity = GetWorldMoveDirection(input) * targetSpeed;

        float accel = input.sqrMagnitude > 0.0001f ? acceleration : deceleration;
        planarVelocity = Vector3.MoveTowards(planarVelocity, desiredPlanarVelocity, accel * Time.deltaTime);

        HandleVerticalMotion();

        Vector3 motion = planarVelocity + Vector3.up * verticalVelocity;

        if (characterController != null && characterController.enabled)
        {
            characterController.Move(motion * Time.deltaTime);
        }
        else
        {
            transform.position += motion * Time.deltaTime;
        }
    }

    private bool CanRunDesktopMove()
    {
        if (Keyboard.current == null)
            return false;

        if (!allowDesktopMoveWhileXRIsActive && IsXRCurrentlyActive())
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

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (targetCamera == null)
            targetCamera = GetComponentInChildren<Camera>(true);

        if (moveForwardSource == null && targetCamera != null)
            moveForwardSource = targetCamera.transform;
    }

    private void HandleVerticalMotion()
    {
        bool grounded = characterController != null && characterController.enabled && characterController.isGrounded;

        if (grounded && verticalVelocity < 0f)
            verticalVelocity = groundedStickForce;

        if (allowJump && grounded && WasPressedThisFrame(jumpAction))
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        if (useGravity)
            verticalVelocity += gravity * Time.deltaTime;
        else if (!grounded)
            verticalVelocity = 0f;
    }

    private Vector3 GetWorldMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        Transform basis = moveForwardSource != null ? moveForwardSource : transform;

        Vector3 forward = Vector3.ProjectOnPlane(basis.forward, Vector3.up);
        Vector3 right = Vector3.ProjectOnPlane(basis.right, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(Vector3.up, forward);

        forward.Normalize();
        right.Normalize();

        return (forward * input.y + right * input.x);
    }

    private void SyncCharacterControllerToCamera()
    {
        if (characterController == null || targetCamera == null)
            return;

        Vector3 localCameraPos = transform.InverseTransformPoint(targetCamera.transform.position);

        float height = Mathf.Clamp(localCameraPos.y, minControllerHeight, maxControllerHeight);
        characterController.height = height;

        Vector3 center = characterController.center;
        center.x = localCameraPos.x;
        center.y = (height * 0.5f) + (characterController.skinWidth * 0.5f);
        center.z = localCameraPos.z;
        characterController.center = center;
    }

    private void DampenVelocityWhenInactive()
    {
        planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, deceleration * Time.deltaTime);

        if (characterController != null && characterController.enabled && characterController.isGrounded)
            verticalVelocity = groundedStickForce;
    }

    private void CreateDefaultActionsIfMissing()
    {
        if (moveAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopMove",
                type: InputActionType.Value,
                expectedControlType: "Vector2");

            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            moveAction = new InputActionProperty(action);
            ownsMoveAction = true;
        }

        if (sprintAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopSprint",
                type: InputActionType.Button,
                expectedControlType: "Button");

            action.AddBinding("<Keyboard>/leftShift");
            action.AddBinding("<Keyboard>/rightShift");

            sprintAction = new InputActionProperty(action);
            ownsSprintAction = true;
        }

        if (jumpAction.action == null)
        {
            var action = new InputAction(
                name: "DesktopJump",
                type: InputActionType.Button,
                expectedControlType: "Button");

            action.AddBinding("<Keyboard>/space");

            jumpAction = new InputActionProperty(action);
            ownsJumpAction = true;
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
        if (ownsMoveAction && moveAction.action != null)
            moveAction.action.Dispose();

        if (ownsSprintAction && sprintAction.action != null)
            sprintAction.action.Dispose();

        if (ownsJumpAction && jumpAction.action != null)
            jumpAction.action.Dispose();
    }

    private static Vector2 ReadVector2(InputActionProperty property)
    {
        return property.action != null ? property.action.ReadValue<Vector2>() : Vector2.zero;
    }

    private static bool ReadButton(InputActionProperty property)
    {
        return property.action != null && property.action.IsPressed();
    }

    private static bool WasPressedThisFrame(InputActionProperty property)
    {
        return property.action != null && property.action.WasPressedThisFrame();
    }
}
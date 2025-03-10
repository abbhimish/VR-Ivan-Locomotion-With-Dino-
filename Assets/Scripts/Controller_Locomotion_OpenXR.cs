using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

// VR Locomotion controller that handles both translation and rotation in virtual reality
// Translation is controlled via thumbstick input with configurable forward direction
// Rotation can be handled through physical head movement and/or virtual rotation via thumbstick

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager))]
public class Controller_Locomotion_OpenXR : MonoBehaviour
{
    #region Public Fields
    [Header("Input Settings")]
    [Tooltip("Input Action Reference for movement/translation via thumbstick")]
    public InputActionReference moveAction;
    [Tooltip("Input Action Reference for rotation via thumbstick")]
    public InputActionReference rotateAction;
    [Tooltip("Reference to the VR controller GameObject")]
    public GameObject controller;

    [Tooltip("Reference to the body transform for body-relative movement direction")]
    public Transform bodyTransform;

    [Header("Movement Settings")]
    [Tooltip("Maximum movement speed in meters per second")]
    [SerializeField] private float maxSpeed = 1.5f;
    [Tooltip("Power value for exponential movement acceleration (higher values create more aggressive acceleration)")]
    [Range(1f, 2f)]
    [SerializeField] float exponentialTransferFunctionPower = 1.53f;
    [Tooltip("Multiplier for movement speed sensitivity")]
    [Range(1f, 5f)]
    public float speedSensitivity = 1f;

    public enum ForwardDirectionInputDevice { Controller, Camera, Body };
    public enum ForwardDirectionMode { Continuous, Initial };
    public enum VirtualRotationMethod
    {
        QuickTurns,
        SnapTurns,
        SmoothTurns,
        Scrolling,    // Continuous rotation based on head rotation threshold
        RotationGains // Amplified rotation based on head movement
    };

    public enum NavigationMode
    {
        TwoDimensional,  // Current behavior - movement in XZ plane only
        ThreeDimensional // New behavior - movement in XYZ space
    }

    [Header("Control Settings")]
    [Tooltip("Determines whether movement occurs in 2D (XZ plane) or 3D (XYZ space)")]
    public NavigationMode navigationMode = NavigationMode.TwoDimensional;

    [Tooltip("Determines which device's forward direction is used for movement")]
    public ForwardDirectionInputDevice directionDevice = ForwardDirectionInputDevice.Controller;

    [Tooltip("Controls whether forward direction updates continuously or remains fixed when movement starts")]
    public ForwardDirectionMode directionMode = ForwardDirectionMode.Continuous;

    [Header("Rotation Settings")]
    [Tooltip("Enables virtual rotation control in addition to physical head rotation")]
    public bool virtualRotationEnabled = true;

    [Tooltip("Determines the method used for virtual rotation")]
    public VirtualRotationMethod rotationMethod = VirtualRotationMethod.QuickTurns;

    [Header("Basic Rotation Parameters")]
    [Tooltip("Angle of rotation for quick turns and snap turns in degrees")]
    [Range(15f, 90f)]
    public float turnAngle = 45f;

    [Tooltip("Duration of the quick turn animation in milliseconds")]
    [Range(50f, 600f)]
    public float quickTurnDuration = 100f;

    [Tooltip("Speed multiplier for smooth turns (higher values create faster rotation)")]
    [Range(1f, 10f)]
    public float smoothTurnSpeed = 3f;

    [Header("Scrolling Rotation Parameters")]
    [Tooltip("Threshold angle that triggers scrolling rotation (degrees)")]
    [Range(15f, 90f)]
    public float scrollingThresholdAngle = 45f;

    [Tooltip("Base rotation speed when threshold is reached (degrees/second)")]
    [Range(5f, 90f)] // Reduced from original range of 10f-180f
    public float baseScrollingSpeed = 30f;

    [Tooltip("How much the rotation speed increases per degree over threshold")]
    [Range(0.1f, 2f)] // Reduced from original range of 0.1f-5f
    public float scrollingSpeedMultiplier = 0.5f;

    [Header("Rotation Gains Parameters")]
    [Tooltip("Multiplier applied to physical head rotation (1-5)")]
    [Range(1f, 5f)]
    public float rotationGainFactor = 2f;    

    [Header("Dead-Zone Settings")]
    [Tooltip("Minimum movement input magnitude required for translation (0-0.99)")]
    [Range(0f, 0.99f)]
    public float movementDeadZone = 0.1f;

    [Tooltip("Minimum rotation input magnitude required for rotation (0-0.99)")]
    [Range(0f, 0.99f)]
    public float rotationDeadZone = 0.9f;

    [Header("Debug Visualization")]
    [Tooltip("Show debug information on screen")]
    [SerializeField] private bool showGUI;
    [Tooltip("Show movement and rotation gizmos in scene view")]
    [SerializeField] private bool showGizmos = true;
    [Tooltip("Show separate visualization for joystick input")]
    [SerializeField] private bool separateJoystickVisualization;

    [Header("Gizmo Settings")]
    [Tooltip("Choose between 2D (forward/right) or 3D (forward/right/up) direction visualization")]
    public GizmoVisualizationMode gizmoMode = GizmoVisualizationMode.TwoDimensional;

    public enum GizmoVisualizationMode
    {
        TwoDimensional,
        ThreeDimensional
    }
    #endregion


    #region Private Fields
    private Transform hmdTransform;
    private CharacterController characterController;

    // Translation-related variables
    private Vector3 currentLinearVelocity;  // Current linear velocity vector
    private float currentLinearSpeed;       // Magnitude of linear velocity
    private Vector2 currentJoystickInput;   // Current movement joystick input

    // Rotation-related variables
    private Vector2 currentRotationInput;   // Current rotation joystick input
    private float currentAngularVelocity;   // Current rotation speed in degrees per second
    private float currentAngularSpeed;      // Absolute value of angular velocity

    // Cached direction vectors
    private Vector3 cachedForwardDir;
    private Vector3 cachedRightDir;
    private Vector3 cachedMoveDirection;
    private Vector3 initialForwardDir;
    private Vector3 initialRightDir;

    // State tracking
    private bool isMoving;
    private bool isRotating;
    private bool canAcceptRotationInput = true;

    // Rotation animation variables
    private float currentRotationAngle;
    private float targetRotationAngle;
    private float rotationStartTime;
    private float previousRotationY;        // Used for angular velocity calculation

    // Scrolling rotation variables
    private float calibratedForwardYaw;
    private bool isCalibrated = false;
    private float currentScrollingSpeed;
    private float lastVirtualRotation = 0f;
    private const KeyCode CALIBRATION_KEY = KeyCode.C;

    // Rotation tracking
    private Quaternion lastHeadRotation;

    // Cached calculations
    private float oneMinusMovementDeadZone;
    private float oneMinusRotationDeadZone;

    // Visualization
    private readonly Vector3[] circlePoints;
    private const int CIRCLE_SEGMENTS = 32;
    private static readonly Quaternion ARROW_RIGHT_ROTATION = Quaternion.Euler(0, 30, 0);
    private static readonly Quaternion ARROW_LEFT_ROTATION = Quaternion.Euler(0, -30, 0);
    private GUIStyle guiStyle;

    // Gizmo positioning
    private Vector3 gizmoPosition;
    private readonly Quaternion gizmoRotation = Quaternion.identity; // Fixed rotation for gizmos
    #endregion

    /// <summary>
    /// Constructor initializes the circle points array used for gizmo visualization
    /// </summary>
    public Controller_Locomotion_OpenXR()
    {
        circlePoints = new Vector3[CIRCLE_SEGMENTS + 1];
        float angleStep = 2f * Mathf.PI / CIRCLE_SEGMENTS;
        for (int i = 0; i <= CIRCLE_SEGMENTS; i++)
        {
            float angle = i * angleStep;
            circlePoints[i] = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        hmdTransform = Camera.main.transform;

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        UpdateCachedValues();
        InitializeGUIStyle();
        lastHeadRotation = hmdTransform.rotation;
    }

    /// <summary>
    /// Enables input actions and subscribes to input events
    /// </summary>
    private void OnEnable()
    {
        // Setup movement input
        moveAction.action.Enable();
        moveAction.action.performed += OnMovePerformed;
        moveAction.action.canceled += OnMoveCanceled;

        // Setup rotation input
        rotateAction.action.Enable();
        rotateAction.action.performed += OnRotatePerformed;
        rotateAction.action.canceled += OnRotateCanceled;

        // Initialize tracking variables
        previousRotationY = transform.eulerAngles.y;
    }

    private void OnDisable()
    {
        moveAction.action.performed -= OnMovePerformed;
        moveAction.action.canceled -= OnMoveCanceled;
        moveAction.action.Disable();

        rotateAction.action.performed -= OnRotatePerformed;
        rotateAction.action.canceled -= OnRotateCanceled;
        rotateAction.action.Disable();
    }

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        currentJoystickInput = context.ReadValue<Vector2>();

        if (!isMoving && currentJoystickInput.magnitude > movementDeadZone)
        {
            UpdateDirectionVectors();
            initialForwardDir = cachedForwardDir;
            initialRightDir = cachedRightDir;
            isMoving = true;
        }
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        currentJoystickInput = Vector2.zero;
        isMoving = false;
    }

    private void OnRotatePerformed(InputAction.CallbackContext context)
    {
        if (!virtualRotationEnabled) return;

        currentRotationInput = context.ReadValue<Vector2>();
        float inputX = currentRotationInput.x;
        float inputMagnitude = Mathf.Abs(inputX);

        switch (rotationMethod)
        {
            case VirtualRotationMethod.QuickTurns:
                if (inputMagnitude > rotationDeadZone && canAcceptRotationInput && !isRotating)
                {
                    float direction = Mathf.Sign(inputX);
                    targetRotationAngle = transform.eulerAngles.y + (direction * turnAngle);
                    rotationStartTime = Time.time;
                    isRotating = true;
                    canAcceptRotationInput = false;
                }
                break;

            case VirtualRotationMethod.SnapTurns:
                if (inputMagnitude > rotationDeadZone && canAcceptRotationInput && !isRotating)
                {
                    float direction = Mathf.Sign(inputX);
                    float newAngle = transform.eulerAngles.y + (direction * turnAngle);
                    transform.rotation = Quaternion.Euler(0, newAngle, 0);
                    isRotating = true;
                    canAcceptRotationInput = false;
                }
                break;

            case VirtualRotationMethod.SmoothTurns:
                if (inputMagnitude > rotationDeadZone)
                {
                    float normalizedInput = (inputMagnitude - rotationDeadZone) / (oneMinusRotationDeadZone);
                    float rotationAmount = normalizedInput * smoothTurnSpeed * Time.deltaTime * 100f;
                    transform.Rotate(0, rotationAmount * Mathf.Sign(inputX), 0);
                }
                break;
        }
    }

    private void OnRotateCanceled(InputAction.CallbackContext context)
    {
        currentRotationInput = Vector2.zero;
        if (rotationMethod != VirtualRotationMethod.QuickTurns)
        {
            isRotating = false;
        }

        // Reset rotation input acceptance when thumbstick returns to center
        if (context.ReadValue<Vector2>().magnitude < rotationDeadZone)
        {
            canAcceptRotationInput = true;
        }
    }

    /// <summary>
    /// Updates both translation and rotation states each frame
    /// </summary>
    private void Update()
    {
        // Update gizmo position to follow player but maintain fixed rotation
        gizmoPosition = new Vector3(transform.position.x, transform.position.y, transform.position.z) + Vector3.back * 2f;

        // Handle calibration for scrolling rotation
        if (Input.GetKeyDown(CALIBRATION_KEY) && rotationMethod == VirtualRotationMethod.Scrolling)
        {
            CalibrateScrollingRotation();
        }

        // Handle different rotation methods
        if (virtualRotationEnabled)
        {
            switch (rotationMethod)
            {
                case VirtualRotationMethod.QuickTurns:
                case VirtualRotationMethod.SnapTurns:
                case VirtualRotationMethod.SmoothTurns:
                    HandleStandardRotation();
                    break;

                case VirtualRotationMethod.Scrolling:
                    if (isCalibrated)
                    {
                        HandleScrollingRotation();
                    }
                    break;

                case VirtualRotationMethod.RotationGains:
                    HandleRotationGains();
                    break;
            }
        }

        // Calculate angular velocity
        float currentRotationY = transform.eulerAngles.y;
        float deltaRotation = Mathf.DeltaAngle(previousRotationY, currentRotationY);
        currentAngularVelocity = deltaRotation / Time.deltaTime;
        currentAngularSpeed = Mathf.Abs(currentAngularVelocity);
        previousRotationY = currentRotationY;

        // Handle movement
        HandleMovement();
    }

    /// <summary>
    /// Calibrates the forward direction for scrolling rotation
    /// </summary>
    private void CalibrateScrollingRotation()
    {
        calibratedForwardYaw = hmdTransform.eulerAngles.y;
        lastVirtualRotation = transform.eulerAngles.y;
        isCalibrated = true;
        Debug.Log($"Calibrated forward yaw: {calibratedForwardYaw}");
    }

    /// <summary>
    /// Handles rotation for QuickTurns, SnapTurns, and SmoothTurns methods
    /// </summary>
    private void HandleStandardRotation()
    {
        if (rotationMethod == VirtualRotationMethod.QuickTurns && isRotating)
        {
            float elapsed = (Time.time - rotationStartTime) * 1000f;
            float t = Mathf.Clamp01(elapsed / quickTurnDuration);

            float newAngle = Mathf.LerpAngle(currentRotationAngle, targetRotationAngle, t);
            transform.rotation = Quaternion.Euler(0, newAngle, 0);

            if (t >= 1f)
            {
                isRotating = false;
                currentRotationAngle = targetRotationAngle;
            }
        }
    }

    /// <summary>
    /// Handles scrolling rotation based on head rotation threshold
    /// </summary>
    private void HandleScrollingRotation()
    {
        if (!isCalibrated) return;

        // Get current head yaw and adjust for accumulated virtual rotation
        float virtualRotationDelta = transform.eulerAngles.y - lastVirtualRotation;
        calibratedForwardYaw += virtualRotationDelta;
        lastVirtualRotation = transform.eulerAngles.y;

        float currentYaw = hmdTransform.eulerAngles.y;
        float yawDifference = Mathf.DeltaAngle(currentYaw, calibratedForwardYaw);

        if (Mathf.Abs(yawDifference) > scrollingThresholdAngle)
        {
            float overThreshold = Mathf.Abs(yawDifference) - scrollingThresholdAngle;
            float targetSpeed = baseScrollingSpeed + (overThreshold * scrollingSpeedMultiplier);
            currentScrollingSpeed = Mathf.Lerp(currentScrollingSpeed, targetSpeed, Time.deltaTime * 2f);

            float rotationAmount = currentScrollingSpeed * Time.deltaTime;
            if (yawDifference > 0)
            {
                transform.Rotate(0, -rotationAmount, 0);
            }
            else
            {
                transform.Rotate(0, rotationAmount, 0);
            }
        }
        else
        {
            currentScrollingSpeed = Mathf.Lerp(currentScrollingSpeed, 0f, Time.deltaTime * 3f);
        }
    }

    /// <summary>
    /// Handles amplified rotation based on head movement
    /// </summary>
    private void HandleRotationGains()
    {
        Quaternion deltaRotation = Quaternion.Inverse(lastHeadRotation) * hmdTransform.rotation;
        float deltaYaw = deltaRotation.eulerAngles.y;

        // Normalize the angle to [-180, 180]
        if (deltaYaw > 180f)
        {
            deltaYaw -= 360f;
        }

        // Apply gain factor to the rotation
        if (Mathf.Abs(deltaYaw) > 0.01f) // Threshold to avoid micro-movements
        {
            float amplifiedRotation = deltaYaw * (rotationGainFactor - 1f);
            transform.Rotate(0, amplifiedRotation, 0);
        }

        lastHeadRotation = hmdTransform.rotation;
    }

    private bool ValidateReferences()
    {
        if (!hmdTransform || !controller || moveAction == null || rotateAction == null)
        {
            Debug.LogError("Required references missing! Ensure Camera exists and input actions are assigned.");
            return false;
        }

        if (directionDevice == ForwardDirectionInputDevice.Body && !bodyTransform)
        {
            Debug.LogError("Body Transform reference is required when using Body direction device!");
            return false;
        }

        return true;
    }

    private void UpdateCachedValues()
    {
        oneMinusMovementDeadZone = 1f - movementDeadZone;
        oneMinusRotationDeadZone = 1f - rotationDeadZone;
    }

    private void OnValidate()
    {
        movementDeadZone = Mathf.Clamp(movementDeadZone, 0f, 0.99f);
        rotationDeadZone = Mathf.Clamp(rotationDeadZone, 0f, 0.99f);
        UpdateCachedValues();
    }

    /// <summary>
    /// Processes translation movement based on input
    /// </summary>
    private void HandleMovement()
    {
        float inputMagnitude = currentJoystickInput.magnitude;

        if (inputMagnitude < movementDeadZone)
        {
            ResetMovement();
            return;
        }

        float speed = CalculateSpeed(inputMagnitude);

        if (directionMode == ForwardDirectionMode.Continuous || !isMoving)
        {
            UpdateDirectionVectors();
        }
        else
        {
            cachedForwardDir = initialForwardDir;
            cachedRightDir = initialRightDir;
        }

        UpdateMoveDirection();
        currentLinearVelocity = cachedMoveDirection * speed;
        currentLinearSpeed = currentLinearVelocity.magnitude;
        characterController.Move(currentLinearVelocity * Time.deltaTime);
    }

    private void ResetMovement()
    {
        if (currentLinearVelocity != Vector3.zero)
        {
            currentLinearVelocity = Vector3.zero;
            cachedMoveDirection = Vector3.zero;
            isMoving = false;
        }
    }

    private float CalculateSpeed(float inputMagnitude)
    {
        float normalizedDistance = (inputMagnitude - movementDeadZone) / oneMinusMovementDeadZone;
        return maxSpeed * Mathf.Min(
            Mathf.Pow(speedSensitivity * normalizedDistance, exponentialTransferFunctionPower),
            1f
        );
    }

    private void InitializeGUIStyle()
    {
        guiStyle = new GUIStyle
        {
            fontSize = 24,
            normal = { textColor = Color.green }
        };
    }

    private void UpdateDirectionVectors()
    {
        switch (directionDevice)
        {
            case ForwardDirectionInputDevice.Controller:
                cachedForwardDir = controller.transform.forward;
                cachedRightDir = controller.transform.right;
                break;
            case ForwardDirectionInputDevice.Camera:
                cachedForwardDir = hmdTransform.forward;
                cachedRightDir = hmdTransform.right;
                break;
            case ForwardDirectionInputDevice.Body:
                cachedForwardDir = bodyTransform.forward;
                cachedRightDir = bodyTransform.right;
                break;
        }

        if (navigationMode == NavigationMode.TwoDimensional)
        {
            // Flatten vectors for 2D movement
            cachedForwardDir.y = 0f;
            cachedForwardDir.Normalize();

            cachedRightDir.y = 0f;
            cachedRightDir.Normalize();
        }
        // For 3D mode, we keep the original vectors with their Y components
    }

    private void UpdateMoveDirection()
    {
        if (navigationMode == NavigationMode.TwoDimensional)
        {
            // Current 2D behavior
            float theta = CalculateMovementAngle();
            cachedMoveDirection = (cachedForwardDir * Mathf.Sin(theta) +
                                 cachedRightDir * Mathf.Cos(theta)).normalized;
        }
        else
        {
            // 3D behavior - use the full 3D vectors for movement
            cachedMoveDirection = (cachedForwardDir * currentJoystickInput.y +
                                 cachedRightDir * currentJoystickInput.x).normalized;
        }
    }

    private float CalculateMovementAngle()
    {
        return directionDevice == ForwardDirectionInputDevice.Controller
            ? Mathf.Atan2(currentJoystickInput.y, currentJoystickInput.x)
            : Mathf.Atan2(currentJoystickInput.y, currentJoystickInput.x);
    }

    /// <summary>
    /// Displays debug information on screen when enabled
    /// </summary>
    private void OnGUI()
    {
        if (!showGUI) return;

        string info = $"Navigation Mode: {navigationMode}\n" +
                     $"Translation Metrics:\n" +
                     $"Linear Velocity: {currentLinearVelocity:F2} m/s\n" +
                     $"Linear Speed: {currentLinearSpeed:F2} m/s\n" +
                     $"Movement Input: {currentJoystickInput:F2}\n" +
                     $"Movement Magnitude: {currentJoystickInput.magnitude:F2}\n\n" +
                     $"Rotation Metrics:\n" +
                     $"Angular Velocity: {currentAngularVelocity:F1} deg/s\n" +
                     $"Angular Speed: {currentAngularSpeed:F1} deg/s\n" +
                     $"Rotation Input: {currentRotationInput:F2}\n";

        if (rotationMethod == VirtualRotationMethod.Scrolling)
        {
            float currentYaw = hmdTransform.eulerAngles.y;
            float yawDifference = Mathf.DeltaAngle(currentYaw, calibratedForwardYaw);

            info += $"Scrolling Rotation:\n" +
                   $"Yaw Difference: {yawDifference:F1}°\n" +
                   $"Calibrated: {isCalibrated}\n" +
                   $"Threshold: {scrollingThresholdAngle}°\n";
        }
        else if (rotationMethod == VirtualRotationMethod.RotationGains)
        {
            info += $"Gain Factor: {rotationGainFactor:F2}x\n";
        }

        info += $"\nSystem State:\n" +
                $"Direction Device: {directionDevice}\n" +
                $"Direction Mode: {directionMode}\n" +
                $"Rotation Method: {rotationMethod}\n" +
                $"Is Moving: {isMoving}\n" +
                $"Is Rotating: {isRotating}\n" +
                $"Can Accept Rotation: {canAcceptRotationInput}";

        GUI.Label(new Rect(10, 40, 500, 300), info, guiStyle);
    }

    // Gizmo drawing methods remain unchanged
    private void OnDrawGizmos()
    {
        if (!UnityEngine.Application.isPlaying || !showGizmos) return;

        Vector3 origin = hmdTransform.position + Vector3.back * 2f;

        if (cachedForwardDir != Vector3.zero)
        {
            if (!separateJoystickVisualization)
            {
                DrawDirectionGizmos(origin);
            }
            else
            {
                DrawDirectionGizmos(origin);
                DrawJoystickGizmos(origin + Vector3.right * 2.5f);
            }
        }
    }

    private void DrawDirectionGizmos(Vector3 origin)
    {
        Vector3 displayForwardDir = cachedForwardDir;
        Vector3 displayRightDir = cachedRightDir;
        Vector3 displayUpDir = Vector3.up;

        // Transform the direction vectors to world space while maintaining fixed rotation
        if (directionDevice == ForwardDirectionInputDevice.Controller)
        {
            if (gizmoMode == GizmoVisualizationMode.ThreeDimensional)
            {
                displayForwardDir = gizmoRotation * controller.transform.forward;
                displayRightDir = gizmoRotation * controller.transform.right;
                displayUpDir = gizmoRotation * controller.transform.up;
            }
            else
            {
                // 2D mode - flatten the vectors
                Vector3 forward = controller.transform.forward;
                forward.y = 0;
                forward.Normalize();
                displayForwardDir = gizmoRotation * forward;

                Vector3 right = controller.transform.right;
                right.y = 0;
                right.Normalize();
                displayRightDir = gizmoRotation * right;
            }
        }
        else if (directionDevice == ForwardDirectionInputDevice.Camera)
        {
            if (gizmoMode == GizmoVisualizationMode.ThreeDimensional)
            {
                displayForwardDir = gizmoRotation * hmdTransform.forward;
                displayRightDir = gizmoRotation * hmdTransform.right;
                displayUpDir = gizmoRotation * hmdTransform.up;
            }
            else
            {
                Vector3 forward = hmdTransform.forward;
                forward.y = 0;
                forward.Normalize();
                displayForwardDir = gizmoRotation * forward;
                displayRightDir = gizmoRotation * Vector3.Cross(Vector3.up, forward);
            }
        }
        else if (directionDevice == ForwardDirectionInputDevice.Body && bodyTransform != null)
        {
            if (gizmoMode == GizmoVisualizationMode.ThreeDimensional)
            {
                displayForwardDir = gizmoRotation * bodyTransform.forward;
                displayRightDir = gizmoRotation * bodyTransform.right;
                displayUpDir = gizmoRotation * bodyTransform.up;
            }
            else
            {
                Vector3 forward = bodyTransform.forward;
                forward.y = 0;
                forward.Normalize();
                displayForwardDir = gizmoRotation * forward;

                Vector3 right = bodyTransform.right;
                right.y = 0;
                right.Normalize();
                displayRightDir = gizmoRotation * right;
            }
        }

        // Draw reference coordinate system
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(gizmoPosition, gizmoPosition + displayForwardDir);
        DrawArrowhead(gizmoPosition + displayForwardDir, displayForwardDir, 0.1f, Color.blue);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(gizmoPosition, gizmoPosition + displayRightDir);
        DrawArrowhead(gizmoPosition + displayRightDir, displayRightDir, 0.1f, Color.red);

        if (gizmoMode == GizmoVisualizationMode.ThreeDimensional)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(gizmoPosition, gizmoPosition + displayUpDir);
            DrawArrowhead(gizmoPosition + displayUpDir, displayUpDir, 0.1f, Color.green);
        }

        // Draw controller input direction
        if (currentJoystickInput.magnitude > movementDeadZone)
        {
            // Calculate the input direction in world space based on the direction device
            Vector3 inputDirection;
            if (directionMode == ForwardDirectionMode.Continuous || !isMoving)
            {
                // Use current direction vectors
                inputDirection = (displayForwardDir * currentJoystickInput.y +
                                displayRightDir * currentJoystickInput.x).normalized;
            }
            else
            {
                // Use initial direction vectors for the entire movement
                inputDirection = (initialForwardDir * currentJoystickInput.y +
                                initialRightDir * currentJoystickInput.x).normalized;
            }

            float inputMagnitude = currentJoystickInput.magnitude;
            Vector3 scaledInputDir = inputDirection * inputMagnitude;

            // Draw the actual movement direction
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(gizmoPosition, gizmoPosition + scaledInputDir);
            DrawArrowhead(gizmoPosition + scaledInputDir, scaledInputDir, 0.1f, Color.yellow);

            // Draw raw controller input as a separate vector (in cyan)
            Vector3 rawInputDir = new Vector3(currentJoystickInput.x, 0, currentJoystickInput.y).normalized;
            Vector3 rawScaledInputDir = rawInputDir * inputMagnitude;

            // Offset the raw input visualization slightly upward to distinguish it
            Vector3 rawInputOrigin = gizmoPosition + Vector3.up * 0.1f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(rawInputOrigin, rawInputOrigin + rawScaledInputDir);
            DrawArrowhead(rawInputOrigin + rawScaledInputDir, rawScaledInputDir, 0.1f, Color.cyan);

            // Add vector labels slightly offset from the arrows
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(gizmoPosition + scaledInputDir + Vector3.up * 0.1f, "Adjusted Direction");
            UnityEditor.Handles.Label(rawInputOrigin + rawScaledInputDir + Vector3.up * 0.1f, "Raw Input");
        }

        // Draw text information below the gizmo
        Vector3 textPosition = gizmoPosition + Vector3.down * 0.5f;
        UnityEditor.Handles.color = Color.white;
        string configInfo = $"Direction Device: {directionDevice}\nDirection Mode: {directionMode}";
        UnityEditor.Handles.Label(textPosition, configInfo);

        // Draw scrolling rotation visualization if enabled
        if (rotationMethod == VirtualRotationMethod.Scrolling && isCalibrated)
        {
            float radius = 1f;
            Vector3 calibratedForward = gizmoRotation * (Quaternion.Euler(0, calibratedForwardYaw, 0) * Vector3.forward);

            // Draw calibrated forward direction
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(gizmoPosition, gizmoPosition + calibratedForward);
            DrawArrowhead(gizmoPosition + calibratedForward, calibratedForward, 0.1f, Color.cyan);

            // Draw threshold indicators
            Color thresholdColor = new Color(1f, 1f, 0f, 0.3f);
            Vector3 positiveThresholdDir = gizmoRotation * (Quaternion.Euler(0, calibratedForwardYaw + scrollingThresholdAngle, 0) * Vector3.forward);
            Vector3 negativeThresholdDir = gizmoRotation * (Quaternion.Euler(0, calibratedForwardYaw - scrollingThresholdAngle, 0) * Vector3.forward);

            Gizmos.color = thresholdColor;
            Gizmos.DrawLine(gizmoPosition, gizmoPosition + positiveThresholdDir);
            Gizmos.DrawLine(gizmoPosition, gizmoPosition + negativeThresholdDir);
            DrawArrowhead(gizmoPosition + positiveThresholdDir, positiveThresholdDir, 0.1f, thresholdColor);
            DrawArrowhead(gizmoPosition + negativeThresholdDir, negativeThresholdDir, 0.1f, thresholdColor);

            // Get current head yaw and calculate difference
            float currentYaw = hmdTransform.eulerAngles.y;
            float yawDifference = Mathf.DeltaAngle(currentYaw, calibratedForwardYaw);
            Vector3 currentHeadForward = gizmoRotation * (Quaternion.Euler(0, currentYaw, 0) * Vector3.forward);

            // Draw current head direction
            Gizmos.color = Color.white;
            Gizmos.DrawLine(gizmoPosition, gizmoPosition + currentHeadForward);
            DrawArrowhead(gizmoPosition + currentHeadForward, currentHeadForward, 0.1f, Color.white);

            // Add scrolling rotation information below the main configuration text
            Vector3 scrollingTextPosition = textPosition + Vector3.down * 0.3f;
            string scrollingInfo = $"Yaw Difference: {yawDifference:F1}°\n" +
                                 $"Threshold: ±{scrollingThresholdAngle}°\n" +
                                 (Mathf.Abs(yawDifference) > scrollingThresholdAngle ?
                                 $"Over Threshold: {(Mathf.Abs(yawDifference) - scrollingThresholdAngle):F1}°" :
                                 "Within Threshold");
            UnityEditor.Handles.Label(scrollingTextPosition, scrollingInfo);

            // Draw rotation arcs if movement detected
            if (Mathf.Abs(yawDifference) > 0.1f)
            {
                bool isRotatingRight = yawDifference > 0;
                float thresholdAngle = calibratedForwardYaw + (scrollingThresholdAngle * (isRotatingRight ? 1 : -1));
                Vector3 thresholdDir = gizmoRotation * (Quaternion.Euler(0, thresholdAngle, 0) * Vector3.forward);

                if (Mathf.Abs(yawDifference) <= scrollingThresholdAngle)
                {
                    Color preThresholdColor = new Color(0.5f, 1f, 0.5f, 0.5f);
                    DrawArcBetweenVectors(gizmoPosition, calibratedForward, currentHeadForward, radius, preThresholdColor);
                }
                else
                {
                    Color preThresholdColor = new Color(0.5f, 1f, 0.5f, 0.5f);
                    DrawArcBetweenVectors(gizmoPosition, calibratedForward, thresholdDir, radius, preThresholdColor);
                    Color postThresholdColor = new Color(1f, 0.5f, 0.5f, 0.5f);
                    DrawArcBetweenVectors(gizmoPosition, thresholdDir, currentHeadForward, radius, postThresholdColor);
                }
            }
        }
    }


    // Add new helper method for drawing arcs
    private void DrawArcBetweenVectors(Vector3 center, Vector3 from, Vector3 to, float radius, Color color)
    {
        const int segments = 32;
        float angle = Vector3.SignedAngle(from, to, Vector3.up);
        float angleStep = angle / segments;

        Gizmos.color = color;
        Vector3 previousPoint = center + from * radius;

        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = angleStep * i;
            Vector3 currentPoint = center + (Quaternion.Euler(0, currentAngle, 0) * from) * radius;
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }

    private void DrawJoystickGizmos(Vector3 origin)
    {
        // Draw movement joystick visualization
        Vector3 moveOrigin = origin;

        // Movement deadzone circle
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        DrawCircle(moveOrigin, movementDeadZone);

        // Movement max range circle
        Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
        DrawCircle(moveOrigin, 1f);

        // Current movement input
        if (currentJoystickInput != Vector2.zero)
        {
            Gizmos.color = Color.yellow;
            Vector3 inputDir = new Vector3(currentJoystickInput.x, 0, currentJoystickInput.y);
            Gizmos.DrawLine(moveOrigin, moveOrigin + inputDir);
            DrawArrowhead(moveOrigin + inputDir, inputDir, 0.1f, Color.yellow);
        }

        // Draw rotation joystick visualization
        if (virtualRotationEnabled)
        {
            Vector3 rotateOrigin = origin + Vector3.right * 2.5f;

            // Rotation deadzone circle
            Gizmos.color = new Color(1f, 0.5f, 1f, 0.5f); // Pink-ish for rotation
            DrawCircle(rotateOrigin, rotationDeadZone);

            // Rotation max range circle
            Gizmos.color = new Color(1f, 0f, 1f, 0.5f); // Magenta for rotation
            DrawCircle(rotateOrigin, 1f);

            // Current rotation input
            if (currentRotationInput != Vector2.zero)
            {
                Gizmos.color = Color.magenta;
                Vector3 rotInputDir = new Vector3(currentRotationInput.x, 0, currentRotationInput.y);
                Gizmos.DrawLine(rotateOrigin, rotateOrigin + rotInputDir);
                DrawArrowhead(rotateOrigin + rotInputDir, rotInputDir, 0.1f, Color.magenta);

                // Display rotation state
                string stateText = isRotating ? "Rotating" : (canAcceptRotationInput ? "Ready" : "Waiting");
                UnityEditor.Handles.Label(rotateOrigin + Vector3.up * 0.2f, stateText);
            }
        }
    }

    private void DrawArrowhead(Vector3 position, Vector3 direction, float size, Color color)
    {
        Vector3 right = ARROW_RIGHT_ROTATION * -direction * size;
        Vector3 left = ARROW_LEFT_ROTATION * -direction * size;

        Gizmos.color = color;
        Gizmos.DrawLine(position, position + right);
        Gizmos.DrawLine(position, position + left);
    }

    private void DrawCircle(Vector3 center, float radius)
    {
        Vector3 prevPoint = center + circlePoints[0] * radius;

        for (int i = 1; i <= CIRCLE_SEGMENTS; i++)
        {
            Vector3 newPoint = center + circlePoints[i] * radius;
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
    }
}
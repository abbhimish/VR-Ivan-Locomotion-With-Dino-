using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Input;
using System;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(LineRenderer))]
public class VRTeleportController : MonoBehaviour
{
    // Enums for configuration
    public enum TeleportTriggerType
    {
        ThumbstickDistance,
        Thumbstick,
        Button
    }

    public enum LandingOrientation
    {
        None,
        Camera,
        Controller,
        ControllerRoll,
        Joystick,
        Body
    }

    public enum ArcType
    {
        Bezier,
        Parabolic,
        TallRayCast
    }

    public enum TeleportType
    {
        InstantTeleport,
        DashTeleport,
        DashTeleportArc
    }

    #region Public Fields

    [Header("Required XR References")]
    [SerializeField]
    [Tooltip("Reference to the XR Rig's Camera Offset transform")]
    private Transform cameraOffset;

    [SerializeField]
    [Tooltip("Reference to the XR Camera transform")]
    private Transform xrCamera;

    [SerializeField]
    [Tooltip("Reference to the controller used for orientation")]
    private Transform orientationController;

    [Header("Teleport Configuration")]
    [SerializeField]
    [Tooltip("Choose between thumbstick or button activation for teleport")]
    private TeleportTriggerType triggerType;

    [SerializeField]
    [Tooltip("Determines how the player's orientation is set after teleporting")]
    private LandingOrientation orientationType;

    [SerializeField]
    [Tooltip("The type of arc visualization used for teleport targeting")]
    private ArcType arcType;

    [SerializeField]
    [Tooltip("Choose between instant or dash teleportation")]
    private TeleportType teleportType;

    [SerializeField]
    [Tooltip("Reference object for body-based orientation (if using Body orientation type)")]
    private GameObject bodyReference;

    [SerializeField]
    [Tooltip("Maximum teleport distance")]
    private float maxDistance = 20f;

    [SerializeField]
    [Tooltip("Minimum angle of the controller for teleport activation")]
    private float minControllerAngle = 0f;

    [SerializeField]
    [Tooltip("Maximum angle of the controller for teleport activation")]
    private float maxControllerAngle = 75f;

    [SerializeField]
    [Tooltip("Width of the teleport arc line")]
    private float lineWidth = 0.02f;

    [SerializeField]
    [Tooltip("Tag used to identify valid teleport surfaces")]
    private string teleportableTag = "Floor";

    [Header("Dash Teleport Settings")]
    [SerializeField]
    [Tooltip("Duration of dash teleport in milliseconds (1-2000)")]
    [Range(1, 2000)]
    private int dashDuration = 100;

    [Header("Input Configuration")]
    [SerializeField]
    [Tooltip("Input Action Reference for joystick/thumbstick control")]
    private InputActionReference joystickAction;

    [SerializeField]
    [Tooltip("Input Action Reference for teleport button")]
    private InputActionReference teleportButton;

    [SerializeField]
    [Tooltip("Input Action Reference for canceling teleport")]
    private InputActionReference cancelTeleportButton;

    [Header("Input Settings")]
    [SerializeField]
    [Tooltip("Deadzone for joystick input")]
    [Range(0f, 1f)]
    private float joystickDeadzone = 0.1f;

    [Header("Parabolic Settings")]
    [SerializeField]
    [Tooltip("Initial velocity for parabolic arc")]
    private float initialVelocity = 10f;

    [Header("Visual Settings")]
    [SerializeField]
    [Tooltip("Material used when teleport location is valid")]
    private Material validTeleportMaterial;

    [SerializeField]
    [Tooltip("Material used when teleport location is invalid")]
    private Material invalidTeleportMaterial;

    [SerializeField]
    [Tooltip("Prefab for the direction indicator")]
    private GameObject directionIndicatorPrefab;

    [SerializeField]
    [Tooltip("Number of segments in the teleport arc line")]
    private int lineSegments = 50;

    [Header("Controller Roll Settings")]
    [SerializeField]
    [Tooltip("Multiplier for roll-to-yaw rotation when using ControllerRoll orientation (1-4)")]
    [Range(1f, 4f)]
    private float rollSensitivityMultiplier = 1f;

    [SerializeField]
    [Tooltip("Smoothing time for thumbstick input (lower = more responsive, higher = more stable)")]
    [Range(0.01f, 0.2f)]
    private float thumbstickSmoothTime = 0.1f;

    #endregion

    #region Private Fields

    // Private variables
    private LineRenderer lineRenderer;
    private GameObject directionIndicator;
    private Transform directionArrow;
    private Vector2 currentJoystickInput;
    private bool teleportButtonPressed;
    private bool cancelButtonPressed;
    private bool isTeleportActive;
    private Vector3 teleportTarget;
    private Quaternion targetRotation;
    private bool isValidTeleportLocation;
    private Vector2 lastValidThumbstickDirection;
    private Quaternion initialControllerRotation;
    private bool isControllerRollCalibrated;
    private Vector2 smoothedThumbstickInput;
    private Vector2 thumbstickVelocity;

    #endregion


    private void OnEnable()
    {
        // Enable and subscribe to input actions
        EnableInputActions();
    }

    private void OnDisable()
    {
        // Disable and unsubscribe from input actions
        DisableInputActions();
    }

    private void Start()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        InitializeComponents();
    }

    private void EnableInputActions()
    {
        joystickAction.action.Enable();
        joystickAction.action.performed += OnTeleportJoystickPerformed;
        joystickAction.action.canceled += OnTeleportJoystickPerformed;

        teleportButton.action.Enable();
        teleportButton.action.performed += OnTeleportButtonPerformed;
        teleportButton.action.canceled += OnTeleportButtonPerformed;

        cancelTeleportButton.action.Enable();
        cancelTeleportButton.action.performed += OnCancelTeleportPerformed;
        cancelTeleportButton.action.canceled += OnCancelTeleportPerformed;
    }

    private void DisableInputActions()
    {
        joystickAction.action.performed -= OnTeleportJoystickPerformed;
        joystickAction.action.canceled -= OnTeleportJoystickPerformed;
        joystickAction.action.Disable();

        teleportButton.action.performed -= OnTeleportButtonPerformed;
        teleportButton.action.canceled -= OnTeleportButtonPerformed;
        teleportButton.action.Disable();

        cancelTeleportButton.action.performed -= OnCancelTeleportPerformed;
        cancelTeleportButton.action.canceled -= OnCancelTeleportPerformed;
        cancelTeleportButton.action.Disable();
    }

    private void InitializeComponents()
    {
        // Initialize LineRenderer
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = lineSegments;
        lineRenderer.enabled = false;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;

        // Initialize direction indicator
        directionIndicator = Instantiate(directionIndicatorPrefab);
        directionIndicator.SetActive(false);
        directionArrow = directionIndicator.transform.Find("Directional_Arrow");
    }

    private bool ValidateReferences()
    {
        if (cameraOffset == null || xrCamera == null || orientationController == null)
        {
            Debug.LogError("Required references not assigned in VRTeleportController!");
            return false;
        }
        return true;
    }

    private void Update()
    {
        if (cancelButtonPressed)
        {
            return;
        }

        HandleTeleportInput();

        if (isTeleportActive)
        {
            UpdateArcVisualization();
            UpdateDirectionIndicator();
        }
    }

    private void OnCancelTeleportPerformed(InputAction.CallbackContext context)
    {
        cancelButtonPressed = context.ReadValueAsButton();
        if (cancelButtonPressed && isTeleportActive)
        {
            CancelTeleport();
        }
    }

    private void OnTeleportJoystickPerformed(InputAction.CallbackContext context)
    {
        currentJoystickInput = context.ReadValue<Vector2>();
        if (currentJoystickInput.magnitude <= joystickDeadzone)
        {
            currentJoystickInput = Vector2.zero;
        }
    }

    private void OnTeleportButtonPerformed(InputAction.CallbackContext context)
    {
        bool isPressed = context.ReadValueAsButton();

        if (triggerType == TeleportTriggerType.ThumbstickDistance)
        {
            // Execute teleport on button press
            if (isPressed && isTeleportActive && isValidTeleportLocation)
            {
                ExecuteTeleport();
            }
            teleportButtonPressed = isPressed;
        }
        else
        {
            // Original behavior for other trigger types
            teleportButtonPressed = isPressed;
        }
    }

    private void UpdateControllerRollOrientation()
    {
        if (!isValidTeleportLocation)
        {
            targetRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(orientationController.forward, Vector3.up), Vector3.up);
            return;
        }

        if (!isControllerRollCalibrated)
        {
            initialControllerRotation = orientationController.rotation;
            isControllerRollCalibrated = true;
            targetRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(orientationController.forward, Vector3.up), Vector3.up);
        }
        else
        {
            // Get the current roll angle
            float currentRoll = orientationController.eulerAngles.z;
            float initialRoll = initialControllerRotation.eulerAngles.z;

            // Calculate the shortest angle difference between the initial and current roll
            float rollDifference = Mathf.DeltaAngle(initialRoll, currentRoll);

            // Apply the sensitivity multiplier to the roll difference
            float yawRotation = rollDifference * rollSensitivityMultiplier;

            // Get the initial forward direction projected onto the horizontal plane
            Vector3 initialForward = Vector3.ProjectOnPlane(initialControllerRotation * Vector3.forward, Vector3.up);
            float initialYaw = Quaternion.LookRotation(initialForward, Vector3.up).eulerAngles.y;

            // Apply the yaw rotation relative to the initial forward direction
            targetRotation = Quaternion.Euler(0, initialYaw - yawRotation, 0);
        }
    }

    // Add function to convert from cartesian to spherical coordinates
    private Vector3 CartesianToSpherical(Vector2 thumbstick)
    {
        // Calculate r (magnitude), theta (azimuth), and phi (polar angle)
        float r = thumbstick.magnitude;
        float theta = Mathf.Atan2(thumbstick.x, thumbstick.y); // azimuth angle in radians
        float phi = Mathf.PI / 2; // Keep phi constant at 90 degrees for horizontal plane

        return new Vector3(r, theta, phi);
    }

    // Function to get the normalized teleport distance based on thumbstick position
    private float GetNormalizedTeleportDistance(Vector2 thumbstick)
    {
        // When called from arc calculation methods, use the smoothed input
        if (triggerType == TeleportTriggerType.ThumbstickDistance)
        {
            thumbstick = smoothedThumbstickInput;
        }

        float magnitude = thumbstick.magnitude;

        if (magnitude <= joystickDeadzone)
        {
            return 0f;
        }

        // Add extra smoothing to the distance calculation
        float normalizedDistance = (magnitude - joystickDeadzone) / (1f - joystickDeadzone);
        return Mathf.Clamp01(normalizedDistance);
    }

    // Reset function to clear smoothing when teleport starts/ends
    private void ResetSmoothing()
    {
        smoothedThumbstickInput = Vector2.zero;
        thumbstickVelocity = Vector2.zero;
    }

    private void HandleTeleportInput()
    {
        switch (triggerType)
        {
            case TeleportTriggerType.Thumbstick:
                HandleThumbstickInput();
                break;
            case TeleportTriggerType.Button:
                HandleButtonInput();
                break;
            case TeleportTriggerType.ThumbstickDistance:
                HandleThumbstickDistanceInput();
                break;
        }
    }

    private void HandleThumbstickDistanceInput()
    {
        // Smooth the thumbstick input using SmoothDamp
        smoothedThumbstickInput = Vector2.SmoothDamp(
            smoothedThumbstickInput,
            currentJoystickInput,
            ref thumbstickVelocity,
            thumbstickSmoothTime
        );

        float magnitude = smoothedThumbstickInput.magnitude;

        if (magnitude > joystickDeadzone && !isTeleportActive)
        {
            StartTeleport();
            Vector3 spherical = CartesianToSpherical(smoothedThumbstickInput);
            lastValidThumbstickDirection = new Vector2(Mathf.Cos(spherical.y), Mathf.Sin(spherical.y));
        }
        else if (isTeleportActive)
        {
            if (magnitude > joystickDeadzone)
            {
                Vector3 spherical = CartesianToSpherical(smoothedThumbstickInput);
                lastValidThumbstickDirection = new Vector2(Mathf.Cos(spherical.y), Mathf.Sin(spherical.y));
            }
        }
    }

    private void HandleThumbstickInput()
    {
        float magnitude = currentJoystickInput.magnitude;

        if (magnitude > joystickDeadzone && !isTeleportActive)
        {
            StartTeleport();
            lastValidThumbstickDirection = currentJoystickInput.normalized;
        }
        else if (isTeleportActive)
        {
            if (magnitude > joystickDeadzone)
            {
                lastValidThumbstickDirection = currentJoystickInput.normalized;
            }
            else if (magnitude <= joystickDeadzone)
            {
                ExecuteTeleport();
            }
        }
    }

    private void HandleButtonInput()
    {
        if (teleportButtonPressed && !isTeleportActive)
        {
            StartTeleport();
        }
        else if (!teleportButtonPressed && isTeleportActive)
        {
            ExecuteTeleport();
        }
    }

    private void UpdateArcVisualization()
    {
        Vector3[] points = new Vector3[lineSegments];
        bool hitFound = false;

        switch (arcType)
        {
            case ArcType.Bezier:
                hitFound = CalculateBezierPoints(points);
                break;
            case ArcType.Parabolic:
                hitFound = CalculateParabolicPoints(points);
                break;
            case ArcType.TallRayCast:
                hitFound = CalculateTallRaycastPoints(points);
                break;
        }

        lineRenderer.SetPositions(points);
        lineRenderer.material = hitFound ? validTeleportMaterial : invalidTeleportMaterial;
        isValidTeleportLocation = hitFound;
    }

    private bool CalculateBezierPoints(Vector3[] points)
    {
        Vector3 start = orientationController.position;
        Vector3 forward = orientationController.forward;
        Vector3 up = transform.up;

        // Calculate target distance based on trigger type and input
        float targetDistance = maxDistance;
        if (triggerType == TeleportTriggerType.ThumbstickDistance)
        {
            targetDistance = maxDistance * GetNormalizedTeleportDistance(currentJoystickInput);
        }

        Vector3 end = start + forward * targetDistance + Vector3.down * 2f;
        Vector3 control = start + (end - start) * 0.5f + up * 2f;

        bool hitFound = false;
        RaycastHit hit;

        for (int i = 0; i < lineSegments; i++)
        {
            float t = i / (float)(lineSegments - 1);
            Vector3 point = SampleBezierCurve(start, end, control, t);
            points[i] = point;

            if (!hitFound && i > 0)
            {
                if (Physics.Raycast(points[i - 1], points[i] - points[i - 1], out hit,
                    Vector3.Distance(points[i], points[i - 1])))
                {
                    if (hit.collider.CompareTag(teleportableTag))
                    {
                        teleportTarget = hit.point;
                        hitFound = true;
                    }
                    else
                    {
                        hitFound = false;
                    }

                    for (int j = i; j < lineSegments; j++)
                    {
                        points[j] = hit.point;
                    }
                    break;
                }
            }
        }

        return hitFound;
    }

    private Vector3 SampleBezierCurve(Vector3 start, Vector3 end, Vector3 control, float t)
    {
        Vector3 q0 = Vector3.Lerp(start, control, t);
        Vector3 q1 = Vector3.Lerp(control, end, t);
        return Vector3.Lerp(q0, q1, t);
    }

    private bool CalculateParabolicPoints(Vector3[] points)
    {
        // Calculate initial velocity based on trigger type and input
        float currentVelocity = initialVelocity;
        if (triggerType == TeleportTriggerType.ThumbstickDistance)
        {
            currentVelocity = initialVelocity * GetNormalizedTeleportDistance(currentJoystickInput);
        }

        Vector3 velocity = orientationController.forward * currentVelocity;
        Vector3 position = orientationController.position;
        float timeStep = 0.1f;
        bool hitFound = false;
        RaycastHit hit;

        for (int i = 0; i < lineSegments; i++)
        {
            float time = timeStep * i;
            Vector3 point = position + velocity * time +
                0.5f * Physics.gravity * time * time;
            points[i] = point;

            if (!hitFound && i > 0)
            {
                if (Physics.Raycast(points[i - 1], points[i] - points[i - 1], out hit,
                    Vector3.Distance(points[i], points[i - 1])))
                {
                    if (hit.collider.CompareTag(teleportableTag))
                    {
                        teleportTarget = hit.point;
                        hitFound = true;
                    }
                    else
                    {
                        hitFound = false;
                    }

                    for (int j = i; j < lineSegments; j++)
                    {
                        points[j] = hit.point;
                    }
                    break;
                }
            }
        }

        return hitFound;
    }

    private bool CalculateTallRaycastPoints(Vector3[] points)
    {
        Vector3 horizontalDir = Vector3.ProjectOnPlane(orientationController.forward, Vector3.up).normalized;
        float controllerAngle = Vector3.Angle(Vector3.down, orientationController.forward);
        float normalizedAngle = Mathf.Clamp01((controllerAngle - minControllerAngle) /
            (maxControllerAngle - minControllerAngle));

        // Calculate distance based on trigger type and input
        float distance = maxDistance * normalizedAngle;
        if (triggerType == TeleportTriggerType.ThumbstickDistance)
        {
            distance *= GetNormalizedTeleportDistance(currentJoystickInput);
        }

        Vector3 rayStart = xrCamera.position + Vector3.up * 5f;
        Vector3 targetPoint = xrCamera.position + horizontalDir * distance;

        RaycastHit hit;
        bool hitFound = false;

        if (Physics.Raycast(rayStart, (targetPoint - rayStart).normalized, out hit))
        {
            if (hit.collider.CompareTag(teleportableTag))
            {
                teleportTarget = hit.point;
                hitFound = true;
            }
            targetPoint = hit.point;
        }

        Vector3 start = orientationController.position;
        Vector3 end = targetPoint;
        Vector3 control = start + (end - start) * 0.5f + Vector3.up * 2f;

        for (int i = 0; i < lineSegments; i++)
        {
            float t = i / (float)(lineSegments - 1);
            points[i] = SampleBezierCurve(start, end, control, t);
        }

        return hitFound;
    }

    private void UpdateDirectionIndicator()
    {
        if (!isValidTeleportLocation)
        {
            directionIndicator.SetActive(false);
            return;
        }

        directionIndicator.SetActive(true);
        directionIndicator.transform.position = teleportTarget;

        // Show/hide direction arrow based on orientation type
        if (directionArrow != null)
        {
            directionArrow.gameObject.SetActive(orientationType != LandingOrientation.None);
        }

        // Calculate rotation based on orientation type
        switch (orientationType)
        {
            case LandingOrientation.None:
                // No rotation adjustment needed
                break;
            case LandingOrientation.Camera:
                targetRotation = Quaternion.Euler(0, xrCamera.eulerAngles.y, 0);
                break;
            case LandingOrientation.Controller:
                targetRotation = Quaternion.Euler(0, orientationController.eulerAngles.y, 0);
                break;
            case LandingOrientation.ControllerRoll:
                UpdateControllerRollOrientation();
                break;
            case LandingOrientation.Joystick:
                UpdateJoystickOrientation();
                break;
            case LandingOrientation.Body:
                if (bodyReference != null)
                {
                    targetRotation = Quaternion.Euler(0, bodyReference.transform.eulerAngles.y, 0);
                }
                break;
        }

        if (orientationType != LandingOrientation.None)
        {
            directionIndicator.transform.rotation = targetRotation;
        }
    }

    private void UpdateJoystickOrientation()
    {
        if (triggerType == TeleportTriggerType.Thumbstick)
        {
            float angle = Mathf.Atan2(lastValidThumbstickDirection.x, lastValidThumbstickDirection.y) * Mathf.Rad2Deg;
            targetRotation = Quaternion.Euler(0, angle, 0);
        }
        else
        {
            Vector2 thumbstick = currentJoystickInput;
            if (thumbstick.magnitude > joystickDeadzone)
            {
                float angle = Mathf.Atan2(thumbstick.x, thumbstick.y) * Mathf.Rad2Deg;
                targetRotation = Quaternion.Euler(0, angle, 0);
            }
        }
    }

    private IEnumerator DashTeleport(Vector3 targetPosition, Quaternion targetRotation)
    {
        Vector3 startPosition = transform.position;
        Quaternion startRotation = cameraOffset.rotation;
        float elapsedTime = 0f;
        float duration = dashDuration / 1000f; // Convert milliseconds to seconds

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration;

            transform.position = Vector3.Lerp(startPosition, targetPosition, t);

            if (orientationType != LandingOrientation.None)
            {
                cameraOffset.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            }

            yield return null;
        }

        transform.position = targetPosition;
        if (orientationType != LandingOrientation.None)
        {
            cameraOffset.rotation = targetRotation;
        }
    }

    private IEnumerator DashTeleportArc(Vector3 targetPosition, Quaternion targetRotation)
    {
        Vector3[] arcPoints = new Vector3[lineSegments];
        Vector3 startPosition = transform.position;
        bool validArc = false;

        // Calculate arc points based on current arc type
        switch (arcType)
        {
            case ArcType.Bezier:
                validArc = CalculateBezierPoints(arcPoints);
                break;
            case ArcType.Parabolic:
                validArc = CalculateParabolicPoints(arcPoints);
                break;
            case ArcType.TallRayCast:
                validArc = CalculateTallRaycastPoints(arcPoints);
                break;
        }

        if (!validArc)
        {
            yield break;
        }

        // Calculate the height-adjusted target position exactly as used in ExecuteTeleport
        Vector3 heightAdjustedTarget = new Vector3(
            teleportTarget.x,
            transform.position.y,
            teleportTarget.z
        );

        // Calculate the target camera height
        float targetCameraHeight = xrCamera.position.y - transform.position.y;

        // Adjust arc points to maintain minimum camera height and prevent negative XR Origin position
        for (int i = 0; i < arcPoints.Length; i++)
        {
            // Force the XR Origin position to never go below 0
            float minRigHeight = 0f;

            // Calculate what the camera height would be at this point
            float potentialCameraHeight = arcPoints[i].y + targetCameraHeight;

            // Calculate the required rig position to maintain minimum camera height
            // while ensuring the rig never goes below 0
            float requiredRigHeight = Mathf.Max(
                minRigHeight,  // Never go below 0
                arcPoints[i].y,  // Original height
                potentialCameraHeight - targetCameraHeight  // Height needed to maintain camera height
            );

            // Apply the adjusted height while maintaining X and Z coordinates
            arcPoints[i] = new Vector3(arcPoints[i].x, requiredRigHeight, arcPoints[i].z);
        }

        // Adjust the final point to exactly match the height-adjusted target
        arcPoints[arcPoints.Length - 1] = heightAdjustedTarget;

        Quaternion startRotation = cameraOffset.rotation;
        float elapsedTime = 0f;
        float duration = dashDuration / 1000f; // Convert milliseconds to seconds

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration;

            // Find the appropriate point along the arc
            int index = Mathf.FloorToInt(t * (arcPoints.Length - 1));
            float subT = (t * (arcPoints.Length - 1)) - index;

            Vector3 currentPosition;
            if (index >= arcPoints.Length - 1)
            {
                currentPosition = heightAdjustedTarget;
            }
            else
            {
                currentPosition = Vector3.Lerp(arcPoints[index], arcPoints[index + 1], subT);
            }

            // Ensure the Y position never goes negative
            currentPosition.y = Mathf.Max(0f, currentPosition.y);

            transform.position = currentPosition;

            if (orientationType != LandingOrientation.None)
            {
                cameraOffset.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            }

            yield return null;
        }

        // Set the final position using the same height-adjusted target
        transform.position = heightAdjustedTarget;

        if (orientationType != LandingOrientation.None)
        {
            cameraOffset.rotation = targetRotation;
        }
    }

    private void StartTeleport()
    {
        isTeleportActive = true;
        lineRenderer.enabled = true;
        isControllerRollCalibrated = false;

        if (triggerType == TeleportTriggerType.ThumbstickDistance)
        {
            ResetSmoothing();
        }
    }

    private void CancelTeleport()
    {
        isTeleportActive = false;
        lineRenderer.enabled = false;
        directionIndicator.SetActive(false);
        isControllerRollCalibrated = false;

        if (triggerType == TeleportTriggerType.ThumbstickDistance)
        {
            ResetSmoothing();
        }
    }

    private void ExecuteTeleport()
    {
        if (!isValidTeleportLocation)
        {
            CancelTeleport();
            return;
        }

        Vector3 heightAdjustedTarget = new Vector3(
            teleportTarget.x,
            transform.position.y,
            teleportTarget.z
        );

        switch (teleportType)
        {
            case TeleportType.InstantTeleport:
                transform.position = heightAdjustedTarget;
                if (orientationType != LandingOrientation.None)
                {
                    cameraOffset.rotation = targetRotation;
                }
                break;

            case TeleportType.DashTeleport:
                StartCoroutine(DashTeleport(heightAdjustedTarget, targetRotation));
                break;

            case TeleportType.DashTeleportArc:
                StartCoroutine(DashTeleportArc(heightAdjustedTarget, targetRotation));
                break;
        }

        CancelTeleport();
    }
}
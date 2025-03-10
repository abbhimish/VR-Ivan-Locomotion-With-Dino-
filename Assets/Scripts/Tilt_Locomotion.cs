using UnityEngine;

/// <summary>
/// Implements locomotion control based on tilt angles.
/// This script enables movement in VR through tilting, providing an alternative
/// to position-based locomotion while maintaining directional control.
/// </summary>
public class Tilt_Locomotion : MonoBehaviour
{
    public enum TrackingReference
    {
        CameraBased,
        BodyBased
    }

    #region Public Fields
    [Header("Tracking Settings")]
    [Tooltip("Choose whether to use HMD or a body tracker for tilt reference")]
    public TrackingReference trackingReference = TrackingReference.CameraBased;

    [Tooltip("Reference transform for body tracking (only used when BodyBased is selected)")]
    public Transform bodyTracker;
    
    [Header("Configuration")]
    [Tooltip("Distance offset from the tracked headset position to the center of rotation, in meters")]
    [Min(0f)]
    [SerializeField] private float yawRotationAxisOffset = 0.15f;

    [Tooltip("Maximum movement speed in meters per second")]
    [SerializeField] private float maxSpeed = 1.5f;

    [Tooltip("The maximum tilt angle that results in maximum speed")]
    [Range(0f, 45f)]
    public float maxTiltAngle = 30f;

    [Tooltip("Power of the exponential transfer function - controls how quickly speed ramps up with tilt angle")]
    [Range(1f, 2f)]
    [SerializeField] float exponentialTransferFunctionPower = 1.53f;

    [Tooltip("Sensitivity (inside the exponential function). 1 = no multiplied speed gain")]
    [Range(1f, 5f)]
    public float speedSensitivity = 1f;

    public enum DeadzoneAngleSettings { DeadzonePercentage = 0, DeadzoneAngleValue = 1 };

    [Space]
    [Header("Dead-Zone Settings")]

    [Tooltip("Deadzone Percentage = Deadzone will be based on a percentage of the maximum tilt angle. \n\n" +
        "Deadzone Angle Value = Deadzone will be based on a specific angle in degrees.")]
    public DeadzoneAngleSettings _deadzoneAngleSetting = DeadzoneAngleSettings.DeadzonePercentage;

    [Tooltip("Tilt angle dead-zone in percent")]
    [Range(0f, 0.99f)]
    public float deadZonePercentage = 0.1f;

    [Tooltip("Define the minimum tilt angle which results in movement")]
    [Range(0f, 45f)]
    public float deadzoneAngle = 5f;

    [Header("Debug Visualization")]
    [Tooltip("Enable or disable the visual representation of the tilt boundaries")]
    [SerializeField] private bool showVisualBoundaries = false;
    [SerializeField] private bool showGUI = false;

    [Header("Gizmo Settings")]
    [SerializeField] private Color maxTiltColor = Color.yellow;
    [SerializeField] private Color deadzoneTiltColor = new Color(1f, 0.5f, 0f); // Orange
    [SerializeField] private float gizmoSize = 0.5f; // Size of the visualization

    #endregion

    // Core component references
    private Transform hmdTransform;                 // Reference to the VR headset transform
    private Transform cachedTransform;              // Cached reference to this object's transform
    private CharacterController characterController; // Character controller for movement
    private GameObject yawRotationAxis;             // Center of rotation reference
    private Vector3 movementDirection2D_Axis = Vector3.zero;

    // Visual representation objects
    private GameObject visualsParent;               // Parent object for visualizations
    private Material maxTiltMaterial;               // Material for tilt visualization

    // Runtime state
    private float sensitivityCoeff;                 // Calculated sensitivity coefficient
    private Vector3 currentVelocity;                // Current movement velocity
    private Quaternion calibratedRotation;          // Initial calibrated rotation
    private bool isCalibrated;                      // Calibration state

    // Constants
    private readonly Vector3 zeroVector = Vector3.zero;

    /// <summary>
    /// Validates configuration parameters and updates derived values.
    /// </summary>
    private void OnValidate()
    {
        // Add body tracker validation
        if (trackingReference == TrackingReference.BodyBased && bodyTracker == null)
        {
            Debug.LogWarning("Body tracker reference is required when using BodyBased tracking!");
        }

        DeadZoneSetup();

        // Validate deadzone angle
        if (deadzoneAngle < 0f)
        {
            Debug.LogWarning("DeadzoneAngle must be non-negative. Setting to 0.");
            deadzoneAngle = 0f;
        }

        // Validate max tilt angle
        if (maxTiltAngle < 0f)
        {
            Debug.LogWarning("MaxTiltAngle must be non-negative. Setting to 0.");
            maxTiltAngle = 0f;
        }

        // Ensure deadzone is smaller than max tilt
        if (maxTiltAngle == 0f)
        {
            deadzoneAngle = 0f;
        }
        else if (deadzoneAngle >= maxTiltAngle)
        {
            Debug.LogWarning("DeadzoneAngle must be less than MaxTiltAngle when MaxTiltAngle > 0. Adjusting DeadzoneAngle.");
            deadzoneAngle = maxTiltAngle * 0.99f;
            DeadZoneSetup();
        }

        // Only validate yawRotationAxisOffset for camera-based tracking
        if (trackingReference == TrackingReference.CameraBased && yawRotationAxisOffset < 0f)
        {
            Debug.LogWarning("YawRotationAxisOffset must be non-negative. Setting to 0.");
            yawRotationAxisOffset = 0f;
        }

        UpdateSensitivityCoefficient();

        // Update visual representations if they exist
        if (Application.isPlaying && isCalibrated)
        {
            SetVisualsActive(showVisualBoundaries);
        }
    }

    /// <summary>
    /// Sets up the deadzone based on percentage or direct angle value.
    /// </summary>
    void DeadZoneSetup()
    {
        if (_deadzoneAngleSetting == DeadzoneAngleSettings.DeadzonePercentage)
        {
            deadzoneAngle = deadZonePercentage * maxTiltAngle;
        }
        else
        {
            if (maxTiltAngle > 0)
                deadZonePercentage = deadzoneAngle / maxTiltAngle;
            else
                deadZonePercentage = 1;
        }
    }

    /// <summary>
    /// Updates the sensitivity coefficient used for velocity calculations.
    /// </summary>
    private void UpdateSensitivityCoefficient()
    {
        float denominator = maxTiltAngle - deadzoneAngle;
        sensitivityCoeff = denominator > 0f ? 1f / denominator : 0f;
    }

    /// <summary>
    /// Initializes required components and visual representations.
    /// </summary>
    private void Awake()
    {
        cachedTransform = transform;
        characterController = GetComponent<CharacterController>();
        hmdTransform = Camera.main.transform;

        // Validate references based on tracking mode
        if (trackingReference == TrackingReference.CameraBased && hmdTransform == null)
        {
            Debug.LogError("HMDTransform reference is missing for camera-based tracking!");
            enabled = false;
            return;
        }
        else if (trackingReference == TrackingReference.BodyBased && bodyTracker == null)
        {
            Debug.LogError("Body tracker reference is missing for body-based tracking!");
            enabled = false;
            return;
        }

        UpdateSensitivityCoefficient();

        if (showVisualBoundaries)
        {
            CreateVisualBoundaries();
        }
    }

    /// <summary>
    /// Handles the core locomotion update logic including calibration and movement calculations.
    /// </summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            Calibrate();
            return;
        }

        if (isCalibrated)
        {
            // Get current rotation relative to calibrated rotation
            Quaternion relativeRotation = Quaternion.Inverse(calibratedRotation) * yawRotationAxis.transform.rotation;
            Vector3 tiltAngles = relativeRotation.eulerAngles;

            // Convert angles to -180 to 180 range
            float tiltX = tiltAngles.x > 180 ? tiltAngles.x - 360 : tiltAngles.x;
            float tiltZ = tiltAngles.z > 180 ? tiltAngles.z - 360 : tiltAngles.z;

            // Calculate total tilt angle
            float totalTilt = Mathf.Sqrt(tiltX * tiltX + tiltZ * tiltZ);

            if (totalTilt >= deadzoneAngle)
            {
                float speed = CalculateSpeed(totalTilt);
                if (speed > 0f)
                {
                    UpdateMoveDirection(tiltX, tiltZ);
                    currentVelocity = movementDirection2D_Axis * speed;
                    characterController.Move(currentVelocity * Time.deltaTime);
                }
            }
            else
            {
                currentVelocity = zeroVector;
            }

            if (showVisualBoundaries)
            {
                UpdateVisualBoundaries(totalTilt);
            }
        }
    }

    /// <summary>
    /// Calibrates the system by setting up the initial rotation reference.
    /// </summary>
    private void Calibrate()
    {
        if (trackingReference == TrackingReference.CameraBased)
        {
            Vector3 hmdForward = hmdTransform.forward;
            hmdForward.y = 0f;

            if (hmdForward.sqrMagnitude > 0.001f)
            {
                hmdForward.Normalize();
                Vector3 yawRotationCenter = hmdTransform.position - (hmdForward * yawRotationAxisOffset);
                GameObject go_yawRotationCenter = new GameObject("CenterOfYawRotation");
                yawRotationAxis = Instantiate(go_yawRotationCenter, yawRotationCenter, Quaternion.identity, hmdTransform);

                calibratedRotation = yawRotationAxis.transform.rotation;
                currentVelocity = zeroVector;
                isCalibrated = true;
            }
        }
        else // BodyBased
        {
            if (bodyTracker != null)
            {
                // For body-based tracking, we'll use the body tracker directly
                yawRotationAxis = bodyTracker.gameObject;
                calibratedRotation = bodyTracker.rotation;
                currentVelocity = zeroVector;
                isCalibrated = true;
            }
        }

        if (isCalibrated && showVisualBoundaries)
        {
            SetVisualsActive(true);
        }
    }

    /// <summary>
    /// Calculates movement velocity based on tilt angle.
    /// </summary>
    private float CalculateSpeed(float tiltAngle)
    {
        if (tiltAngle < deadzoneAngle) return 0f;

        float normalizedAngle = tiltAngle - deadzoneAngle;

        if (normalizedAngle >= 1f / sensitivityCoeff) return maxSpeed;

        return maxSpeed * Mathf.Min(Mathf.Pow(sensitivityCoeff * normalizedAngle * speedSensitivity, exponentialTransferFunctionPower), 1);
    }

    /// <summary>
    /// Returns the transform being used for rotation reference.
    /// </summary>
    public Transform GetRotationReference()
    {
        return trackingReference == TrackingReference.CameraBased ? yawRotationAxis.transform : bodyTracker;
    }

    /// <summary>
    /// Updates the movement direction based on tilt angles relative to the CenterOfYawRotation's forward direction.
    /// Forward tilt results in forward movement, right tilt results in right movement.
    /// </summary>
    private void UpdateMoveDirection(float tiltX, float tiltZ)
    {
        if (yawRotationAxis == null) return;

        // Get the forward direction of the rotation center
        Vector3 forwardDir = yawRotationAxis.transform.forward;
        Vector3 rightDir = yawRotationAxis.transform.right;

        // Normalize tilt values to -1 to 1 range based on max tilt angle
        // Positive tiltX means backward tilt, so we keep it negative for forward movement
        float normalizedForwardTilt = tiltX / maxTiltAngle; // Removed the negative sign to fix forward direction
        float normalizedRightTilt = -tiltZ / maxTiltAngle; // Added negative sign to fix right direction

        // Combine the directions based on tilt
        Vector3 moveDirection = (forwardDir * normalizedForwardTilt) + (rightDir * normalizedRightTilt);
        moveDirection.y = 0; // Ensure movement stays on the ground plane

        if (moveDirection.magnitude > 0.01f)
        {
            movementDirection2D_Axis = moveDirection.normalized;
        }
    }

    /// <summary>
    /// Returns the GameObject representing the center of rotation.
    /// </summary>
    public GameObject GetYawRotationCenter()
    {
        return yawRotationAxis;
    }

    #region Visual Guides

    /// <summary>
    /// Creates visual representations for debugging tilt angles.
    /// </summary>
    private void CreateVisualBoundaries()
    {
        visualsParent = new GameObject("Tilt Visuals");
        visualsParent.transform.parent = transform;
        SetVisualsActive(false);
    }

    /// <summary>
    /// Toggles visibility of visual guides.
    /// </summary>
    private void SetVisualsActive(bool active)
    {
        if (visualsParent)
            visualsParent.SetActive(active);
    }

    /// <summary>
    /// Updates visual representations based on current tilt.
    /// </summary>
    private void UpdateVisualBoundaries(float currentTilt)
    {
        if (!showVisualBoundaries) return;

        // Update debug visualization based on tilt angle
        float tiltProgress = Mathf.Clamp01((currentTilt - deadzoneAngle) / (maxTiltAngle - deadzoneAngle));
        
        // You could add additional visual feedback here
    }

#if UNITY_EDITOR
    /// <summary>
    /// Draws debug visualization gizmos in the Unity editor.
    /// Shows tilt boundaries and current tilt direction.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !isCalibrated || yawRotationAxis == null) return;

        // Get the center position from the yaw rotation axis
        Vector3 centerPosition = yawRotationAxis.transform.position;
        
        // Draw the main axes for reference
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(centerPosition, yawRotationAxis.transform.forward * gizmoSize);
        Gizmos.color = Color.red;
        Gizmos.DrawRay(centerPosition, yawRotationAxis.transform.right * gizmoSize);

        // Draw tilt boundaries
        DrawTiltBoundaries(centerPosition);

        // Draw current tilt direction with speed-based length
        if (currentVelocity.magnitude > 0.01f)
        {
            Gizmos.color = Color.green;
            // Scale the line length based on current speed relative to max speed
            float speedRatio = currentVelocity.magnitude / maxSpeed;
            Gizmos.DrawRay(centerPosition, movementDirection2D_Axis * gizmoSize * speedRatio);
        }

        // Draw current rotation state
        Quaternion relativeRotation = Quaternion.Inverse(calibratedRotation) * yawRotationAxis.transform.rotation;
        Vector3 tiltAngles = relativeRotation.eulerAngles;
        float tiltX = tiltAngles.x > 180 ? tiltAngles.x - 360 : tiltAngles.x;
        float tiltZ = tiltAngles.z > 180 ? tiltAngles.z - 360 : tiltAngles.z;

        // Draw current tilt vector
        Vector3 tiltDirection = new Vector3(tiltZ, 0, -tiltX).normalized;
        float totalTilt = Mathf.Sqrt(tiltX * tiltX + tiltZ * tiltZ);
        if (totalTilt > 0.01f)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawRay(centerPosition, tiltDirection * (totalTilt / maxTiltAngle) * gizmoSize);
        }
    }

    /// <summary>
    /// Draws the circular boundaries representing the deadzone and maximum tilt angles.
    /// </summary>
    private void DrawTiltBoundaries(Vector3 center)
    {
        // Draw maximum tilt boundary
        Gizmos.color = maxTiltColor;
        DrawCircle(center, gizmoSize, 32);

        // Draw deadzone boundary
        Gizmos.color = deadzoneTiltColor;
        DrawCircle(center, gizmoSize * (deadzoneAngle / maxTiltAngle), 32);
    }

    /// <summary>
    /// Helper method to draw a circle in the XZ plane using Gizmos.
    /// </summary>
    private void DrawCircle(Vector3 center, float radius, int segments)
    {
        float deltaTheta = (2f * Mathf.PI) / segments;
        float theta = 0f;

        for (int i = 0; i < segments; i++)
        {
            float x1 = radius * Mathf.Cos(theta);
            float z1 = radius * Mathf.Sin(theta);
            float x2 = radius * Mathf.Cos(theta + deltaTheta);
            float z2 = radius * Mathf.Sin(theta + deltaTheta);

            Vector3 pos1 = center + new Vector3(x1, 0f, z1);
            Vector3 pos2 = center + new Vector3(x2, 0f, z2);

            Gizmos.DrawLine(pos1, pos2);

            theta += deltaTheta;
        }
    }

    private void OnGUI()
    {
        if (showGUI && isCalibrated)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 24;
            style.normal.textColor = Color.green;

            Transform referenceTransform = GetRotationReference();
            if (referenceTransform == null) return;

            // Get the relative rotation from calibrated position
            Quaternion relativeRotation = Quaternion.Inverse(calibratedRotation) * referenceTransform.rotation;
            Vector3 angles = relativeRotation.eulerAngles;
            
            // Convert angles to -180 to 180 range for clearer debugging
            Vector3 relativeDegrees = new Vector3(
                angles.x > 180 ? angles.x - 360 : angles.x,
                angles.y > 180 ? angles.y - 360 : angles.y,
                angles.z > 180 ? angles.z - 360 : angles.z
            );

            string info = $"Tracking Mode: {trackingReference}\n" +
                         $"Current Rotation: {referenceTransform.eulerAngles:F2}\n" +
                         $"Calibrated Rotation: {calibratedRotation.eulerAngles:F2}\n" +
                         $"Relative Rotation: {relativeDegrees:F2}\n" +
                         $"Forward Dir: {referenceTransform.forward:F2}\n" +
                         $"Move Direction: {movementDirection2D_Axis:F2}\n" +
                         $"Velocity: {currentVelocity:F2} m/s\n" +
                         $"Speed: {currentVelocity.magnitude:F2} m/s\n" +
                         $"Speed Ratio: {(currentVelocity.magnitude / maxSpeed):P1}";

            GUI.Label(new Rect(10, 40, 500, 200), info, style);
        }
    }
#endif
    #endregion
}
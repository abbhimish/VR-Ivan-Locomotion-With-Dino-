using UnityEngine;
using UnityEngine.InputSystem;

public enum DeadzoneRadiusSettings { DeadzonePercentage = 0, DeadzoneRadiusValue = 1 };
public enum CalibrationType { KeyboardOnly, ControllerOnly, Both}
public enum BreakType { None, Instant, Gradual, Controlled }

/// <summary>
/// Implements locomotion control for leaning while standing (NaviBoard) and while sitting (NaviChair).
/// This script enables natural movement in VR through body leaning or stepping,
/// Providing vestibular and proprioceptive feedback while maintaining full rotational control.
/// </summary>

public class Leaning_Locomotion : MonoBehaviour
{
    #region Public Fields

    [Tooltip("Reference tracker object for TrackerReferencedLeaning mode")]
    public GameObject referenceTracker;

    [Header("Calibration Settings")]

    [Tooltip("Choose how calibration can be triggered")]
    public CalibrationType calibrationType = CalibrationType.Both;

    [SerializeField]
    [Tooltip("Input Action Reference for calibration button")]
    private InputActionReference calibrateButton;
        
    [Header("Breaking System")]
    [Tooltip("Choose the type of breaking system to use")]
    public BreakType breakType = BreakType.None;

    [SerializeField]
    [Tooltip("Input Action Reference for break button")]
    private InputActionReference breakButton;

    [SerializeField]
    [Tooltip("Input Action Reference for break trigger")]
    private InputActionReference breakTrigger;

    [Tooltip("Percentage of speed reduction when breaking (0-1)")]
    [Range(0f, 1f)]
    public float breakStrength = 0.5f;

    [Tooltip("Time in seconds to reach full break strength when using gradual break")]
    public float gradualBreakTime = 0.5f;

    [Tooltip("Maximum movement speed in meters per second")]
    [SerializeField] private float maxSpeed = 1.5f;

    [Header("Leaning Settings")]
    [Tooltip("Optional scriptable object containing leaning settings. If not set, will use inspector values below.")]
    public LeaningSettings leaningSettings;

    [Header("Configuration")]
    [Tooltip("Distance offset from the tracked headset position to the center of the person's head, in meters.")]
    [Min(0f)]
    [SerializeField] private float yawRotationAxisOffset = 0.15f;

    [Tooltip("The distance (radius), in meters, from the center which results in maximum axis deviation (max speed).")]
    [Range(0f, 0.6f)]
    public float maxLeaningRadius = 0.4f;

    [Tooltip("Power of the exponential transfer function - controls how quickly speed ramps up with lean angle")]
    [Range(1f, 2f)]
    [SerializeField] float exponentialTransferFunctionPower = 1.53f;

    [Tooltip("Sensitivity (inside the exponential function). 1 = no multiplied speed gain")]
    [Range(1f, 5f)]
    public float speedSensitivity = 1f;    

    //[Space]
    //[Header("Dead-Zone Settings")]

    //[Tooltip("Deadzone Percentage = Deadzone radius will be based on a percentage of the maximum leaning radius. \n\n" +
        //"Deadzone Radius Value = Deadzone radius will be based on a radius value in Meters.")]

    [Space]
    [Header("Dead-Zone Settings")]
    [Tooltip("Choose how to define deadzone sizes")]
    public DeadzoneRadiusSettings _deadzoneRadiusSetting = DeadzoneRadiusSettings.DeadzonePercentage;

    [Tooltip("Leaning forward dead-zone in percent.")]
    [Range(0f, 0.99f)]
    public float deadZonePercentage = 0.1f;

    [Tooltip("Define the minimum distance (radius), in meters, from the center which results in the minimum axis deviation (idle/dead-zone)")]
    [Range(0f, 0.6f)]
    public float deadzoneRadius = 0.1f;    

    [Header("Debug Visualization")]
    [Tooltip("Enable or disable the visual representation of the locomotion boundaries")]
    [SerializeField] private bool showVisualBoundaries = false;
    [SerializeField] private bool showGUI = false;

    #endregion


    #region Private Fields

    private Quaternion initialForwardRotation;
    private float currentYawRotation;

    private bool breakButtonPressed;
    private bool calibrateButtonPressed;
    private float triggerValue;
    private float currentBreakMultiplier = 1f;

    // Core component references
    private Transform hmdTransform;                 // Reference to the VR headset transform
    private Transform cachedTransform;              // Cached reference to this object's transform
    private CharacterController characterController; // Character controller for movement
    private GameObject yawRotationAxis = null;      // Visual representation of rotation axis
    private Vector3 movementDirection2D_Axis = Vector3.zero;
    private Vector3 initialTrackerDistance; // For TrackerReferencedLeaning
    private bool isBreaking = false;

    // Visual representation objects
    private GameObject visualsParent;               // Holds all of the visual objects
    private GameObject calibratedPositionSphere;    // Sphere showing calibrated position
    private GameObject idleZoneCylinder;            // Cylinder showing idle/deadzone
    private GameObject maxLeaningCylinder;          // Cylinder showing max leaning zone
    private Material maxLeaningMaterial;            // Material for max leaning visualization
    private GameObject directionalBoundariesParent; // Helper field for visualization


    // Runtime state
    private float sensitivityCoeff;                 // Calculated sensitivity coefficient
    private Vector3 currentVelocity;                // Current movement velocity
    private Vector3 calibratedLocalPosition;        // Stored calibration position
    private bool isCalibrated;                      // Whether system is calibrated

    // Constants
    private readonly Vector3 groundPlaneNormal = Vector3.up;
    private readonly Vector3 zeroVector = Vector3.zero;

    #endregion

    private void OnEnable()
    {
        EnableInputActions();
    }

    private void OnDisable()
    {
        DisableInputActions();
    }

    private void EnableInputActions()
    {
        if (breakButton != null)
        {
            breakButton.action.Enable();
            breakButton.action.performed += OnBreakButtonPerformed;
            breakButton.action.canceled += OnBreakButtonPerformed;
        }

        if (breakTrigger != null)
        {
            breakTrigger.action.Enable();
            breakTrigger.action.performed += OnBreakTriggerPerformed;
            breakTrigger.action.canceled += OnBreakTriggerPerformed;
        }

        if (calibrateButton != null)
        {
            calibrateButton.action.Enable();
            calibrateButton.action.performed += OnCalibrateButtonPerformed;
            calibrateButton.action.canceled += OnCalibrateButtonPerformed;
        }
    }

    private void DisableInputActions()
    {
        if (breakButton != null)
        {
            breakButton.action.performed -= OnBreakButtonPerformed;
            breakButton.action.canceled -= OnBreakButtonPerformed;
            breakButton.action.Disable();
        }

        if (breakTrigger != null)
        {
            breakTrigger.action.performed -= OnBreakTriggerPerformed;
            breakTrigger.action.canceled -= OnBreakTriggerPerformed;
            breakTrigger.action.Disable();
        }

        if (calibrateButton != null)
        {
            calibrateButton.action.performed -= OnCalibrateButtonPerformed;
            calibrateButton.action.canceled -= OnCalibrateButtonPerformed;
            calibrateButton.action.Disable();
        }
    }

    private void OnBreakButtonPerformed(InputAction.CallbackContext context)
    {
        breakButtonPressed = context.ReadValueAsButton();
    }

    private void OnBreakTriggerPerformed(InputAction.CallbackContext context)
    {
        triggerValue = context.ReadValue<float>();
    }

    private void OnCalibrateButtonPerformed(InputAction.CallbackContext context)
    {
        calibrateButtonPressed = context.ReadValueAsButton();
        if (calibrateButtonPressed && (calibrationType == CalibrationType.ControllerOnly || calibrationType == CalibrationType.Both))
        {
            Calibrate();
        }
    }

    /// <summary>
    /// Validates configuration parameters and updates derived values.
    /// </summary>
    private void OnValidate()
    {
        // Original validation for other leaning types
        DeadZoneSetup();
        
        UpdateSensitivityCoefficient();

        if (Application.isPlaying && isCalibrated)
        {
            SetVisualsActive(showVisualBoundaries);

            if (showVisualBoundaries)
                UpdateVisualBoundaries();
        }
    }

    // Sets the deadzone size, based on deadzonePercentage or updates percentage size based on direct radius size
    void DeadZoneSetup()
    {
        // Sets the deadzone radius based on the percentage set by the user in the inspector
        if (_deadzoneRadiusSetting == DeadzoneRadiusSettings.DeadzonePercentage)
        {
            deadzoneRadius = deadZonePercentage * maxLeaningRadius;
        }

        // Updates the deadzone percentage in the inspector if setting the radius size directly
        else
        {
            if (maxLeaningRadius > 0)
                deadZonePercentage = deadzoneRadius / maxLeaningRadius;
            else
                deadZonePercentage = 1;
        }
    }    

    /// <summary>
    /// Calculates the sensitivity coefficient used for velocity calculations.
    /// This determines how quickly movement speed increases with lean angle.
    /// </summary>
    private void UpdateSensitivityCoefficient()
    {       
        // Original sensitivity calculation for other leaning types
        float denominator = maxLeaningRadius - deadzoneRadius;
        sensitivityCoeff = denominator > 0f ? 1f / denominator : 0f;
    }

    /// <summary>
    /// Initializes required components and visual representations.
    /// </summary>
    private void Awake()
    {
        // Cache component references
        cachedTransform = transform;
        characterController = GetComponent<CharacterController>();
        hmdTransform = Camera.main.transform;

        if (hmdTransform == null)
        {
            Debug.LogError("HMDTransform reference is missing!");
            enabled = false;
            return;
        }

        UpdateSensitivityCoefficient();

        if (showVisualBoundaries)
        {
            CreateVisualBoundaries();
        }
    }

    private void Start()
    {
        if (leaningSettings != null)
        {
            // Load common settings
            calibrationType = leaningSettings.calibrationType;
            breakType = leaningSettings.breakType;
            breakStrength = leaningSettings.breakStrength;
            gradualBreakTime = leaningSettings.gradualBreakTime;
            maxSpeed = leaningSettings.maxSpeed;
            yawRotationAxisOffset = leaningSettings.yawRotationAxisOffset;
            exponentialTransferFunctionPower = leaningSettings.exponentialTransferFunctionPower;
            speedSensitivity = leaningSettings.speedSensitivity;
                       
            // Load standard settings
            maxLeaningRadius = leaningSettings.maxLeaningRadius;
            _deadzoneRadiusSetting = leaningSettings.deadzoneRadiusSetting;
            deadZonePercentage = leaningSettings.deadZonePercentage;
            deadzoneRadius = leaningSettings.deadzoneRadius;            
        }

        DeadZoneSetup();                
    }

    /// <summary>
    /// Handles the core locomotion update logic including calibration,
    /// movement calculations, and visual boundary updates.
    /// </summary>
    private void Update()
    {
        if (!isCalibrated) return;

        // Update break system
        UpdateBreakSystem();

        // Calculate movement based on selected leaning type
        Vector3 relativePosition;

        relativePosition = CalculateNaviBoardPosition();
        ProcessMovement(relativePosition);

        // Update visual representations if showing visual boundaries
        if (showVisualBoundaries)
            UpdateVisualBoundaries();
    }    

    private void UpdateBreakSystem()
    {
        switch (breakType)
        {
            case BreakType.None:
                currentBreakMultiplier = 1f;
                break;

            case BreakType.Instant:
                currentBreakMultiplier = breakButtonPressed ? (1f - breakStrength) : 1f;
                break;

            case BreakType.Gradual:
                float breakSpeed = breakStrength / gradualBreakTime;
                if (breakButtonPressed && currentBreakMultiplier > (1f - breakStrength))
                {
                    currentBreakMultiplier -= breakSpeed * Time.deltaTime;
                    currentBreakMultiplier = Mathf.Max(currentBreakMultiplier, 1f - breakStrength);
                }
                else if (!breakButtonPressed && currentBreakMultiplier < 1f)
                {
                    currentBreakMultiplier += breakSpeed * Time.deltaTime;
                    currentBreakMultiplier = Mathf.Min(currentBreakMultiplier, 1f);
                }
                break;

            case BreakType.Controlled:
                currentBreakMultiplier = 1f - (triggerValue * breakStrength);
                break;
        }
    }

    private Vector3 CalculateNaviBoardPosition()
    {
        Vector3 calibratedPosition = cachedTransform.TransformPoint(calibratedLocalPosition);
        Vector3 currentHMDPositionWithOffset = GetYawRotationCenter().transform.position;
        return currentHMDPositionWithOffset - calibratedPosition;
    }

    private Vector3 CalculateTrackerReferencedPosition()
    {
        if (referenceTracker == null) return Vector3.zero;

        Vector3 currentHMDPositionWithOffset = GetYawRotationCenter().transform.position;
        Vector3 currentTrackerDistance = currentHMDPositionWithOffset - referenceTracker.transform.position;
        return currentTrackerDistance - initialTrackerDistance;
    }    

    private void ProcessMovement(Vector3 relativePosition)
    {        
        float rHeadSqr = relativePosition.sqrMagnitude;
        if (rHeadSqr > 0.0001f)
        {
            float rHead = Mathf.Sqrt(rHeadSqr);
            float theta = Mathf.Acos(relativePosition.y / rHead);
            float rGround = rHead * Mathf.Sin(theta);

            if (rGround >= deadzoneRadius)
            {
                float speed = CalculateVelocity(rGround);
                if (speed > 0f)
                {
                    UpdateMoveDirection(relativePosition);
                    currentVelocity = movementDirection2D_Axis * speed * currentBreakMultiplier;
                    characterController.Move(currentVelocity * Time.deltaTime);
                }
            }
        }

        else
        {
            currentVelocity = Vector3.zero;
        }        
    }    

    /// <summary>
    /// Calibrates the system by setting up the yaw rotation center and initial position.
    /// This should be called when the user is in their neutral/starting position.
    /// </summary>
    private void Calibrate()
    {
        // If there's an existing yaw rotation axis, destroy it
        if (yawRotationAxis != null)
        {
            Destroy(yawRotationAxis);
            yawRotationAxis = null;
        }

        // Determine which transform to use for calibration
        Transform calibrationTransform = hmdTransform;

        Vector3 calibrationForward = calibrationTransform.forward;
        calibrationForward.y = 0f;

        if (calibrationForward.sqrMagnitude > 0.001f)
        {
            calibrationForward.Normalize();
            Vector3 yawRotationCenter = calibrationTransform.position - (calibrationForward * yawRotationAxisOffset);
            GameObject go_yawRotationCenter = new GameObject("CenterOfYawRotation");
            yawRotationAxis = Instantiate(go_yawRotationCenter, yawRotationCenter, Quaternion.identity, calibrationTransform);
            Destroy(go_yawRotationCenter); // Clean up the template object

            calibratedLocalPosition = cachedTransform.InverseTransformPoint(GetYawRotationCenter().transform.position);

            currentVelocity = Vector3.zero;
            isCalibrated = true;

            if (showVisualBoundaries)
            {
                SetVisualsActive(true);
                UpdateVisualBoundaries();
            }
        }
    }

    private void DisableLocomotion()
    {
        if (yawRotationAxis != null)
        {
            Destroy(yawRotationAxis);
            yawRotationAxis = null;
        }

        isCalibrated = false;
        currentVelocity = Vector3.zero;
        SetVisualsActive(false);
    }    


    /// <summary>
    /// Calculates movement velocity based on ground distance from center.
    /// Uses an exponential transfer function to provide smooth acceleration.
    /// </summary>
    private float CalculateVelocity(float rGround)
    {
        // If in the deadZone, return 0 (do not move)
        if (rGround < deadzoneRadius) return 0f;

        float normalizedDistance = rGround - deadzoneRadius;

        // If in the greater than max leaning radius, return 1 (to use max speed)
        if (normalizedDistance >= 1f / sensitivityCoeff) return maxSpeed;

        // If between the deadZone and max leaning radius, calculate speed using exponential function
        return maxSpeed * Mathf.Min(Mathf.Pow(sensitivityCoeff * normalizedDistance * speedSensitivity, exponentialTransferFunctionPower), 1);
    }

    /// <summary>
    /// Updates the movement direction based on head position input.
    /// </summary>
    private void UpdateMoveDirection(Vector3 relativePosition)
    {
        float phi = Mathf.Atan2(relativePosition.z, relativePosition.x);
        movementDirection2D_Axis = new Vector3(
            Mathf.Cos(phi),
            0f,
            Mathf.Sin(phi)
        );
    }

    /// <summary>
    /// Returns the GameObject representing the center of yaw rotation.
    /// </summary>
    public GameObject GetYawRotationCenter()
    {
        return yawRotationAxis;
    }

    // Visuals to help understand movement
    #region Visuals Guides

    /// <summary>
    /// Creates visual representations of the locomotion boundaries including
    /// the calibrated position, idle zone, and max leaning zone.
    /// </summary>
    private void CreateVisualBoundaries()
    {
        //spawn object
        visualsParent = new GameObject("Leaning Visuals Holder");
        visualsParent.transform.localPosition = Vector3.zero;
        visualsParent.transform.localRotation = Quaternion.identity;

        // Create calibrated position sphere
        calibratedPositionSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        calibratedPositionSphere.name = "CalibratedPosition";
        calibratedPositionSphere.transform.localScale = Vector3.one * 0.1f;
        calibratedPositionSphere.transform.parent = visualsParent.transform;
        calibratedPositionSphere.GetComponent<Renderer>().material.color = Color.blue;
        Destroy(calibratedPositionSphere.GetComponent<SphereCollider>());

        // Create idle zone cylinder - slightly raised to prevent z-fighting
        idleZoneCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        idleZoneCylinder.name = "IdleZone";
        idleZoneCylinder.transform.localScale = new Vector3(deadzoneRadius * 2, 0.01f, deadzoneRadius * 2);
        idleZoneCylinder.transform.position = new Vector3(0f, 0.006f, 0f); // Raise slightly to prevent z-fighting
        idleZoneCylinder.transform.parent = visualsParent.transform;
        idleZoneCylinder.GetComponent<Renderer>().material.color = new Color(1, 0.5f, 0, 0.3f);
        Destroy(idleZoneCylinder.GetComponent<CapsuleCollider>());

        // Create max leaning cylinder
        maxLeaningCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        maxLeaningCylinder.name = "MaxLeaningZone";
        maxLeaningCylinder.transform.localScale = new Vector3(maxLeaningRadius * 2, 0.01f, maxLeaningRadius * 2);
        maxLeaningCylinder.transform.parent = visualsParent.transform;
        maxLeaningMaterial = new Material(Shader.Find("Standard"));
        maxLeaningMaterial.color = new Color(1, 1, 0, 0.3f);
        maxLeaningCylinder.GetComponent<Renderer>().material = maxLeaningMaterial;
        Destroy(maxLeaningCylinder.GetComponent<CapsuleCollider>());

        // Initially hide all objects until calibration
        SetVisualsActive(false);
    }

    /// <summary>
    /// Toggles visibility of all visual boundary representations.
    /// </summary>
    private void SetVisualsActive(bool active)
    {
        // If the visual objects exist, set their parent as active or not
        if (calibratedPositionSphere && idleZoneCylinder && maxLeaningCylinder)
            visualsParent.SetActive(active);
    }

    /// <summary>
    /// Updates the position and appearance of visual boundary representations.
    /// Adjusts cylinder sizes based on current radius settings and updates
    /// the max leaning zone color based on current lean amount.
    /// </summary>
    private void UpdateVisualBoundaries()
    {
        if (!showVisualBoundaries) return;

        Vector3 centerPosition;
        centerPosition = cachedTransform.TransformPoint(calibratedLocalPosition);

        centerPosition.y = 0; // Keep visuals at floor level

        // Update calibrated position sphere
        if (calibratedPositionSphere)
            calibratedPositionSphere.transform.position = centerPosition;
        
        // Standard circular boundaries for other modes
        if (idleZoneCylinder)
        {
            idleZoneCylinder.transform.position = centerPosition + new Vector3(0f, 0.006f, 0f);
            idleZoneCylinder.transform.localScale = new Vector3(deadzoneRadius * 2, 0.01f, deadzoneRadius * 2);
        }

        if (maxLeaningCylinder)
        {
            maxLeaningCylinder.transform.position = centerPosition;
            maxLeaningCylinder.transform.localScale = new Vector3(maxLeaningRadius * 2, 0.01f, maxLeaningRadius * 2);

            // Update color based on current lean
            if (isCalibrated)
            {
                Vector3 currentHMDPositionWithOffset = GetYawRotationCenter().transform.position;
                Vector3 relativePosition = CalculateRelativePosition(currentHMDPositionWithOffset, centerPosition);
                float rGround = new Vector2(relativePosition.x, relativePosition.z).magnitude;

                float leanProgress = Mathf.Clamp01((rGround - deadzoneRadius) / (maxLeaningRadius - deadzoneRadius));
                Color gradientColor = Color.Lerp(new Color(1, 1, 0, 0.3f), new Color(0, 1, 0, 0.3f), leanProgress);
                maxLeaningMaterial.color = gradientColor;
            }
        }        
    }

    /// <summary>
    /// Calculates the relative position between two points, typically used for
    /// determining the offset between the current head position and a reference point.
    /// </summary>
    /// <param name="currentPosition">Current position (usually head/HMD position)</param>
    /// <param name="referencePosition">Reference position to calculate relative to</param>
    /// <returns>Vector3 representing the relative position</returns>
    private Vector3 CalculateRelativePosition(Vector3 currentPosition, Vector3 referencePosition)
    {
        // Get the raw relative position
        Vector3 relativePosition = currentPosition - referencePosition;

        // Project onto ground plane if needed
        if (groundPlaneNormal != Vector3.up)
        {
            // Project the relative position onto the ground plane
            relativePosition = Vector3.ProjectOnPlane(relativePosition, groundPlaneNormal);
        }

        return relativePosition;
    }


#if UNITY_EDITOR
    /// <summary>
    /// Draws debug visualization gizmos in the Unity editor.
    /// Shows boundaries, head position, and debug lines for movement visualization.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !isCalibrated || !cachedTransform) return;

        Vector3 centerPosition;
        centerPosition = cachedTransform.TransformPoint(calibratedLocalPosition);

        Gizmos.matrix = Matrix4x4.TRS(centerPosition, Quaternion.identity, new Vector3(1f, 0.01f, 1f));

        // Draw boundaries
        if (maxLeaningRadius > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(zeroVector, maxLeaningRadius);
        }

        if (deadzoneRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(zeroVector, deadzoneRadius);
        }

        if (hmdTransform != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Vector3 headPos = GetYawRotationCenter().transform.position;
            Vector3 groundProjection = new Vector3(headPos.x, centerPosition.y, headPos.z);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(headPos, 0.02f);

            // Draw debug lines
            Gizmos.color = Color.green;
            Gizmos.DrawLine(centerPosition, groundProjection);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(groundProjection, headPos);
        }
    }

    // Helper method to draw dashed lines in the editor
    private void DrawDashedLine(Vector3 start, Vector3 end, float dashLength, Color color)
    {
        Vector3 direction = end - start;
        float distance = direction.magnitude;
        direction.Normalize();

        float drawnLength = 0f;
        bool drawLine = true;

        while (drawnLength < distance)
        {
            float remainingDistance = distance - drawnLength;
            float currentDashLength = Mathf.Min(dashLength, remainingDistance);

            if (drawLine)
            {
                Gizmos.color = color;
                Gizmos.DrawLine(
                    start + direction * drawnLength,
                    start + direction * (drawnLength + currentDashLength)
                );
            }

            drawnLength += currentDashLength;
            drawLine = !drawLine;
        }
    }

    private void OnGUI()
    {
        if (showGUI)
        {
            // Display calibration status
            GUIStyle style = new GUIStyle();
            style.fontSize = 24;
            style.normal.textColor = isCalibrated ? Color.green : Color.yellow;

            if (isCalibrated)
            {
                // Display distance from calibrated position
                Vector3 calibratedPosition = cachedTransform.TransformPoint(calibratedLocalPosition);
                calibratedPosition.y = 0;

                Vector3 currentHMDPositionWithOffset = GetYawRotationCenter().transform.position;
                currentHMDPositionWithOffset.y = 0;

                Vector3 distance = currentHMDPositionWithOffset - calibratedPosition;

                string info = $"Distance vector from calibrated posture: {distance:F2}m \n" +
                    $"Distance: {distance.magnitude:F2}m \n" +
                    $"Velocity: {currentVelocity:F2} m/s \n" +
                    $"Speed: {currentVelocity.magnitude:F2} m/s";

                GUI.Label(new Rect(10, 40, 500, 200), info, style);            
            }
        }
    }
#endif
    #endregion
}
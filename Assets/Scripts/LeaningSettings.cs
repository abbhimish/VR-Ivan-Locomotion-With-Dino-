using UnityEngine;

[CreateAssetMenu(fileName = "LeaningSettings", menuName = "ScriptableObjects/Leaning Settings")]
public class LeaningSettings : ScriptableObject
{
    [Header("Locomotion Type Settings")]
    public CalibrationType calibrationType = CalibrationType.Both;

    [Header("Breaking System")]
    public BreakType breakType = BreakType.None;
    [Range(0f, 1f)]
    public float breakStrength = 0.5f;
    public float gradualBreakTime = 0.5f;

    [Header("Speed Settings")]
    [Tooltip("Maximum movement speed in meters per second")]
    public float maxSpeed = 1.5f;

    [Header("General Leaning Settings")]
    [Tooltip("Distance offset from the tracked headset position to the center of the person's head, in meters.")]
    [Min(0f)]
    public float yawRotationAxisOffset = 0.15f;

    [Tooltip("The distance (radius), in meters, from the center which results in maximum axis deviation (max speed).")]
    [Range(0f, 0.6f)]
    public float maxLeaningRadius = 0.4f;

    [Tooltip("Power of the exponential transfer function - controls how quickly speed ramps up with lean angle")]
    [Range(1f, 2f)]
    public float exponentialTransferFunctionPower = 1.53f;

    [Tooltip("Sensitivity (inside the exponential function). 1 = no multiplied speed gain")]
    [Range(1f, 5f)]
    public float speedSensitivity = 1f;

    [Header("Standard Dead-Zone Settings")]
    [Tooltip("Choose how to define deadzone sizes")]
    public DeadzoneRadiusSettings deadzoneRadiusSetting = DeadzoneRadiusSettings.DeadzonePercentage;

    [Tooltip("Leaning dead-zone in percent")]
    [Range(0f, 0.99f)]
    public float deadZonePercentage = 0.1f;

    [Tooltip("Define the minimum distance (radius), in meters, from the center")]
    [Range(0f, 0.6f)]
    public float deadzoneRadius = 0.1f;    

    private void OnValidate()
    {        
        // Original validation for other leaning types
        DeadZoneSetup();
    }

    // Sets the deadzone size, based on deadzonePercentage or updates percentage size based on direct radius size
    void DeadZoneSetup()
    {
        // Sets the deadzone radius based on the percentage set by the user in the inspector
        if (deadzoneRadiusSetting == DeadzoneRadiusSettings.DeadzonePercentage)
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
}
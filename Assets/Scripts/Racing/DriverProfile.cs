using UnityEngine;

[System.Serializable]
public struct DriverProfile
{
    public string driverName;
    [Range(0f, 1f)] public float skillLevel;
    [Range(0f, 1f)] public float aggression;
    [Range(0f, 1f)] public float consistency;
    [Range(0f, 1f)] public float brakingCourage;
    [Range(0f, 1f)] public float linePreference;
}

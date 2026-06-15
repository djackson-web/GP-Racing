using System;
using UnityEditor;

/// <summary>
/// Holds all brush and placement settings for the <see cref="TreePlacerEditor"/> tool.
/// Settings are persisted per component instance across domain reloads via <see cref="SessionState"/>.
/// </summary>
[Serializable]
public class TreePlacerEditorSettings
{
    /// <summary>Prefix for all <see cref="SessionState"/> keys written by this class.</summary>
    private const string KeyPrefix = "TreePlacerEditor";

    /// <summary>Brush radius in world units. Default 1.</summary>
    public float BrushRadius = 1f;

    /// <summary>
    /// Fraction of the brush radius applied as bidirectional scatter at paint time.
    /// 0 means no scatter; 1 means full brush radius scatter. Default 1.
    /// </summary>
    public float BrushScatterStrength = 1f;

    /// <summary>
    /// Minimum world-unit distance the cursor must travel between paint strokes
    /// before a new tree is placed. Default 0.5.
    /// </summary>
    public float MinimumStrokeDistance = 0.5f;

    /// <summary>Base yaw rotation in degrees applied to every painted tree. Default 0.</summary>
    public float BaseYawDegrees = 0f;

    /// <summary>Maximum random yaw offset in degrees added on top of <see cref="BaseYawDegrees"/>. Default 0.</summary>
    public float YawVarianceDegrees = 0f;

    /// <summary>Base lateral offset from the spline in world units. Default 0.</summary>
    public float BaseLateralOffset = 0f;

    /// <summary>Maximum random lateral distance in world units added on top of <see cref="BaseLateralOffset"/>. Default 0.</summary>
    public float LateralOffsetVariance = 0f;

    /// <summary>
    /// Constructs a new <see cref="TreePlacerEditorSettings"/> instance with every field
    /// populated from <see cref="SessionState"/>, falling back to each field's declared default.
    /// </summary>
    /// <param name="instanceId">Instance ID of the target <see cref="TreePlacer"/> component.</param>
    /// <returns>A settings instance reflecting the last saved state for the given component.</returns>
    public static TreePlacerEditorSettings Load(int instanceId)
    {
        TreePlacerEditorSettings settings = new TreePlacerEditorSettings();
        settings.BrushRadius = LoadFloat(instanceId, nameof(BrushRadius), settings.BrushRadius);
        settings.BrushScatterStrength = LoadFloat(instanceId, nameof(BrushScatterStrength), settings.BrushScatterStrength);
        settings.MinimumStrokeDistance = LoadFloat(instanceId, nameof(MinimumStrokeDistance), settings.MinimumStrokeDistance);
        settings.BaseYawDegrees = LoadFloat(instanceId, nameof(BaseYawDegrees), settings.BaseYawDegrees);
        settings.YawVarianceDegrees = LoadFloat(instanceId, nameof(YawVarianceDegrees), settings.YawVarianceDegrees);
        settings.BaseLateralOffset = LoadFloat(instanceId, nameof(BaseLateralOffset), settings.BaseLateralOffset);
        settings.LateralOffsetVariance = LoadFloat(instanceId, nameof(LateralOffsetVariance), settings.LateralOffsetVariance);
        return settings;
    }

    /// <summary>
    /// Writes every field value to <see cref="SessionState"/> keyed by the given component instance ID.
    /// </summary>
    /// <param name="instanceId">Instance ID of the target <see cref="TreePlacer"/> component.</param>
    public void Save(int instanceId)
    {
        SaveFloat(instanceId, nameof(BrushRadius), BrushRadius);
        SaveFloat(instanceId, nameof(BrushScatterStrength), BrushScatterStrength);
        SaveFloat(instanceId, nameof(MinimumStrokeDistance), MinimumStrokeDistance);
        SaveFloat(instanceId, nameof(BaseYawDegrees), BaseYawDegrees);
        SaveFloat(instanceId, nameof(YawVarianceDegrees), YawVarianceDegrees);
        SaveFloat(instanceId, nameof(BaseLateralOffset), BaseLateralOffset);
        SaveFloat(instanceId, nameof(LateralOffsetVariance), LateralOffsetVariance);
    }

    /// <summary>
    /// Reads a single float from <see cref="SessionState"/>,
    /// returning <paramref name="defaultValue"/> when no stored value exists.
    /// </summary>
    /// <param name="instanceId">Component instance ID used to scope the key.</param>
    /// <param name="fieldName">Name of the field being loaded.</param>
    /// <param name="defaultValue">Value returned when no stored value is found.</param>
    /// <returns>The stored float, or <paramref name="defaultValue"/> if absent.</returns>
    private static float LoadFloat(int instanceId, string fieldName, float defaultValue)
    {
        string key = BuildKey(instanceId, fieldName);
        return SessionState.GetFloat(key, defaultValue);
    }

    /// <summary>
    /// Writes a single float to <see cref="SessionState"/> under the key formed from
    /// <paramref name="instanceId"/> and <paramref name="fieldName"/>.
    /// </summary>
    /// <param name="instanceId">Component instance ID used to scope the key.</param>
    /// <param name="fieldName">Name of the field being saved.</param>
    /// <param name="value">The value to store.</param>
    private static void SaveFloat(int instanceId, string fieldName, float value)
    {
        string key = BuildKey(instanceId, fieldName);
        SessionState.SetFloat(key, value);
    }

    /// <summary>
    /// Returns a <see cref="SessionState"/> key in the format
    /// <c>{<see cref="KeyPrefix"/>}_{instanceId}_{fieldName}</c>.
    /// </summary>
    /// <param name="instanceId">Component instance ID.</param>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The composite key string.</returns>
    private static string BuildKey(int instanceId, string fieldName)
    {
        return $"{KeyPrefix}_{instanceId}_{fieldName}";
    }
}

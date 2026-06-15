using System;

/// <summary>
/// Represents a single placed tree instance, storing all per-instance placement
/// data serialised alongside the <see cref="TreePlacer"/> component.
/// </summary>
[Serializable]
public struct TreePlacementRecord
{
    /// <summary>Normalized spline parameter in the range 0–1 at which this tree is placed.</summary>
    public float NormalizedT;

    /// <summary>
    /// Signed lateral offset from the spline in world units.
    /// Negative values place the tree to the left of the spline; positive values to the right.
    /// </summary>
    public float SignedLateralOffset;

    /// <summary>Final yaw rotation in degrees applied to this tree instance.</summary>
    public float YawDegrees;

    /// <summary>Uniform scale multiplier applied to this tree instance.</summary>
    public float UniformScale;

    /// <summary>Index into the <see cref="TreePlacer.TreePalette"/> from which this tree's prefab was sampled.</summary>
    public int PrefabIndex;
}

using UnityEngine;

/// <summary>
/// Represents a single sampled point on the spline track, providing the
/// world-space position and orientation needed to place and align objects
/// (such as the player or AI racers) at a specific point along the track.
/// Produced by <see cref="SplineTrack.Evaluate"/>.
/// </summary>
public struct TrackSample
{
    /// <summary>World-space position of this point on the track.</summary>
    public Vector3 position;

    /// <summary>World-space direction the track is heading at this point, tangent to the spline.</summary>
    public Vector3 forward;

    /// <summary>
    /// World-space direction pointing to the right of the track at this point.
    /// Derived from the cross product of the spline's up and forward vectors.
    /// </summary>
    public Vector3 right;
}
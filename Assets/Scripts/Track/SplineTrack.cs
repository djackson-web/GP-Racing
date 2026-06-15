using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Wraps a <see cref="SplineContainer"/> to provide world-space track sampling
/// for player movement and placement along a looping spline track.
/// Exposes a single <see cref="Evaluate"/> method that converts a normalized
/// progress value into a <see cref="TrackSample"/> containing position and
/// orientation data.
/// </summary>
/// <remarks>
/// Runs in edit mode so track length and gizmos remain accurate while
/// authoring the spline in the editor.
/// </remarks>
[ExecuteInEditMode]
[RequireComponent(typeof(SplineContainer))]
public class SplineTrack : MonoBehaviour
{
    /// <summary>The SplineContainer component that holds the track spline data.</summary>
    private SplineContainer splineContainer;

    /// <summary>
    /// The total arc length of the spline in world units.
    /// Calculated once on <see cref="Awake"/> from the spline's geometry.
    /// </summary>
    public float TrackLength { get; private set; }

    /// <summary>
    /// Caches the <see cref="SplineContainer"/> reference and computes
    /// the initial track length.
    /// </summary>
    private void Awake()
    {
        splineContainer = GetComponent<SplineContainer>();
        TrackLength = splineContainer.Spline.GetLength();
    }

    /// <summary>
    /// Samples the spline at the given normalized progress and returns
    /// world-space position and orientation for that point on the track.
    /// Progress wraps automatically, so values outside 0–1 loop around
    /// the track rather than clamping.
    /// </summary>
    /// <param name="progress">
    /// Normalized position along the spline (0 = start, 1 = end/loop).
    /// Values outside this range are wrapped via <see cref="Mathf.Repeat"/>.
    /// </param>
    /// <returns>
    /// A <see cref="TrackSample"/> with world-space position, forward, and right vectors.
    /// Returns <c>default</c> if the spline container reference is missing.
    /// </returns>
    public TrackSample Evaluate(float progress)
    {
        if (splineContainer == null)
        {
            return default;
        }

        float wrappedProgress = Mathf.Repeat(progress, 1.0f);

        splineContainer.Evaluate(
            wrappedProgress,
            out float3 position,
            out float3 tangent,
            out float3 upVector
        );

        // Convert from spline-local space to world space before building the sample
        Vector3 worldPosition = transform.TransformPoint((Vector3)position);
        Vector3 worldTangent = transform.TransformDirection((Vector3)tangent);
        return BuildTrackSample((float3)worldPosition, (float3)worldTangent, upVector);
    }

    /// <summary>
    /// Constructs a <see cref="TrackSample"/> from raw spline evaluation outputs.
    /// Derives the right vector via the cross product of the spline's up and forward
    /// vectors, giving a consistent local frame regardless of track curvature.
    /// </summary>
    /// <param name="position">World-space position on the spline.</param>
    /// <param name="tangent">World-space tangent direction at this point (not necessarily normalized).</param>
    /// <param name="upVector">Spline up vector at this point, used to compute the right axis.</param>
    /// <returns>A fully populated <see cref="TrackSample"/>.</returns>
    private TrackSample BuildTrackSample(float3 position, float3 tangent, float3 upVector)
    {
        Vector3 forward = ((Vector3)tangent).normalized;
        Vector3 right = Vector3.Cross(upVector, forward).normalized;

        return new TrackSample
        {
            position = position,
            forward = forward,
            right = right
        };
    }
}

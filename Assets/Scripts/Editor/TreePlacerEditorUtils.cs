using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Pure static utility class containing all placement logic for the <see cref="TreePlacer"/> tool.
/// Contains zero UnityEditor references; uses only UnityEngine and UnityEngine.Splines.
/// </summary>
public static class TreePlacerEditorUtils
{
    /// <summary>
    /// Returns the normalized spline parameter (0–1) of the point on <paramref name="spline"/>
    /// nearest to <paramref name="worldPoint"/>.
    /// </summary>
    /// <param name="spline">The spline to query.</param>
    /// <param name="containerTransform">Transform of the spline container, used for coordinate conversion.</param>
    /// <param name="worldPoint">The world-space position to find the nearest spline point for.</param>
    /// <returns>Normalized spline parameter in the range 0–1.</returns>
    public static float GetNearestSplineTParameter(Spline spline, Transform containerTransform, Vector3 worldPoint)
    {
        float3 localPoint = containerTransform.InverseTransformPoint(worldPoint);
        SplineUtility.GetNearestPoint(spline, localPoint, out float3 _, out float normalizedT);
        return normalizedT;
    }

    /// <summary>
    /// Returns whether <paramref name="worldHitPoint"/> lies on the right side of <paramref name="spline"/>
    /// at the given <paramref name="normalizedT"/>.
    /// A non-negative dot product of the cursor-to-spline direction and the world-space right axis
    /// indicates the right side.
    /// </summary>
    /// <param name="spline">The spline to evaluate.</param>
    /// <param name="containerTransform">Transform of the spline container, used for coordinate conversion.</param>
    /// <param name="worldHitPoint">The world-space point to test.</param>
    /// <param name="normalizedT">Normalized spline parameter defining the nearest track point.</param>
    /// <returns><c>true</c> if <paramref name="worldHitPoint"/> is on the right side; otherwise <c>false</c>.</returns>
    public static bool IsOnRightSideOfSpline(Spline spline, Transform containerTransform, Vector3 worldHitPoint, float normalizedT)
    {
        Vector3 worldPosition = EvaluateWorldPosition(spline, containerTransform, normalizedT);
        Vector3 worldTangent = EvaluateWorldTangent(spline, containerTransform, normalizedT);
        Vector3 right = Vector3.Cross(worldTangent, Vector3.up).normalized;
        Vector3 cursorDirection = worldHitPoint - worldPosition;
        return Vector3.Dot(cursorDirection, right) >= 0f;
    }

    /// <summary>
    /// Returns whether a candidate spline position is within <paramref name="minimumSpacingDistance"/>
    /// world units of any existing record on the same side of the track.
    /// Compares arc-length distances via <see cref="SplineUtility.ConvertIndexUnit"/> to correctly
    /// handle curved and unequal-length spline segments.
    /// </summary>
    /// <param name="records">All previously placed tree records to check against.</param>
    /// <param name="spline">The spline, used to convert normalised parameters to arc-length distances.</param>
    /// <param name="normalizedT">Candidate normalized spline parameter.</param>
    /// <param name="sideSign">Positive for the right side of the spline, negative for the left.</param>
    /// <param name="minimumSpacingDistance">Minimum allowed arc-length distance in world units between same-side trees.</param>
    /// <returns><c>true</c> if placement should be blocked; otherwise <c>false</c>.</returns>
    public static bool IsTooCloseToExistingRecord(List<TreePlacementRecord> records, Spline spline, float normalizedT, float sideSign, float minimumSpacingDistance)
    {
        float splineLength = spline.GetLength();
        float candidateDistance = SplineUtility.ConvertIndexUnit(spline, normalizedT, PathIndexUnit.Normalized, PathIndexUnit.Distance);
        foreach (TreePlacementRecord record in records)
        {
            if (RecordBlocksPlacement(record, spline, splineLength, candidateDistance, sideSign, minimumSpacingDistance))
                return true;
        }
        return false;
    }

    private static Vector3 EvaluateWorldPosition(Spline spline, Transform containerTransform, float normalizedT)
    {
        spline.Evaluate(normalizedT, out float3 localPosition, out _, out _);
        return containerTransform.TransformPoint((Vector3)localPosition);
    }

    private static Vector3 EvaluateWorldTangent(Spline spline, Transform containerTransform, float normalizedT)
    {
        spline.Evaluate(normalizedT, out _, out float3 localTangent, out _);
        return containerTransform.TransformDirection((Vector3)localTangent).normalized;
    }

    private static bool RecordBlocksPlacement(TreePlacementRecord record, Spline spline, float splineLength, float candidateDistance, float sideSign, float minimumSpacingDistance)
    {
        bool isSameSide = Mathf.Sign(record.SignedLateralOffset) == Mathf.Sign(sideSign);
        if (!isSameSide)
        {
            return false;
        }
        float recordDistance = SplineUtility.ConvertIndexUnit(spline, record.NormalizedT, PathIndexUnit.Normalized, PathIndexUnit.Distance);
        float delta = Mathf.Abs(candidateDistance - recordDistance);
        float wrappedDelta = Mathf.Min(delta, splineLength - delta);
        return wrappedDelta < minimumSpacingDistance;
    }
}

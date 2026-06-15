using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bakes a racing line for a SplineTrack as lateral offsets from the centreline.
/// Each relaxation pass moves every sample toward the position that minimises the
/// local curvature of the path (with a touch of tension so straights stay taut),
/// clamped to the available track width. Minimum curvature is what real drivers
/// approximate: sweep wide on entry, clip a single apex, release wide on exit.
/// Baked once per track and shared by all AI cars.
/// </summary>
public static class RacingLine
{
    private const int SampleCount = 256;
    private const int IterationsPerLevel = 400;
    // Small pull toward the shortest path keeps the line from wandering on
    // straights; the dominant force is curvature smoothing.
    private const float ShortestPathBias = 0.05f;
    // Coarse-to-fine resolutions: wide corner-entry sweeps are long-wavelength
    // features that take forever to emerge at full resolution, but converge in
    // a handful of passes on a coarse grid and survive upsampling.
    private static readonly int[] LevelSampleCounts = { 32, 64, 128, 256 };

    private static readonly Dictionary<SplineTrack, float[]> Cache =
        new Dictionary<SplineTrack, float[]>();

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearCache()
    {
        // Survives disabled domain reload; stale lines must not leak between runs
        Cache.Clear();
    }
#endif

    /// <summary>
    /// Lateral offset of the racing line at the given spline progress.
    /// maxOffset is only used the first time a track is baked.
    /// </summary>
    public static float SampleOffset(SplineTrack track, float progress, float maxOffset)
    {
        float[] offsets = GetOrBake(track, maxOffset);

        float wrapped = progress - Mathf.Floor(progress);
        float scaled = wrapped * SampleCount;
        int index = Mathf.FloorToInt(scaled);
        float blend = scaled - index;

        float current = offsets[index % SampleCount];
        float next = offsets[(index + 1) % SampleCount];
        return Mathf.Lerp(current, next, blend);
    }

    private static float[] GetOrBake(SplineTrack track, float maxOffset)
    {
        float[] offsets;
        if (!Cache.TryGetValue(track, out offsets))
        {
            offsets = Bake(track, maxOffset);
            Cache.Add(track, offsets);
        }
        return offsets;
    }

    private static float[] Bake(SplineTrack track, float maxOffset)
    {
        Vector3[] centres = new Vector3[SampleCount];
        Vector3[] rights = new Vector3[SampleCount];

        for (int i = 0; i < SampleCount; i++)
        {
            TrackSample sample = track.Evaluate((float)i / SampleCount);
            centres[i] = sample.position;
            rights[i] = sample.right;
        }

        float[] offsets = new float[LevelSampleCounts[0]];
        for (int level = 0; level < LevelSampleCounts.Length; level++)
        {
            if (offsets.Length != LevelSampleCounts[level])
            {
                offsets = Upsample(offsets);
            }
            Relax(centres, rights, offsets, maxOffset);
        }

        return offsets;
    }

    private static void Relax(Vector3[] centres, Vector3[] rights, float[] offsets, float maxOffset)
    {
        int count = offsets.Length;
        int stride = SampleCount / count;

        for (int iteration = 0; iteration < IterationsPerLevel; iteration++)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 previous2 = LinePoint(centres, rights, offsets, stride, i - 2);
                Vector3 previous1 = LinePoint(centres, rights, offsets, stride, i - 1);
                Vector3 next1 = LinePoint(centres, rights, offsets, stride, i + 1);
                Vector3 next2 = LinePoint(centres, rights, offsets, stride, i + 2);

                // Exact stationary point of the discrete bending energy
                // Σ‖pᵢ₋₁−2pᵢ+pᵢ₊₁‖² with respect to pᵢ: each update strictly
                // reduces total curvature, so the relaxation cannot oscillate.
                Vector3 minimumCurvature = (4f * (previous1 + next1) - (previous2 + next2)) / 6f;
                Vector3 shortestPath = (previous1 + next1) * 0.5f;
                Vector3 target = Vector3.Lerp(minimumCurvature, shortestPath, ShortestPathBias);

                int fullIndex = i * stride;
                float pulled = Vector3.Dot(target - centres[fullIndex], rights[fullIndex]);
                offsets[i] = Mathf.Clamp(pulled, -maxOffset, maxOffset);
            }
        }
    }

    private static Vector3 LinePoint(Vector3[] centres, Vector3[] rights, float[] offsets, int stride, int index)
    {
        int count = offsets.Length;
        int wrapped = (index % count + count) % count;
        int fullIndex = wrapped * stride;
        return centres[fullIndex] + rights[fullIndex] * offsets[wrapped];
    }

    private static float[] Upsample(float[] coarse)
    {
        float[] fine = new float[coarse.Length * 2];
        for (int i = 0; i < coarse.Length; i++)
        {
            float nextValue = coarse[(i + 1) % coarse.Length];
            fine[i * 2] = coarse[i];
            fine[i * 2 + 1] = (coarse[i] + nextValue) * 0.5f;
        }
        return fine;
    }
}

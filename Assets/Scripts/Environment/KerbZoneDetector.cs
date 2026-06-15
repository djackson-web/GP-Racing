using UnityEngine;
using System.Collections.Generic;

public class KerbZoneDetector : MonoBehaviour
{
    [SerializeField] private SplineTrack _splineTrack;
    [SerializeField] private int _resolution = 500;

    [Tooltip("Angle in degrees above which a sample is considered a corner.")]
    [SerializeField] private float _cornerAngleThreshold = 0.3f;

    [Tooltip("Minimum gap between zones as a fraction of track length.")]
    [SerializeField] private float _minimumZoneGap = 0.005f;

    [Tooltip("How much to pad the start and end of each zone.")]
    [SerializeField] private float _zonePadding = 0.005f;

    private void Start()
    {
        DetectAndLogKerbZones();
    }

    private void DetectAndLogKerbZones()
    {
        List<KerbZone> detectedZones = BuildKerbZones();
        ApplyZonesToKerbMeshBuilder(detectedZones);
    }

    private List<KerbZone> BuildKerbZones()
    {
        bool[] isCornerAtSample = ClassifyCornerSamples();
        List<KerbZone> zones = ExtractZonesFromSamples(isCornerAtSample);
        return zones;
    }

    private bool[] ClassifyCornerSamples()
    {
        Vector3[] forwardDirections = SampleForwardDirections();
        bool[] isCorner = new bool[_resolution];

        for (int sampleIndex = 0; sampleIndex < _resolution - 1; sampleIndex++)
        {
            float angle = Vector3.Angle(forwardDirections[sampleIndex], forwardDirections[sampleIndex + 1]);
            isCorner[sampleIndex] = angle > _cornerAngleThreshold;
        }

        return isCorner;
    }

    private Vector3[] SampleForwardDirections()
    {
        Vector3[] forwardDirections = new Vector3[_resolution];

        for (int sampleIndex = 0; sampleIndex < _resolution; sampleIndex++)
        {
            float progress = (float)sampleIndex / (_resolution - 1);
            forwardDirections[sampleIndex] = _splineTrack.Evaluate(progress).forward;
        }

        return forwardDirections;
    }

    private List<KerbZone> ExtractZonesFromSamples(bool[] isCorner)
    {
        List<KerbZone> zones = new List<KerbZone>();

        float zoneStart = -1f;
        float lastZoneEnd = -1f;

        for (int sampleIndex = 0; sampleIndex < _resolution; sampleIndex++)
        {
            float progress = (float)sampleIndex / (_resolution - 1);

            if (isCorner[sampleIndex] && zoneStart < 0f)
            {
                if (progress - lastZoneEnd > _minimumZoneGap)
                {
                    zoneStart = Mathf.Max(0f, progress - _zonePadding);
                }
            }
            else if (!isCorner[sampleIndex] && zoneStart >= 0f)
            {
                float zoneEnd = Mathf.Min(1f, progress + _zonePadding);
                KerbZone zone = new KerbZone();
                zone.startProgress = zoneStart;
                zone.endProgress = zoneEnd;
                zones.Add(zone);

                lastZoneEnd = zoneEnd;
                zoneStart = -1f;
            }
        }

        return zones;
    }

    private void ApplyZonesToKerbMeshBuilder(List<KerbZone> detectedZones)
    {
        KerbMeshBuilder kerbMeshBuilder = FindFirstObjectByType<KerbMeshBuilder>();

        if (kerbMeshBuilder == null)
        {
            return;
        }

        kerbMeshBuilder.BuildMeshWithZones(detectedZones);
    }
}

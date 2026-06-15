using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class KerbMeshBuilder : MonoBehaviour
{
    private const int VerticesPerSample = 4;
    private const int TriangleIndicesPerSegment = 12;
    private const float FullAlpha = 1f;
    private const float ZeroAlpha = 0f;

    [SerializeField] private SplineTrack splineTrack;
    [SerializeField] private float roadWidth = 20f;
    [SerializeField] private float kerbWidth = 2f;
    [SerializeField] private int resolution = 500;
    [SerializeField] private float heightOffset = 0.01f;

    [Header("Kerb Zones")]
    [SerializeField] private List<KerbZone> kerbZones = new List<KerbZone>();

    private void Start()
    {
        BuildMesh();
    }

    public void BuildMeshWithZones(List<KerbZone> zones)
    {
        kerbZones = zones;
        BuildMesh();
    }

    private void BuildMesh()
    {
        if (splineTrack == null) return;

        Vector3[] vertices = new Vector3[resolution * VerticesPerSample];
        int[] triangles = new int[(resolution - 1) * TriangleIndicesPerSegment];
        Vector2[] uvs = new Vector2[resolution * VerticesPerSample];
        Color[] colours = new Color[resolution * VerticesPerSample];

        Vector3[] forwardDirections = SampleForwardDirections();

        float[] leftOuterDistances = CalculateEdgeDistances(side: -1f, edgeDistance: roadWidth / 2f + kerbWidth);
        float[] leftInnerDistances = CalculateEdgeDistances(side: -1f, edgeDistance: roadWidth / 2f);
        float[] rightInnerDistances = CalculateEdgeDistances(side: 1f, edgeDistance: roadWidth / 2f);
        float[] rightOuterDistances = CalculateEdgeDistances(side: 1f, edgeDistance: roadWidth / 2f + kerbWidth);

        PopulateVertices(vertices, uvs, colours, forwardDirections,
            leftOuterDistances, leftInnerDistances,
            rightInnerDistances, rightOuterDistances);

        PopulateTriangles(triangles);

        Mesh mesh = new Mesh();
        mesh.name = "KerbMesh";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.colors = colours;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;

        AssignKerbTexture();
    }

    private Vector3[] SampleForwardDirections()
    {
        Vector3[] forwardDirections = new Vector3[resolution];

        for (int sampleIndex = 0; sampleIndex < resolution; sampleIndex++)
        {
            float progress = (float)sampleIndex / (resolution - 1);
            forwardDirections[sampleIndex] = splineTrack.Evaluate(progress).forward;
        }

        return forwardDirections;
    }

    private float[] CalculateEdgeDistances(float side, float edgeDistance)
    {
        float[] cumulativeDistances = new float[resolution];
        cumulativeDistances[0] = 0f;

        for (int sampleIndex = 1; sampleIndex < resolution; sampleIndex++)
        {
            float previousProgress = (float)(sampleIndex - 1) / (resolution - 1);
            float currentProgress = (float)sampleIndex / (resolution - 1);

            TrackSample previousSample = splineTrack.Evaluate(previousProgress);
            TrackSample currentSample = splineTrack.Evaluate(currentProgress);

            Vector3 previousEdgePosition = previousSample.position + previousSample.right * side * edgeDistance;
            Vector3 currentEdgePosition = currentSample.position + currentSample.right * side * edgeDistance;

            float segmentLength = Vector3.Distance(previousEdgePosition, currentEdgePosition);
            cumulativeDistances[sampleIndex] = cumulativeDistances[sampleIndex - 1] + segmentLength;
        }

        return cumulativeDistances;
    }

    private void PopulateVertices(Vector3[] vertices, Vector2[] uvs, Color[] colours,
        Vector3[] forwardDirections,
        float[] leftOuterDistances, float[] leftInnerDistances,
        float[] rightInnerDistances, float[] rightOuterDistances)
    {
        for (int sampleIndex = 0; sampleIndex < resolution; sampleIndex++)
        {
            float progress = (float)sampleIndex / (resolution - 1);
            TrackSample sample = splineTrack.Evaluate(progress);
            float kerbWidthScale = CalculateKerbWidthAtSample(progress);

            AssignKerbVertices(vertices, sample, sampleIndex, kerbWidthScale);
            AssignKerbUVs(uvs, sampleIndex,
                leftOuterDistances[sampleIndex], leftInnerDistances[sampleIndex],
                rightInnerDistances[sampleIndex], rightOuterDistances[sampleIndex]);
            AssignKerbColours(colours, sampleIndex);
        }
    }

    private float CalculateKerbWidthAtSample(float progress)
    {
        const float taperLength = 0.005f;

        float closestBoundaryDistance = float.MaxValue;

        foreach (KerbZone zone in kerbZones)
        {
            if (progress >= zone.startProgress && progress <= zone.endProgress)
            {
                float distanceFromStart = progress - zone.startProgress;
                float distanceFromEnd = zone.endProgress - progress;
                float closestEdge = Mathf.Min(distanceFromStart, distanceFromEnd);
                closestBoundaryDistance = Mathf.Min(closestBoundaryDistance, closestEdge);
            }
        }

        if (closestBoundaryDistance == float.MaxValue)
            return 0f;

        return Mathf.Clamp01(closestBoundaryDistance / taperLength);
    }

    private void AssignKerbVertices(Vector3[] vertices, TrackSample sample, int sampleIndex, float kerbScale)
    {
        Vector3 heightAdjustment = Vector3.up * heightOffset;
        float innerEdgeDistance = roadWidth / 2f;
        float outerEdgeDistance = innerEdgeDistance + (kerbWidth * kerbScale);

        Vector3 leftOuterVertex = sample.position - sample.right * outerEdgeDistance + heightAdjustment;
        Vector3 leftInnerVertex = sample.position - sample.right * innerEdgeDistance + heightAdjustment;
        Vector3 rightInnerVertex = sample.position + sample.right * innerEdgeDistance + heightAdjustment;
        Vector3 rightOuterVertex = sample.position + sample.right * outerEdgeDistance + heightAdjustment;

        int baseIndex = sampleIndex * VerticesPerSample;
        vertices[baseIndex] = leftOuterVertex;
        vertices[baseIndex + 1] = leftInnerVertex;
        vertices[baseIndex + 2] = rightInnerVertex;
        vertices[baseIndex + 3] = rightOuterVertex;
    }

    private void AssignKerbUVs(Vector2[] uvs, int sampleIndex,
        float leftOuterDistance, float leftInnerDistance,
        float rightInnerDistance, float rightOuterDistance)
    {
        float leftAverageDistance = (leftOuterDistance + leftInnerDistance) / 2f;
        float rightAverageDistance = (rightInnerDistance + rightOuterDistance) / 2f;

        int baseIndex = sampleIndex * VerticesPerSample;
        uvs[baseIndex] = new Vector2(0f, leftAverageDistance);
        uvs[baseIndex + 1] = new Vector2(1f, leftAverageDistance);
        uvs[baseIndex + 2] = new Vector2(0f, rightAverageDistance);
        uvs[baseIndex + 3] = new Vector2(1f, rightAverageDistance);
    }

    private void AssignKerbColours(Color[] colours, int sampleIndex)
    {
        float progress = (float)sampleIndex / (resolution - 1);
        float kerbWidthScale = CalculateKerbWidthAtSample(progress);
        float alpha = kerbWidthScale > 0f ? FullAlpha : ZeroAlpha;
        Color kerbColour = new Color(1f, 1f, 1f, alpha);

        int baseIndex = sampleIndex * VerticesPerSample;
        colours[baseIndex] = kerbColour;
        colours[baseIndex + 1] = kerbColour;
        colours[baseIndex + 2] = kerbColour;
        colours[baseIndex + 3] = kerbColour;
    }

    private void AssignKerbTexture()
    {
        const int textureWidth = 4;
        const int textureHeight = 64;
        const int halfTextureHeight = textureHeight / 2;

        Texture2D kerbTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGB24, false);
        kerbTexture.filterMode = FilterMode.Point;
        kerbTexture.wrapMode = TextureWrapMode.Repeat;

        Color[] pixels = new Color[textureWidth * textureHeight];

        for (int verticalPixel = 0; verticalPixel < textureHeight; verticalPixel++)
        {
            Color stripeColour = verticalPixel < halfTextureHeight ? Color.red : Color.white;

            for (int horizontalPixel = 0; horizontalPixel < textureWidth; horizontalPixel++)
            {
                pixels[verticalPixel * textureWidth + horizontalPixel] = stripeColour;
            }
        }

        kerbTexture.SetPixels(pixels);
        kerbTexture.Apply();

        GetComponent<MeshRenderer>().sharedMaterial.mainTexture = kerbTexture;
    }

    private void PopulateTriangles(int[] triangles)
    {
        for (int segmentIndex = 0; segmentIndex < resolution - 1; segmentIndex++)
        {
            int triangleBaseIndex = segmentIndex * TriangleIndicesPerSegment;
            int vertexBaseIndex = segmentIndex * VerticesPerSample;

            AssignLeftKerbTriangles(triangles, triangleBaseIndex, vertexBaseIndex);
            AssignRightKerbTriangles(triangles, triangleBaseIndex, vertexBaseIndex);
        }
    }

    private void AssignLeftKerbTriangles(int[] triangles, int triangleBaseIndex, int vertexBaseIndex)
    {
        triangles[triangleBaseIndex] = vertexBaseIndex;
        triangles[triangleBaseIndex + 1] = vertexBaseIndex + 4;
        triangles[triangleBaseIndex + 2] = vertexBaseIndex + 1;
        triangles[triangleBaseIndex + 3] = vertexBaseIndex + 1;
        triangles[triangleBaseIndex + 4] = vertexBaseIndex + 4;
        triangles[triangleBaseIndex + 5] = vertexBaseIndex + 5;
    }

    private void AssignRightKerbTriangles(int[] triangles, int triangleBaseIndex, int vertexBaseIndex)
    {
        triangles[triangleBaseIndex + 6] = vertexBaseIndex + 2;
        triangles[triangleBaseIndex + 7] = vertexBaseIndex + 6;
        triangles[triangleBaseIndex + 8] = vertexBaseIndex + 3;
        triangles[triangleBaseIndex + 9] = vertexBaseIndex + 3;
        triangles[triangleBaseIndex + 10] = vertexBaseIndex + 6;
        triangles[triangleBaseIndex + 11] = vertexBaseIndex + 7;
    }

    public void SetKerbZones(List<KerbZone> zones)
    {
        kerbZones = zones;
    }

    public SplineTrack GetSplineTrack()
    {
        return splineTrack;
    }

    public void AddKerbZone(float start, float end)
    {
        KerbZone newZone = new KerbZone();
        newZone.startProgress = start;
        newZone.endProgress = end;
        kerbZones.Add(newZone);

        MergeOverlappingZones();
        BuildMesh();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    private void MergeOverlappingZones()
    {
        kerbZones.Sort((zoneA, zoneB) => zoneA.startProgress.CompareTo(zoneB.startProgress));

        for (int zoneIndex = kerbZones.Count - 1; zoneIndex > 0; zoneIndex--)
        {
            KerbZone currentZone = kerbZones[zoneIndex];
            KerbZone previousZone = kerbZones[zoneIndex - 1];

            if (currentZone.startProgress <= previousZone.endProgress)
            {
                previousZone.endProgress = Mathf.Max(previousZone.endProgress, currentZone.endProgress);
                kerbZones.RemoveAt(zoneIndex);
            }
        }
    }

    public List<KerbZone> GetKerbZones()
    {
        return kerbZones;
    }

    public void RemoveKerbZone(KerbZone zone)
    {
        kerbZones.Remove(zone);
        BuildMesh();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    public void RemoveLastKerbZone()
    {
        if (kerbZones.Count == 0) return;

        kerbZones.RemoveAt(kerbZones.Count - 1);
        BuildMesh();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
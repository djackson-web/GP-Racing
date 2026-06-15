using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class RoadMeshBuilder : MonoBehaviour
{
    private const int VerticesPerSample = 2;
    private const int TriangleIndicesPerSegment = 6;

    [SerializeField] private SplineTrack splineTrack;
    [SerializeField] private float roadWidth = 20f;
    [SerializeField] private int resolution = 500;
    // Multiplier curve (Y-axis) over lap progress (X-axis, 0–1). Default flat
    // at 1.0 = uniform width. Set keys in the Inspector to narrow chicanes or
    // widen pit straights. Start and end values should match for a seamless loop.
    [SerializeField] private AnimationCurve _roadWidthCurve = AnimationCurve.Constant(0f, 1f, 1f);

    private void Start()
    {
        float[] halfWidthSamples = BakeWidthSamples();
        BuildMesh(halfWidthSamples);
        splineTrack.SetWidthSamples(halfWidthSamples);
    }

    private float[] BakeWidthSamples()
    {
        float[] samples = new float[resolution];
        for (int i = 0; i < resolution; i++)
        {
            float progress = (float)i / (resolution - 1);
            float multiplier = _roadWidthCurve.Evaluate(progress);
            samples[i] = roadWidth * Mathf.Max(multiplier, 0f) * 0.5f;
        }
        return samples;
    }

    private void BuildMesh(float[] halfWidthSamples)
    {
        Vector3[] vertices = new Vector3[resolution * VerticesPerSample];
        int[] triangles = new int[(resolution - 1) * TriangleIndicesPerSegment];
        Vector2[] uvs = new Vector2[resolution * VerticesPerSample];

        PopulateVertices(vertices, uvs, halfWidthSamples);
        PopulateTriangles(triangles);

        Mesh mesh = new Mesh();
        mesh.name = "RoadMesh";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;
    }

    private void PopulateVertices(Vector3[] vertices, Vector2[] uvs, float[] halfWidthSamples)
    {
        for (int sampleIndex = 0; sampleIndex < resolution; sampleIndex++)
        {
            float progress = (float)sampleIndex / (resolution - 1);
            TrackSample sample = splineTrack.Evaluate(progress);

            AssignRoadVertices(vertices, sample, sampleIndex, halfWidthSamples[sampleIndex]);
            AssignRoadUVs(uvs, progress, sampleIndex);
        }
    }

    private void AssignRoadVertices(Vector3[] vertices, TrackSample sample, int sampleIndex, float halfWidth)
    {
        Vector3 leftVertex = sample.position - sample.right * halfWidth;
        Vector3 rightVertex = sample.position + sample.right * halfWidth;

        int baseIndex = sampleIndex * VerticesPerSample;
        vertices[baseIndex] = leftVertex;
        vertices[baseIndex + 1] = rightVertex;
    }

    private void AssignRoadUVs(Vector2[] uvs, float progress, int sampleIndex)
    {
        int baseIndex = sampleIndex * VerticesPerSample;
        uvs[baseIndex] = new Vector2(0f, progress);
        uvs[baseIndex + 1] = new Vector2(1f, progress);
    }

    private void PopulateTriangles(int[] triangles)
    {
        for (int segmentIndex = 0; segmentIndex < resolution - 1; segmentIndex++)
        {
            int triangleBaseIndex = segmentIndex * TriangleIndicesPerSegment;
            int vertexBaseIndex = segmentIndex * VerticesPerSample;

            AssignRoadSegmentTriangles(triangles, triangleBaseIndex, vertexBaseIndex);
        }
    }

    private void AssignRoadSegmentTriangles(int[] triangles, int triangleBaseIndex, int vertexBaseIndex)
    {
        triangles[triangleBaseIndex] = vertexBaseIndex;
        triangles[triangleBaseIndex + 1] = vertexBaseIndex + 2;
        triangles[triangleBaseIndex + 2] = vertexBaseIndex + 1;
        triangles[triangleBaseIndex + 3] = vertexBaseIndex + 1;
        triangles[triangleBaseIndex + 4] = vertexBaseIndex + 2;
        triangles[triangleBaseIndex + 5] = vertexBaseIndex + 3;
    }
}
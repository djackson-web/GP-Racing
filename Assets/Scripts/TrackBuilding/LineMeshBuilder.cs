using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class LineMeshBuilder : MonoBehaviour
{
    private const int VerticesPerSample = 4;
    private const int TriangleIndicesPerSegment = 12;

    [SerializeField] private SplineTrack splineTrack;
    [SerializeField] private float roadWidth = 20f;
    [SerializeField] private float lineWidth = 0.3f;
    [SerializeField] private int resolution = 500;
    [SerializeField] private float heightOffset = 0.02f;

    [Header("Curvature")]
    [Tooltip("Angle difference in degrees at which a segment is considered fully a corner.")]
    [SerializeField] private float curvatureMaxDegrees = 20f;

    private void Start()
    {
        BuildMesh();
    }

    private void BuildMesh()
    {
        Vector3[] vertices = new Vector3[resolution * VerticesPerSample];
        int[] triangles = new int[(resolution - 1) * TriangleIndicesPerSegment];
        Vector2[] uvs = new Vector2[resolution * VerticesPerSample];
        Color[] colours = new Color[resolution * VerticesPerSample];

        Vector3[] forwardDirections = SampleForwardDirections();

        PopulateVertices(vertices, uvs, colours, forwardDirections);
        PopulateTriangles(triangles);

        Mesh mesh = new Mesh();
        mesh.name = "LineMesh";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.colors = colours;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;
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

    private void PopulateVertices(Vector3[] vertices, Vector2[] uvs, Color[] colours, Vector3[] forwardDirections)
    {
        for (int sampleIndex = 0; sampleIndex < resolution; sampleIndex++)
        {
            float progress = (float)sampleIndex / (resolution - 1);
            TrackSample sample = splineTrack.Evaluate(progress);

            AssignLineVertices(vertices, sample, sampleIndex);
            AssignLineUVs(uvs, progress, sampleIndex);
            AssignLineColours(colours, forwardDirections, progress, sampleIndex);
        }
    }

    private void AssignLineVertices(Vector3[] vertices, TrackSample sample, int sampleIndex)
    {
        Vector3 heightAdjustment = Vector3.up * heightOffset;
        float innerEdgeDistance = roadWidth / 2f - lineWidth;
        float outerEdgeDistance = roadWidth / 2f;

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

    private void AssignLineUVs(Vector2[] uvs, float progress, int sampleIndex)
    {
        int baseIndex = sampleIndex * VerticesPerSample;
        uvs[baseIndex] = new Vector2(0f, progress);
        uvs[baseIndex + 1] = new Vector2(1f, progress);
        uvs[baseIndex + 2] = new Vector2(0f, progress);
        uvs[baseIndex + 3] = new Vector2(1f, progress);
    }

    private void AssignLineColours(Color[] colours, Vector3[] forwardDirections, float progress, int sampleIndex)
    {
        float curvature = CalculateCurvatureAtSample(forwardDirections, sampleIndex);

        // Lines are visible on straights and invisible on corners
        float alpha = 1f - curvature;

        Color lineColour = new Color(1f, 1f, 1f, alpha);

        int baseIndex = sampleIndex * VerticesPerSample;
        colours[baseIndex] = lineColour;
        colours[baseIndex + 1] = lineColour;
        colours[baseIndex + 2] = lineColour;
        colours[baseIndex + 3] = lineColour;
    }

    private float CalculateCurvatureAtSample(Vector3[] forwardDirections, int sampleIndex)
    {
        int nextSampleIndex = Mathf.Min(sampleIndex + 1, resolution - 1);
        float angleBetweenSamples = Vector3.Angle(forwardDirections[sampleIndex], forwardDirections[nextSampleIndex]);
        return Mathf.Clamp01(angleBetweenSamples / curvatureMaxDegrees);
    }

    private void PopulateTriangles(int[] triangles)
    {
        for (int segmentIndex = 0; segmentIndex < resolution - 1; segmentIndex++)
        {
            int triangleBaseIndex = segmentIndex * TriangleIndicesPerSegment;
            int vertexBaseIndex = segmentIndex * VerticesPerSample;

            AssignLeftLineTriangles(triangles, triangleBaseIndex, vertexBaseIndex);
            AssignRightLineTriangles(triangles, triangleBaseIndex, vertexBaseIndex);
        }
    }

    private void AssignLeftLineTriangles(int[] triangles, int triangleBaseIndex, int vertexBaseIndex)
    {
        triangles[triangleBaseIndex] = vertexBaseIndex;
        triangles[triangleBaseIndex + 1] = vertexBaseIndex + 4;
        triangles[triangleBaseIndex + 2] = vertexBaseIndex + 1;
        triangles[triangleBaseIndex + 3] = vertexBaseIndex + 1;
        triangles[triangleBaseIndex + 4] = vertexBaseIndex + 4;
        triangles[triangleBaseIndex + 5] = vertexBaseIndex + 5;
    }

    private void AssignRightLineTriangles(int[] triangles, int triangleBaseIndex, int vertexBaseIndex)
    {
        triangles[triangleBaseIndex + 6] = vertexBaseIndex + 2;
        triangles[triangleBaseIndex + 7] = vertexBaseIndex + 6;
        triangles[triangleBaseIndex + 8] = vertexBaseIndex + 3;
        triangles[triangleBaseIndex + 9] = vertexBaseIndex + 3;
        triangles[triangleBaseIndex + 10] = vertexBaseIndex + 6;
        triangles[triangleBaseIndex + 11] = vertexBaseIndex + 7;
    }
}
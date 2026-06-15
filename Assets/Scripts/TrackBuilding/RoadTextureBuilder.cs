using UnityEngine;

/// <summary>
/// Generates a runtime Texture2D mimicking the OutRun arcade road style:
/// red/white rumble strips on the edges, dark tarmac.
/// Attach to the Kerbs GameObject so the kerb mesh has a red/white texture.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class RoadTextureBuilder : MonoBehaviour
{
    [Header("Material")]
    [Tooltip("A URP Unlit material created in the Editor. Drag it here.")]
    [SerializeField] private Material roadMaterial;

    [Header("Texture Resolution")]
    [Tooltip("Pixels across the road (U axis).")]
    [SerializeField] private int textureWidth = 64;

    [Tooltip("Pixels along the road (V axis). Controls kerb repeat frequency.")]
    [SerializeField] private int textureHeight = 512;

    [Header("Colours")]
    [SerializeField] private Color tarmacColour = new Color(0.15f, 0.15f, 0.15f);
    [SerializeField] private Color firstKerbColour = Color.red;
    [SerializeField] private Color secondKerbColour = Color.white;

    [Header("Proportions (0–1 in U)")]
    [Tooltip("How wide each kerb band is as a fraction of the total texture width.")]
    [SerializeField][Range(0f, 0.2f)] private float kerbFraction = 0.08f;

    [Header("Kerb Pattern")]
    [Tooltip("How many red/white alternations fit into the texture height.")]
    [SerializeField] private int kerbRepeatCount = 16;

    private void Awake()
    {
        Texture2D kerbTexture = BuildKerbTexture();
        AssignTextureToMaterial(kerbTexture);
    }

    private void AssignTextureToMaterial(Texture2D kerbTexture)
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

        if (roadMaterial == null)
        {
            Debug.LogWarning("RoadTextureBuilder: No road material assigned. Drag a URP Unlit material into the Road Material slot.");
            return;
        }

        meshRenderer.material = new Material(roadMaterial);
        meshRenderer.material.mainTexture = kerbTexture;
    }

    public Texture2D BuildKerbTexture()
    {
        Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGB24, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;

        Color[] pixels = GeneratePixels();

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    private Color[] GeneratePixels()
    {
        Color[] pixels = new Color[textureWidth * textureHeight];
        int kerbWidthInPixels = Mathf.RoundToInt(kerbFraction * textureWidth);

        for (int verticalPixel = 0; verticalPixel < textureHeight; verticalPixel++)
        {
            Color kerbColour = GetKerbColourAtRow(verticalPixel);

            for (int horizontalPixel = 0; horizontalPixel < textureWidth; horizontalPixel++)
            {
                pixels[verticalPixel * textureWidth + horizontalPixel] =
                    IsKerbPixel(horizontalPixel, kerbWidthInPixels) ? kerbColour : tarmacColour;
            }
        }

        return pixels;
    }

    private Color GetKerbColourAtRow(int verticalPixel)
    {
        float kerbPhase = (float)(verticalPixel * kerbRepeatCount) / textureHeight;
        return (Mathf.FloorToInt(kerbPhase) % 2 == 0) ? firstKerbColour : secondKerbColour;
    }

    private bool IsKerbPixel(int horizontalPixel, int kerbWidthInPixels)
    {
        bool isLeftKerb = horizontalPixel < kerbWidthInPixels;
        bool isRightKerb = horizontalPixel >= textureWidth - kerbWidthInPixels;
        return isLeftKerb || isRightKerb;
    }
}
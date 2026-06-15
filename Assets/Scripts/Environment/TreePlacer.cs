using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class TreePlacer : MonoBehaviour
{
    private const float RaycastOriginHeight = 50f;
    private const float RaycastMaxDistance = 100f;

    /// <summary>Unity layer index for the Ground layer. Used to restrict ground-height raycasts so trees land on terrain only.</summary>
    private const int GroundLayerIndex = 3;

    /// <summary>Layer mask built from <see cref="GroundLayerIndex"/>. Passed to raycasts to avoid hitting triggers or non-ground colliders.</summary>
    private LayerMask _groundLayerMask;

    [Header("References")]
    public SplineContainer splineContainer;

    /// <summary>[Legacy] Will be removed after the editor migration is complete. Use <see cref="TreePalette"/> instead.</summary>
    public GameObject treePrefab;

    [Header("Placement Settings")]
    public float trackOffset = 5f;
    public float heightOffset = 0f;
    public float spacing = 3f;

    [Header("Rotation Settings")]
    public float rotationOffset = 0f;

    [Header("Scale Settings")]
    public float minScale = 0.9f;
    public float maxScale = 1.1f;
    public float baseScale = 1f;
    public float spriteHeightUnits = 1f;

    /// <summary>[Legacy] Will be removed after the editor migration is complete. Use <see cref="PlacementRecords"/> instead.</summary>
    [HideInInspector] public List<float> placedTs = new List<float>();

    /// <summary>[Legacy] Will be removed after the editor migration is complete. Use <see cref="PlacementRecords"/> instead.</summary>
    [HideInInspector] public List<float> placedOffsets = new List<float>();

    /// <summary>[Legacy] Will be removed after the editor migration is complete. Use <see cref="PlacementRecords"/> instead.</summary>
    [HideInInspector] public List<float> placedRotations = new List<float>();

    /// <summary>[Legacy] Will be removed after the editor migration is complete. Use <see cref="PlacementRecords"/> instead.</summary>
    [HideInInspector] public List<float> placedScales = new List<float>();

    /// <summary>Weighted palette of prefabs to place. Sampled at paint time using normalised weights.</summary>
    public List<WeightedPrefab> TreePalette = new List<WeightedPrefab>();

    /// <summary>All placed tree records. Replaces the legacy parallel lists.</summary>
    public List<TreePlacementRecord> PlacementRecords = new List<TreePlacementRecord>();

    /// <summary>Initialises runtime-only state. Builds <see cref="_groundLayerMask"/> from <see cref="GroundLayerIndex"/>.</summary>
    private void Awake()
    {
        _groundLayerMask = 1 << GroundLayerIndex;
    }

    /// <summary>
    /// Instantiates a single prefab for <paramref name="record"/>, positions and rotates it,
    /// and registers the created object with Undo.
    /// </summary>
    public void SpawnRecord(TreePlacementRecord record)
    {
        if (record.PrefabIndex >= TreePalette.Count) return;
        GameObject prefab = TreePalette[record.PrefabIndex].Prefab;
        Vector3 spawnPosition = CalculateSpawnPosition(record);
        Quaternion spawnRotation = CalculateInwardRotation(spawnPosition, record);
        float finalScale = record.UniformScale * baseScale;
        int treeIndex = transform.childCount;
        GameObject tree = InstantiateTree(prefab, spawnPosition, spawnRotation);
        tree.transform.localScale = Vector3.one * finalScale;
        tree.name = $"{prefab.name} ({treeIndex})";
    }

    /// <summary>Destroys all child objects and re-spawns one per <see cref="PlacementRecords"/> entry. Use only to recover from a desync.</summary>
    public void RebuildAll()
    {
        DestroyAllChildren();
        foreach (TreePlacementRecord record in PlacementRecords)
            SpawnRecord(record);
    }

    /// <summary>Destroys the child at <paramref name="index"/> in the transform hierarchy using Undo.</summary>
    public void DestroyRecordAt(int index)
    {
        if (index < 0 || index >= transform.childCount) return;
#if UNITY_EDITOR
        Undo.DestroyObjectImmediate(transform.GetChild(index).gameObject);
#else
        DestroyImmediate(transform.GetChild(index).gameObject);
#endif
    }

    /// <summary>Destroys the last child in the transform hierarchy using Undo.</summary>
    public void DestroyLastRecord()
    {
        DestroyRecordAt(transform.childCount - 1);
    }

    /// <summary>
    /// Registers the full component state for undo, clears all placement data,
    /// and destroys all spawned children. Does not rebuild — there is nothing to rebuild.
    /// </summary>
    public void ClearAll()
    {
#if UNITY_EDITOR
        Undo.RegisterCompleteObjectUndo(this, "Clear All Trees");
#endif
        placedTs.Clear();
        placedOffsets.Clear();
        placedRotations.Clear();
        placedScales.Clear();
        PlacementRecords.Clear();
        DestroyAllChildren();
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>
    /// Destroys all child GameObjects. In the editor all destructions are collapsed
    /// into a single undo group so one Ctrl+Z restores every child at once.
    /// </summary>
    private void DestroyAllChildren()
    {
#if UNITY_EDITOR
        Undo.SetCurrentGroupName("Destroy Tree Children");
        int group = Undo.GetCurrentGroup();
#endif
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);
#else
            DestroyImmediate(transform.GetChild(i).gameObject);
#endif
        }
#if UNITY_EDITOR
        Undo.CollapseUndoOperations(group);
#endif
    }

    /// <summary>Instantiates <paramref name="prefab"/> as a child, registering it with Undo in the editor.</summary>
    private GameObject InstantiateTree(GameObject prefab, Vector3 position, Quaternion rotation)
    {
#if UNITY_EDITOR
        GameObject tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, transform);
        tree.transform.SetPositionAndRotation(position, rotation);
        Undo.RegisterCreatedObjectUndo(tree, "Spawn Tree");
        return tree;
#else
        return Instantiate(prefab, position, rotation, transform);
#endif
    }

    /// <summary>Evaluates the spline at <see cref="TreePlacementRecord.NormalizedT"/> and returns the laterally-offset world position.</summary>
    private Vector3 CalculateSpawnPosition(TreePlacementRecord record)
    {
        Spline spline = splineContainer.Spline;
        spline.Evaluate(record.NormalizedT, out float3 localPosition, out float3 localTangent, out float3 _);

        Vector3 worldPosition = splineContainer.transform.TransformPoint((Vector3)localPosition);
        Vector3 worldTangent = splineContainer.transform.TransformDirection((Vector3)localTangent).normalized;
        Vector3 right = Vector3.Cross(worldTangent, Vector3.up).normalized;

        Vector3 spawnPosition = worldPosition + right * record.SignedLateralOffset;
        spawnPosition.y = CalculateGroundHeight(spawnPosition, worldPosition, record);

        return spawnPosition;
    }

    /// <summary>Raycasts downward to find the ground Y, falling back to the spline position if nothing is hit.</summary>
    private float CalculateGroundHeight(Vector3 spawnPosition, Vector3 worldPosition, TreePlacementRecord record)
    {
        float finalScale = record.UniformScale * baseScale;
        float halfHeight = spriteHeightUnits * finalScale / 2f;

        Vector3 raycastOrigin = spawnPosition + Vector3.up * RaycastOriginHeight;
        bool hitGround = Physics.Raycast(raycastOrigin, Vector3.down, out RaycastHit hit, RaycastMaxDistance, _groundLayerMask, QueryTriggerInteraction.Ignore);

        if (hitGround)
            return hit.point.y + heightOffset + halfHeight;

        return worldPosition.y + heightOffset + halfHeight;
    }

    /// <summary>Returns a rotation facing inward toward the spline, combined with <see cref="rotationOffset"/> and <see cref="TreePlacementRecord.YawDegrees"/>.</summary>
    private Quaternion CalculateInwardRotation(Vector3 spawnPosition, TreePlacementRecord record)
    {
        Spline spline = splineContainer.Spline;
        spline.Evaluate(record.NormalizedT, out float3 localPosition, out float3 _, out float3 __);
        Vector3 worldPosition = splineContainer.transform.TransformPoint((Vector3)localPosition);

        Vector3 inwardDirection = new Vector3(worldPosition.x - spawnPosition.x, 0f, worldPosition.z - spawnPosition.z).normalized;

        if (inwardDirection == Vector3.zero)
            return Quaternion.identity;

        return Quaternion.LookRotation(inwardDirection) * Quaternion.Euler(0f, rotationOffset + record.YawDegrees, 0f);
    }
}

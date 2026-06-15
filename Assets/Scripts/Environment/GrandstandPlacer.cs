using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteInEditMode]
public class GrandstandPlacer : MonoBehaviour
{
    [Header("References")]
    public SplineContainer splineContainer;
    public GameObject standPrefab;

    [Header("Placement Settings")]
    public float trackOffset = 2f;
    public float heightOffset = 0f;
    public float rotationOffset = 0f;
    public float spacing = 2f;

    [HideInInspector] public List<float> placedTs = new List<float>();

    public void RebuildStands()
    {
        for (int childIndex = transform.childCount - 1; childIndex >= 0; childIndex--)
        {
            DestroyImmediate(transform.GetChild(childIndex).gameObject);
        }

        if (splineContainer == null || standPrefab == null)
        {
            return;
        }

        Spline spline = splineContainer.Spline;

        for (int placedIndex = 0; placedIndex < placedTs.Count; placedIndex++)
        {
            float t = placedTs[placedIndex];

            spline.Evaluate(t, out float3 localPos, out float3 localTangent, out _);

            Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)localPos);
            Vector3 worldTangent = splineContainer.transform.TransformDirection((Vector3)localTangent).normalized;

            Vector3 right = Vector3.Cross(worldTangent, Vector3.up).normalized;

            Vector3 spawnPos = worldPos + right * trackOffset;
            spawnPos.y = worldPos.y + heightOffset;

            GameObject stand = Instantiate(standPrefab, spawnPos, Quaternion.identity, transform);

            Vector3 inward = worldPos - spawnPos;
            inward.y = 0;
            inward.Normalize();

            if (inward != Vector3.zero)
            {
                Quaternion baseRot = Quaternion.LookRotation(inward);
                stand.transform.rotation = baseRot * Quaternion.Euler(0, rotationOffset, 0);
            }

            stand.name = $"StandLeft ({placedIndex})";
        }
    }

    public void ClearAll()
    {
        placedTs.Clear();
        RebuildStands();
    }
}

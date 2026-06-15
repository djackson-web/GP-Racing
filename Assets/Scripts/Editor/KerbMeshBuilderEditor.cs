using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Custom editor for <see cref="KerbMeshBuilder"/> that provides a two-click
/// Scene view tool for defining kerb zones along the spline track. Each zone
/// is defined by a start and end normalized spline progress value, placed by
/// clicking directly on the track in the Scene view. Existing zones are listed
/// in the inspector with individual remove buttons.
/// </summary>
[CustomEditor(typeof(KerbMeshBuilder))]
public class KerbMeshBuilderEditor : Editor
{
    private bool _isPlacingZone = false;
    // -1 means no start point chosen yet; valid progress values are always in [0, 1].
    private float _zoneStartProgress = -1f;

    public override void OnInspectorGUI()
    {
        KerbMeshBuilder kerbMeshBuilder = (KerbMeshBuilder)target;

        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Kerb Zone Actions", EditorStyles.boldLabel);

        // Collect removals into a list to avoid modifying the collection mid-iteration.
        List<KerbZone> zonesToRemove = new List<KerbZone>();

        for (int zoneIndex = 0; zoneIndex < kerbMeshBuilder.GetKerbZones().Count; zoneIndex++)
        {
            KerbZone zone = kerbMeshBuilder.GetKerbZones()[zoneIndex];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Zone {zoneIndex + 1}: {zone.startProgress:F4} → {zone.endProgress:F4}");

            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                zonesToRemove.Add(zone);
            }

            EditorGUILayout.EndHorizontal();
        }

        foreach (KerbZone zone in zonesToRemove)
        {
            kerbMeshBuilder.RemoveKerbZone(zone);
        }

        EditorGUILayout.Space();

        if (!_isPlacingZone)
        {
            if (GUILayout.Button("Place Kerb Zone"))
            {
                _isPlacingZone = true;
                _zoneStartProgress = -1f;
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                _zoneStartProgress < 0f
                    ? "Click on the track to set the START of the kerb zone."
                    : "Click on the track to set the END of the kerb zone.",
                MessageType.Info);

            if (GUILayout.Button("Cancel"))
            {
                _isPlacingZone = false;
                _zoneStartProgress = -1f;
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Remove Last Kerb Zone"))
        {
            kerbMeshBuilder.RemoveLastKerbZone();
        }
    }

    private void OnSceneGUI()
    {
        if (!_isPlacingZone)
        {
            return;
        }

        KerbMeshBuilder kerbMeshBuilder = (KerbMeshBuilder)target;
        SplineTrack splineTrack = kerbMeshBuilder.GetSplineTrack();

        if (splineTrack == null)
        {
            return;
        }

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        Event currentEvent = Event.current;

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            float closestProgress = FindClosestProgressToRay(ray, splineTrack);

            if (_zoneStartProgress < 0f)
            {
                _zoneStartProgress = closestProgress;
            }
            else
            {
                // Sort so start is always less than end regardless of which end the designer clicked first.
                float start = Mathf.Min(_zoneStartProgress, closestProgress);
                float end = Mathf.Max(_zoneStartProgress, closestProgress);

                kerbMeshBuilder.AddKerbZone(start, end);

                _isPlacingZone = false;
                _zoneStartProgress = -1f;
            }

            currentEvent.Use();
        }
    }

    private float FindClosestProgressToRay(Ray ray, SplineTrack splineTrack)
    {
        const int searchResolution = 1000;
        float closestProgress = 0f;
        float closestDistance = float.MaxValue;

        for (int sampleIndex = 0; sampleIndex < searchResolution; sampleIndex++)
        {
            float progress = (float)sampleIndex / (searchResolution - 1);
            Vector3 splinePoint = splineTrack.Evaluate(progress).position;
            Vector3 closestPoint = ClosestPointOnRay(ray, splinePoint);
            float distance = Vector3.Distance(splinePoint, closestPoint);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestProgress = progress;
            }
        }

        return closestProgress;
    }

    private Vector3 ClosestPointOnRay(Ray ray, Vector3 point)
    {
        Vector3 rayToPoint = point - ray.origin;
        float dot = Vector3.Dot(rayToPoint, ray.direction);
        return ray.origin + ray.direction * dot;
    }
}

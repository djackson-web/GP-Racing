using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Custom editor for <see cref="StartingGrid"/> that adds a draggable Scene view
/// handle for positioning the grid start point directly on the spline track.
/// </summary>
[CustomEditor(typeof(StartingGrid))]
public class StartingGridEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }

    private void OnSceneGUI()
    {
        serializedObject.Update();

        SerializedProperty splineTrackProperty = serializedObject.FindProperty("splineTrack");
        SerializedProperty gridProgressProperty = serializedObject.FindProperty("gridStartProgress");

        SplineTrack splineTrack = splineTrackProperty.objectReferenceValue as SplineTrack;
        if (splineTrack == null) return;

        SplineContainer container = splineTrack.GetComponent<SplineContainer>();
        if (container == null || container.Spline.Count == 0) return;

        float currentProgress = gridProgressProperty.floatValue;
        container.Spline.Evaluate(currentProgress, out float3 localPosition, out _, out _); // discards tangent and up vector
        Vector3 handlePosition = container.transform.TransformPoint((Vector3)localPosition);

        EditorGUI.BeginChangeCheck();

        float handleSize = HandleUtility.GetHandleSize(handlePosition) * 0.3f;
        Handles.color = Color.yellow;
        Vector3 newPosition = Handles.FreeMoveHandle(handlePosition, handleSize, Vector3.zero, Handles.SphereHandleCap);
        Handles.Label(handlePosition + Vector3.up * (handleSize * 2f), "P1 (Grid Start)");

        if (EditorGUI.EndChangeCheck())
        {
            float3 localPoint = container.transform.InverseTransformPoint(newPosition);
            SplineUtility.GetNearestPoint(container.Spline, localPoint, out float3 _, out float nearestProgress);

            Undo.RecordObject(target, "Move Starting Grid");
            gridProgressProperty.floatValue = nearestProgress;
            serializedObject.ApplyModifiedProperties();
        }
    }
}

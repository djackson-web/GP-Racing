using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Custom editor for <see cref="GrandstandPlacer"/> that adds spline-aware
/// paint tooling directly into the Scene view. Allows designers to click or
/// click-and-drag along the track to place grandstands at evenly spaced
/// intervals, with undo support and a live preview of all placed positions.
/// </summary>
[CustomEditor(typeof(GrandstandPlacer))]
public class GrandstandPlacerEditor : Editor
{
    private GrandstandPlacer _placer;
    private bool _isPainting = false;
    // Large negative so the first placement is always accepted regardless of where on the spline it lands.
    private float _lastPlacedT = -999f;

    private void OnEnable()
    {
        _placer = (GrandstandPlacer)target;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        _isPainting = false;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        GUI.color = _isPainting ? Color.red : Color.green;
        if (GUILayout.Button(_isPainting ? "Stop Painting" : "Start Painting", GUILayout.Height(35)))
        {
            _isPainting = !_isPainting;
            _lastPlacedT = -999f;
        }
        GUI.color = Color.white;

        EditorGUILayout.HelpBox(
            _isPainting
                ? "Click along the track in the Scene view to paint stands. Holds to paint continuously."
                : "Press 'Start Painting' then click in the Scene view.",
            MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Rebuild Stands"))
        {
            _placer.RebuildStands();
        }

        if (GUILayout.Button("Clear All"))
        {
            if (EditorUtility.DisplayDialog("Clear All Stands", "Are you sure?", "Yes", "Cancel"))
            {
                _placer.ClearAll();
            }
        }

        EditorGUILayout.LabelField($"Stands placed: {_placer.placedTs.Count}");
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!_isPainting || _placer.splineContainer == null)
        {
            return;
        }

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        Event currentEvent = Event.current;

        if (currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag)
        {
            if (currentEvent.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

                if (groundPlane.Raycast(ray, out float distance))
                {
                    Vector3 hitPoint = ray.GetPoint(distance);

                    SplineUtility.GetNearestPoint(
                        _placer.splineContainer.Spline,
                        _placer.splineContainer.transform.InverseTransformPoint(hitPoint),
                        out float3 nearest,
                        out float t
                    );

                    // Divide world-unit spacing by spline length to convert to a normalized t interval.
                    float splineLength = _placer.splineContainer.Spline.GetLength();
                    float tSpacing = _placer.spacing / splineLength;

                    // Prevent over-placement during a drag by enforcing a minimum gap between stands.
                    if (Mathf.Abs(t - _lastPlacedT) >= tSpacing)
                    {
                        Undo.RecordObject(_placer, "Paint Grandstand");
                        _placer.placedTs.Add(t);
                        _placer.RebuildStands();
                        _lastPlacedT = t;
                        EditorUtility.SetDirty(_placer);
                    }
                }

                currentEvent.Use();
            }
        }

        Handles.color = Color.yellow;
        foreach (float t in _placer.placedTs)
        {
            _placer.splineContainer.Spline.Evaluate(t, out float3 pos, out float3 tan, out float3 up);
            Vector3 worldPos = _placer.splineContainer.transform.TransformPoint((Vector3)pos);
            Handles.SphereHandleCap(0, worldPos, Quaternion.identity, 0.3f, EventType.Repaint);
        }

        sceneView.Repaint();
    }
}

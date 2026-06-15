using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Random = UnityEngine.Random;

/// <summary>
/// Custom editor for <see cref="TreePlacer"/> providing a brush-based Scene view paint tool.
/// Supports placing and erasing trees along the spline track with weighted palette selection,
/// lateral offset sampling, yaw variance, and session-persistent brush settings.
/// </summary>
[CustomEditor(typeof(TreePlacer))]
public class TreePlacerEditor : Editor
{
    private enum PaintMode
    {
        None,
        Paint,
        Erase
    }

    private const float PaintButtonHeight = 35f;
    private const float TreeHandleRadius = 0.3f;
    private const float MaximumYawDegrees = 180f;
    private const float MaximumLateralOffset = 20f;
    private const float MinimumBrushRadius = 0.5f;
    private const float MaximumBrushRadius = 20f;
    private const float FoldoutIndentWidth = 10f;
    private const float OffsetPreviewAlpha = 0.15f;
    private const float BrushFillAlpha = 0.3f;
    private const float ExclusionArcAlpha = 0.4f;
    private const float BrushResizeStep = 0.5f;
    private const int ExclusionArcSegments = 8;

    private TreePlacer _placer = null;
    private TreePlacerEditorSettings _settings = null;
    private PaintMode _currentMode = PaintMode.None;
    private Vector3 _brushWorldPosition = Vector3.zero;
    private bool _isBrushPositionValid = false;
    private Vector3 _lastStrokePosition = Vector3.zero;
    private bool _hasStrokeOrigin = false;

    /// <summary>
    /// Set to true by <see cref="SetMode"/> and cleared at the end of the next Scene view frame.
    /// Causes <see cref="HandleMouseInput"/> to discard the first mouse event after a mode change,
    /// preventing Inspector button clicks from bleeding into the Scene view as paint or erase input.
    /// </summary>
    private bool _modeChangedThisFrame = false;

    private ReorderableList _prefabPaletteList = null;
    private readonly float _minimumStrokeDistanceWorld = 0f;
    private Vector2 _debugScrollPosition = Vector2.zero;
    private bool _prefabPaletteFoldout = true;
    private bool _placementFoldout = true;
    private bool _brushFoldout = true;
    private bool _modesFoldout = true;
    private bool _debugFoldout = false;
    private bool _feedbackFoldout = true;
    private bool _showDebugHandles = true;

    private void OnEnable()
    {
        _placer = (TreePlacer)target;
        _settings = TreePlacerEditorSettings.Load(_placer.GetInstanceID());
        _prefabPaletteList = BuildPrefabPaletteList();
        LoadFoldoutState(_placer.GetInstanceID());
        SceneView.duringSceneGui += OnSceneGuiDelegate;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGuiDelegate;
        _settings.Save(_placer.GetInstanceID());
        SaveFoldoutState(_placer.GetInstanceID());
        _currentMode = PaintMode.None;
    }

    private void LoadFoldoutState(int instanceId)
    {
        _prefabPaletteFoldout = SessionState.GetBool($"TreePlacerEditor_{instanceId}_foldout_prefabPalette", true);
        _placementFoldout = SessionState.GetBool($"TreePlacerEditor_{instanceId}_foldout_placement", true);
        _brushFoldout = SessionState.GetBool($"TreePlacerEditor_{instanceId}_foldout_brush", true);
        _modesFoldout = SessionState.GetBool($"TreePlacerEditor_{instanceId}_foldout_modes", true);
        _debugFoldout = SessionState.GetBool($"TreePlacerEditor_{instanceId}_foldout_debug", false);
        _feedbackFoldout = SessionState.GetBool($"TreePlacerEditor_{instanceId}_foldout_feedback", true);
        _showDebugHandles = SessionState.GetBool($"TreePlacerEditor_{instanceId}_showDebugHandles", true);
    }

    private void SaveFoldoutState(int instanceId)
    {
        SessionState.SetBool($"TreePlacerEditor_{instanceId}_foldout_prefabPalette", _prefabPaletteFoldout);
        SessionState.SetBool($"TreePlacerEditor_{instanceId}_foldout_placement", _placementFoldout);
        SessionState.SetBool($"TreePlacerEditor_{instanceId}_foldout_brush", _brushFoldout);
        SessionState.SetBool($"TreePlacerEditor_{instanceId}_foldout_modes", _modesFoldout);
        SessionState.SetBool($"TreePlacerEditor_{instanceId}_foldout_debug", _debugFoldout);
        SessionState.SetBool($"TreePlacerEditor_{instanceId}_foldout_feedback", _feedbackFoldout);
        SessionState.SetBool($"TreePlacerEditor_{instanceId}_showDebugHandles", _showDebugHandles);
    }

    public override void OnInspectorGUI()
    {
        bool placerFieldChanged = DrawDefaultInspectorWithChangeCheck();
        EditorGUILayout.Space();
        DrawPrefabPaletteFoldout();
        DrawPlacementFoldout();
        DrawBrushFoldout();
        DrawModesFoldout();
        DrawDebugFoldout();
        DrawFeedbackStrip();
        EditorGUILayout.Space();
        DrawClearAllButton();

        if (placerFieldChanged)
            OnPlacerFieldChanged();
    }

    private bool DrawDefaultInspectorWithChangeCheck()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        return EditorGUI.EndChangeCheck();
    }

    private void DrawPrefabPaletteFoldout()
    {
        _prefabPaletteFoldout = EditorGUILayout.Foldout(_prefabPaletteFoldout, "Prefab Palette", true);
        if (_prefabPaletteFoldout)
            DrawPrefabPalette();
    }

    private void DrawPlacementFoldout()
    {
        _placementFoldout = EditorGUILayout.Foldout(_placementFoldout, "Placement", true);
        if (_placementFoldout)
            DrawPlacementControls();
    }

    private void DrawBrushFoldout()
    {
        _brushFoldout = EditorGUILayout.Foldout(_brushFoldout, "Brush", true);
        if (_brushFoldout)
            DrawBrushControls();
    }

    private void DrawModesFoldout()
    {
        _modesFoldout = EditorGUILayout.Foldout(_modesFoldout, "Modes", true);
        if (!_modesFoldout) return;
        DrawModeButtons();
        DrawModeHelpBox();
    }

    private void DrawDebugFoldout()
    {
        _debugFoldout = EditorGUILayout.Foldout(_debugFoldout, "Debug", true);
        if (!_debugFoldout) return;
        _showDebugHandles = EditorGUILayout.Toggle("Show Handles & Arcs", _showDebugHandles);
        DrawForceRebuildButton();
        DrawDebugRecordList();
    }

    private void DrawForceRebuildButton()
    {
        if (!GUILayout.Button("Force Rebuild")) return;
        Undo.RegisterCompleteObjectUndo(new Object[] { _placer, _placer.gameObject }, "Force Rebuild");
        _placer.RebuildAll();
        EditorUtility.SetDirty(_placer);
    }

    private void DrawFeedbackStrip()
    {
        string modeText = _currentMode.ToString();
        string countText = $"Trees: {_placer.PlacementRecords.Count}";
        string validText = _isBrushPositionValid ? "Brush: valid" : "Brush: invalid";
        string positionText = $"Pos: ({_brushWorldPosition.x:F2}, {_brushWorldPosition.y:F2}, {_brushWorldPosition.z:F2})";
        EditorGUILayout.HelpBox($"{modeText}  |  {countText}  |  {validText}  |  {positionText}", MessageType.None);
    }

    private void DrawDebugRecordList()
    {
        int displayCount = Mathf.Min(_placer.PlacementRecords.Count, 50);
        _debugScrollPosition = EditorGUILayout.BeginScrollView(_debugScrollPosition, GUILayout.MaxHeight(150f));
        for (int index = 0; index < displayCount; index++)
        {
            TreePlacementRecord record = _placer.PlacementRecords[index];
            EditorGUILayout.LabelField($"[{index}] T={record.NormalizedT:F2} Offset={record.SignedLateralOffset:F2}");
        }
        EditorGUILayout.EndScrollView();
        if (50 < _placer.PlacementRecords.Count)
            EditorGUILayout.HelpBox($"Showing 50 of {_placer.PlacementRecords.Count} records.", MessageType.None);
    }

    private void DrawPrefabPalette()
    {
        _prefabPaletteList.DoLayoutList();
        if (_placer.TreePalette.Count == 0)
            EditorGUILayout.HelpBox("Add at least one prefab to paint.", MessageType.Warning);
    }

    private ReorderableList BuildPrefabPaletteList()
    {
        ReorderableList list = new ReorderableList(_placer.TreePalette, typeof(WeightedPrefab), true, true, true, true);
        list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Prefabs");
        list.drawElementCallback = DrawPaletteElement;
        list.onChangedCallback = _ => EditorUtility.SetDirty(_placer);
        return list;
    }

    private void DrawPaletteElement(Rect elementRectangle, int index, bool isActive, bool isFocused)
    {
        WeightedPrefab entry = _placer.TreePalette[index];
        float halfWidth = elementRectangle.width / 2f;
        Rect prefabRectangle = new Rect(elementRectangle.x, elementRectangle.y + 1f, halfWidth - 4f, EditorGUIUtility.singleLineHeight);
        Rect weightRectangle = new Rect(elementRectangle.x + halfWidth + 4f, elementRectangle.y + 1f, halfWidth - 4f, EditorGUIUtility.singleLineHeight);
        EditorGUI.BeginChangeCheck();
        entry.Prefab = (GameObject)EditorGUI.ObjectField(prefabRectangle, entry.Prefab, typeof(GameObject), false);
        entry.Weight = EditorGUI.Slider(weightRectangle, entry.Weight, 0f, 1f);
        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_placer);
    }

    private void DrawPlacementControls()
    {
        _settings.BaseLateralOffset = EditorGUILayout.Slider("Base Lateral Offset", _settings.BaseLateralOffset, 0f, MaximumLateralOffset);
        _settings.LateralOffsetVariance = EditorGUILayout.Slider("Lateral Variance", _settings.LateralOffsetVariance, 0f, MaximumLateralOffset);
        _settings.BaseYawDegrees = EditorGUILayout.Slider("Base Yaw", _settings.BaseYawDegrees, -MaximumYawDegrees, MaximumYawDegrees);
        _settings.YawVarianceDegrees = EditorGUILayout.Slider("Yaw Variance", _settings.YawVarianceDegrees, 0f, MaximumYawDegrees);
    }

    private void DrawBrushControls()
    {
        _settings.BrushRadius = EditorGUILayout.Slider("Brush Radius", _settings.BrushRadius, MinimumBrushRadius, MaximumBrushRadius);
        _settings.BrushScatterStrength = EditorGUILayout.Slider("Scatter Strength", _settings.BrushScatterStrength, 0f, 1f);
        _settings.MinimumStrokeDistance = EditorGUILayout.Slider("Min Stroke Distance", _settings.MinimumStrokeDistance, 0f, _settings.BrushRadius);
    }

    private void DrawModeButtons()
    {
        EditorGUILayout.BeginHorizontal();
        DrawPaintButton();
        DrawEraseButton();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPaintButton()
    {
        GUI.color = _currentMode == PaintMode.Paint ? Color.green : Color.white;
        if (GUILayout.Button("Paint", GUILayout.Height(PaintButtonHeight)))
            SetMode(_currentMode == PaintMode.Paint ? PaintMode.None : PaintMode.Paint);
        GUI.color = Color.white;
    }

    private void DrawEraseButton()
    {
        GUI.color = _currentMode == PaintMode.Erase ? Color.red : Color.white;
        if (GUILayout.Button("Erase", GUILayout.Height(PaintButtonHeight)))
            SetMode(_currentMode == PaintMode.Erase ? PaintMode.None : PaintMode.Erase);
        GUI.color = Color.white;
    }

    /// <summary>
    /// Sets the active mode and clears transient brush and stroke state so no stale input
    /// carries over across transitions. Forces an immediate repaint of the Inspector and all Scene views.
    /// </summary>
    private void SetMode(PaintMode newMode)
    {
        _currentMode = newMode;
        _hasStrokeOrigin = false;
        _isBrushPositionValid = false;
        _modeChangedThisFrame = true;
        SceneView.RepaintAll();
        Repaint();
    }

    private void DrawModeHelpBox()
    {
        EditorGUILayout.HelpBox(GetModeHelpText(), MessageType.Info);
    }

    private string GetModeHelpText()
    {
        switch (_currentMode)
        {
            case PaintMode.Paint: return "Click or drag to paint trees. [ / ] to resize brush. P to toggle.";
            case PaintMode.Erase: return "Click to remove the nearest tree within brush radius. E to toggle.";
            default: return "Select Paint or Erase, then click in the Scene view.";
        }
    }

    private void DrawClearAllButton()
    {
        if (!GUILayout.Button("Clear All")) return;
        bool confirmed = EditorUtility.DisplayDialog("Clear All Trees", "Are you sure?", "Yes", "Cancel");
        if (!confirmed) return;
        Undo.RegisterCompleteObjectUndo(new Object[] { _placer, _placer.gameObject }, "Clear All Trees");
        _placer.PlacementRecords.Clear();
        _placer.ClearAll();
        EditorUtility.SetDirty(_placer);
    }

    private void OnPlacerFieldChanged()
    {
        _placer.RebuildAll();
        EditorUtility.SetDirty(_placer);
    }

    private void OnSceneGuiDelegate(SceneView sceneView)
    {
        if (!IsToolActive()) return;
        SuppressDefaultSceneViewControl();
        UpdateBrushWorldPosition(Event.current);
        HandleMouseInput(Event.current);
        DrawBrushPreview();
        DrawPlacedTreeHandles();
        sceneView.Repaint();
        _modeChangedThisFrame = false;
    }

    private bool IsToolActive()
    {
        if (_currentMode == PaintMode.None) return false;
        if (_placer.splineContainer == null) return false;
        if (_placer.TreePalette.Count == 0) return false;
        return true;
    }

    private void SuppressDefaultSceneViewControl()
    {
        // Without this, left-clicks in paint mode also trigger Unity's object selection.
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
    }

    private void UpdateBrushWorldPosition(Event currentEvent)
    {
        Ray mouseRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        if (TryRaycastSceneGeometry(mouseRay, out Vector3 hitPoint))
        {
            _brushWorldPosition = hitPoint;
            _isBrushPositionValid = true;
            return;
        }
        TryRaycastGroundPlane(mouseRay);
    }

    private bool TryRaycastSceneGeometry(Ray ray, out Vector3 hitPoint)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            hitPoint = Vector3.zero;
            return false;
        }
        hitPoint = hit.point;
        return true;
    }

    private void TryRaycastGroundPlane(Ray ray)
    {
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (!groundPlane.Raycast(ray, out float hitDistance))
        {
            _isBrushPositionValid = false;
            return;
        }
        _brushWorldPosition = ray.GetPoint(hitDistance);
        _isBrushPositionValid = true;
    }

    private void HandleMouseInput(Event currentEvent)
    {
        if (currentEvent.button != 0) return;
        if (currentEvent.type != EventType.MouseDown && currentEvent.type != EventType.MouseDrag) return;
        if (_modeChangedThisFrame)
        {
            _modeChangedThisFrame = false;
            currentEvent.Use();
            return;
        }
        if (_currentMode == PaintMode.Paint) HandlePaintInput(currentEvent);
        if (_currentMode == PaintMode.Erase) HandleEraseInput();
        currentEvent.Use();
    }

    private void HandlePaintInput(Event currentEvent)
    {
        if (!_isBrushPositionValid) return;
        // Reset on each mouse-down so every drag stroke becomes its own collapsible undo group.
        if (currentEvent.type == EventType.MouseDown) _hasStrokeOrigin = false;
        if (!HasMetMinimumStrokeDistance()) return;
        Undo.SetCurrentGroupName("Paint Tree");
        int undoGroup = Undo.GetCurrentGroup();
        TryPaintTree();
        _lastStrokePosition = _brushWorldPosition;
        _hasStrokeOrigin = true;
        Undo.CollapseUndoOperations(undoGroup);
    }

    private void HandleEraseInput()
    {
        if (!_isBrushPositionValid) return;
        int nearestIndex = FindNearestRecordWithinBrush();
        if (nearestIndex < 0) return;
        Undo.RegisterCompleteObjectUndo(new Object[] { _placer, _placer.gameObject }, "Erase Tree");
        _placer.PlacementRecords.RemoveAt(nearestIndex);
        _placer.DestroyRecordAt(nearestIndex);
        EditorUtility.SetDirty(_placer);
    }

    private bool HasMetMinimumStrokeDistance()
    {
        if (!_hasStrokeOrigin) return true;
        return Vector3.Distance(_lastStrokePosition, _brushWorldPosition) > _settings.MinimumStrokeDistance;
    }

    private void TryPaintTree()
    {
        Spline spline = _placer.splineContainer.Spline;
        Transform containerTransform = _placer.splineContainer.transform;
        float splineProgress = TreePlacerEditorUtils.GetNearestSplineTParameter(spline, containerTransform, _brushWorldPosition);
        bool isOnRight = TreePlacerEditorUtils.IsOnRightSideOfSpline(spline, containerTransform, _brushWorldPosition, splineProgress);
        float sideSign = isOnRight ? 1f : -1f;
        if (TreePlacerEditorUtils.IsTooCloseToExistingRecord(_placer.PlacementRecords, spline, splineProgress, sideSign, _placer.spacing)) return;
        PlaceTree(splineProgress, sideSign);
    }

    private void PlaceTree(float splineProgress, float sideSign)
    {
        Undo.RegisterCompleteObjectUndo(new Object[] { _placer, _placer.gameObject }, "Paint Tree");
        TreePlacementRecord record = BuildPlacementRecord(splineProgress, sideSign);
        _placer.PlacementRecords.Add(record);
        _placer.SpawnRecord(record);
        EditorUtility.SetDirty(_placer);
    }

    private TreePlacementRecord BuildPlacementRecord(float splineProgress, float sideSign)
    {
        return new TreePlacementRecord
        {
            NormalizedT = splineProgress,
            SignedLateralOffset = SampleLateralOffset(sideSign),
            YawDegrees = SampleYaw(),
            UniformScale = SampleScale(),
            PrefabIndex = SamplePrefabIndex()
        };
    }

    private int FindNearestRecordWithinBrush()
    {
        int nearestIndex = -1;
        float nearestDistance = _settings.BrushRadius;
        for (int index = 0; index < _placer.PlacementRecords.Count; index++)
            UpdateNearestRecord(index, ref nearestIndex, ref nearestDistance);
        return nearestIndex;
    }

    private void UpdateNearestRecord(int index, ref int nearestIndex, ref float nearestDistance)
    {
        float distance = Vector3.Distance(_brushWorldPosition, GetRecordWorldPosition(_placer.PlacementRecords[index]));
        if (distance >= nearestDistance) return;
        nearestDistance = distance;
        nearestIndex = index;
    }

    private Vector3 GetRecordWorldPosition(TreePlacementRecord record)
    {
        Spline spline = _placer.splineContainer.Spline;
        Transform containerTransform = _placer.splineContainer.transform;
        spline.Evaluate(record.NormalizedT, out float3 localPosition, out float3 localTangent, out _);
        Vector3 worldPosition = containerTransform.TransformPoint((Vector3)localPosition);
        Vector3 worldTangent = containerTransform.TransformDirection((Vector3)localTangent).normalized;
        Vector3 right = Vector3.Cross(worldTangent, Vector3.up).normalized;
        return worldPosition + right * record.SignedLateralOffset;
    }

    private float SampleLateralOffset(float sideSign)
    {
        return (_placer.trackOffset + _settings.BaseLateralOffset + SampleVariance(_settings.LateralOffsetVariance)) * sideSign + SampleSignedScatter();
    }

    private float SampleSignedScatter()
    {
        return Random.Range(-_settings.BrushRadius, _settings.BrushRadius) * _settings.BrushScatterStrength;
    }

    private float SampleYaw()
    {
        return _settings.BaseYawDegrees + Random.Range(-_settings.YawVarianceDegrees, _settings.YawVarianceDegrees);
    }

    private float SampleVariance(float range)
    {
        return Random.Range(0f, range);
    }

    private float SampleScale()
    {
        return Random.Range(_placer.minScale, _placer.maxScale);
    }

    /// <summary>
    /// Selects a palette index using normalised weights: sums total weight, picks a random threshold,
    /// and returns the first index whose accumulated weight meets or exceeds the threshold.
    /// Returns 0 when total weight is zero.
    /// </summary>
    private int SamplePrefabIndex()
    {
        float totalWeight = ComputeTotalPaletteWeight();
        if (totalWeight <= 0f) return 0;
        float threshold = Random.Range(0f, totalWeight);
        float accumulated = 0f;
        for (int index = 0; index < _placer.TreePalette.Count; index++)
        {
            accumulated += _placer.TreePalette[index].Weight;
            if (threshold <= accumulated) return index;
        }
        return _placer.TreePalette.Count - 1;
    }

    private float ComputeTotalPaletteWeight()
    {
        float total = 0f;
        foreach (WeightedPrefab prefab in _placer.TreePalette)
            total += prefab.Weight;
        return total;
    }

    private void DrawBrushPreview()
    {
        if (!_isBrushPositionValid) return;
        Color brushColor = _currentMode == PaintMode.Erase ? Color.red : Color.yellow;
        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, BrushFillAlpha);
        Handles.DrawSolidDisc(_brushWorldPosition, Vector3.up, _settings.BrushRadius);
        Handles.color = brushColor;
        Handles.DrawWireDisc(_brushWorldPosition, Vector3.up, _settings.BrushRadius);
        DrawOffsetPreviewRing();
    }

    private void DrawOffsetPreviewRing()
    {
        if (_placer.splineContainer == null || !_isBrushPositionValid) return;
        Spline spline = _placer.splineContainer.Spline;
        Transform containerTransform = _placer.splineContainer.transform;
        float splineProgress = TreePlacerEditorUtils.GetNearestSplineTParameter(spline, containerTransform, _brushWorldPosition);
        bool isOnRight = TreePlacerEditorUtils.IsOnRightSideOfSpline(spline, containerTransform, _brushWorldPosition, splineProgress);
        Vector3 previewCenter = ComputeOffsetPreviewCenter(spline, containerTransform, splineProgress, isOnRight);
        float previewRadius = _settings.BrushRadius * 0.25f;
        DrawOffsetPreviewDiscs(previewCenter, previewRadius);
    }

    private Vector3 ComputeOffsetPreviewCenter(Spline spline, Transform containerTransform, float splineProgress, bool isOnRight)
    {
        float sideSign = isOnRight ? 1f : -1f;
        Vector3 rightAxis = ComputeWorldRightAtSplineProgress(spline, containerTransform, splineProgress);
        return _brushWorldPosition + rightAxis * _settings.BaseLateralOffset * sideSign;
    }

    private void DrawOffsetPreviewDiscs(Vector3 center, float radius)
    {
        Handles.color = new Color(0f, 1f, 1f, OffsetPreviewAlpha);
        Handles.DrawSolidDisc(center, Vector3.up, radius);
        Handles.color = Color.cyan;
        Handles.DrawWireDisc(center, Vector3.up, radius);
    }

    private Vector3 ComputeWorldRightAtSplineProgress(Spline spline, Transform containerTransform, float splineProgress)
    {
        spline.Evaluate(splineProgress, out float3 _, out float3 localTangent, out _);
        Vector3 worldTangent = containerTransform.TransformDirection((Vector3)localTangent).normalized;
        return Vector3.Cross(worldTangent, Vector3.up).normalized;
    }

    private void DrawPlacedTreeHandles()
    {
        if (!_showDebugHandles && _currentMode != PaintMode.Erase) return;
        int nearestEraseIndex = GetNearestEraseIndex();
        DrawExclusionZoneArcs();
        for (int index = 0; index < _placer.PlacementRecords.Count; index++)
        {
            Vector3 worldPosition = GetRecordWorldPosition(_placer.PlacementRecords[index]);
            bool isNearest = index == nearestEraseIndex;
            Handles.color = isNearest ? Color.red : Color.green;
            float radius = isNearest ? TreeHandleRadius * 2f : TreeHandleRadius;
            Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, radius, EventType.Repaint);
        }
    }

    private int GetNearestEraseIndex()
    {
        if (_currentMode != PaintMode.Erase || !_isBrushPositionValid) return -1;
        return FindNearestRecordWithinBrush();
    }

    private void DrawExclusionZoneArcs()
    {
        if (!_showDebugHandles) return;
        if (_placer.splineContainer == null) return;
        Spline spline = _placer.splineContainer.Spline;
        float splineLength = spline.GetLength();
        Handles.color = new Color(1f, 0.5f, 0f, ExclusionArcAlpha);
        foreach (TreePlacementRecord record in _placer.PlacementRecords)
            DrawExclusionArc(record, spline, splineLength, _placer.spacing);
    }

    private void DrawExclusionArc(TreePlacementRecord record, Spline spline, float splineLength, float spacingDistance)
    {
        float recordDistance = SplineUtility.ConvertIndexUnit(spline, record.NormalizedT, PathIndexUnit.Normalized, PathIndexUnit.Distance);
        float startDistance = recordDistance - spacingDistance;
        float endDistance = recordDistance + spacingDistance;
        float startProgress = SplineUtility.ConvertIndexUnit(spline, Mathf.Repeat(startDistance, splineLength), PathIndexUnit.Distance, PathIndexUnit.Normalized);
        Vector3 previousPoint = GetSplineWorldPosition(startProgress);
        for (int segment = 1; segment <= ExclusionArcSegments; segment++)
        {
            float sampleDistance = startDistance + (endDistance - startDistance) * segment / ExclusionArcSegments;
            float sampleProgress = SplineUtility.ConvertIndexUnit(spline, Mathf.Repeat(sampleDistance, splineLength), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            Vector3 segmentPoint = GetSplineWorldPosition(sampleProgress);
            Handles.DrawLine(previousPoint, segmentPoint);
            previousPoint = segmentPoint;
        }
    }

    private Vector3 GetSplineWorldPosition(float splineProgress)
    {
        _placer.splineContainer.Spline.Evaluate(splineProgress, out float3 localPosition, out _, out _);
        return _placer.splineContainer.transform.TransformPoint((Vector3)localPosition);
    }
}

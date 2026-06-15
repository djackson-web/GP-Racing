using UnityEngine;

public class AIDebugOverlay : MonoBehaviour
{
    [SerializeField] private bool _visible;
    [SerializeField] private RacerBase _player;

    private const float PanelX = 10f;
    private const float PanelY = 10f;
    private const float PanelWidth = 728f;
    private const float RowHeight = 20f;
    private const float HeaderHeight = 24f;
    private const float Padding = 6f;
    private const int RefreshEvery = 60;
    private const int LabelFontSize = 11;

    private static readonly Color RowTextColor = new Color(0.9f, 0.9f, 0.9f);
    private static readonly Color DefendStateColor = new Color(0.4f, 0.6f, 1f);
    private static readonly Color PlayerRowColor = new Color(1f, 0.85f, 0.2f);

    private static readonly float[] ColumnWidths = { 90f, 68f, 56f, 44f, 44f, 56f, 56f, 56f, 56f, 46f, 40f, 58f, 58f };
    private static readonly string[] ColumnHeaders =
        { "Car Name", "State", "Speed", "Throt", "Brake", "TargSpd", "LatOff", "TargLat", "Band", "BandOn", "Lap", "CurLap", "LastLap" };

    private PlayerController _playerController;
    private AIController[] _controllers = new AIController[0];
    private int _frameCounter;

    private float[] _aiLapStarts = new float[0];
    private float[] _aiLastLaps = new float[0];
    private float[] _aiNextLapThresholds = new float[0];

    private float _playerLapStart;
    private float _playerLastLap = -1f;
    private float _playerNextLapThreshold = -1f;

    private GUIStyle _headerStyle = new GUIStyle();
    private GUIStyle _rowStyle = new GUIStyle();
    private GUIStyle _playerStyle = new GUIStyle();
    private GUIStyle _stateRaceStyle = new GUIStyle();
    private GUIStyle _stateOvertakeStyle = new GUIStyle();
    private GUIStyle _stateDefendStyle = new GUIStyle();
    private GUIStyle _stateRecoverStyle = new GUIStyle();
    private GUIStyle _bandBoostStyle = new GUIStyle();
    private GUIStyle _bandSlowStyle = new GUIStyle();
    private GUIStyle _bandActiveStyle = new GUIStyle();
    private GUIStyle _bandInactiveStyle = new GUIStyle();
    private bool _stylesReady;

    private void Start()
    {
        if (_player != null)
        {
            _playerController = _player.GetComponent<PlayerController>();
        }
    }

    private void Update()
    {
        UpdateLapTimes();

        if (!_visible)
        {
            return;
        }

        _frameCounter++;
        if (_frameCounter >= RefreshEvery)
        {
            _frameCounter = 0;
            _controllers = FindObjectsByType<AIController>(FindObjectsSortMode.None);
            System.Array.Sort(_controllers, CompareControllersByName);
            EnsureLapArrays();
        }
    }

    private void UpdateLapTimes()
    {
        if (_player != null)
        {
            float progress = _player.SplineProgress;
            if (_playerNextLapThreshold < 0f)
            {
                _playerNextLapThreshold = progress + 1f;
                _playerLapStart = Time.time;
            }
            else if (progress >= _playerNextLapThreshold)
            {
                _playerLastLap = Time.time - _playerLapStart;
                _playerLapStart = Time.time;
                _playerNextLapThreshold += 1f;
            }
        }

        for (int i = 0; i < _controllers.Length; i++)
        {
            if (_controllers[i] == null || i >= _aiNextLapThresholds.Length) continue;
            float progress = _controllers[i].SplineProgress;
            if (_aiNextLapThresholds[i] < 0f)
            {
                _aiNextLapThresholds[i] = progress + 1f;
                _aiLapStarts[i] = Time.time;
            }
            else if (progress >= _aiNextLapThresholds[i])
            {
                _aiLastLaps[i] = Time.time - _aiLapStarts[i];
                _aiLapStarts[i] = Time.time;
                _aiNextLapThresholds[i] += 1f;
            }
        }
    }

    private void EnsureLapArrays()
    {
        int count = _controllers.Length;
        if (_aiLapStarts.Length == count) return;

        float[] newStarts = new float[count];
        float[] newLasts = new float[count];
        float[] newThresholds = new float[count];

        int copy = Mathf.Min(count, _aiLapStarts.Length);
        System.Array.Copy(_aiLapStarts, newStarts, copy);
        System.Array.Copy(_aiLastLaps, newLasts, copy);
        System.Array.Copy(_aiNextLapThresholds, newThresholds, copy);

        for (int i = copy; i < count; i++)
        {
            newStarts[i] = Time.time;
            newLasts[i] = -1f;
            newThresholds[i] = -1f;
        }

        _aiLapStarts = newStarts;
        _aiLastLaps = newLasts;
        _aiNextLapThresholds = newThresholds;
    }

    private void OnGUI()
    {
        if (!_visible) return;

        if (!_stylesReady)
        {
            InitStyles();
        }

        int rowCount = _controllers.Length + 1;
        float panelHeight = Padding + HeaderHeight + rowCount * RowHeight + Padding;
        GUI.Box(new Rect(PanelX, PanelY, PanelWidth, panelHeight), GUIContent.none);

        float currentY = PanelY + Padding;
        float leftEdge = PanelX + Padding;

        DrawHeaderRow(leftEdge, currentY);
        currentY += HeaderHeight;

        DrawPlayerRow(leftEdge, currentY);
        currentY += RowHeight;

        for (int i = 0; i < _controllers.Length; i++)
        {
            if (_controllers[i] == null) continue;
            DrawDataRow(_controllers[i], i, leftEdge, currentY);
            currentY += RowHeight;
        }
    }

    private int CompareControllersByName(AIController a, AIController b)
    {
        return string.Compare(a.name, b.name, System.StringComparison.Ordinal);
    }

    private void DrawHeaderRow(float startX, float startY)
    {
        float x = startX;
        for (int i = 0; i < ColumnHeaders.Length; i++)
        {
            x = DrawCell(x, startY, i, ColumnHeaders[i], HeaderHeight, _headerStyle);
        }
    }

    private void DrawPlayerRow(float startX, float startY)
    {
        if (_player == null)
        {
            DrawCell(startX, startY, 0, "PLAYER — assign in Inspector", RowHeight, _stateRecoverStyle);
            return;
        }

        float curLap = _playerNextLapThreshold >= 0f ? Time.time - _playerLapStart : 0f;
        float throttle = _playerController != null ? _playerController.DebugThrottle : 0f;
        float brake = _playerController != null ? _playerController.DebugBrake : 0f;
        GUIStyle throttleStyle = throttle > 0.01f ? _bandBoostStyle : _playerStyle;
        GUIStyle brakeStyle = brake > 0.01f ? _bandSlowStyle : _playerStyle;
        float x = startX;
        x = DrawCell(x, startY, 0, "PLAYER", RowHeight, _playerStyle);
        x = DrawCell(x, startY, 1, "—", RowHeight, _playerStyle);
        x = DrawCell(x, startY, 2, _player.CurrentSpeed.ToString("F0"), RowHeight, _playerStyle);
        x = DrawCell(x, startY, 3, FormatPercent(throttle), RowHeight, throttleStyle);
        x = DrawCell(x, startY, 4, FormatPercent(brake), RowHeight, brakeStyle);
        x = DrawCell(x, startY, 5, "—", RowHeight, _playerStyle);
        x = DrawCell(x, startY, 6, _player.LateralPosition.ToString("F2"), RowHeight, _playerStyle);
        x = DrawCell(x, startY, 7, "—", RowHeight, _playerStyle);
        x = DrawCell(x, startY, 8, "—", RowHeight, _playerStyle);
        x = DrawCell(x, startY, 9, "—", RowHeight, _playerStyle);
        x = DrawCell(x, startY, 10, _player.LapCount.ToString(), RowHeight, _playerStyle);
        x = DrawCell(x, startY, 11, FormatLapTime(curLap), RowHeight, _playerStyle);
        DrawCell(x, startY, 12, FormatLapTime(_playerLastLap), RowHeight, _playerStyle);
    }

    private void DrawDataRow(AIController ai, int index, float startX, float startY)
    {
        AIController.AIState state = ai.DebugState;
        float curLap = index < _aiLapStarts.Length ? Time.time - _aiLapStarts[index] : 0f;
        float lastLap = index < _aiLastLaps.Length ? _aiLastLaps[index] : -1f;

        GUIStyle throttleStyle = ai.DebugThrottle > 0.01f ? _bandBoostStyle : _rowStyle;
        GUIStyle brakeStyle = ai.DebugBrake > 0.01f ? _bandSlowStyle : _rowStyle;
        float x = startX;
        x = DrawCell(x, startY, 0, ai.gameObject.name, RowHeight, _rowStyle);
        x = DrawCell(x, startY, 1, state.ToString(), RowHeight, GetStyleForState(state));
        x = DrawCell(x, startY, 2, ai.CurrentSpeed.ToString("F0"), RowHeight, _rowStyle);
        x = DrawCell(x, startY, 3, FormatPercent(ai.DebugThrottle), RowHeight, throttleStyle);
        x = DrawCell(x, startY, 4, FormatPercent(ai.DebugBrake), RowHeight, brakeStyle);
        x = DrawCell(x, startY, 5, ai.DebugTargetSpeed.ToString("F0"), RowHeight, _rowStyle);
        x = DrawCell(x, startY, 6, ai.LateralPosition.ToString("F2"), RowHeight, _rowStyle);
        x = DrawCell(x, startY, 7, ai.DebugTargetLateralOffset.ToString("F2"), RowHeight, _rowStyle);
        float banding = ai.DebugBandingMultiplier;
        GUIStyle bandStyle = banding > 1.001f ? _bandBoostStyle : banding < 0.999f ? _bandSlowStyle : _rowStyle;
        x = DrawCell(x, startY, 8, banding.ToString("F3"), RowHeight, bandStyle);
        GUIStyle bandActiveStyle = ai.DebugBandingActive ? _bandActiveStyle : _bandInactiveStyle;
        x = DrawCell(x, startY, 9, ai.DebugBandingActive ? "YES" : "no", RowHeight, bandActiveStyle);
        x = DrawCell(x, startY, 10, ai.LapCount.ToString(), RowHeight, _rowStyle);
        x = DrawCell(x, startY, 11, FormatLapTime(curLap), RowHeight, _rowStyle);
        DrawCell(x, startY, 12, FormatLapTime(lastLap), RowHeight, _rowStyle);
    }

    private static string FormatPercent(float value)
    {
        if (value <= 0.01f) return "—";
        return Mathf.RoundToInt(value * 100f).ToString();
    }

    private static string FormatLapTime(float seconds)
    {
        if (seconds < 0f) return "--:--.--";
        int mins = (int)(seconds / 60f);
        float secs = seconds % 60f;
        return $"{mins}:{secs:00.00}";
    }

    private float DrawCell(float x, float startY, int columnIndex, string text, float height, GUIStyle style)
    {
        GUI.Label(new Rect(x, startY, ColumnWidths[columnIndex], height), text, style);
        return x + ColumnWidths[columnIndex];
    }

    private GUIStyle GetStyleForState(AIController.AIState state)
    {
        if (state == AIController.AIState.Race) return _stateRaceStyle;
        if (state == AIController.AIState.Overtake) return _stateOvertakeStyle;
        if (state == AIController.AIState.Defend) return _stateDefendStyle;
        if (state == AIController.AIState.Recover) return _stateRecoverStyle;
        return _rowStyle;
    }

    private void InitStyles()
    {
        _headerStyle = MakeStyle(Color.white, bold: true, size: LabelFontSize);
        _rowStyle = MakeStyle(RowTextColor, size: LabelFontSize);
        _playerStyle = MakeStyle(PlayerRowColor, bold: true, size: LabelFontSize);
        _stateRaceStyle = MakeStyle(Color.green, bold: true, size: LabelFontSize);
        _stateOvertakeStyle = MakeStyle(Color.yellow, bold: true, size: LabelFontSize);
        _stateDefendStyle = MakeStyle(DefendStateColor, bold: true, size: LabelFontSize);
        _stateRecoverStyle = MakeStyle(Color.red, bold: true, size: LabelFontSize);
        _bandBoostStyle = MakeStyle(Color.green, size: LabelFontSize);
        _bandSlowStyle = MakeStyle(Color.red, size: LabelFontSize);
        _bandActiveStyle = MakeStyle(Color.green, bold: true, size: LabelFontSize);
        _bandInactiveStyle = MakeStyle(new Color(0.5f, 0.5f, 0.5f), size: LabelFontSize);
        _stylesReady = true;
    }

    private GUIStyle MakeStyle(Color color, bool bold = false, bool italic = false, int size = LabelFontSize)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = size;
        if (bold && italic) style.fontStyle = FontStyle.BoldAndItalic;
        else if (bold) style.fontStyle = FontStyle.Bold;
        else if (italic) style.fontStyle = FontStyle.Italic;
        style.normal.textColor = color;
        return style;
    }
}

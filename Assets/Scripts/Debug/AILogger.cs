using System.IO;
using UnityEngine;

/// <summary>
/// Writes a CSV log of every AI car's state to AI_log.txt at the project root.
/// Attach to any scene GameObject. Toggle logging with the enabled checkbox.
/// </summary>
public class AILogger : MonoBehaviour
{
    [SerializeField] private float _logInterval = 0.5f;
    [SerializeField] private bool _logStateChanges = true;
    [SerializeField] private string _fileName = "AI_log.txt";

    private StreamWriter _writer;
    private AIController[] _controllers = new AIController[0];
    private AIController.AIState[] _lastStates = new AIController.AIState[0];
    private float _nextLogTime;

    private void Start()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", _fileName));
        _writer = new StreamWriter(path, append: false);
        _writer.WriteLine("Time,Car,State,Speed,TargetSpeed,SplineProgress,Lap,LateralOffset,Banding,Event");
        _writer.Flush();
        Debug.Log($"[AILogger] Logging to: {path}");

        RefreshControllers();
        _nextLogTime = Time.time + _logInterval;
    }

    private void Update()
    {
        if (_logStateChanges)
        {
            CheckStateChanges();
        }

        if (Time.time >= _nextLogTime)
        {
            WriteAll("tick");
            _nextLogTime = Time.time + _logInterval;
        }
    }

    private void CheckStateChanges()
    {
        for (int controllerIndex = 0; controllerIndex < _controllers.Length; controllerIndex++)
        {
            if (_controllers[controllerIndex] == null)
            {
                continue;
            }

            AIController.AIState current = _controllers[controllerIndex].DebugState;
            if (current != _lastStates[controllerIndex])
            {
                WriteRow(_controllers[controllerIndex], $"state:{_lastStates[controllerIndex]}->{current}");
                _lastStates[controllerIndex] = current;
            }
        }
    }

    private void WriteAll(string evt)
    {
        foreach (AIController aiController in _controllers)
        {
            if (aiController != null)
            {
                WriteRow(aiController, evt);
            }
        }
    }

    private void WriteRow(AIController aiController, string evt)
    {
        // SplineProgress is lap-accumulating (e.g. 2.75 = lap 2, 75% through).
        // We log both the raw value and the fractional position within the lap.
        float raw = aiController.SplineProgress;
        float frac = raw - Mathf.Floor(raw);

        _writer.WriteLine(
            $"{Time.time:F2}," +
            $"{aiController.gameObject.name}," +
            $"{aiController.DebugState}," +
            $"{aiController.CurrentSpeed:F1}," +
            $"{aiController.DebugTargetSpeed:F1}," +
            $"{frac:F4}," +
            $"{aiController.LapCount}," +
            $"{aiController.LateralPosition:F2}," +
            $"{aiController.DebugBandingMultiplier:F3}," +
            $"{evt}");
        _writer.Flush();
    }

    [ContextMenu("Refresh AI List")]
    private void RefreshControllers()
    {
        _controllers = FindObjectsByType<AIController>(FindObjectsSortMode.None);
        System.Array.Sort(_controllers, (a, b) =>
            string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        _lastStates = new AIController.AIState[_controllers.Length];
        for (int controllerIndex = 0; controllerIndex < _controllers.Length; controllerIndex++)
        {
            _lastStates[controllerIndex] = _controllers[controllerIndex].DebugState;
        }

        Debug.Log($"[AILogger] Tracking {_controllers.Length} AI cars.");
    }

    private void OnDestroy()
    {
        if (_writer != null)
        {
            _writer.Close();
        }
    }
}

using System;
using UnityEngine;

public class RaceStandingsTracker : MonoBehaviour
{
    public static RaceStandingsTracker Instance { get; private set; }

    [SerializeField] private int _totalLaps = 3;

    private RacerBase[] _racers;
    private RacerBase[] _standings;

    public bool IsRaceComplete { get; private set; }
    public float RaceStartTime { get; private set; } = -1f;

    public int TotalLaps
    {
        get { return _totalLaps; }
    }

    public event Action OnRaceComplete;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        RacerBase[] found = FindObjectsOfType<RacerBase>();
        _racers = Array.FindAll(found, r => r != null);
        _standings = new RacerBase[_racers.Length];
        Array.Copy(_racers, _standings, _racers.Length);
    }

    public void StartRace()
    {
        RaceStartTime = Time.time;
        foreach (RacerBase racer in _racers)
        {
            AIController aiController = racer as AIController;
            if (aiController != null)
            {
                aiController.SetRaceStartTime(RaceStartTime);
            }
        }
    }

    private void Update()
    {
        if (_standings == null)
        {
            return;
        }

        Array.Sort(_standings, (a, b) =>
        {
            if (a == null) return 1;
            if (b == null) return -1;
            return b.RaceProgress.CompareTo(a.RaceProgress);
        });

        bool leaderFinished = _standings.Length > 0
            && _standings[0] != null
            && _standings[0].LapCount >= _totalLaps;

        if (!IsRaceComplete && leaderFinished)
        {
            IsRaceComplete = true;
            if (OnRaceComplete != null)
            {
                OnRaceComplete.Invoke();
            }
        }
    }

    public RacerBase[] GetStandings()
    {
        return _standings;
    }

    public int GetPosition(RacerBase racer)
    {
        for (int i = 0; i < _standings.Length; i++)
        {
            if (_standings[i] == racer)
            {
                return i + 1;
            }
        }
        return -1;
    }

    /// <summary>Returns the car immediately ahead of the given racer, or null if they are in first place.</summary>
    public RacerBase GetCarAhead(RacerBase racer)
    {
        int position = GetPosition(racer);
        if (position <= 1)
        {
            return null;
        }
        return _standings[position - 2];
    }

    /// <summary>Returns the car immediately behind the given racer, or null if they are last.</summary>
    public RacerBase GetCarBehind(RacerBase racer)
    {
        int position = GetPosition(racer);
        if (position < 0 || position >= _standings.Length)
        {
            return null;
        }
        return _standings[position];
    }
}

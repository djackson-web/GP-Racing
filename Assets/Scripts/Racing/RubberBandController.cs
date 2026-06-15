using UnityEngine;

public class RubberBandController : MonoBehaviour
{
    [SerializeField] private RaceStandingsTracker _standings;
    [SerializeField] private RacerBase _playerRacer;

    [Header("Banding Curve")]
    [SerializeField] private float _deadZone = 0.05f;
    [SerializeField] private float _maxBandingRange = 0.5f;
    [SerializeField] private float _maxBehindMultiplier = 1.15f;
    [SerializeField] private float _maxAheadMultiplier = 0.88f;

    private void OnDisable()
    {
        // Leave no stale multipliers behind — disabling this component must
        // return every car to neutral pace.
        if (_standings == null)
        {
            return;
        }

        foreach (RacerBase racer in _standings.GetStandings())
        {
            AIController aiController = racer as AIController;
            if (aiController != null)
            {
                aiController.SetBandingMultiplier(1f);
            }
        }
    }

    private void Update()
    {
        if (_standings == null || _playerRacer == null || _standings.IsRaceComplete)
        {
            return;
        }

        RacerBase[] racers = _standings.GetStandings();
        float playerProgress = _playerRacer.RaceProgress;
        int totalCars = racers.Length;

        for (int i = 0; i < totalCars; i++)
        {
            if (racers[i] == null || racers[i] == _playerRacer)
            {
                continue;
            }

            AIController aiController = racers[i] as AIController;
            if (aiController == null)
            {
                continue;
            }

            float gap = playerProgress - aiController.RaceProgress;

            // Graduated banding: rear of field gets full correction, front runners get less
            float positionFraction = 0f;
            if (totalCars > 1)
            {
                positionFraction = (float)i / (totalCars - 1);
            }
            float bandingStrength = Mathf.Lerp(0.5f, 1.0f, positionFraction);

            float multiplier = CalculateBandingMultiplier(gap, bandingStrength);

            // Skill shapes how much of the banding range each car can use, but
            // every car keeps at least half the range so the effect stays
            // perceptible — fully skill-proportional caps stacked with the
            // graduation above used to cancel banding out almost entirely.
            float ceilingScale = Mathf.Lerp(0.5f, 1f, aiController.Profile.skillLevel);
            float floorScale = Mathf.Lerp(1f, 0.5f, aiController.Profile.skillLevel);
            float skillCeiling = 1f + ceilingScale * (_maxBehindMultiplier - 1f);
            float skillFloor = 1f - floorScale * (1f - _maxAheadMultiplier);
            multiplier = Mathf.Clamp(multiplier, skillFloor, skillCeiling);

            aiController.SetBandingMultiplier(multiplier);
        }
    }

    private float CalculateBandingMultiplier(float gap, float bandingStrength)
    {
        if (gap > _deadZone)
        {
            // Car is behind the player — become a little more skilled
            float t = Mathf.Clamp01((gap - _deadZone) / _maxBandingRange) * bandingStrength;
            return Mathf.Lerp(1f, _maxBehindMultiplier, t);
        }

        if (gap < -_deadZone)
        {
            // Car is ahead of the player — become a little less skilled
            float t = Mathf.Clamp01((-gap - _deadZone) / _maxBandingRange) * bandingStrength;
            return Mathf.Lerp(1f, _maxAheadMultiplier, t);
        }

        return 1f;
    }
}

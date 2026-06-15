using UnityEngine;

public class StartingGrid : MonoBehaviour
{
    [SerializeField] private SplineTrack _splineTrack;
    [SerializeField] private RacerBase[] _racers;
    [SerializeField] private float _gridStartProgress = 0f;
    [SerializeField] private float _rowSpacing = 1f;
    [SerializeField] private float _columnOffset = 2f;

    private void Start()
    {
        if (_splineTrack == null || _racers == null)
        {
            return;
        }

        float rowSpacingProgress = 0f;
        if (_splineTrack.TrackLength > 0f)
        {
            rowSpacingProgress = _rowSpacing / _splineTrack.TrackLength;
        }

        for (int racerIndex = 0; racerIndex < _racers.Length; racerIndex++)
        {
            if (_racers[racerIndex] == null)
            {
                continue;
            }

            float progress = _gridStartProgress - racerIndex * rowSpacingProgress;

            float lateral;
            if (racerIndex % 2 == 0)
            {
                lateral = -_columnOffset;
            }
            else
            {
                lateral = _columnOffset;
            }

            _racers[racerIndex].ResetToSpline(progress, lateral);
        }
    }

    public void StartRace()
    {
        if (RaceStandingsTracker.Instance != null)
        {
            RaceStandingsTracker.Instance.StartRace();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_splineTrack == null)
        {
            return;
        }

        float trackLength = _splineTrack.TrackLength;
        if (trackLength <= 0f)
        {
            return;
        }

        float rowSpacingProgress = _rowSpacing / trackLength;
        int slotCount = 1;
        if (_racers != null)
        {
            slotCount = Mathf.Max(_racers.Length, 1);
        }

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            float progress = _gridStartProgress - slotIndex * rowSpacingProgress;

            float lateral;
            if (slotIndex % 2 == 0)
            {
                lateral = -_columnOffset;
            }
            else
            {
                lateral = _columnOffset;
            }

            TrackSample sample = _splineTrack.Evaluate(progress);
            Vector3 position = sample.position + sample.right * lateral;

            if (slotIndex == 0)
            {
                Gizmos.color = Color.yellow;
            }
            else
            {
                Gizmos.color = Color.white;
            }

            Gizmos.DrawWireSphere(position, 0.3f);
            Gizmos.DrawRay(position, sample.forward * 0.6f);
        }
    }
#endif
}

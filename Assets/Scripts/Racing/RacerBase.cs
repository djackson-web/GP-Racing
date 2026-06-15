using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10)]
public abstract class RacerBase : MonoBehaviour
{
    [SerializeField] private SplineTrack _splineTrack;
    [SerializeField] private float _heightOffset = 0.1f;
    [SerializeField] private float _maxLateralOffset = 6f;
    [SerializeField] private float _grip = 4f;
    [SerializeField] private float _overSteerThreshold = 20f;
    [SerializeField] private float _overSteerForce = 1.5f;
    [SerializeField] private float _maximumHeadingDeviation = 90f;

    [Header("Kerb Physics")]
    [SerializeField] private KerbMeshBuilder _kerbMeshBuilder;
    [SerializeField] private float _kerbStartOffset = 4f;
    [SerializeField] private float _kerbGripMultiplier = 0.4f;
    [SerializeField] private float _kerbSpeedPenalty = 20f;

    // 1 mph = 0.44704 m/s, 1 m = 0.1 Unity units → 1 mph = 0.044704 Unity units/s
    public const float MphToUnityUnitsPerSecond = 0.044704f;

    private List<KerbZone> _kerbZones = new List<KerbZone>();

    protected float currentSpeed;
    protected float splineProgress;
    protected float lateralOffset;
    protected float heading;
    protected float velocityAngle;

    public float CurrentSpeed
    {
        get { return currentSpeed; }
    }

    public float SplineProgress
    {
        get { return splineProgress; }
    }

    public float RaceProgress
    {
        get { return splineProgress; }
    }

    public int LapCount
    {
        get { return Mathf.FloorToInt(Mathf.Max(0f, splineProgress)); }
    }

    public float LateralPosition
    {
        get { return lateralOffset; }
    }

    protected SplineTrack SplineTrack
    {
        get { return _splineTrack; }
    }

    protected bool IsAtTrackLimit
    {
        get { return Mathf.Abs(lateralOffset) >= _maxLateralOffset; }
    }

    protected float MaxLateralOffset
    {
        get { return _maxLateralOffset; }
    }

    // Signed angle between the car's heading and the track direction at its
    // current position. Near zero when driving normally, large when spun/sideways.
    protected float HeadingDeviationFromTrack
    {
        get
        {
            TrackSample sample = _splineTrack.Evaluate(splineProgress);
            float splineAngle = Mathf.Atan2(sample.forward.x, sample.forward.z) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(splineAngle, heading);
        }
    }

    private bool IsOnKerb()
    {
        if (Mathf.Abs(lateralOffset) < _kerbStartOffset)
        {
            return false;
        }

        for (int zoneIndex = 0; zoneIndex < _kerbZones.Count; zoneIndex++)
        {
            KerbZone zone = _kerbZones[zoneIndex];
            if (splineProgress >= zone.startProgress && splineProgress <= zone.endProgress)
            {
                return true;
            }
        }

        return false;
    }

    protected virtual void Awake()
    {
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_maxLateralOffset < 1f)
        {
            Debug.LogWarning(
                $"[{name}] RacerBase: maxLateralOffset={_maxLateralOffset} is too small — " +
                $"cars will be stuck in Recover permanently. Set it to 6.", this);
        }
    }
#endif

    protected virtual void Start()
    {
        if (_kerbMeshBuilder != null)
        {
            _kerbZones = _kerbMeshBuilder.GetKerbZones();
        }

        TrackSample sample = _splineTrack.Evaluate(splineProgress);
        heading = Mathf.Atan2(sample.forward.x, sample.forward.z) * Mathf.Rad2Deg;
        velocityAngle = heading;
        transform.rotation = Quaternion.Euler(0f, heading, 0f);
        transform.position = sample.position + Vector3.up * _heightOffset;
    }

    protected virtual void Update()
    {
        TrackSample sample = _splineTrack.Evaluate(splineProgress);
        float splineAngle = Mathf.Atan2(sample.forward.x, sample.forward.z) * Mathf.Rad2Deg;

        float slipAngle = Mathf.DeltaAngle(velocityAngle, heading);
        float effectiveGrip = IsOnKerb() ? _grip * _kerbGripMultiplier : _grip;

        ApplyKerbPenalty();
        ApplyTyrePhysics(slipAngle, effectiveGrip);
        ClampHeading(splineAngle);
        MoveAlongSpline(splineAngle);

        transform.rotation = Quaternion.Euler(0f, heading, 0f);
        transform.position = sample.position + Vector3.up * _heightOffset + sample.right * lateralOffset;
    }

    private void ApplyKerbPenalty()
    {
        if (IsOnKerb())
        {
            currentSpeed -= _kerbSpeedPenalty * Time.deltaTime;
            currentSpeed = Mathf.Max(0f, currentSpeed);
        }
    }

    private void ApplyTyrePhysics(float slipAngle, float effectiveGrip)
    {
        // Understeer: velocity direction lags behind heading based on grip
        velocityAngle += slipAngle * effectiveGrip * Time.deltaTime;

        // Oversteer: once slip exceeds threshold the rear kicks out further
        float excessSlip = Mathf.Abs(slipAngle) - _overSteerThreshold;
        if (excessSlip > 0f)
        {
            heading += Mathf.Sign(slipAngle) * excessSlip * _overSteerForce * Time.deltaTime;
        }
    }

    private void ClampHeading(float splineAngle)
    {
        float headingDeviation = Mathf.DeltaAngle(splineAngle, heading);
        if (Mathf.Abs(headingDeviation) > _maximumHeadingDeviation)
        {
            heading = splineAngle + Mathf.Sign(headingDeviation) * _maximumHeadingDeviation;
        }
    }

    private void MoveAlongSpline(float splineAngle)
    {
        float deviation = Mathf.DeltaAngle(velocityAngle, splineAngle);
        float speedInUnitsPerSecond = currentSpeed * MphToUnityUnitsPerSecond;
        float forwardSpeed = speedInUnitsPerSecond * Mathf.Cos(deviation * Mathf.Deg2Rad);
        float lateralVelocity = -speedInUnitsPerSecond * Mathf.Sin(deviation * Mathf.Deg2Rad);

        splineProgress += forwardSpeed / _splineTrack.TrackLength * Time.deltaTime;
        lateralOffset += lateralVelocity * Time.deltaTime;
        lateralOffset = Mathf.Clamp(lateralOffset, -_maxLateralOffset, _maxLateralOffset);
    }

    public void ResetToSpline(float progress, float offset)
    {
        splineProgress = progress;
        lateralOffset = offset;
        currentSpeed = 0f;

        TrackSample sample = _splineTrack.Evaluate(splineProgress);
        heading = Mathf.Atan2(sample.forward.x, sample.forward.z) * Mathf.Rad2Deg;
        velocityAngle = heading;

        transform.rotation = Quaternion.Euler(0f, heading, 0f);
        transform.position = sample.position + Vector3.up * _heightOffset + sample.right * lateralOffset;
    }
}

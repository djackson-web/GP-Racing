using UnityEngine;

public class AIController : RacerBase
{
    public enum AIState { Race, Overtake, Defend, Recover }

    private const float LowSpeedSteeringThreshold = 20f;
    // Two-segment high-speed steering curve: full authority to SteeringFullAuthoritySpeed,
    // first reduction to SteeringMidFactor at SteeringMidSpeed, shallower reduction to
    // _minimumHighSpeedSteeringFactor at top speed. Keeps chicane feel neutral while
    // limiting over-correction at race speeds.
    private const float SteeringFullAuthoritySpeed = 40f;
    private const float SteeringMidSpeed = 80f;
    private const float SteeringMidFactor = 0.75f;
    private const float StartDelaySkillFactor = 0.15f;
    private const float MaxConsistencyNoiseRange = 0.05f;
    private const float OvertakeAggressionThreshold = 0.35f;
    private const float DefendAggressionThreshold = 0.25f;
    private const float DefendReactionTimerScale = 0.4f;
    private const float OvertakeSpeedBoostMultiplier = 1.04f;
    // dot(overtakeSide * right, lookAheadForward) — negative values mean the AI is heading
    // to the outside of an upcoming corner; suppress the attempt below this threshold.
    private const float OvertakeCurvatureSuppressThreshold = -0.05f;
    private const float OvertakeCurvatureLookAheadTime = 2.5f;
    private const float RecoverMinimumTime = 1f;
    private const float CollisionHeadingKick = 15f;
    private const float RecoverEntryHeadingDeviation = 25f;
    private const float RecoverExitHeadingDeviation = 10f;
    // Lift engages once the car heads this far past its intended path, measured
    // as a fraction of the room left between that path and the boundary.
    private const float EdgeDangerLiftStart = 0.25f;
    private const float WideLineMaximumLift = 0.05f;
    private const float WideLineMinimumTrackWidth = 1f;
    private const float DriftPredictionTime = 0.5f;
    private const float DriftRateSmoothing = 10f;
    private const float MaximumDriftRate = 5f;
    // The racing line keeps this fraction of the car's lateral range, leaving
    // margin for state offsets (overtake/defend) and drift before the boundary.
    public const float RacingLineWidthFraction = 0.85f;
    // Pursuit steering: aim this many seconds of travel ahead, clamped so slow
    // hairpins are tracked tightly and high speed doesn't aim past the corner.
    private const float PursuitTime = 0.9f;
    private const float MinimumPursuitDistance = 2.5f;
    private const float MaximumPursuitDistance = 10f;
    // Never command a steering target beyond this fraction of the lateral range
    private const float SteeringOffsetMargin = 0.9f;
    public const float CurvatureStepDistance = 2.5f;
    private const int BrakingLookAheadSteps = 12;
    private const int DynamicLookAheadSteps = 8;
    // Zones further away than this are ignored — braking from top speed to the
    // slowest corner takes well under this distance.
    private const float BrakingHorizonDistance = 25f;
    // Finish braking this far before the zone entry so turn-in starts settled.
    private const float BrakingEntrySlack = 1f;
    // apexTiming shifts the brake point by this fraction of the braking zone length.
    // Late apexers (−1) brake deeper into the zone; early apexers (+1) initiate sooner.
    private const float ApexBrakeShiftScale = 0.15f;
    // Overspeed below this is coasted off through drag/engine braking — the
    // brakes are reserved for real braking zones, not mid-corner trim.
    private const float CoastingOverspeedTolerance = 8f;
    // Throttle eases off across this band under the target instead of switching
    // hard between full power and nothing.
    private const float ThrottleSoftnessRange = 5f;

    // Fraction of the local road half-width the AI may use before the wall.
    // 0.80 leaves 20 % for vehicle body and kerb buffer on either side.
    private const float TrackUsableFraction = 0.80f;
    // Vehicle half-width in Unity units (0.3 m × 0.1 Unity/m).
    private const float VehicleHalfWidth = 0.03f;
    // Hard floor so the corridor never collapses on very narrow sections.
    private const float MinimumCorridorHalfWidth = 0.15f;

    // Lateral boundary at the car's current position: scales with local road
    // width so the AI always has room proportional to the actual track surface.
    protected override float MaxLateralOffset
    {
        get
        {
            if (SplineTrack != null && SplineTrack.HalfWidth > 0f)
            {
                float localHalf = SplineTrack.SampleHalfWidth(splineProgress);
                return Mathf.Max(localHalf * TrackUsableFraction - VehicleHalfWidth, MinimumCorridorHalfWidth);
            }
            return base.MaxLateralOffset;
        }
    }

    // The narrowest the corridor ever gets, derived from SplineTrack.HalfWidth
    // (the track-wide minimum). Used to bake the racing line at a consistent
    // width that stays valid at every point around the circuit.
    private float GlobalCorridorHalfWidth
    {
        get
        {
            float globalHalf = SplineTrack != null ? SplineTrack.HalfWidth : base.MaxLateralOffset;
            return Mathf.Max(globalHalf * TrackUsableFraction - VehicleHalfWidth, MinimumCorridorHalfWidth);
        }
    }

    [SerializeField] private float _topSpeed = 220f;
    [SerializeField] private float _acceleration = 25f;
    [SerializeField] private float _brakeForce = 110f;
    [SerializeField] private float _drag = 30f;
    [SerializeField] private float _engineBrakingForce = 60f;
    [SerializeField] private float _steeringRate = 60f;
    [SerializeField] private float _minimumHighSpeedSteeringFactor = 0.50f;

    [Header("Driver Profile")]
    [SerializeField] private DriverProfile _profile;

    [Header("Race Behaviour")]
    [SerializeField] private float _overtakeThresholdProgress = 0.008f;
    [SerializeField] private float _defendThresholdProgress = 0.010f;
    [SerializeField] private float _overtakeFraction = 0.35f;
    [SerializeField] private float _defendFraction = 0.25f;
    [SerializeField] private float _linePreferenceFraction = 0.3f;
    [SerializeField] private float _overtakeTimeout = 3f;
    [SerializeField] private float _defendTimeout = 1.5f;

    private AIState _state = AIState.Race;
    private float _stateTimer;
    private float _bandingMultiplier = 1f;
    private float _gearMaxSpeed = 100f;
    private float _consistencyNoise = 1f;
    private int _lastLapCount;
    private float _raceStartTime = -1f;
    private float _startDelay;
    private float _skillAcceleration;
    private float _previousLateralOffset;
    private float _lateralDriftRate;
    private float _targetLateralOffset;
    private float _overtakeSide;
    private float _defendReactionTimer;
    private bool _defendMoveDone;
    private RaceStandingsTracker _standings;

    public DriverProfile Profile
    {
        get { return _profile; }
    }

    public AIState DebugState
    {
        get { return _state; }
    }

    public float DebugBandingMultiplier
    {
        get { return _bandingMultiplier; }
    }

    public float DebugConsistencyNoise
    {
        get { return _consistencyNoise; }
    }

    public bool DebugBandingActive { get; private set; }

    public float DebugTargetLateralOffset
    {
        get { return _targetLateralOffset; }
    }

    [System.NonSerialized] public Vector3 DebugSteeringTarget;
    [System.NonSerialized] public float DebugTargetSpeed;
    [System.NonSerialized] public float DebugThrottle;
    [System.NonSerialized] public float DebugBrake;

    protected override void Awake()
    {
        base.Awake();
        _startDelay = (1f - _profile.skillLevel) * StartDelaySkillFactor;
        _targetLateralOffset = LinePreferenceOffset();
        _skillAcceleration = AISpeedModel.SkillAcceleration(_acceleration, _profile.skillLevel);
    }

    protected override void Start()
    {
        base.Start();
        _standings = RaceStandingsTracker.Instance;
        _lastLapCount = LapCount;
    }

    public void SetRaceStartTime(float time)
    {
        _raceStartTime = time;
    }

    public void SetBandingMultiplier(float multiplier)
    {
        _bandingMultiplier = multiplier;
    }

    protected override void Update()
    {
        bool raceStarted = _raceStartTime >= 0f && Time.time >= _raceStartTime + _startDelay;

        UpdateConsistencyNoise();
        UpdateLateralDrift();
        DebugBandingActive = Mathf.Abs(_bandingMultiplier - 1f) > 0.001f;

        if (raceStarted)
        {
            UpdateFSM();
        }

        if (raceStarted)
        {
            DebugTargetSpeed = CalculateTargetSpeed();
        }
        else
        {
            DebugTargetSpeed = 0f;
        }

        float targetSpeed = DebugTargetSpeed;

        ApplySpeedTowardTarget(targetSpeed);
        ApplyDrag();
        ApplyAutoShift();

        currentSpeed = Mathf.Clamp(currentSpeed, 0f, _topSpeed);

        ApplySteering();

        base.Update();
    }

    private void UpdateLateralDrift()
    {
        if (Time.deltaTime <= 0f)
        {
            return;
        }

        float rawDriftRate = (lateralOffset - _previousLateralOffset) / Time.deltaTime;
        rawDriftRate = Mathf.Clamp(rawDriftRate, -MaximumDriftRate, MaximumDriftRate);
        _lateralDriftRate = Mathf.Lerp(_lateralDriftRate, rawDriftRate, DriftRateSmoothing * Time.deltaTime);
        _previousLateralOffset = lateralOffset;
    }

    private void UpdateConsistencyNoise()
    {
        int currentLap = LapCount;
        if (currentLap != _lastLapCount)
        {
            _lastLapCount = currentLap;
            float noiseRange = (1f - _profile.consistency) * MaxConsistencyNoiseRange;
            _consistencyNoise = 1f + Random.Range(-noiseRange, noiseRange);
        }
    }

    private void ApplySpeedTowardTarget(float targetSpeed)
    {
        float speedError = targetSpeed - currentSpeed;

        if (speedError >= 0f)
        {
            // Banding works through acceleration: it shapes corner exits and the
            // drag-limited cruise speed without ever defying cornering physics.
            // Squared because drag grows quadratically with speed — this makes
            // the configured multiplier show up one-to-one in sustained speed.
            float throttle = Mathf.Clamp01(speedError / ThrottleSoftnessRange);
            float bandedAcceleration = _skillAcceleration * _bandingMultiplier * _bandingMultiplier;
            currentSpeed += bandedAcceleration * throttle * Time.deltaTime;
            DebugThrottle = throttle;
            DebugBrake = 0f;
        }
        else if (-speedError > CoastingOverspeedTolerance)
        {
            currentSpeed = Mathf.Max(currentSpeed - _brakeForce * Time.deltaTime, targetSpeed);
            DebugThrottle = 0f;
            DebugBrake = 1f;
        }
        else
        {
            // Small overspeed: coast — drag and engine braking shed it naturally.
            DebugThrottle = 0f;
            DebugBrake = 0f;
        }
    }

    private void ApplyDrag()
    {
        float speedFraction = currentSpeed / _topSpeed;
        currentSpeed -= _drag * speedFraction * speedFraction * Time.deltaTime;
    }

    private void ApplyAutoShift()
    {
        if (currentSpeed > _gearMaxSpeed)
        {
            _gearMaxSpeed = AISpeedModel.TopGearMaximumSpeed;
            currentSpeed -= _engineBrakingForce * Time.deltaTime;
        }
    }

    private void ApplySteering()
    {
        float desiredHeading = CalculateDesiredHeading();
        float headingDelta = Mathf.DeltaAngle(heading, desiredHeading);
        float lowSpeedFactor = Mathf.Clamp01(currentSpeed / LowSpeedSteeringThreshold);
        float highSpeedFactor = CalculateHighSpeedSteeringFactor();
        float maximumSteerThisFrame = _steeringRate * lowSpeedFactor * highSpeedFactor * Time.deltaTime;
        heading += Mathf.Clamp(headingDelta, -maximumSteerThisFrame, maximumSteerThisFrame);
    }

    private float CalculateHighSpeedSteeringFactor()
    {
        if (currentSpeed <= SteeringFullAuthoritySpeed)
        {
            return 1f;
        }
        if (currentSpeed <= SteeringMidSpeed)
        {
            return Mathf.Lerp(1f, SteeringMidFactor,
                (currentSpeed - SteeringFullAuthoritySpeed) /
                (SteeringMidSpeed - SteeringFullAuthoritySpeed));
        }
        return Mathf.Lerp(SteeringMidFactor, _minimumHighSpeedSteeringFactor,
            (currentSpeed - SteeringMidSpeed) / (_topSpeed - SteeringMidSpeed));
    }

    // ── FSM ────────────────────────────────────────────────────────────────

    private void UpdateFSM()
    {
        _stateTimer += Time.deltaTime;

        // Riding the lateral limit mid-corner is normal racing; only recover
        // when the car is also pointing well away from the track direction.
        if (_state != AIState.Recover && IsAtTrackLimit
            && Mathf.Abs(HeadingDeviationFromTrack) > RecoverEntryHeadingDeviation)
        {
            TransitionTo(AIState.Recover);
            return;
        }

        if (_state == AIState.Race)
        {
            UpdateRace();
        }
        else if (_state == AIState.Overtake)
        {
            UpdateOvertake();
        }
        else if (_state == AIState.Defend)
        {
            UpdateDefend();
        }
        else if (_state == AIState.Recover)
        {
            UpdateRecover();
        }
    }

    private void UpdateRace()
    {
        _targetLateralOffset = CalculateApexTimedLateralOffset();

        if (_standings == null)
        {
            return;
        }

        RacerBase carAhead = _standings.GetCarAhead(this);
        if (carAhead != null && _profile.aggression > OvertakeAggressionThreshold)
        {
            float progressGap = carAhead.RaceProgress - RaceProgress;
            if (progressGap >= 0f && progressGap < _overtakeThresholdProgress)
            {
                float proposedSide = (carAhead.LateralPosition >= 0f) ? -1f : 1f;
                if (IsOvertakeCurvatureFavorable(proposedSide))
                {
                    _overtakeSide = proposedSide;
                    TransitionTo(AIState.Overtake);
                    return;
                }
            }
        }

        RacerBase carBehind = _standings.GetCarBehind(this);
        if (carBehind != null && _profile.aggression > DefendAggressionThreshold)
        {
            float progressGap = RaceProgress - carBehind.RaceProgress;
            if (progressGap >= 0f && progressGap < _defendThresholdProgress)
            {
                _defendReactionTimer = (1f - _profile.consistency) * DefendReactionTimerScale;
                _defendMoveDone = false;
                TransitionTo(AIState.Defend);
            }
        }
    }

    private void UpdateOvertake()
    {
        _targetLateralOffset = _overtakeSide * MaxLateralOffset * _overtakeFraction;

        RacerBase carAhead = null;
        if (_standings != null)
        {
            carAhead = _standings.GetCarAhead(this);
        }

        float progressGap = float.MaxValue;
        if (carAhead != null)
        {
            progressGap = carAhead.RaceProgress - RaceProgress;
        }

        bool cleared = carAhead == null || progressGap > _overtakeThresholdProgress * 2f;
        if (cleared || _stateTimer >= _overtakeTimeout)
        {
            TransitionTo(AIState.Race);
        }
    }

    private void UpdateDefend()
    {
        _defendReactionTimer -= Time.deltaTime;

        if (_defendReactionTimer <= 0f && !_defendMoveDone)
        {
            RacerBase carBehind = null;
            if (_standings != null)
            {
                carBehind = _standings.GetCarBehind(this);
            }

            if (carBehind != null)
            {
                _targetLateralOffset = Mathf.Sign(carBehind.LateralPosition) * MaxLateralOffset * _defendFraction;
            }

            _defendMoveDone = true;
        }

        RacerBase attacker = null;
        if (_standings != null)
        {
            attacker = _standings.GetCarBehind(this);
        }

        float progressGap = float.MaxValue;
        if (attacker != null)
        {
            progressGap = RaceProgress - attacker.RaceProgress;
        }

        bool safe = attacker == null || progressGap > _defendThresholdProgress * 2f;
        if (safe || _stateTimer >= _defendTimeout)
        {
            TransitionTo(AIState.Race);
        }
    }

    private void UpdateRecover()
    {
        _targetLateralOffset = 0f;

        bool pointingForward = Mathf.Abs(HeadingDeviationFromTrack) < RecoverExitHeadingDeviation;
        if (_stateTimer > RecoverMinimumTime && pointingForward && !IsAtTrackLimit)
        {
            TransitionTo(AIState.Race);
        }
    }

    private void TransitionTo(AIState newState)
    {
        _state = newState;
        _stateTimer = 0f;
    }

    // Samples the look-ahead tangent and checks whether the proposed overtake side
    // is the inside (or a straight) rather than the outside of an upcoming corner.
    // Uses dot(overtakeSide × right, lookAheadForward): negative past the suppress
    // threshold means the AI would dive to the outside — block the commit.
    private bool IsOvertakeCurvatureFavorable(float overtakeSide)
    {
        if (SplineTrack == null || SplineTrack.TrackLength <= 0f)
        {
            return true;
        }

        float lookAheadDistance = currentSpeed * MphToUnityUnitsPerSecond * OvertakeCurvatureLookAheadTime;
        float lookAheadProgress = splineProgress + lookAheadDistance / SplineTrack.TrackLength;

        TrackSample currentSample = SplineTrack.Evaluate(splineProgress);
        TrackSample aheadSample = SplineTrack.Evaluate(lookAheadProgress);

        float curvatureDot = Vector3.Dot(currentSample.right * overtakeSide, aheadSample.forward);
        return curvatureDot >= OvertakeCurvatureSuppressThreshold;
    }

    // Line preference is a personality lean around the racing line: 0.5 sits
    // exactly on it, 0 and 1 lean to either side. It must never become a
    // permanent handicap that drags a driver off the optimal path.
    private float LinePreferenceOffset()
    {
        return (_profile.linePreference - 0.5f) * MaxLateralOffset * _linePreferenceFraction;
    }

    // Blends the line-preference lean in as the car approaches a braking zone,
    // with the blend point shifted by apexTiming so the lateral and longitudinal
    // commitments stay coupled: early apexers move to their line sooner, late
    // apexers stay neutral until deeper into the approach.
    private float CalculateApexTimedLateralOffset()
    {
        if (SplineTrack == null || SplineTrack.TrackLength <= 0f)
        {
            return LinePreferenceOffset();
        }

        float lapProgress = splineProgress - Mathf.Floor(splineProgress);
        float maximumProximity = 0f;
        BrakingZone[] zones = SilverstoneBrakingZones.Zones;

        for (int i = 0; i < zones.Length; i++)
        {
            BrakingZone zone = zones[i];

            bool insideZone = lapProgress >= zone.entryProgress && lapProgress <= zone.exitProgress;
            if (insideZone)
            {
                maximumProximity = 1f;
                break;
            }

            float progressToEntry = zone.entryProgress - lapProgress;
            if (progressToEntry < 0f)
            {
                progressToEntry += 1f;
            }

            float distanceToEntry = progressToEntry * SplineTrack.TrackLength;
            if (distanceToEntry > BrakingHorizonDistance)
            {
                continue;
            }

            float brakeZoneLength = (zone.exitProgress - zone.entryProgress) * SplineTrack.TrackLength;
            float apexShift = _profile.apexTiming * brakeZoneLength * ApexBrakeShiftScale;
            float effectiveDistanceToEntry = distanceToEntry - apexShift;
            float proximity = 1f - Mathf.Clamp01(effectiveDistanceToEntry / BrakingHorizonDistance);

            if (proximity > maximumProximity)
            {
                maximumProximity = proximity;
            }
        }

        return LinePreferenceOffset() * maximumProximity;
    }

    // ── Speed & Steering ───────────────────────────────────────────────────

    private float CalculateTargetSpeed()
    {
        if (SplineTrack == null || SplineTrack.TrackLength <= 0f)
        {
            return _topSpeed;
        }

        float lapProgress = splineProgress - Mathf.Floor(splineProgress);
        float cornerMultiplier = AISpeedModel.CornerSkillMultiplier(_profile);
        float targetSpeed = CalculateDynamicTargetSpeed(lapProgress, cornerMultiplier);

        BrakingZone[] zones = SilverstoneBrakingZones.Zones;
        for (int i = 0; i < zones.Length; i++)
        {
            float zoneSpeed = ZoneTargetSpeed(zones[i], cornerMultiplier);
            float allowedSpeed = AllowedSpeedForZone(zones[i], zoneSpeed, lapProgress);
            if (allowedSpeed < targetSpeed)
            {
                targetSpeed = allowedSpeed;
            }
        }

        targetSpeed = ApplyWideLineLift(targetSpeed);
        return Mathf.Clamp(targetSpeed, AISpeedModel.AbsoluteMinimumCornerSpeed, _topSpeed);
    }

    // The speed this driver carries through the zone: authored perfect-driver
    // speed scaled by skill and per-lap consistency. Banding is deliberately
    // absent — it acts on acceleration, since corner speed is physics-capped.
    private float ZoneTargetSpeed(BrakingZone zone, float cornerMultiplier)
    {
        float zoneSpeed = zone.targetSpeed * cornerMultiplier * _consistencyNoise;
        if (_state == AIState.Overtake)
        {
            zoneSpeed *= OvertakeSpeedBoostMultiplier;
        }
        return zoneSpeed;
    }

    private float AllowedSpeedForZone(BrakingZone zone, float zoneSpeed, float lapProgress)
    {
        bool insideZone = lapProgress >= zone.entryProgress && lapProgress <= zone.exitProgress;
        if (insideZone)
        {
            return zoneSpeed;
        }

        float progressToEntry = zone.entryProgress - lapProgress;
        if (progressToEntry < 0f)
        {
            progressToEntry += 1f;
        }

        float distanceToEntry = progressToEntry * SplineTrack.TrackLength;
        if (distanceToEntry > BrakingHorizonDistance)
        {
            return _topSpeed;
        }

        // Longitudinal brake-point shift: positive apexTiming (early apex) reduces
        // the effective braking distance, forcing earlier deceleration.
        float brakeZoneLength = (zone.exitProgress - zone.entryProgress) * SplineTrack.TrackLength;
        float apexShift = _profile.apexTiming * brakeZoneLength * ApexBrakeShiftScale;
        float brakingDistance = Mathf.Max(distanceToEntry - BrakingEntrySlack - apexShift, 0f);
        return AISpeedModel.BrakingAllowedSpeed(zoneSpeed, _brakeForce, brakingDistance);
    }

    // Samples spline curvature at DynamicLookAheadSteps steps ahead and returns
    // the minimum braking-allowed speed across the window. Acts as the primary
    // speed target on open track; authored zones override it for tight corners.
    private float CalculateDynamicTargetSpeed(float lapProgress, float cornerMultiplier)
    {
        float effectiveGrip = AISpeedModel.EffectiveLateralGrip(_profile.brakingCourage);
        float progressStep = CurvatureStepDistance / SplineTrack.TrackLength;
        float minimumAllowedSpeed = _topSpeed;
        TrackSample previousSample = SplineTrack.Evaluate(lapProgress);

        for (int stepIndex = 1; stepIndex <= DynamicLookAheadSteps; stepIndex++)
        {
            TrackSample sample = SplineTrack.Evaluate(lapProgress + stepIndex * progressStep);
            float angleDelta = Vector3.Angle(previousSample.forward, sample.forward);
            float curvature = angleDelta / CurvatureStepDistance;

            float rawCornerSpeed = AISpeedModel.CornerSpeedFromGrip(curvature, effectiveGrip);
            if (rawCornerSpeed > _topSpeed)
            {
                previousSample = sample;
                continue;
            }

            float cornerSpeed = Mathf.Max(
                rawCornerSpeed * cornerMultiplier * _consistencyNoise,
                AISpeedModel.AbsoluteMinimumCornerSpeed);
            float distance = stepIndex * CurvatureStepDistance;
            float allowedSpeed = AISpeedModel.BrakingAllowedSpeed(cornerSpeed, _brakeForce, distance);

            if (allowedSpeed < minimumAllowedSpeed)
            {
                minimumAllowedSpeed = allowedSpeed;
            }

            previousSample = sample;
        }

        return minimumAllowedSpeed;
    }

    private Vector3 RacingLinePosition(float progress, float lineWidth)
    {
        TrackSample sample = SplineTrack.Evaluate(progress);
        float offset = RacingLine.SampleOffset(SplineTrack, progress, lineWidth);
        return sample.position + sample.right * offset;
    }

    private float ApplyWideLineLift(float targetSpeed)
    {
        // Lift off when the car is heading past its intended path toward the
        // boundary, so drift converges back onto the line instead of pinning.
        // Measured as deviation from the path, not absolute width — a car
        // tracking the line cleanly feels no lift even at an apex right against
        // the edge. Skipped on narrow corridors where pinning is accepted.
        if (MaxLateralOffset < WideLineMinimumTrackWidth)
        {
            return targetSpeed;
        }

        float lineOffset = RacingLine.SampleOffset(
            SplineTrack, splineProgress, GlobalCorridorHalfWidth * RacingLineWidthFraction);
        float offsetLimit = MaxLateralOffset * SteeringOffsetMargin;
        float intendedOffset = Mathf.Clamp(lineOffset + _targetLateralOffset, -offsetLimit, offsetLimit);

        float predictedOffset = lateralOffset + _lateralDriftRate * DriftPredictionTime;
        float roomToEdge = Mathf.Max(MaxLateralOffset - Mathf.Abs(intendedOffset), 0.05f);
        float danger = (Mathf.Abs(predictedOffset) - Mathf.Abs(intendedOffset)) / roomToEdge;
        float liftStrength = Mathf.Clamp01((danger - EdgeDangerLiftStart) / (1f - EdgeDangerLiftStart));

        // Once pinned against the clamp the drift rate reads zero and the
        // prediction saturates — force the full lift so the car actually slows
        // enough to peel off the boundary.
        if (IsAtTrackLimit)
        {
            liftStrength = 1f;
        }

        return targetSpeed * (1f - WideLineMaximumLift * liftStrength);
    }

    private float CalculateDesiredHeading()
    {
        if (SplineTrack == null || SplineTrack.TrackLength <= 0f)
        {
            return heading;
        }

        // Aim a fixed travel-time ahead: far at speed for smooth early turn-in,
        // close in slow corners so tight radii are tracked accurately.
        float pursuitDistance = Mathf.Clamp(
            currentSpeed * MphToUnityUnitsPerSecond * PursuitTime,
            MinimumPursuitDistance, MaximumPursuitDistance);
        float targetProgress = splineProgress + pursuitDistance / SplineTrack.TrackLength;
        TrackSample ahead = SplineTrack.Evaluate(targetProgress);

        // Follow the baked racing line; the FSM's lateral offset is a delta
        // around it (overtake to one side of the line, defend off-line, etc.),
        // clamped so a manoeuvre near an apex never aims at the boundary itself.
        float lineOffset = RacingLine.SampleOffset(
            SplineTrack, targetProgress, GlobalCorridorHalfWidth * RacingLineWidthFraction);
        float offsetLimit = MaxLateralOffset * SteeringOffsetMargin;
        float totalOffset = Mathf.Clamp(lineOffset + _targetLateralOffset, -offsetLimit, offsetLimit);

        DebugSteeringTarget = ahead.position + ahead.right * totalOffset;
        Vector3 toTarget = (DebugSteeringTarget - transform.position).normalized;
        return Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
    }

    // ── Collision ──────────────────────────────────────────────────────────

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.GetComponent<RacerBase>() != null)
        {
            heading += Random.Range(-CollisionHeadingKick, CollisionHeadingKick);
            TransitionTo(AIState.Recover);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_state == AIState.Race)
        {
            Gizmos.color = Color.green;
        }
        else if (_state == AIState.Overtake)
        {
            Gizmos.color = Color.yellow;
        }
        else if (_state == AIState.Defend)
        {
            Gizmos.color = Color.blue;
        }
        else if (_state == AIState.Recover)
        {
            Gizmos.color = Color.red;
        }
        else
        {
            Gizmos.color = Color.white;
        }

        Gizmos.DrawSphere(transform.position + Vector3.up * 2f, 0.4f);

        if (DebugSteeringTarget != Vector3.zero)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, DebugSteeringTarget);
        }

        if (SplineTrack != null && SplineTrack.TrackLength > 0f)
        {
            TrackSample sample = SplineTrack.Evaluate(splineProgress);
            float halfWidth = MaxLateralOffset;
            Gizmos.color = Color.white;
            Gizmos.DrawLine(sample.position + sample.right * -halfWidth,
                            sample.position + sample.right * halfWidth);

            if (Application.isPlaying)
            {
                // Baked racing line for the stretch ahead
                Gizmos.color = Color.magenta;
                float lineWidth = GlobalCorridorHalfWidth * RacingLineWidthFraction;
                float stepProgress = CurvatureStepDistance / SplineTrack.TrackLength;
                Vector3 previousPoint = RacingLinePoint(splineProgress, lineWidth);
                for (int step = 1; step <= BrakingLookAheadSteps; step++)
                {
                    Vector3 nextPoint = RacingLinePoint(splineProgress + step * stepProgress, lineWidth);
                    Gizmos.DrawLine(previousPoint, nextPoint);
                    previousPoint = nextPoint;
                }
            }
        }

        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f,
            $"{gameObject.name} [{_state}]");
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || SplineTrack == null || SplineTrack.TrackLength <= 0f)
        {
            return;
        }

        // Full baked racing line around the whole track; braking zones in red
        float lineWidth = GlobalCorridorHalfWidth * RacingLineWidthFraction;
        const int segments = 256;
        Vector3 previousPoint = RacingLinePoint(0f, lineWidth);
        for (int i = 1; i <= segments; i++)
        {
            float progress = (float)i / segments;
            if (IsInsideBrakingZone(progress))
            {
                Gizmos.color = Color.red;
            }
            else
            {
                Gizmos.color = Color.magenta;
            }

            Vector3 nextPoint = RacingLinePoint(progress, lineWidth);
            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }

    private static bool IsInsideBrakingZone(float lapProgress)
    {
        foreach (BrakingZone zone in SilverstoneBrakingZones.Zones)
        {
            if (lapProgress >= zone.entryProgress && lapProgress <= zone.exitProgress)
            {
                return true;
            }
        }
        return false;
    }

    private Vector3 RacingLinePoint(float progress, float lineWidth)
    {
        return RacingLinePosition(progress, lineWidth) + Vector3.up * 0.1f;
    }
#endif
}

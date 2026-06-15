using UnityEngine;

/// <summary>
/// The AI speed model: how fast a car can corner, how hard it can brake and
/// how quickly it accelerates, as functions of car stats and driver profile.
/// Shared by <see cref="AIController"/> (runtime driving) and the editor
/// lap-time predictor (calibration), so both always agree on the same physics.
/// </summary>
public static class AISpeedModel
{
    // The planner only budgets this share of the steering rate for path
    // curvature — the reserve absorbs tracking errors instead of becoming drift.
    public const float SteeringAuthorityBudget = 0.9f;
    public const float CorneringSafetyMargin = 0.95f;
    public const float MinimumCornerSkillMultiplier = 0.9f;
    public const float MaximumCornerSkillMultiplier = 1.02f;
    // How much skill outweighs braking courage in the corner multiplier; a
    // brave but unskilled driver shouldn't corner like an ace.
    public const float CornerSkillWeight = 0.75f;
    public const float MinimumAccelerationSkillFactor = 0.92f;
    public const float MaximumAccelerationSkillFactor = 1.08f;
    // Absolute floor only — keeps cars from stalling, never overrides physics.
    // The sharpest hairpins genuinely demand ~35-45 mph; a higher floor forces
    // cars in above the holdable limit and guarantees they run wide.
    public const float AbsoluteMinimumCornerSpeed = 20f;
    public const float TopGearMaximumSpeed = 212f;
    // v² = vCorner² + 2·a·d, with brake force in mph/s and distance in track units
    public const float MphSquaredPerBrakeUnit = 2f / RacerBase.MphToUnityUnitsPerSecond;

    /// <summary>
    /// How close this driver gets to the ideal corner speed. Skill dominates,
    /// braking courage contributes.
    /// </summary>
    /// <param name="profile">The driver whose skill and courage set the multiplier.</param>
    public static float CornerSkillMultiplier(DriverProfile profile)
    {
        float weightedSkill = Mathf.Lerp(profile.brakingCourage, profile.skillLevel, CornerSkillWeight);
        return Mathf.Lerp(MinimumCornerSkillMultiplier, MaximumCornerSkillMultiplier, weightedSkill);
    }

    /// <summary>
    /// The driver's effective acceleration: better drivers get more out of the
    /// same car, which shapes corner exits and drag-limited cruise speed.
    /// </summary>
    /// <param name="baseAcceleration">The car's acceleration stat in mph/s.</param>
    /// <param name="skillLevel">Driver skill from 0 to 1.</param>
    public static float SkillAcceleration(float baseAcceleration, float skillLevel)
    {
        return baseAcceleration * Mathf.Lerp(
            MinimumAccelerationSkillFactor, MaximumAccelerationSkillFactor, skillLevel);
    }

    /// <summary>
    /// The fastest speed the steering can hold through the given curvature:
    /// turn rate needed grows with speed while available steering fades with
    /// speed, so solve steeringRate · highSpeedFactor(s) = curvature · speed(s).
    /// </summary>
    public static float CornerSpeed(float curvatureDegreesPerUnit, float steeringRate,
        float minimumHighSpeedSteeringFactor, float topSpeed, float cornerMultiplier)
    {
        float budgetedSteering = steeringRate * SteeringAuthorityBudget;
        float turnRateNeededPerMph = curvatureDegreesPerUnit * RacerBase.MphToUnityUnitsPerSecond;
        float steeringFadePerMph = budgetedSteering * (1f - minimumHighSpeedSteeringFactor) / topSpeed;
        float physicalLimit = budgetedSteering / (turnRateNeededPerMph + steeringFadePerMph);

        float cornerSpeed = physicalLimit * CorneringSafetyMargin * cornerMultiplier;
        return Mathf.Clamp(cornerSpeed, AbsoluteMinimumCornerSpeed, topSpeed);
    }

    /// <summary>
    /// The fastest speed a car can carry now and still brake down to
    /// cornerSpeed within the given distance.
    /// </summary>
    public static float BrakingAllowedSpeed(float cornerSpeed, float brakeForce, float brakingDistance)
    {
        return Mathf.Sqrt(cornerSpeed * cornerSpeed
            + MphSquaredPerBrakeUnit * brakeForce * brakingDistance);
    }

    /// <summary>
    /// Net full-throttle acceleration in mph/s at the given speed: engine power
    /// minus quadratic drag, minus engine braking above the top gear limit.
    /// Mirrors AIController's per-frame ApplySpeedTowardTarget/ApplyDrag/
    /// ApplyAutoShift combination for the lap-time predictor.
    /// </summary>
    public static float FullThrottleAcceleration(float speed, float skillAcceleration,
        float drag, float engineBrakingForce, float topSpeed)
    {
        float speedFraction = speed / topSpeed;
        float net = skillAcceleration - drag * speedFraction * speedFraction;
        if (speed > TopGearMaximumSpeed)
        {
            net -= engineBrakingForce;
        }
        return net;
    }
}

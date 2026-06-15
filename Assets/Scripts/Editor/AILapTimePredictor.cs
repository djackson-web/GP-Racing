using UnityEditor;
using UnityEngine;

/// <summary>
/// Predicts the lap time the AI speed model produces on the scene's track,
/// without entering Play mode. Walks the baked racing line, applies the same
/// corner-speed, braking and acceleration maths as <see cref="AIController"/>
/// (via <see cref="AISpeedModel"/>), and logs the resulting lap time for a
/// perfect-skill driver and for the best driver in the scene.
/// Use it to calibrate AISpeedModel constants against a target lap time.
/// This is an ideal-conditions estimate — real laps run slightly slower with
/// steering lag, consistency noise and traffic.
/// </summary>
public static class AILapTimePredictor
{
    private const int SettlePasses = 2;

    private struct CarParameters
    {
        public float topSpeed;
        public float acceleration;
        public float brakeForce;
        public float drag;
        public float engineBrakingForce;
        public float steeringRate;
        public float minimumHighSpeedSteeringFactor;
        public float maxLateralOffset;
    }

    /// <summary>
    /// Runs the prediction for the open scene and logs the result to the console.
    /// </summary>
    [MenuItem("Tools/GP Racing/Predict AI Lap Time")]
    public static void Predict()
    {
        SplineTrack track = Object.FindFirstObjectByType<SplineTrack>();
        AIController[] cars = Object.FindObjectsByType<AIController>(FindObjectsSortMode.None);

        if (track == null || cars.Length == 0)
        {
            Debug.LogWarning("[AILapTimePredictor] Needs a SplineTrack and at least one AIController in the open scene.");
            return;
        }

        if (track.TrackLength <= 0f)
        {
            Debug.LogWarning("[AILapTimePredictor] Track length is zero — the spline has not initialised yet.");
            return;
        }

        LogPrediction(track, cars);
    }

    private static void LogPrediction(SplineTrack track, AIController[] cars)
    {
        CarParameters car = ReadCarParameters(cars[0]);

        DriverProfile perfectProfile = new DriverProfile
        {
            driverName = "Perfect",
            skillLevel = 1f,
            brakingCourage = 1f,
            consistency = 1f
        };
        float perfectLapSeconds = SimulateLap(track, car, perfectProfile);

        AIController bestDriver = FindHighestSkillDriver(cars);
        float bestLapSeconds = SimulateLap(track, car, bestDriver.Profile);

        Debug.Log(
            $"[AILapTimePredictor] Perfect skill (1.0): {FormatLapTime(perfectLapSeconds)} | " +
            $"Best in scene ({bestDriver.name}, skill {bestDriver.Profile.skillLevel:F2}): {FormatLapTime(bestLapSeconds)} | " +
            $"Track {track.TrackLength:F0} units. Ideal-conditions estimate — real laps run slightly slower.");
    }

    private static AIController FindHighestSkillDriver(AIController[] cars)
    {
        AIController best = cars[0];
        foreach (AIController candidate in cars)
        {
            if (candidate.Profile.skillLevel > best.Profile.skillLevel)
            {
                best = candidate;
            }
        }
        return best;
    }

    private static CarParameters ReadCarParameters(AIController car)
    {
        SerializedObject serialized = new SerializedObject(car);
        CarParameters parameters = new CarParameters
        {
            topSpeed = serialized.FindProperty("_topSpeed").floatValue,
            acceleration = serialized.FindProperty("_acceleration").floatValue,
            brakeForce = serialized.FindProperty("_brakeForce").floatValue,
            drag = serialized.FindProperty("_drag").floatValue,
            engineBrakingForce = serialized.FindProperty("_engineBrakingForce").floatValue,
            steeringRate = serialized.FindProperty("_steeringRate").floatValue,
            minimumHighSpeedSteeringFactor = serialized.FindProperty("_minimumHighSpeedSteeringFactor").floatValue,
            maxLateralOffset = serialized.FindProperty("_maxLateralOffset").floatValue
        };
        return parameters;
    }

    private static float SimulateLap(SplineTrack track, CarParameters car, DriverProfile profile)
    {
        int stepCount = Mathf.CeilToInt(track.TrackLength / AIController.CurvatureStepDistance);
        float lineWidth = car.maxLateralOffset * AIController.RacingLineWidthFraction;

        Vector3[] points = SampleRacingLine(track, stepCount, lineWidth);
        float[] segmentLengths = MeasureSegments(points);
        float[] speeds = ZoneSpeedLimits(stepCount, car, profile);

        ApplyBrakingLimits(speeds, segmentLengths, car);
        ApplyAccelerationLimits(speeds, segmentLengths, car, profile);

        return IntegrateLapTime(speeds, segmentLengths);
    }

    private static Vector3[] SampleRacingLine(SplineTrack track, int stepCount, float lineWidth)
    {
        Vector3[] points = new Vector3[stepCount];
        for (int i = 0; i < stepCount; i++)
        {
            float progress = (float)i / stepCount;
            TrackSample sample = track.Evaluate(progress);
            float offset = RacingLine.SampleOffset(track, progress, lineWidth);
            points[i] = sample.position + sample.right * offset;
        }
        return points;
    }

    private static float[] MeasureSegments(Vector3[] points)
    {
        float[] lengths = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            int next = (i + 1) % points.Length;
            lengths[i] = Vector3.Distance(points[i], points[next]);
        }
        return lengths;
    }

    private static float[] ZoneSpeedLimits(int stepCount, CarParameters car, DriverProfile profile)
    {
        float cornerMultiplier = AISpeedModel.CornerSkillMultiplier(profile);

        float[] speeds = new float[stepCount];
        for (int i = 0; i < stepCount; i++)
        {
            float lapProgress = (float)i / stepCount;
            speeds[i] = car.topSpeed;

            foreach (BrakingZone zone in SilverstoneBrakingZones.Zones)
            {
                if (lapProgress < zone.entryProgress || lapProgress > zone.exitProgress)
                {
                    continue;
                }

                float zoneSpeed = zone.targetSpeed * cornerMultiplier;
                if (zoneSpeed < speeds[i])
                {
                    speeds[i] = zoneSpeed;
                }
            }
        }
        return speeds;
    }

    private static void ApplyBrakingLimits(float[] speeds, float[] segmentLengths, CarParameters car)
    {
        int count = speeds.Length;
        for (int pass = 0; pass < SettlePasses; pass++)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                int next = (i + 1) % count;
                float allowed = AISpeedModel.BrakingAllowedSpeed(speeds[next], car.brakeForce, segmentLengths[i]);
                if (allowed < speeds[i])
                {
                    speeds[i] = allowed;
                }
            }
        }
    }

    private static void ApplyAccelerationLimits(float[] speeds, float[] segmentLengths,
        CarParameters car, DriverProfile profile)
    {
        int count = speeds.Length;
        float skillAcceleration = AISpeedModel.SkillAcceleration(car.acceleration, profile.skillLevel);

        for (int pass = 0; pass < SettlePasses; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                float acceleration = AISpeedModel.FullThrottleAcceleration(
                    speeds[i], skillAcceleration, car.drag, car.engineBrakingForce, car.topSpeed);
                if (acceleration <= 0f)
                {
                    continue;
                }

                float unitsPerSecond = Mathf.Max(speeds[i] * RacerBase.MphToUnityUnitsPerSecond, 0.5f);
                float reachable = speeds[i] + (acceleration / unitsPerSecond) * segmentLengths[i];
                if (reachable < speeds[next])
                {
                    speeds[next] = reachable;
                }
            }
        }
    }

    private static float IntegrateLapTime(float[] speeds, float[] segmentLengths)
    {
        float lapSeconds = 0f;
        for (int i = 0; i < speeds.Length; i++)
        {
            int next = (i + 1) % speeds.Length;
            float averageSpeed = (speeds[i] + speeds[next]) * 0.5f;
            float averageUnitsPerSecond = averageSpeed * RacerBase.MphToUnityUnitsPerSecond;
            lapSeconds += segmentLengths[i] / averageUnitsPerSecond;
        }
        return lapSeconds;
    }

    private static string FormatLapTime(float seconds)
    {
        int minutes = (int)(seconds / 60f);
        float remainingSeconds = seconds % 60f;
        return $"{minutes}:{remainingSeconds:00.000}";
    }
}

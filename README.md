# GP Racing

A single-player Formula 1 racing game built in Unity. Players drive around real-world circuits against AI opponents across multiple laps, competing for position through braking, overtaking, and clean lap times.

---

## Overview

GP Racing is a top-down arcade racer with a simulation-inspired physics model. The focus is on tight, skill-based competition — the AI opponents race with distinct personalities, the track is built from real GPS data, and the physics reward clean lines and well-timed braking.

**Engine:** Unity  
**Language:** C#  
**Track:** Silverstone Circuit (real GPS coordinates)

---

## Architecture

### Spline-Based Movement

Every vehicle — player or AI — moves along a parametric spline that represents the track. Position is stored as two values: a normalised progress along the spline (0–1, wrapping each lap) and a signed lateral offset from the centre line. World position is derived from these at runtime.

```csharp
// SplineTrack.cs
public TrackSample Evaluate(double progress)
{
    double wrappedProgress = progress % 1.0;
    SplineUtility.Evaluate(_spline, (float)wrappedProgress, out float3 position,
        out float3 tangent, out float3 upVector);

    Vector3 worldPosition = transform.TransformPoint(position);
    Vector3 forward = transform.TransformDirection(tangent).normalized;
    Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;

    return new TrackSample(worldPosition, forward, right);
}
```

This approach keeps vehicles firmly on the track surface, makes lap progress trivially comparable for standings, and enables lane changes to be expressed as a single float — which is exactly what the AI needs.

---

### Physics Model

The physics layer lives in `RacerBase` and runs on top of the spline. It models the behaviours that make a racing game feel physical without the cost of a rigid body simulation.

**Slip angle** — the heading and velocity angle are tracked separately. The velocity direction lags behind the heading based on available grip, producing natural understeer through corners.

**Oversteer** — when the slip angle exceeds a threshold, the car rotates further than intended. Kerb contact drops grip to 40% and bleeds speed at 20 mph/sec.

**Drag** — applied quadratically, so top speed has a natural ceiling rather than a hard clamp.

```csharp
// RacerBase.cs — velocity direction lags heading based on grip
double slipAngle = _velocityAngle - _heading;
double gripFactor = IsOnKerb ? KerbGripMultiplier : 1.0;
double alignmentRate = BaseAlignmentRate * gripFactor;
_velocityAngle += slipAngle * alignmentRate * deltaTime;

// Quadratic drag
double dragForce = DragCoefficient * currentSpeed * currentSpeed;
currentSpeed -= dragForce * deltaTime;
```

---

### AI System

Each AI driver is defined by a `DriverProfile` — a set of 0–1 sliders that shape how they race.

```csharp
// DriverProfile.cs
public struct DriverProfile
{
    public string DriverName;
    public float SkillLevel;       // corner speed, braking courage, start delay
    public float Aggression;       // overtake/defend threshold
    public float Consistency;      // lap-to-lap noise
    public float BrakingCourage;   // how late they brake
    public float LinePreference;   // left/right lateral bias
}
```

At runtime the AI runs a finite state machine with four states:

| State | Behaviour |
|---|---|
| **Race** | Follows the racing line |
| **Overtake** | Moves to the opposite side of the car ahead and builds a speed boost |
| **Defend** | Moves to block the approaching car from behind |
| **Recover** | Returns to centre after a track limit violation or collision |

Corner speed is calculated by sampling the spline curvature 15 units ahead. The tighter the corner, the lower the target speed — then skill, banding, and consistency noise are layered on top.

```csharp
// AIController.cs — corner speed from spline curvature
private double CalculateTargetSpeedForCurvature()
{
    TrackSample currentSample = _splineTrack.Evaluate(_splineProgress);
    TrackSample lookaheadSample = _splineTrack.Evaluate(_splineProgress + LookaheadDistance);

    double angleDegrees = Vector3.Angle(currentSample.Forward, lookaheadSample.Forward);
    double baseCornerSpeed = CornerSpeedConstant / angleDegrees;
    double clampedCornerSpeed = Mathd.Clamp(baseCornerSpeed, MinCornerSpeed, MaxCornerSpeed);

    return clampedCornerSpeed * _driverProfile.SkillLevel * _bandingMultiplier;
}
```

---

### Rubber Banding

A dedicated `RubberBandController` adjusts each AI's speed multiplier in real time based on their gap to the player. Cars running ahead of the player are slowed; cars running behind are boosted.

The effect is graduated by field position and capped by driver skill, so backmarkers cannot magically close a 30-second gap and front-runners do not lose an unrealistic amount of pace.

```csharp
// RubberBandController.cs
private double CalculateBandingMultiplier(RacerBase racer, double gapToPlayer)
{
    double rawMultiplier = gapToPlayer > 0 ? AheadMultiplier : BehindMultiplier;

    double fieldStrength = CalculateFieldStrength(racer);     // 0.3–1.0 by position
    double skillCeiling  = racer.DriverProfile.SkillLevel;

    return 1.0 + (rawMultiplier - 1.0) * fieldStrength * skillCeiling;
}
```

---

### Track Generation

The Silverstone circuit is constructed from real GPS coordinates at startup. Longitude/latitude pairs are projected into flat-earth metres, scaled to Unity units, and fed into Unity's Spline API as Bezier knots.

```csharp
// Silverstone.cs — GPS to Unity world position
private Vector3 ConvertGpsToUnityPosition(double latitude, double longitude)
{
    double latitudeOffsetDegrees  = latitude  - OriginLatitude;
    double longitudeOffsetDegrees = longitude - OriginLongitude;

    double metresNorth = latitudeOffsetDegrees * MetresPerDegreeLatitude;
    double metresEast  = longitudeOffsetDegrees * MetresPerDegreeLatitude *
                         System.Math.Cos(OriginLatitude * DegreesToRadians);

    float unityX = (float)(metresEast  * UnityUnitsPerMetre);
    float unityZ = (float)(metresNorth * UnityUnitsPerMetre);

    return new Vector3(unityX, 0f, unityZ);
}
```

The road surface, kerb edges, and centre line are all procedural meshes built from the spline in edit mode. Corner zones are detected automatically by sampling heading changes along the spline — any change greater than 0.3° per sample triggers a kerb zone, which the physics layer then uses to apply grip and speed penalties.

---

### Camera

The chase camera follows the player with a smooth heading lag and applies two speed-scaled effects: field of view expansion (60° → 85° at top speed) and a low-frequency screen shake on both axes.

```csharp
// CameraController.cs — speed-scaled FOV and shake
float speedRatio = currentSpeed / MaxSpeed;

_camera.fieldOfView = Mathf.Lerp(BaseFov, MaxFov, speedRatio);

float shakeIntensity = speedRatio * speedRatio * MaxShakeAmplitude;
float shakeOffsetX   = Mathf.Sin(Time.time * ShakeFrequencyX) * shakeIntensity;
float shakeOffsetY   = Mathf.Sin(Time.time * ShakeFrequencyY) * shakeIntensity;
```

---

### Speedometer & Gear System

The player has a two-gear manual gearbox. The speedometer renders a gear-relative fill bar (rather than an absolute speed bar), so the bar resets at each gear change — matching the feel of watching engine revs rather than road speed.

When the player holds a gear past its maximum speed, engine braking applies. A redline flash overlays the bar above 95% of the gear ceiling.

---

## Debug Tooling

Two debug tools ship alongside the game and can be toggled at runtime.

**AIDebugOverlay** renders a colour-coded table of every AI car — state, current speed, target speed, lateral offset, banding multiplier, and lap — updated each frame.

**AILogger** writes a CSV to disk every 0.5 seconds and on every state change, producing a full telemetry record for post-session AI tuning.

---

## Project Structure

```
Assets/Scripts/
├── Racing/
│   ├── RacerBase.cs              Physics foundation for all vehicles
│   ├── AIController.cs           AI finite state machine and driver behaviour
│   ├── RaceStandingsTracker.cs   Live race position and lap tracking
│   ├── RubberBandController.cs   Dynamic difficulty scaling
│   ├── StartingGrid.cs           Grid layout and race start
│   └── DriverProfile.cs          Serialisable AI personality struct
├── Track/
│   ├── SplineTrack.cs            Spline evaluation and arc length
│   ├── Tracks/Silverstone.cs     GPS coordinate → spline builder
│   └── TrackSample.cs            Position/forward/right struct
├── PlayerVehicle/
│   ├── PlayerController.cs       Input handling and player physics
│   ├── CameraController.cs       Chase camera with speed effects
│   ├── GearSystem.cs             Two-gear manual gearbox
│   ├── CrashHandler.cs           Collision detection and respawn
│   └── VehicleSpeed.cs           Speed tracking for UI
├── TrackBuilding/
│   ├── RoadMeshBuilder.cs        Quad-strip road surface
│   ├── KerbMeshBuilder.cs        Kerb edge mesh with UV taper
│   ├── LineMeshBuilder.cs        Centre line with curvature fade
│   └── RoadTextureBuilder.cs     Procedural red/white kerb texture
├── Environment/
│   ├── TreePlacer.cs             Editor-mode tree placement tool
│   ├── GrandstandPlacer.cs       Editor-mode grandstand placement
│   ├── GrandStandController.cs   Runtime crowd facing behaviour
│   └── KerbZoneDetector.cs       Automatic corner zone detection
├── UI/
│   └── Speedometer.cs            Gear-relative speed bar and display
└── Debug/
    ├── AIDebugOverlay.cs          On-screen AI state table
    └── AILogger.cs                CSV telemetry writer
```

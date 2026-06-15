# GP Racing — Portfolio Documentation

A Unity arcade racing game built around a spline-based track system inspired by classic titles like OutRun. All gameplay, track construction, and environment authoring is driven by custom C# scripts; there are no physics-based vehicle components.

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Architecture Summary](#architecture-summary)
3. [Track System](#track-system)
4. [Track Building](#track-building)
5. [Player Vehicle](#player-vehicle)
6. [Environment](#environment)
7. [UI](#ui)
8. [Editor Tools](#editor-tools)
9. [Script Reference](#script-reference)

---

## Project Overview

| Detail | Value |
|--------|-------|
| Engine | Unity (URP) |
| Language | C# |
| Track | Silverstone (GPS-accurate spline) |
| Movement | Spline-rail (no Rigidbody) |
| Gear model | 2-gear manual shift |

The game places the player on a spline that represents the track centre line. Forward movement is driven by advancing a normalised progress value (0–1) along the spline; lateral movement offsets the car left/right from that centre line. This approach gives precise, deterministic control over car position without any physics simulation.

---

## Architecture Summary

```
Assets/Scripts/
├── Track/
│   ├── SplineTrack.cs          — wraps SplineContainer; exposes Evaluate()
│   ├── TrackSample.cs          — plain struct: position, forward, right
│   └── Tracks/
│       └── Silverstone.cs      — GPS → Unity-space spline at Awake
├── TrackBuilding/
│   ├── RoadMeshBuilder.cs      — flat tarmac ribbon
│   ├── KerbMeshBuilder.cs      — kerb strips with taper and zone support
│   ├── LineMeshBuilder.cs      — white edge lines that fade on corners
│   └── RoadTextureBuilder.cs   — procedural red/white kerb texture
├── Racing/
│   └── RacerBase.cs            — abstract base: spline advance + transform sync
├── PlayerVehicle/
│   ├── PlayerController.cs     — input, throttle/brake, gear shifting
│   ├── VehicleSpeed.cs         — converts internal speed to mph
│   ├── GearSystem.cs           — 2-gear gearbox with speed ranges
│   └── CrashHandler.cs         — collision detection + flash/respawn coroutine
├── Environment/
│   ├── KerbZone.cs             — serialisable [start, end] progress range
│   ├── KerbZoneDetector.cs     — auto-detects corners and feeds KerbMeshBuilder
│   ├── TreePlacer.cs           — spline-aligned tree spawner
│   ├── GrandstandPlacer.cs     — spline-aligned grandstand spawner
│   └── GrandStandController.cs — FacePlayer: rotates stand toward player
├── UI/
│   └── Speedometer.cs          — gear-relative bar fill + redline overlay
└── Editor/
    ├── TreePlacerEditor.cs         — paint-brush tool for tree placement
    ├── GrandstandPlacerEditor.cs   — paint-brush tool for grandstand placement
    └── KerbMeshBuilderEditor.cs    — click-to-place kerb zone tool
```

---

## Track System

### `SplineTrack` — [Assets/Scripts/Track/SplineTrack.cs](Assets/Scripts/Track/SplineTrack.cs)

The central query interface for anything that needs to know where the track is.

- Wraps Unity's `SplineContainer` and exposes a single `Evaluate(float progress)` method.
- `progress` is normalised (0–1) and wraps automatically, so callers never need to clamp.
- Returns a `TrackSample` struct containing world-space `position`, `forward`, and `right` vectors.
- Caches `TrackLength` on `Awake` so mesh builders can compute per-unit distances without re-querying the spline.
- Decorated with `[ExecuteInEditMode]` so mesh previews update live while editing the spline.

### `TrackSample` — [Assets/Scripts/Track/TrackSample.cs](Assets/Scripts/Track/TrackSample.cs)

A lightweight value type (`struct`) that carries the three vectors needed to position anything on the track: `position`, `forward`, and `right`. Passed by value to avoid allocation on hot paths.

### `Silverstone` (`SplineFromGps`) — [Assets/Scripts/Track/Tracks/Silverstone.cs](Assets/Scripts/Track/Tracks/Silverstone.cs)

Converts a hardcoded array of real GPS coordinates (longitude/latitude) into a closed Unity spline at `Awake`, before any other component reads the track.

**Conversion pipeline:**
1. Compute offsets in degrees from a fixed origin (52.071°N, -1.017°E).
2. Scale latitude offset by 111,320 m/° and longitude offset by the same factor × cos(lat) to account for meridian convergence.
3. Multiply by a `UnityUnitsPerMetre` constant (0.1) to keep the scene scale manageable.
4. Insert each point as a `BezierKnot` with `TangentMode.AutoSmooth`; close the spline.

This produces a geometrically accurate Silverstone layout from publicly available GPS data with no external assets.

---

## Track Building

All four mesh builders share the same pattern: sample the spline at a configurable `resolution`, build vertex/triangle/UV arrays, and assign them to a `MeshFilter`. They run in edit mode so the track can be previewed without entering Play mode.

### `RoadMeshBuilder` — [Assets/Scripts/TrackBuilding/RoadMeshBuilder.cs](Assets/Scripts/TrackBuilding/RoadMeshBuilder.cs)

Generates the flat tarmac surface as a triangle strip. Each sample contributes two vertices (left and right road edge) placed at `±roadWidth/2` along the `right` vector. UV `v` is mapped to normalised spline progress so a single tiling texture stretches uniformly around the track.

### `KerbMeshBuilder` — [Assets/Scripts/TrackBuilding/KerbMeshBuilder.cs](Assets/Scripts/TrackBuilding/KerbMeshBuilder.cs)

Builds the kerb geometry that sits just outside the tarmac edge. Key design decisions:

- **Kerb zones** — a `List<KerbZone>` defines which sections of the track have kerbs (corners only). Outside a zone the kerb width tapers to zero.
- **Tapering** — `CalculateKerbWidthAtSample` uses the distance from the nearest zone boundary to smoothly scale kerb width over a short `taperLength` (0.005 normalised units), avoiding hard pop-in at zone edges.
- **World-space UV distances** — rather than using normalised progress for the V coordinate, the builder accumulates the actual 3D distance along each kerb edge. This keeps the red/white stripe pattern physically consistent regardless of spline curvature.
- **Procedural texture** — `AssignKerbTexture` generates a tiny 4×64 red-and-white pixel texture at runtime and applies it directly to the shared material.
- **Runtime API** — `AddKerbZone`, `RemoveKerbZone`, and `RemoveLastKerbZone` are called by the editor tool; `BuildMeshWithZones` is called by `KerbZoneDetector` for automated placement.

### `LineMeshBuilder` — [Assets/Scripts/TrackBuilding/LineMeshBuilder.cs](Assets/Scripts/TrackBuilding/LineMeshBuilder.cs)

Draws the white edge lines that run along the inside of the kerbs. The lines disappear on corners by fading their vertex colour alpha to zero: `alpha = 1 − curvature`, where curvature is the normalised angle change between adjacent forward vectors clamped to `curvatureMaxDegrees`. This replicates the classic OutRun aesthetic where straight-road markings vanish in bends.

### `RoadTextureBuilder` — [Assets/Scripts/TrackBuilding/RoadTextureBuilder.cs](Assets/Scripts/TrackBuilding/RoadTextureBuilder.cs)

Generates a `Texture2D` at runtime that mimics the OutRun rumble-strip style: narrow red/white bands on the left and right edges of a dark tarmac field. All proportions (kerb fraction, repeat count, colours) are serialised fields, making visual tuning possible from the Inspector without recompiling.

---

## Player Vehicle

### `RacerBase` — [Assets/Scripts/Racing/RacerBase.cs](Assets/Scripts/Racing/RacerBase.cs)

Abstract base class for anything that moves along the track. Subclasses set `currentSpeed`, `lateralOffset`, and `splineProgress`; `RacerBase` handles the translation to world-space each frame.

- `AdvanceAlongTrack` increments `splineProgress` by `currentSpeed / trackLength × deltaTime`, giving a correct speed-to-progress relationship regardless of track length.
- `ApplyPositionToTransform` queries the spline, adds lateral displacement, applies the height offset, and sets both `position` and `forward` on the transform — so the car always faces the direction of travel.
- `MoveLaterally` and `ResetToSpline` are protected helpers for subclasses (and the crash system).

### `PlayerController` — [Assets/Scripts/PlayerVehicle/PlayerController.cs](Assets/Scripts/PlayerVehicle/PlayerController.cs)

Extends `RacerBase` with Unity Input System bindings. Reads five actions: `Move` (analogue stick/WASD), `ShiftUp`, `ShiftDown`, `Throttle`, and `Brake`. Throttle and brake are modelled as separate analogue axes so a trigger controller can apply partial inputs. The `isCrashed` flag short-circuits `Update` so the car freezes during the crash/respawn sequence without any special-case logic in `RacerBase`.

### `VehicleSpeed` — [Assets/Scripts/PlayerVehicle/VehicleSpeed.cs](Assets/Scripts/PlayerVehicle/VehicleSpeed.cs)

Converts the internal speed value (Unity units/second along the spline) to miles per hour using the standard conversion factor (2.23694). Kept as a separate component so the speedometer and any future systems can read `SpeedMilesPerHour` without depending on `PlayerController` directly.

### `GearSystem` — [Assets/Scripts/PlayerVehicle/GearSystem.cs](Assets/Scripts/PlayerVehicle/GearSystem.cs)

A two-gear manual gearbox. Each gear has a minimum and maximum speed in mph that the `Speedometer` uses to calculate the rev display. Gear 1 covers 0–100 mph; Gear 2 covers 100–212 mph (top speed). `ShiftUp` and `ShiftDown` guard against shifting beyond the available range and call `UpdateGearRange` to update the current speed band.

### `CrashHandler` — [Assets/Scripts/PlayerVehicle/CrashHandler.cs](Assets/Scripts/PlayerVehicle/CrashHandler.cs)

Detects `OnTriggerEnter` events with objects tagged `Grandstand` or `Tree` and runs a coroutine-based crash sequence:

1. Immediately freezes the car via `PlayerController.SetCrashed(true)`.
2. Waits `respawnDelay` seconds.
3. Flashes the `SpriteRenderer` on/off `flashCount` times.
4. Teleports the car back to `savedProgress` (the last known safe position, updated every frame while not crashing) via `ResetToSpline`.

---

## Environment

### `KerbZone` — [Assets/Scripts/Environment/KerbZone.cs](Assets/Scripts/Environment/KerbZone.cs)

A `[Serializable]` struct holding `startProgress` and `endProgress` in the 0–1 normalised range. Both fields use `[Range(0f, 1f)]` for safe Inspector editing. Shared between `KerbMeshBuilder`, `KerbZoneDetector`, and the editor tool.

### `KerbZoneDetector` — [Assets/Scripts/Environment/KerbZoneDetector.cs](Assets/Scripts/Environment/KerbZoneDetector.cs)

Automatically identifies corners on the track at `Start` and populates `KerbMeshBuilder` with the result, eliminating manual zone placement for initial setup.

**Algorithm:**
1. Sample `resolution` forward directions from `SplineTrack.Evaluate`.
2. Classify each sample as a corner if the angle to the next sample exceeds `cornerAngleThreshold`.
3. Walk the corner mask to extract contiguous zones, enforcing a `minimumZoneGap` to merge near-touching corners and adding `zonePadding` to each boundary.
4. Call `KerbMeshBuilder.BuildMeshWithZones` to rebuild immediately.

### `TreePlacer` — [Assets/Scripts/Environment/TreePlacer.cs](Assets/Scripts/Environment/TreePlacer.cs)

Manages an array of tree instances alongside the track. Each tree is stored as a spline `t` value, a lateral offset, a rotation offset, and a scale. On `RebuildTrees`, all children are destroyed and re-instantiated from the stored data. A raycast from above (height 50, max distance 100) snaps each tree to the terrain surface. Designed to be driven by `TreePlacerEditor` in edit mode.

### `GrandstandPlacer` — [Assets/Scripts/Environment/GrandstandPlacer.cs](Assets/Scripts/Environment/GrandstandPlacer.cs)

Mirrors `TreePlacer` for grandstand prefabs. Grandstands are placed on the right side of the track at a configurable `trackOffset`. Each stand is rotated to face inward toward the track centre line, with an additional `rotationOffset` for fine-tuning. Driven by `GrandstandPlacerEditor`.

### `GrandStandController` (`FacePlayer`) — [Assets/Scripts/Environment/GrandStandController.cs](Assets/Scripts/Environment/GrandStandController.cs)

Causes individual grandstand sprites to smoothly rotate toward the player as the car passes, within a clamped angular range (`maxRotationAngle`). This creates the illusion of a crowd reacting to the car without any skeletal animation. `originalRotationY` is recorded at `Start` and used as the neutral baseline; the current deviation (`currentDelta`) is lerped toward the target each frame.

---

## UI

### `Speedometer` — [Assets/Scripts/UI/Speedometer.cs](Assets/Scripts/UI/Speedometer.cs)

Drives a three-element HUD display:

| Element | Behaviour |
|---------|-----------|
| `barFill` (Image) | Fill amount maps current speed within the current gear's range (via `Mathf.InverseLerp`) up to 75% of the image fill |
| `redlineOverlay` (Image) | Visible only when fill exceeds 95% of the gear range; animates from the redline threshold up to the bar maximum |
| `speedText` (TextMeshPro) | Displays rounded mph |

The gear-relative speed fraction means the bar resets to the bottom when shifting up, mimicking a traditional rev counter.

---

## Editor Tools

### `TreePlacerEditor` — [Assets/Scripts/Editor/TreePlacerEditor.cs](Assets/Scripts/Editor/TreePlacerEditor.cs)

A `[CustomEditor]` that adds a paint-brush workflow to `TreePlacer` in the Scene view.

- **Brush radius** — a slider (0.5–20 units) controls the scatter radius around the nearest spline point.
- **Paint mode** — while active, left-click or drag plants a tree; a yellow translucent disc visualises the brush.
- **Spacing enforcement** — trees on the same side are rejected if the spline `t` distance to any existing tree is less than `spacing / splineLength`.
- **Side detection** — the dot product of the cursor-to-spline vector with the spline's right vector determines which side of the track the tree is placed on.
- All placements are wrapped in `Undo.RecordObject` for full undo support.

### `GrandstandPlacerEditor` — [Assets/Scripts/Editor/GrandstandPlacerEditor.cs](Assets/Scripts/Editor/GrandstandPlacerEditor.cs)

Equivalent paint tool for `GrandstandPlacer`. Click or drag along the track in the Scene view to place stands at evenly spaced intervals. Yellow sphere handles mark existing placements. Supports undo and `EditorUtility.SetDirty` for serialisation.

### `KerbMeshBuilderEditor` — [Assets/Scripts/Editor/KerbMeshBuilderEditor.cs](Assets/Scripts/Editor/KerbMeshBuilderEditor.cs)

Provides a two-click workflow for adding kerb zones directly on the track mesh in the Scene view:

1. Click **Place Kerb Zone** in the Inspector.
2. Click the track to set the zone start (the nearest spline progress point to the mouse ray is found by brute-force sampling at 1000 points).
3. Click again to set the zone end; the builder immediately rebuilds with the new zone.

Existing zones are listed in the Inspector with individual **Remove** buttons, plus a **Remove Last** shortcut.

---

## Script Reference

| Script | Category | Key Responsibility |
|--------|----------|--------------------|
| [SplineTrack.cs](Assets/Scripts/Track/SplineTrack.cs) | Track | Spline query interface (`Evaluate`) |
| [TrackSample.cs](Assets/Scripts/Track/TrackSample.cs) | Track | Value type: position + orientation |
| [Silverstone.cs](Assets/Scripts/Track/Tracks/Silverstone.cs) | Track | GPS coordinates → closed spline |
| [RoadMeshBuilder.cs](Assets/Scripts/TrackBuilding/RoadMeshBuilder.cs) | Track Building | Tarmac ribbon mesh |
| [KerbMeshBuilder.cs](Assets/Scripts/TrackBuilding/KerbMeshBuilder.cs) | Track Building | Kerb mesh with zone tapering |
| [LineMeshBuilder.cs](Assets/Scripts/TrackBuilding/LineMeshBuilder.cs) | Track Building | Edge lines fading on corners |
| [RoadTextureBuilder.cs](Assets/Scripts/TrackBuilding/RoadTextureBuilder.cs) | Track Building | Procedural kerb texture |
| [RacerBase.cs](Assets/Scripts/Racing/RacerBase.cs) | Racing | Abstract spline-movement base |
| [PlayerController.cs](Assets/Scripts/PlayerVehicle/PlayerController.cs) | Player | Input → throttle, brake, steer, shift |
| [VehicleSpeed.cs](Assets/Scripts/PlayerVehicle/VehicleSpeed.cs) | Player | Speed unit conversion (units/s → mph) |
| [GearSystem.cs](Assets/Scripts/PlayerVehicle/GearSystem.cs) | Player | 2-gear gearbox with speed ranges |
| [CrashHandler.cs](Assets/Scripts/PlayerVehicle/CrashHandler.cs) | Player | Collision detection + flash/respawn |
| [KerbZone.cs](Assets/Scripts/Environment/KerbZone.cs) | Environment | Serialisable progress range |
| [KerbZoneDetector.cs](Assets/Scripts/Environment/KerbZoneDetector.cs) | Environment | Auto-detect corner zones |
| [TreePlacer.cs](Assets/Scripts/Environment/TreePlacer.cs) | Environment | Spline-aligned tree placement |
| [GrandstandPlacer.cs](Assets/Scripts/Environment/GrandstandPlacer.cs) | Environment | Spline-aligned grandstand placement |
| [GrandStandController.cs](Assets/Scripts/Environment/GrandStandController.cs) | Environment | Crowd sprite tracks player |
| [Speedometer.cs](Assets/Scripts/UI/Speedometer.cs) | UI | Gear-relative rev bar + speed text |
| [TreePlacerEditor.cs](Assets/Scripts/Editor/TreePlacerEditor.cs) | Editor | Paint-brush tree authoring tool |
| [GrandstandPlacerEditor.cs](Assets/Scripts/Editor/GrandstandPlacerEditor.cs) | Editor | Paint-brush grandstand authoring tool |
| [KerbMeshBuilderEditor.cs](Assets/Scripts/Editor/KerbMeshBuilderEditor.cs) | Editor | Click-to-place kerb zone tool |

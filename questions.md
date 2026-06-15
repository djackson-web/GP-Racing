# Technical Interview Questions — GP Racing

Questions are grouped by system. Each answer references the actual implementation in this project.

---

## Table of Contents

- [Unity & C# Fundamentals](#unity--c-fundamentals)
- [Spline & Track System](#spline--track-system)
- [Vehicle Physics](#vehicle-physics)
- [Camera System](#camera-system)
- [AI & Finite State Machines](#ai--finite-state-machines)
- [Race Management & Game Loop](#race-management--game-loop)
- [Procedural Mesh Generation](#procedural-mesh-generation)
- [Editor Tooling](#editor-tooling)
- [Performance & Architecture](#performance--architecture)
- [Debugging & Tooling](#debugging--tooling)

---

## Unity & C# Fundamentals

---

**Q: What is the difference between `Update`, `LateUpdate`, and `FixedUpdate`, and when would you use each?**

**A:**
- `Update` runs once per rendered frame. Use it for input polling, game logic, and anything that should respond to frame rate.
- `LateUpdate` runs after all `Update` calls on all objects have completed. It guarantees that the target object has finished moving before you react to it. In this project the camera uses `LateUpdate` so it reads the player's final position after physics and input have been applied.
- `FixedUpdate` runs at a fixed timestep (default 50 Hz), decoupled from frame rate. It is the correct place for physics forces and rigid body manipulation.

In GP Racing the camera is deliberately in `LateUpdate`, not `Update`, so there is never a one-frame lag where the car appears to jump ahead of the camera.

---

**Q: What does `[SerializeField]` do and why use it instead of making a field `public`?**

**A:** `[SerializeField]` exposes a private field to the Unity Inspector without making it accessible from other scripts. Making a field `public` achieves the same Inspector visibility but also makes it part of the class's public API, which breaks encapsulation and makes it hard to reason about who can modify the value.

In `CameraController.cs` every tunable parameter (`behindDistance`, `rotationLagSpeed`, `maximumShakeAmount`, etc.) is `[SerializeField] private` so designers can tweak them in the Inspector without any script being able to set them at runtime.

---

**Q: Explain the difference between a `struct` and a `class` in C#. Why is `TrackSample` a struct?**

**A:** A `class` is a reference type — allocated on the heap, accessed via a pointer. A `struct` is a value type — allocated on the stack (or inline in the parent object), copied on assignment.

`TrackSample` holds three `Vector3` fields (position, forward, right). It is returned from `SplineTrack.Evaluate()` on every frame for every racer. If it were a class, each call would allocate heap memory and create garbage collection pressure. As a struct the data lives on the stack and is copied by value, producing zero allocations.

The rule of thumb: prefer a struct when the object is small (a few fields), short-lived, and logically a value rather than an identity.

---

**Q: What is a coroutine in Unity and how does it differ from a thread?**

**A:** A coroutine is a method that can suspend execution and resume in a later frame using `yield` statements. It runs on the main thread — it is not concurrent. It is cooperative multitasking managed by Unity's scheduler.

A thread is a separate execution context managed by the OS and can truly run in parallel on another CPU core.

In `CrashHandler.cs` the respawn sequence is a coroutine:

```csharp
IEnumerator CrashSequence()
{
    // disable input
    yield return new WaitForSeconds(1f);        // wait 1 second
    for (int i = 0; i < 6; i++)
    {
        spriteRenderer.enabled = !spriteRenderer.enabled;
        yield return new WaitForSeconds(0.1f);  // flash sprite
    }
    // respawn
}
```

Using a thread here would be dangerous — Unity's API is not thread-safe and calling `spriteRenderer.enabled` from another thread would throw exceptions.

---

**Q: What is the Singleton pattern and what are its trade-offs?**

**A:** The Singleton pattern ensures a class has exactly one instance and provides a global access point to it.

In `RaceStandingsTracker.cs`:

```csharp
public static RaceStandingsTracker Instance { get; private set; }

void Awake()
{
    Instance = this;
}
```

**Advantages:**
- Convenient global access — any racer can call `RaceStandingsTracker.Instance.GetPosition(this)` without needing a reference passed in.
- Simple to implement.

**Trade-offs:**
- Hidden coupling — any class can depend on it without that dependency being visible in the constructor or method signature.
- Difficult to unit test — you cannot inject a mock easily.
- Lifetime management — if the scene unloads and the singleton object is destroyed, stale references elsewhere will throw null reference exceptions.

For a game of this scope the convenience outweighs the downsides, but in a larger codebase you would likely replace it with dependency injection or a service locator.

---

**Q: What is `Mathf.Lerp` and what is a common mistake developers make with it?**

**A:** `Mathf.Lerp(a, b, t)` linearly interpolates between `a` and `b` by fraction `t` (0 = `a`, 1 = `b`).

The common mistake is using it for "smooth damping" like this:

```csharp
value = Mathf.Lerp(value, target, speed * Time.deltaTime);
```

This produces an exponential decay (each frame closes a fraction of the remaining gap), not a linear interpolation. It *looks* smooth but the rate of change depends on frame rate — on a slow machine it moves more slowly. For true frame-rate-independent smooth following you should use `Mathf.SmoothDamp` or multiply by `1 - Mathf.Exp(-speed * Time.deltaTime)`.

In this project the camera FOV and rotation both use this pattern intentionally for the "floaty" feel it produces, which is an acceptable artistic choice as long as the team is aware of the frame-rate dependency.

---

## Spline & Track System

---

**Q: What is a spline and why is it a good fit for a racing game track?**

**A:** A spline is a smooth curve defined by a set of control points. The curve interpolates (or approximates) those points using polynomial segments — Bezier or Catmull-Rom are the most common forms.

For a racing track it is ideal because:
1. **Continuous parametrisation** — any position on the track can be addressed by a single float `t ∈ [0, 1]`, which makes progress tracking, AI look-ahead, and distance calculations trivial.
2. **Smooth curvature** — the curve is differentiable, so you can always get a valid tangent (forward direction) at any point, which is needed for steering and camera orientation.
3. **Designer-friendly** — artists can drag control points in the editor to reshape the track without touching code.
4. **Closed loop support** — Unity's Splines package supports closed splines natively, so lap-counting is just `Mathf.Floor(progress)`.

---

**Q: How does `SplineTrack.Evaluate` work, and what does each output field represent?**

**A:** It converts a normalised progress value (which may be > 1 for multiple laps) to a world-space position and orientation:

```csharp
public TrackSample Evaluate(float progress)
{
    float t = Mathf.Repeat(progress, 1f);   // wrap to [0, 1]
    splineContainer.Spline.Evaluate(t, out float3 pos, out float3 tangent, out float3 up);
    Vector3 right = Vector3.Cross(up, tangent).normalized;
    return new TrackSample { position = pos, forward = tangent.normalized, right = right };
}
```

- `position` — world-space point on the centreline.
- `forward` — unit tangent: which direction the track is heading at that point. Used to orient racers and calculate slip angle.
- `right` — perpendicular to the track in the horizontal plane. Used to position racers laterally and calculate whether they are on the kerb or off-track.

---

**Q: How does the GPS import in `Silverstone.cs` work, and what approximation does it make?**

**A:** The script stores 122 latitude/longitude pairs and converts them to Unity world units using a flat-earth (equirectangular) projection centred on a fixed origin point:

```csharp
float northing = (lat - originLat) * 111320f;
float easting  = (lon - originLon) * 111320f * Mathf.Cos(originLat * Mathf.Deg2Rad);
```

111,320 metres is the approximate length of one degree of latitude. The longitude is multiplied by `cos(latitude)` to account for the fact that longitude degrees get shorter as you move away from the equator.

The approximation breaks down over large distances (the Earth is not flat) but for a ~5 km circuit it produces less than 1 metre of distortion — imperceptible at the 0.1 units-per-metre scale used.

---

**Q: How is lap counting implemented without explicit "finish line" logic?**

**A:** Because progress is a continuously incrementing float (not clamped to [0, 1]), the lap count is simply the integer part:

```csharp
public int LapCount => Mathf.FloorToInt(RaceProgress);
```

`RaceProgress` at 2.75 means lap 3, 75% complete. This avoids any trigger volume, collision detection, or finish-line raycast. Standings are sorted by `RaceProgress` descending, so overtaking mid-lap is handled automatically without special cases.

---

## Vehicle Physics

---

**Q: Why are the vehicle physics implemented in spline space rather than using Unity's Rigidbody?**

**A:** A `Rigidbody`-based approach would produce realistic 3D physics but at the cost of complexity: suspension, tyre friction models, weight transfer, and collision resolution all become necessary to get consistent behaviour. Tuning it for an arcade feel is notoriously difficult.

The spline-space approach models the car as a point moving along a 1D track with a lateral offset. Physics resolve to just two numbers — progress and lateral position — which are easy to tune, guarantee the car stays "on" the track, and keep AI and player code identical. The trade-off is that the simulation is less physically accurate and cannot model scenarios like leaving the track completely.

---

**Q: Explain the slip angle model in `RacerBase`. What is understeer and oversteer in this context?**

**A:** The slip angle is the difference between the direction the car is pointing (heading) and the direction it is actually moving (velocity angle).

**Understeer** — the velocity chases the heading at a rate controlled by `grip`:
```csharp
velocityAngle += slipAngle * grip * Time.deltaTime;
```
If you steer sharply, the heading snaps to the new angle but the velocity takes time to catch up. The car pushes wide — intuitive, controllable.

**Oversteer** — once slip exceeds a 20° threshold, the rear kicks harder:
```csharp
if (Mathf.Abs(slipAngle) > overSteerThreshold)
    velocityAngle += Mathf.Sign(slipAngle) * excessSlip * overSteerForce * Time.deltaTime;
```
This creates a runaway rotation if the driver does not correct. A heading clamp of ±90° from the track direction prevents the car from spinning completely.

---

**Q: Why does the drag formula use `speedFraction²` rather than a linear term?**

**A:** Real aerodynamic drag scales with velocity squared (drag force = ½ρCdAv²). Using the quadratic form means:
- At low speed, drag is negligible and the car accelerates freely.
- At high speed, drag grows rapidly and naturally creates a top speed limit where drag equals thrust.

A linear drag term would produce a different (less realistic) curve — the car would reach top speed more gradually and feel "floaty" at high speed.

```csharp
currentSpeed -= drag * speedFraction * speedFraction * Time.deltaTime;
```

---

**Q: How does the gear system enforce a maximum speed, and why is engine braking applied instead of hard clamping?**

**A:** Hard clamping (`currentSpeed = Mathf.Min(currentSpeed, maxSpeed)`) would cause the car to instantly lose speed the moment it crosses the limit — jarring and unrealistic. Instead, when speed exceeds the gear maximum, a braking force is applied:

```csharp
if (currentSpeed > GearSystem.CurrentGearMaximumSpeed)
    currentSpeed -= engineBrakingForce * Time.deltaTime;
```

This produces a gradual deceleration — the car can momentarily exceed the limit (e.g. down a hill) and bleeds speed naturally. It also means the player feels the car "pulling back" rather than hitting an invisible wall.

---

**Q: What is the purpose of `SpriteBillboard` and when would you use billboarding?**

**A:** `SpriteBillboard` rotates an object each frame so it always faces the camera. This is used for the player's 2D sprite in a 3D world — without it the sprite would appear as a thin sliver when viewed from the side.

Billboarding is used when you want a 2D asset (sprite, particle, UI element in world space) to always present its full face to the viewer. Common uses: trees in distance LODs, health bars, damage numbers, simple NPC sprites. It is cheaper than a fully 3D model and can be visually convincing for small or distant objects.

---

## Camera System

---

**Q: Why does the camera use rotation lag instead of directly matching the player's rotation?**

**A:** Direct rotation matching would make the camera snap immediately to every input. On tight corners this feels jarring — the horizon swings hard and the player loses spatial context. Rotation lag (interpolating the stored rotation toward the target each frame) means the camera trails the car through corners, keeping the upcoming track in view slightly ahead of the car's nose. This gives the player more time to react and feels more cinematic.

```csharp
currentRotation = Quaternion.Lerp(currentRotation, target.rotation, rotationLagSpeed * Time.deltaTime);
```

---

**Q: Why does screen shake use `speedFraction²` instead of `speedFraction`?**

**A:** A linear scaling would make shake noticeable at medium speeds where it would feel distracting rather than exciting. Squaring the value compresses most of the effect into the top end of the speed range — at 50% of top speed the shake is only 25% of its maximum, so it only becomes clearly visible when the car is genuinely fast.

```csharp
float shakeAmount = maximumShakeAmount * speedFraction * speedFraction;
```

This is a common technique in game feel design: use a power curve to concentrate an effect at the extreme end of a range.

---

**Q: Why are the two shake sine waves at different frequencies (×1.3 and ×0.7)?**

**A:** If both axes used the same frequency they would oscillate in lockstep, producing a perfectly diagonal back-and-forth movement that reads as artificial. Slightly different frequencies cause the X and Y offsets to drift in and out of phase over time, producing an irregular, organic-feeling wobble that is harder to consciously perceive as a pattern.

```csharp
float shakeOffsetX = Mathf.Sin(shakeTime * 1.3f) * shakeAmount;
float shakeOffsetY = Mathf.Sin(shakeTime * 0.7f) * shakeAmount;
```

---

**Q: The camera is initialised in `Start` to avoid a one-frame pop. What would happen without that initialisation?**

**A:** On the first frame `currentRotation` would default to `Quaternion.identity` (no rotation). The camera would be positioned at identity orientation — likely pointing along the world Z axis — and then snap to the correct position on the second frame. The player would see a flash of the wrong camera angle for one frame. By setting `currentRotation = target.rotation` in `Start` and immediately computing the correct offset, the camera is already in the right place on frame one.

---

## AI & Finite State Machines

---

**Q: What is a Finite State Machine and why is it a good choice for AI in a racing game?**

**A:** An FSM is a model where a system can be in exactly one of a finite set of states at any time, with defined rules (transitions) for moving between them.

For racing AI it is a good fit because:
- The number of meaningful behaviours is small and well-defined: race normally, overtake, defend, recover.
- Transitions have clear, testable conditions (gap thresholds, position limits).
- It is easy to visualise and debug — the current state is a single enum value shown as a coloured gizmo above each car.
- Adding a new behaviour (e.g. "pit stop") means adding one state and its transitions without touching existing states.

---

**Q: How does the AI calculate a safe corner speed?**

**A:** It samples the spline at 6 points, each 15 units apart, and measures the angle between consecutive forward vectors:

```csharp
float maxAngleDeg = 0f;
for (int i = 0; i < lookAheadSamples; i++)
{
    TrackSample a = splineTrack.Evaluate(progress + i * step);
    TrackSample b = splineTrack.Evaluate(progress + (i + 1) * step);
    float angle = Vector3.Angle(a.forward, b.forward);
    maxAngleDeg = Mathf.Max(maxAngleDeg, angle);
}
float baseSpeed = cornerSpeedFactor / maxAngleDeg;   // 900 / maxAngle
baseSpeed = Mathf.Clamp(baseSpeed, 60f, 220f);
```

A tighter corner produces a larger angle, which divides into a smaller base speed. The result is then scaled by the driver profile's `skillLevel` and `brakingCourage`. This gives each AI a naturalistic braking point rather than a lookup table or hand-authored waypoints.

---

**Q: What is rubber banding and how is the graduated effect implemented?**

**A:** Rubber banding artificially adjusts AI speed based on their gap to the player, preventing runaway leads and keeping the race competitive.

The graduated effect in `RubberBandController.cs` applies stronger correction to backmarkers and weaker correction to cars near the front:

```csharp
// Position-based strength: backmarker = 1.0 (full), frontrunner = 0.3 (mild)
float positionStrength = Mathf.Lerp(1f, 0.3f, standingsPosition / totalCars);

float gapFraction = Mathf.InverseLerp(deadZone, maxGap, Mathf.Abs(gap));
if (gap < 0)  // player ahead — AI is behind, speed them up
    multiplier = Mathf.Lerp(1f, 1f + (0.15f * positionStrength), gapFraction);
else          // player behind — AI is ahead, slow them down
    multiplier = Mathf.Lerp(1f, 1f - (0.12f * positionStrength), gapFraction);
```

The dead zone (0.05 laps) prevents the system from activating when cars are side-by-side, which would feel unfair.

---

**Q: Why does the Defend state have a reaction delay tied to the `consistency` trait?**

**A:** A perfect AI that blocks instantly would be frustrating — it would always see the player coming and react before a human could. The delay simulates reaction time:

```csharp
float reactionDelay = Mathf.Lerp(0.4f, 0.0f, profile.consistency);  // low consistency = slower
yield return new WaitForSeconds(reactionDelay);
// now move to blocking position
```

A driver with low `consistency` (erratic, less skilled) reacts slowly and can be caught off guard. A driver with high consistency reacts almost instantly. This makes overtaking feel earned rather than relying on AI mercy.

---

**Q: How does the AI steer — it doesn't have a `Rigidbody`, so how does heading change?**

**A:** The AI inherits `RacerBase`, which tracks a `heading` angle and a `lateralOffset`. Steering means rotating `heading` toward a target bearing and adjusting lateral offset toward a target position:

```csharp
// Determine target point 15 units ahead with desired lateral offset
TrackSample ahead = splineTrack.Evaluate(splineProgress + lookAheadDistance / trackLength);
Vector3 targetPos = ahead.position + ahead.right * targetLateralOffset;

// Compute required heading change
Vector3 toTarget = targetPos - transform.position;
float targetBearing = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
float delta = Mathf.DeltaAngle(heading, targetBearing);
heading += Mathf.Clamp(delta, -maxSteerRate, maxSteerRate) * Time.deltaTime;
```

The heading change propagates into the slip angle model in `RacerBase`, which then drives `lateralOffset` change — the same physics as the player.

---

## Race Management & Game Loop

---

**Q: How does `RaceStandingsTracker` calculate positions without a finish line or sector system?**

**A:** Every racer has a `RaceProgress` property that accumulates as a float across laps (lap 2, 80% complete = 2.8). The tracker sorts all racers by this value each frame:

```csharp
racers.Sort((a, b) => b.RaceProgress.CompareTo(a.RaceProgress));
```

Position is the index in the sorted list + 1. Because `RaceProgress` is monotonically increasing and accounts for full laps, it correctly ranks a car on lap 3 ahead of one on lap 2 regardless of where they are on track.

---

**Q: How does `StartingGrid` position cars in a staggered formation?**

**A:** It evaluates the spline at a configurable start progress, then offsets each car by a row spacing converted to spline progress units and alternates the lateral column:

```csharp
for (int i = 0; i < racers.Count; i++)
{
    float rowProgress = startProgress + i * (rowSpacing / trackLength);
    float lateralOffset = (i % 2 == 0) ? -columnOffset : columnOffset;

    TrackSample sample = splineTrack.Evaluate(rowProgress);
    racers[i].transform.position = sample.position + sample.right * lateralOffset;
    racers[i].transform.forward  = sample.forward;
}
```

This is fully data-driven — adding more AI cars to the scene automatically extends the grid without code changes.

---

**Q: What is an event in C# and how is `OnRaceComplete` used here?**

**A:** A C# event is a delegate that allows subscribers to register callbacks without the publisher knowing who is listening. It follows the observer pattern.

```csharp
// RaceStandingsTracker.cs
public event Action OnRaceComplete;

// When leader completes required laps:
OnRaceComplete?.Invoke();
```

Any system (UI, audio, replay camera) can subscribe:
```csharp
RaceStandingsTracker.Instance.OnRaceComplete += ShowResultsScreen;
```

The advantage over a direct method call is decoupling — `RaceStandingsTracker` does not need to know about the UI, audio, or any other system. Each system self-registers and handles the event independently.

---

## Procedural Mesh Generation

---

**Q: Walk through how `RoadMeshBuilder` constructs the road mesh.**

**A:** It iterates 500 evenly spaced progress values along the spline. At each sample it places two vertices — one on each edge of the road — using the `TrackSample.right` vector:

```csharp
for (int i = 0; i <= resolution; i++)
{
    float t = (float)i / resolution;
    TrackSample s = splineTrack.Evaluate(t);

    vertices[i * 2]     = s.position - s.right * halfWidth;   // left edge
    vertices[i * 2 + 1] = s.position + s.right * halfWidth;   // right edge

    uvs[i * 2]     = new Vector2(0, t);
    uvs[i * 2 + 1] = new Vector2(1, t);
}
```

Then it connects consecutive pairs of vertices into quads (two triangles per segment). UVs stretch the road texture along the length of the track, so the texture scrolls naturally with progress.

---

**Q: Why does `LineMeshBuilder` fade the centre line to transparent on corners?**

**A:** On a straight, a centre line gives the player a clear reference for their lane position. On a tight corner it would cut across the inside of the apex and mislead the eye — the "correct" line through a corner is very different from the geometric centre. Fading it out on curves avoids giving bad visual guidance without needing to author the racing line manually:

```csharp
float curvature = Vector3.Angle(sampleA.forward, sampleB.forward);
float alpha = 1f - Mathf.Clamp01(curvature / fadeAngleThreshold);
```

Sections with more than 20° of angle change per sample become fully transparent.

---

**Q: What is the purpose of the kerb taper in `KerbMeshBuilder`?**

**A:** Without tapering, kerbs would start and end with a hard vertical edge — a visible seam where the mesh abruptly goes from full width to zero. The taper gradually scales the kerb width to zero over the first and last 0.5% of each zone, producing a smooth blend into the flat road surface. This is the same technique used in real road construction where kerbs ramp up from nothing rather than starting at full height.

---

**Q: What does `[ExecuteInEditMode]` do, and why is it used on the mesh builders?**

**A:** `[ExecuteInEditMode]` (or `[ExecuteAlways]` in newer Unity) causes the component's `Update`, `Awake`, and `OnDestroy` methods to also run in the editor outside of play mode. The mesh builders use this so the road, kerbs, and line meshes update in the Scene view immediately as a designer drags a spline control point, without having to enter Play mode to see the result. This dramatically speeds up track layout iteration.

---

## Editor Tooling

---

**Q: What is a custom editor in Unity and how does `TreePlacerEditor` use one?**

**A:** A custom editor is a class in the `Editor` folder that inherits from `UnityEditor.Editor` and is tagged with `[CustomEditor(typeof(TargetClass))]`. It replaces the default Inspector for that component and can also intercept Scene view events via `OnSceneGUI`.

`TreePlacerEditor` overrides `OnSceneGUI` to draw a brush disc, intercept mouse clicks, find the nearest spline point, enforce spacing rules, and call `TreePlacer.PlaceTree()`. Without this the designer would have to manually enter spline T values and lateral offsets as numbers in the Inspector.

---

**Q: How does the weighted prefab palette work for tree placement?**

**A:** Each `WeightedPrefab` has a `Weight` float. When sampling, the weights are normalised and a random 0–1 value selects a prefab by accumulated weight:

```csharp
float total = palette.Sum(p => p.Weight);
float roll  = Random.value * total;
float accum = 0f;
foreach (var entry in palette)
{
    accum += entry.Weight;
    if (roll <= accum) return entry.Prefab;
}
```

A palette of `[Oak: 0.6, Pine: 0.3, Birch: 0.1]` will produce roughly 60% oaks, 30% pines, and 10% birches without any manual counting or repetition in the palette list.

---

**Q: Why is undo support important in an editor tool, and how is it implemented here?**

**A:** Without undo, a mis-click in a paint tool could place an unwanted tree that the designer cannot remove without manually hunting for it or clearing the whole scene. Undo makes the tool safe to use experimentally.

Unity's `Undo` system works by recording the state of serialised objects before modification:

```csharp
Undo.RecordObject(treePlacer, "Paint Tree");
treePlacer.PlacementRecords.Add(newRecord);
```

After `RecordObject`, any changes to the object are trackable and Ctrl+Z will revert them. For grouping multiple operations (a paint stroke) into a single undo step, `Undo.CollapseUndoOperations` merges them after the stroke ends.

---

**Q: What is `SessionState` and why is it used to persist brush settings?**

**A:** `SessionState` is a Unity editor API that stores key-value pairs that survive recompilation and scene changes within a single editor session, but are cleared when the editor is closed. It is used for editor-only transient state that is too ephemeral for `EditorPrefs` (which persist between sessions) but needs to survive the domain reload that happens on script changes.

In `TreePlacerEditorSettings.cs`, brush radius and scatter strength are stored in `SessionState` keyed by the component's instance ID. This means each `TreePlacer` component in the scene remembers its own brush settings during a working session without cluttering the serialised scene data.

---

## Performance & Architecture

---

**Q: How would you profile performance issues in this project?**

**A:**
1. **Unity Profiler** (Window → Analysis → Profiler): Records CPU time per method per frame. Identify spikes in Update or heavy GC.Alloc calls that indicate garbage being created every frame.
2. **Frame Debugger**: Step through draw calls to find overdraw or unexpected render passes.
3. **Memory Profiler**: Snapshot heap state to find object accumulation over time.

Common candidates in this project:
- `RaceStandingsTracker` sorts a list every frame — acceptable for small racer counts but `List.Sort` allocates a comparison delegate. Use `racers.Sort(staticComparer)` with a reusable `IComparer` to eliminate the allocation.
- `AIDebugOverlay` rebuilds its string every frame using `string.Format`. Use `StringBuilder` or `ToString` with pooled buffers.
- Mesh builders create new `Vector3[]` arrays on every rebuild — fine for edit-mode tools, but in a runtime rebuild scenario these should be pre-allocated.

---

**Q: `RacerBase` is abstract. What does `abstract` mean in C# and why is it appropriate here?**

**A:** An `abstract` class cannot be instantiated directly — it exists only to be subclassed. Abstract methods declared in it must be implemented by every concrete subclass.

`RacerBase` is abstract because:
- You never want a "generic racer" in the scene — every racer is either a `PlayerController` or an `AIController`.
- It enforces that both subclasses implement the same physics contract (spline progress, lateral offset, speed) without duplicating the implementation.
- It makes the type system express the design intent: `RacerBase` is a concept, not a concrete thing.

---

**Q: The `SpriteBillboard` uses `sqrMagnitude` instead of `magnitude` for its null check. Why?**

**A:** `Vector3.magnitude` involves a square root operation (`√(x²+y²+z²)`), which is relatively expensive. `sqrMagnitude` returns `x²+y²+z²` with no square root. When you only need to check whether a vector is non-zero (or above some threshold), comparing `sqrMagnitude > 0` is mathematically equivalent and faster.

---

**Q: If the game needed to support 50 AI cars instead of 8, what would you change?**

**A:**
- **`RaceStandingsTracker.Sort`**: Move from O(n log n) per-frame sort to an insertion-sort approach (standings rarely change drastically in one frame), or sort less frequently and interpolate display positions.
- **`AIController` look-ahead sampling**: 6 spline evaluations × 50 cars = 300 spline evaluations per frame. Cache spline curvature in a pre-computed array at startup and do a simple array lookup instead.
- **`AIDebugOverlay`**: Currently iterates all cars every frame. With 50 cars this is acceptable, but the string allocations should be eliminated.
- **`RubberBandController`**: Scales linearly — fine for 50 cars.
- **Mesh builders**: Unaffected — they are edit-mode tools, not runtime systems.

---

## Debugging & Tooling

---

**Q: What is the purpose of `AILogger` and how would you use its output?**

**A:** `AILogger` writes a CSV file (`AI_log.txt`) capturing each AI car's state, speed, target speed, lateral offset, and rubber-band multiplier at 0.5-second intervals, with additional rows on state transitions.

You would use it to:
- **Validate the AI FSM**: Check that cars actually enter Overtake/Defend states and that state durations are sensible.
- **Balance driver profiles**: Plot target speed vs. actual speed to see if `brakingCourage` values feel right around specific corners.
- **Debug rubber banding**: Verify the multiplier stays within expected bounds and does not cause obvious speed surges.
- **Regression testing**: Record a reference run, make a change, re-run, diff the CSVs.

---

**Q: What is a Gizmo in Unity and why are they extensively used in this project?**

**A:** Gizmos are visual aids drawn in the Scene view (and optionally the Game view) that are never rendered in a build. They are drawn in `OnDrawGizmos` or `OnDrawGizmosSelected`.

This project uses them heavily because:
- `AIController` draws a colour-coded sphere above each car so you can see the FSM state at a glance without opening the Inspector.
- `StartingGrid` draws the grid positions before Play mode so you can verify car spacing without running the game.
- `KerbMeshBuilderEditor` previews zone boundaries as the designer clicks.
- `TreePlacerEditor` draws the brush circle and existing tree positions in the Scene view.

All of this removes the need to run the game to verify level design and AI configuration, which dramatically speeds up iteration.

---

**Q: How would you add a new AI state, say a "PitStop" state, to the existing FSM?**

**A:**
1. Add `PitStop` to the `AIState` enum.
2. Define entry condition in `UpdateStateTransitions()` — e.g. lap count >= 2 and not yet pitted.
3. Implement `UpdatePitStopState()` — decelerate, steer to pit lane spline branch, stop for N seconds, rejoin.
4. Define exit condition — timer elapsed and speed above threshold.
5. Add the gizmo colour for `PitStop` in `OnDrawGizmos`.
6. Add a CSV label for `AILogger`.

The existing state methods (`UpdateRaceState`, `UpdateOvertakeState`, etc.) are untouched. This is the open/closed principle — the FSM is open for extension by adding states and transitions, closed for modification of existing state logic.

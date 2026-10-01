# Decisions

## 2026-10-01 - Persistent audio director across scenes via DontDestroyOnLoad

**Decision:** `AnatomyAudioDirector` marks its host GameObject `DontDestroyOnLoad` and enforces a singleton pattern across all scenes.

**Why:** Switching between the Launcher, Eye Anatomy, Engine, and Volvo exhibits previously reloaded or destroyed the audio player on every scene boundary. This created audible popping, stutters, and stopped the background music track midway. A persistent audio root allows the soothing ambient track to stream continuously while micro-interaction sound effects continue to fire with zero interruption.

**Cost:** Audio state survives scene loads, requiring careful volume ducking and level management when transitioning between scenes with dedicated audio (such as the Volvo S90 ignition and exhaust idle).

## 2026-10-01 - 4-state dynamic color feedback on the stylus beam

**Decision:** `AnatomyStylusBeam` drives the stylus beam and tip color dynamically across four visual states: Calm Cyan (resting/primary laser), Amber Orange (Back/Reset button 1), Purple Magenta (Tertiary button 2), and Emerald Green (aiming at interactive button or 3D hotspot badge while pressing select).

**Why:** In stereoscopic 3D space, depth perception alone is insufficient for users to know whether the pointer is accurately hovering over an actionable target or empty air. Changing the beam color to Emerald Green when beaming on interactive UI or hotspot badges provides unmistakable confirmation before triggering actions.

**Cost:** One extra raycast check per frame against `Selectable` and `EyeHotspot` colliders, mitigated by caching hit components.

## 2026-10-01 - UI 50% scale reduction, corner pinning, and AlwaysOnTop canvas sorting

**Decision:** All user interfaces across the suite are scaled down by 50%, pinned strictly to screen corners (controls on top-left, info panels on top-right), and set to render on a dedicated UI camera overlay layer (`UiAlwaysOnTop`).

**Why:** The original wireframe UI occupied substantial screen center space, obscuring the 3D focal models. Furthermore, physics raycasts from the stylus pen were colliding with large background model colliders instead of UI buttons. Pinning UI to corners and enforcing top-level canvas sorting resolved both occlusion and raycast deadlocks.

**Cost:** Text sizes had to be carefully balanced and paired with TextMeshPro Auto-Sizing so legibility remains crisp at 50% scale.

## 2026-10-01 - Hotspots anchored to local mesh vertices without pivot rotation swing

**Decision:** Hotspot badges are positioned in the local coordinate space of their target part mesh, with world positions derived dynamically from the part's transform matrix rather than from an orbit around the exhibit root pivot.

**Why:** Previously, badges orbited the exhibit center at a distance of 15–30 cm. Rotating the model caused the badges to swing wildly away from the parts they labelled, clipping through geometry or floating in empty space. Deriving positions tight to the mesh (2–5 cm) keeps badges visually locked to their corresponding components regardless of exhibit orientation.

**Cost:** Badges must recompute world positions on part transformation, which is already amortized in the controller's update loop.

## 2026-10-01 - Volvo S90 interior courtesy light and dashboard emission separation

**Decision:** The cabin lighting on Volvo S90 is split into a low-intensity courtesy point light (`0.0008f` intensity, `0.039m` range), a low-emission overhead console (`Color(0.12, 0.10, 0.08)`), and a dedicated digital cockpit screen channel (`Color(0.55, 0.62, 0.72)`). Headlights are focused to a 44° cone at `0.018f` intensity angled 4° down.

**Why:** The original lamp rig applied `1.5` HDR emission to the untextured `CeilingConsole` mesh, and blasted an unshadowed `0.012f` point light inside a 3 cm cabin. This bleached the entire interior, windshield, and sunroof to solid white and burned an over-exposed crater into the showroom floor. The calibrated values provide a refined, photorealistic Scandinavian luxury aesthetic.

**Cost:** An additional lighting channel (`"Dashboard"`) wired into `accessoryChannels`.

## 2026-10-01 - Sub-asset persistence for VolumeProfile components via AddObjectToAsset

**Decision:** Volume components added dynamically via editor scripts (`Tonemapping`, `Bloom`) must be explicitly registered via `AssetDatabase.AddObjectToAsset(component, profile)`.

**Why:** Calling `profile.Add<T>()` in an editor script attaches the component in memory, but without `AddObjectToAsset`, Unity fails to serialize the component into the `.asset` YAML on disk, leaving null references `{fileID: 0}`. Without Tonemapping serialized, URP fell back to un-tonemapped rendering where HDR values > 1.0 clipped to blinding white.

**Cost:** Two lines of code in `UpgradePostProcessing`, ensuring robust serialization.

## 2026-09-30 - Finishing a run is not the same as the visitor leaving

**Decision:** Probe turns off `ShowcaseScene.autoResetWhenResolved` and manages its own replay.
Only the idle timeout returns the scene to Attract.

**Why:** a bug that made `S4-6` completely non-functional. Completing a run resolved the scene;
the lifecycle then auto-reset to Attract a few seconds later; and `ProbeGame` treats Attract as
"somebody new has walked up", which clears the difficulty and the best time. So every run started
from difficulty zero and the tunnel never tightened, however well anyone played.

The lifecycle was conflating two different events that happen to look alike from inside it - a
task finishing, and a visitor leaving. They need different consequences: the first offers a
harder tunnel to the same person, the second wipes the slate for a stranger. Only the scene knows
which of those it wants, so the scene owns the replay.

**Cost:** one more scene that configures the shared lifecycle rather than taking its defaults.
Worth it - the default is right for Bloom and Stack, where a run has no natural end.

## 2026-09-30 - The tunnel wall is a distance, not a collider

**Decision:** contact in Probe is the tip's distance from the generated centreline exceeding the
lumen radius, tested against the polyline directly.

**Why:** a collider shell for a thin curved tube is awkward to build, unreliable to test from the
inside, and answers a slightly different question than the rule being scored. A point-to-polyline
distance is exact, costs one pass over 140 segments, and says precisely what the rule says.

It also gives progress along the tunnel for free, from the same query - which the run needs for
entry, completion and the attract marker.

**Cost:** the wall is invisible geometry. Mitigated by the rails and hoops, which mark exactly
where it is.

## 2026-09-30 - The tunnel is swept geometry, never a LineRenderer

**Decision:** Probe's rails and hoops are generated meshes, swept along their centrelines with
parallel-transport frames.

**Why:** `LineRenderer` billboards to the camera, and this display has two. Facing one eye points
it away from the other, and the mismatch will not fuse - the same reason Bloom's motes are spheres
rather than quads. Swept geometry has one orientation that is correct for both eyes by
construction.

Parallel transport rather than recomputing a perpendicular at each sample, because the naive
version makes the rails spiral wherever the curve turns, which reads as the tunnel twisting.

**Cost:** about 4,300 vertices and a sweep routine. The routine does the rails and the hoops both
- a hoop is a closed sweep around a circle - so it is written once.

## 2026-09-30 - The grab offset is stored rotation-only, never through InverseTransformPoint

**Decision:** `StylusGrab` records where a block was grabbed as
`Quaternion.Inverse(held.rotation) * (tipPosition - held.position)` rather than
`held.InverseTransformPoint(tipPosition)`.

**Why:** a bug found by grabbing a block off centre. `Transform.InverseTransformPoint` also
divides by `localScale`, and the offset was being put back with rotation alone - so the scale
never cancelled. Every block in Stack is a unit cube scaled to about 23 mm, which makes that a
factor of roughly forty: **a grab 1 mm off centre held the block 40 mm away from the hand**, the
comfort clamp then pinned it to the edge of the window, and it read as a window violation.

It hid neatly behind the first test, which parked the tip exactly at a block's centre and
therefore had no offset to get wrong. Grabbing things by their middle is the one case where the
bug is invisible.

**Cost:** none. The rotation-only form is also cheaper.

## 2026-09-30 - Layout is expressed in fractions of the comfort volume, never in metres

**Decision:** every dimension in Stack - bench, platform, block size, scatter area - is a
fraction of `StereoVolume.Window` or `StereoVolume.DepthLimit`, resolved at build time.

**Why:** the panel size is not a constant of this project. The SDK's rig prefab defaults to
15.6 inches, a 345 x 194 mm window; the eye exhibit overrides `screen.screenType` to 27, giving
598 x 336. **The two existing exhibits are authored for different panels.**

A bench authored in metres against one of those overhangs the frame on the other, which is a
window violation - and it is invisible while authoring, because flat there is nothing to see. The
first build of this scene did exactly that: a 400 mm bench inside a 345 mm window, one violation
reported by the audit.

Deriving from the volume makes the scene correct on any panel and at any view scale, and it
removes the question of which panel is right from the scene entirely.

**Cost:** a layout struct resolved at build time, and dimensions that cannot be read straight off
the inspector. Worth it - this is the class of error the comfort tooling exists to catch, and it
is better not to make it.

## 2026-09-30 - Being above the platform is not the same as being on it

**Decision:** `StackGame` counts a block towards the tower only when it is over the platform's
horizontal footprint, not merely higher than its top face.

**Why:** the bench top sits slightly below the platform top, so every block still lying where it
started was higher than the platform and counted. A freshly built scene reported ten stacked
blocks before anyone had touched anything, and a block nudged on the bench would have registered
as a collapse.

Height alone is the obvious test and it is wrong whenever the thing you are stacking on is not
the lowest surface in the scene.

**Cost:** one bounds test per block per frame, and a margin so a block overhanging the platform
edge still counts.

## 2026-09-30 - Gravity is divided by the scale factor, not left at Earth's

**Decision:** Stack sets `Physics.gravity` to 2 m/s squared while it is loaded, and restores it
on unload.

**Why:** the blocks are about 23 mm because that is what fits the comfort volume, but they are
standing in for objects roughly five times that size. Earth gravity drops a 23 mm block its own
height in about 70 ms, which reads as frantic and is genuinely too fast to place anything into.

Dividing g by the same factor the world is scaled down by restores the timing of the object being
portrayed - the general rule for a model at scale s is g over s.

Restoring it on unload matters because gravity is a project-wide setting rather than a scene one,
and leaving it lowered would quietly change the physics of every other scene loaded afterwards in
the same session.

**Cost:** a global touched at runtime. Contained by the restore.

## 2026-09-30 - Every haptic call is guarded on the pen being visible

**Decision:** `StylusHaptics` checks `KmaxStylus.Visible` before every call into the SDK's
vibration path, not just for a null component.

**Why:** found by running Bloom. The `KmaxStylus` component exists in the scene whether or not a
physical pen is connected, so a null check passes in the editor - and then
`PenTracker.Vibrate` sends a command through `PNClient`, which has no connection without
hardware and throws a `NullReferenceException` from inside the SDK. It surfaced on play-mode
exit, where `OnDisable` calls `StopVibration`, but every cue had the same hole.

This would have hit all four scenes, and it fails in the most misleading way possible: an
exception with a stack trace entirely inside vendor code, on a frame where nothing obviously
happened.

**Cost:** none. `HasPen` is now meaningful rather than trivially true.

## 2026-09-30 - Motes are spheres, because a billboard has no correct orientation in stereo

**Decision:** Bloom's motes are sphere meshes rather than camera-facing quads.

**Why:** a billboard is oriented to face the camera, and on this display there are two. Facing it
at one eye points it away from the other, and the mismatch is exactly the kind of disagreement
between the two views that will not fuse - the quad reads as a flickering artefact rather than as
an object. There is no single correct orientation to pick.

Real geometry has one orientation that is right for both eyes by construction. At a few hundred
triangles per mote and forty-eight motes it costs nothing worth measuring against the 6.5 M
triangles the Volvo already renders.

**Cost:** about 37 k triangles for the field. Irrelevant at this scene's complexity.

## 2026-09-30 - Window safety is a fade, not a check

**Decision:** motes dim as `StereoVolume.ProjectedMargin` runs out and are retired before they
can be clipped, instead of being given a lifetime tuned to expire in time.

**Why:** the plan assumed a mote reaches the frame edge by drifting sideways. It does not have
to. Projection from the eye magnifies anything in front of the glass, so **a mote travelling
straight at the viewer loses margin on its own** - the nearer it gets, the larger the area of
screen it covers, and the sooner it runs out of window.

A tuned lifetime is a guess about that geometry, and it silently becomes wrong when the view
scale changes, when the panel changes, or when the drift speed is adjusted. A margin test is the
geometry itself, and it stays correct through all three.

**Cost:** one projection per mote per frame. Trivial, and it is the same arithmetic
`ViolatesWindow` already runs.

## 2026-09-30 - URP derives blend state from the surface toggles, so only the toggles are written

**Decision:** the Bloom material sets `_Surface` and `_Blend` and leaves `_SrcBlend` and
`_DstBlend` alone. The verification checks the destination factor, not the source one.

**Why:** two mistakes in one, both found by reading the material back after writing it.

Setting `_SrcBlend` and `_DstBlend` directly does nothing that lasts - URP re-derives them from
`_Surface` and `_Blend` when the material is validated on import, and overwrites whatever was
written. Exactly the same trap as setting `_ClearCoatMask` without `_ClearCoat` on the Volvo:
**write the thing the pipeline reads from, not the thing it computes.**

And URP's additive mode resolves to `SrcAlpha, One`, not `One, One`. The first check flagged a
perfectly correct material as broken. `SrcAlpha, One` is the better of the two here anyway -
alpha drives the trail's fade to nothing while the destination factor keeps the blend purely
additive - so additive is properly defined by the **destination** factor being One, with depth
write off.

**Cost:** a narrower assertion. Worth stating because the obvious assertion is wrong.

## 2026-09-30 - The comfort budget is a static type, not a scene component

**Decision:** `StereoVolume` is a static class that reads the live `XRRig`, rather than a
MonoBehaviour a scene has to carry and configure.

**Why:** the numbers are not the scene's to own. They belong to the SDK and the panel - the
camera distance is a constant on `StereoCamera`, the comfort edges are literals in the rig's
own gizmo code, and the window comes from `VirtualScreen.ScreenType`. A component would add a
second copy of all of that, which could be wired up wrong, forgotten in a new scene, or left
stale after someone changed the view scale.

Static also means the four scenes and the editor validator read exactly the same numbers with
no setup at all, which is most of what makes this reusable on the next Kmax project.

**Cost:** a static that caches the rig, so it has to survive domain reloads and scene changes.
Handled by re-resolving whenever the cached reference is null, which is the case Unity's
fake-null already covers.

## 2026-09-30 - Contact is a sphere query, not a trigger callback

**Decision:** `StylusTip` finds what it is touching with `Physics.OverlapSphereNonAlloc` every
frame, instead of carrying a trigger collider and a kinematic rigidbody.

**Why:** a kinematic trigger moving at hand speed tunnels through thin colliders, and when two
overlaps begin and end on the same frame it reports them in an order that depends on the solver
rather than on what happened. Neither is acceptable for the one interaction the whole suite
rests on.

An explicit query is one call, it is deterministic, and it returns the full current set - which
the grab system needs anyway, because choosing the nearest of several candidates is not
expressible as a stream of enter and exit events.

**Cost:** one physics query per frame, and a hard cap of sixteen simultaneous contacts.

## 2026-09-30 - A held object stays dynamic

**Decision:** `StylusGrab` drives the held body by setting velocity, and leaves it
non-kinematic.

**Why:** the instinct is to make a held object kinematic so it tracks the hand exactly. Then it
passes through everything. In Stack, the collision between the block in your hand and the tower
you are building **is the game** - a kinematic block would slide through the stack and place
itself inside it.

Driving by velocity lets the solver resolve contact normally. The cost is that a held object can
be pushed off the hand by something solid, which is correct and reads as weight.

**Cost:** the follow is a servo, so it needs tuning. `followTime`, at 0.04 s on no evidence, is
the first thing to adjust on hardware.

## 2026-09-30 - Both grab selection modes ship before the gate, not after

**Decision:** `StylusGrab` carries tip-proximity **and** ray selection behind one enum, even
though `S0-7` was supposed to decide which one to build.

**Why:** the plan made `S1-7` depend on a measurement that needs hardware, which would have left
the core unfinishable until the device was available. Writing both took about an hour, because
the two differ only in how a candidate is chosen - everything downstream, the spring follow, the
release, the haptics, the comfort clamping, is identical.

So the gate now flips a field rather than triggering a rewrite, and M0 can no longer invalidate
M1. The measurement still matters enormously - it decides whether the suite is co-located or
merely pointed - but it no longer blocks work.

**Cost:** one code path that will probably never ship. Cheap insurance.

## 2026-09-30 - The tip falls back to the mouse

**Decision:** with no pen tracked, `StylusTip` follows the mouse across the glass with the
scroll wheel controlling depth, and `StylusGrab` grabs with the left mouse button.

**Why:** every scene in the suite is otherwise unreachable, and the hardware is not available
yet. This makes all four buildable and debuggable in the editor.

It is deliberately not dressed up as the real thing. It cannot answer any question about how an
interaction feels, because it is not co-located - it only proves the logic runs.
`StylusTip.IsTracked` is false while it drives, so a scene can refuse to score a run that was
not done on the pen.

**Cost:** a development path in shipping code. Kept honest by the flag.

## 2026-09-30 - The showcase suite is a separate stack, not more exhibits

**Decision:** the four new scenes get their own assembly, `Scripts/Showcase/Runtime`, beside
`KmaxDisplayExample` rather than inside it. Nothing from the Volvo, the eye or the engine is
extended.

**Why:** the existing runtime is a catalogue-driven inspect-and-explode stack. You give
`EyeAnatomyController` a catalogue, a pose set and a model root, and it frames parts, ghosts
their neighbours and flies a camera between them. That generalises across three very
different models, which is the proof it is the right abstraction for what it does.

It is the wrong abstraction for direct manipulation. Nothing in it has a concept of the
viewer's hand being in the same cubic centimetre as an object, and the pieces that would have
to be added - tip colliders, grab state, spring-lagged follow, physics release - do not
compose with focus-and-ghost. Bending one into the other would leave both harder to read.

What does carry over is everything underneath the abstraction: `ProceduralAudio`,
`UiAlwaysOnTop`, `UiButtonMotion`, `ExhibitPostProcessing`, the attract-mode pattern, and
above all the Kmax plumbing - world-space canvas with `UIScaler` and an explicit event
camera, `KmaxInputModule` on the `EventSystem`. That took three sessions to get right and
none of it is exhibit-specific.

**Cost:** two stacks to maintain. Accepted, because they are genuinely different shapes and
the second one is the deliverable that the next Kmax project starts from.

## 2026-09-30 - The suite is designed so depth is required, not decorative

**Decision:** every scene is built around a task that cannot be completed without stereo,
rather than around a scene that looks deep.

**Why:** a diorama gets admired for five seconds. A task where you misjudge a placement,
fail, move your head, and then succeed makes the case for the hardware in a way that
survives being described to someone who was not there - and, more usefully, in a way that
cannot be faked in a screenshot or a video. For a display whose whole value proposition is
the thing a 2D recording cannot carry, that is the only honest demo.

This is why Stack puts the target platform at **zero parallax** and the held block at
-0.05 to -0.08 m. The instinct is to pop out the thing you are aiming at. The opposite is
correct: the stable reference belongs where the display is most comfortable and precise, and
the pop-out belongs on the object under your hand, where the depth contrast is doing work
rather than decoration.

**Cost:** rules out the prettiest option as the headline. Reef is in the suite, but second,
and its job is to show the volume rather than to prove it.

## 2026-09-30 - The pop-out budget becomes an object, not a constant

**Decision:** `StereoVolume` derives the comfort limits from `XRRig.Screen`, `XRRig.ViewScale`
and `StereoCamera.DefaultDistance` at runtime, and exposes `ViolatesWindow(Bounds)`. No scene
hard-codes 0.13.

**Why:** the numbers are the SDK's, not ours. `StereoCamera.DefaultDistance = 0.5f` and the
comfort gizmo in `XRRig.DrawFrustum` at 0.37 and 0.80 give 0.13 m of pop-out and 0.30 m of
depth - but all of it scales with `ViewScale`, which is settable at runtime and raises a
change event. A constant copied into four scenes is wrong the first time anyone zooms.

`ViolatesWindow` is the part with no SDK equivalent. An object at negative parallax that is
clipped by the frame edge gives the viewer disparity saying "in front" and occlusion saying
"behind", and that contradiction is the single most common way a pop-out effect fails. It is
cheap to detect and nearly impossible to eyeball while authoring.

**Cost:** one more component every scene depends on. Worth it - this is the piece most likely
to be lifted into the next Kmax project unchanged.

## 2026-09-30 - M0 is a gate on the whole suite

**Decision:** half a day of measurement on the hardware before any scene is built, with an
explicit go/no-go on tip-grab versus ray-grab.

**Why:** Stack and Probe both assume the tracked pen tip and the rendered geometry occupy the
same physical point to within a few millimetres. Nothing in this project has ever needed that
to be true - `AnatomyStylusInput` uses the pen as a ray-caster and an orbit controller, and a
ray is forgiving of a centimetre of offset in a way that a grab radius is not. So it is an
assumption inherited from the hardware's marketing, not a measurement.

If it is wrong, Stack and Probe do not survive in their designed form and the suite becomes
ray-based. That is a cheap thing to learn in half a day and an expensive one to learn in M3.

The same spike clears four other questions that have been open since the Volvo work and were
never resolvable in the editor: whether 0.13 m of pop-out is comfortable on this panel, how
head tracking behaves when a viewer leans or steps away, end-to-end latency, and whether the
wallpaper effect and semi-transparent fusion problems flagged on the car are real.

**Cost:** half a day before anything visible exists.

## 2026-09-29 - Ghosting is a property of the tour stop, not of focusing

**Decision:** `EyePartDefinition` gains `KeepOthersSolid`, and `EyeFocusView.Focus` takes an
overload saying whether to fade the rest of the model. On the car only the two interior
stops fade it; the other six leave the bodywork alone.

**Why:** fading everything else is right for a part that would otherwise be buried inside
the model - which is every part of an eye - and wrong for one that is already on the
outside. Ghosting a car's bodywork to show its alloy wheel throws away the paint the viewer
has just chosen, so the exhibit's two headline features fought each other: pick a colour,
start the tour, and the colour is gone.

Stored as the negative - "keep solid" rather than "ghost" - because a bool added to a
serialised class deserialises as **false** in every asset written before it existed, whatever
default you would have preferred. False has to mean the old behaviour.

**Cost:** a focused exterior stop is now distinguished only by the framing, the camera
flight, the selected badge and the info panel. That turns out to be plenty.

## 2026-09-29 - The tail lamp glass was never red

**Decision:** the lens materials are tinted by the build step - deep red for the diffusers
and main lens, amber for the indicator section.

**Why:** the model ships every tail lamp lens white or grey with no texture;
`Translucent_Glass` and its .001 sibling are pure white at 55% alpha. All the red came from
the emission the light rig adds at runtime. That held up while the exhibit was a car
floating against black. Over a lit showroom floor the lenses pick up far more light than
the emission adds and the lamp renders pale yellow-white with a red smear in it, which is
what it was reported as.

A tail light is red in daylight too, so the fix belongs in the glass rather than in the
emission. The lamp channels came down at the same time - tail from 5.0 to 3.4, indicators
from 5.0 to 3.6, fog and reverse likewise - because those were all set against a black
backdrop and were the brightest things in frame by a wide margin over a lit floor.

**Cost:** four more materials the build step owns rather than the model.

## 2026-09-29 - A solo panel is re-applied on a re-run, not handed back untouched

**Decision:** `BuildSoloPanel` updates an existing hinge against the spec instead of
returning early.

**Why:** the same mistake `BuildDoor` was fixed for one session earlier, in the one place
that had not been corrected. Raising the sunroof's angle in the table did nothing at all,
silently, because the hinge already existed in the scene - the build logged success and
changed no behaviour. An idempotent build step has to converge on the spec from wherever
the scene is, not only from empty.

The angle itself went from 14 degrees to 32. It did tilt at 14 - measured, the rear edge
lifts 6 mm - but the panel is transparent glass on a car 74 mm tall, and a movement that
small through a clear pane reads as nothing happening at all.

**Cost:** none. This is what the other build steps already did.

## 2026-09-29 - The drifting motes come off this exhibit

**Decision:** the three ambient mote layers are switched off on the Volvo. The burst, ring
and spark systems stay.

**Why:** the motes exist to give a model floating in a void something to be read against.
The showroom floor gives the car a ground plane, a cast shadow and a horizon, which is a
far better depth cue than dust. Over a dark backdrop the motes were invisible until you
looked for them; over a lit floor they are white specks two or three pixels across
scattered across it, and they read as noise in the render rather than as atmosphere.

**Cost:** the exhibit has less going on while nobody is touching it. The attract loop still
moves the car, and the interaction effects still fire.

## 2026-09-29 - The floor's brightness is set by the clipping measurement

**Decision:** floor albedo 0.54 and key intensity 1.15, arrived at by measuring the
rendered frame rather than by eye.

**Why:** at 0.72 and 1.5 the lit window panes on the floor blew out and **14.43%** of the
frame clipped. This module's own bar, set when the eye's post-processing was calibrated, is
0.00% for the model itself. The floor is the largest bright surface in shot, so it is the
one that decides whether there is any headroom left for the things that should be bright.

After: 4.29% clipped, and the brightest floor reads sRGB 0.73-0.82. What remains is the
lamps and the specular highlights on the paint, which are light sources and ought to be at
the top of the range.

**Cost:** two more numbers that only make sense together, and a measurement to redo if
either moves.

## 2026-09-29 - The key light goes through a window

**Decision:** the key directional light carries a generated cookie - a rotated grid of window
panes - thrown across the floor and over the car.

**Why:** an even directional light says nothing about where it comes from. The same light
through a window says there is a window, a wall and a building, none of which have to be
modelled, and it gives the floor something to be interesting about: a plain disc under even
light is a grey disc however well it is shaded. It is the thing the reference shot this was
built against does with its floor, and it cost one 512px texture.

The grid is rotated inside the texture rather than the light being turned, because the key's
direction is already doing a job - raking along the flank the viewer is looking at - and
turning it for a diagonal pattern would cost the modelling.

The mullions are 0.32 rather than 0, because the shaded floor between the panes is still a
lit showroom floor. At 0.22 the gobo read as one bright rectangle on black.

**Cost:** two more numbers that have to agree - `cookieSize2D` is the size of one window in
metres, and it only makes sense against the floor's radius.

## 2026-09-29 - Car paint needs a different shader, not a higher smoothness

**Decision:** `Car Paint` is moved to `Universal Render Pipeline/Complex Lit` with the clear
coat on. The rest of the model stays on Lit.

**Why:** real car paint is two surfaces - a coloured base and a near-mirror lacquer over it -
and the streak that runs the length of a wing is the lacquer reflecting the room, not the
colour underneath catching a highlight. URP models exactly that, but **only in Complex Lit**.
The plain Lit shader has the clear coat *properties* on it, because they are part of the
shared URP property block, and ignores them completely: mask 1, smoothness 0.96, keyword set,
and no change whatsoever to the render.

Three separate traps sat behind getting it on, and each failed silently:

- `Lit` declares no `_CLEARCOAT` keyword at all. `material.shaderKeywords` still reported it
  as present while `IsKeywordEnabled` returned false.
- `Material.EnableKeyword(string)` does not set a local keyword that is not overridable, which
  `_CLEARCOAT` is. The typed `LocalKeyword` overload does. The string overload writes the name
  into the list and returns without error.
- `_ClearCoat` and `_ClearCoatMask` are **different properties** - a feature toggle and its
  strength. URP re-validates the material on import and rebuilds the keyword from the toggle,
  so setting the mask and the keyword while the toggle stayed at 0 had the keyword switched
  straight back off with nothing logged.

`EnableLocalKeyword` now does it properly and warns if it did not take.

**Cost:** Complex Lit is heavier and forward-only. It is on the seventeen body renderers and
nothing else. Property names are shared, so the swatches carry across untouched.

## 2026-09-29 - The studio is a bright floor and a dark ceiling, not a lit ceiling

**Decision:** the reflection cubemap is inverted from its first version - bright below, dark
above, with window bands low on the walls.

**Why:** a car body is a vertical curve, so the top of a wing reflects what is overhead and
its flank reflects what is level with it. Put the bright thing overhead, as the first version
did, and the whole car goes pale and flat. Put it around the walls with dark above and bright
below and the boundary between the two sweeps along the panel as it curves, which is the long
streak that makes paint read as paint rather than as coloured plastic.

A bug was doing most of the damage. `Mathf.SmoothStep(0.55f, 0.95f, x)` was being used as a
0-to-1 mask and cannot be one - it interpolates *between* its first two arguments - so the
entire upper hemisphere was lit to at least 0.55 of full softbox brightness. That is why the
floor's grazing reflection measured sRGB 0.47 and everything read washed out. There is now a
`Step` with real edges.

That helper then had a second bug of its own, found the same way: it clamped its span to a
positive minimum, which turns every *falling* edge into a hard rising one. The window gobo is
defined by distance out from a pane's centre and is therefore all falling edges, so it came
out inverted - bright on its mullions, dark in its panes, with no soft edge anywhere.

**Cost:** the cubemap now differs from the eye's environment in kind, not just in degree. The
eye is near-white tissue at low smoothness and is read from its diffuse shading; it wants the
plain gradient and still has it.

## 2026-09-29 - Headlight beams were set against empty space

**Decision:** the headlight spots drop from intensity 1.4 to 0.16, their range from 2.2 to 1.5
car-lengths, and their cone widens from 52/22 degrees to 78/58.

**Why:** they were authored when there was nothing in front of the car for them to land on -
the comment on them said as much. Once the showroom floor went in they burned a hard white
pool into it a few centimetres from the bumper, which is what "some spot is having concentrated
lights" was. A tight inner cone over that short a throw is precisely what concentrates.

The lamp emissives came down with them, headlights from 5.2 to 3.2 and daytime running lights
from 5.4 to 3.6: those were set against a black backdrop, and over a lit floor with bloom on
top they were the brightest thing in frame by a wide margin.

**Cost:** the beams no longer read as headlights in a dark room, because it is no longer a dark
room. They read as headlights in a showroom, which is what this now is.

## 2026-09-29 - The icons are drawn by the build step

**Decision:** `ExhibitIconFactory` renders six glyphs as signed distance fields and saves them
as sprites. Nothing is downloaded.

**Why:** the build step is the single author of everything this exhibit is made of - the
catalogue, the studio cubemap, the floor and its two maps - and a downloaded icon set is a
third-party licence to track in a repository that currently has none. Six glyphs of circles,
boxes and capsules do not justify one.

Distance fields rather than drawn bitmaps because the antialiasing then comes free and exact:
a pixel's coverage is its distance from the edge through a one-pixel smoothstep, which beats
supersampling and is considerably less code.

Icon **and** label, not icon alone. The buttons could be a third the width without the words,
but this is read by someone who has never seen the exhibit and will not hover anything to find
out what a glyph means. The icon is what lets the row be scanned; the word is what makes it
certain.

**Cost:** they are geometric rather than designed, and a seventh glyph means writing it.

## 2026-09-29 - The pipeline's quality settings were sized for a desktop game, not for this

**Decision:** `EyeAnatomySceneUpgrader.UpgradeRenderQuality` sets the shared URP asset to a
2 m shadow distance, soft shadows on and 4x MSAA, and every builder calls it.

**Why:** all three were at defaults and therefore doing nothing. The shadow distance was
**50 m** over a 2048 shadow map - 24 mm of world per texel, about eleven texels across a
car 260 mm long and fewer than five across the eye. The key light has had `LightShadows.Soft`
set on it since the first build and has never cast a shadow anyone could see. Two metres
covers the whole orbit range and buys back a factor of twenty-five.

Soft shadows and MSAA were both off. These exhibits are small, curved, high-contrast
silhouettes against a near-black backdrop, which is the case aliasing shows up worst on,
and the rig had the headroom: 6.5M triangles across both eyes at 105-164 fps.

In the shared upgrader rather than in one builder because the URP asset is shared by all
three scenes. Split across the builders, whichever ran last would win.

**Cost:** the eye and the engine now get real shadows where they effectively had none, so
both look different. That is a fix, but it is a visible change to two scenes that were
signed off without it.

## 2026-09-29 - The car stands on a floor, and the floor's edge is hidden with smoothness

**Decision:** a generated disc under the car - `Showroom/Floor`, 0.45 m radius - with a
radial albedo gradient *and* a radial smoothness falloff, on its own scene root.

**Why:** the exhibit had a key light casting soft shadows onto nothing, because there was
no surface under the car to receive one. A car floating with no contact beneath it is the
single thing that most says "3D model" rather than "car".

Three measurements drove the rest of it, each correcting a guess:

- The first floor rendered to **within 0.004** of the backdrop and was completely
  invisible. Backdrop measured sRGB 0.169, 0.176, 0.208; the floor is now authored against
  that number rather than against an assumption.
- It was still invisible at the resting view, because that view was level with the car's
  centre and a horizontal disc seen from dead level is a horizontal line. Hence
  `ViewerFlyController.homePitch`, 14 degrees on this exhibit and 0 on the other two.
- The disc's triangles were wound so the face pointed **down**. Correctly placed,
  correctly coloured, and backfacing.

Then the edge. Fading the albedo does not hide it: at its far rim the floor is seen at a
grazing angle, which is exactly where a smooth surface reflects most, and it was mirroring
the studio softboxes at sRGB 0.47 against a backdrop of 0.17 while its own albedo there
was 0.225. The reflection was the whole problem, so the **smoothness** falls off too -
mirror-smooth under the car, matte and reflecting nothing by the rim. Which is also how a
real infinity floor behaves.

Kept to 0.45 m rather than the metre a showroom suggests, because this is a stereo
display: geometry in front of the screen plane running off the edge of the frame is the
classic window violation, and a floor is the easiest way to make one.

Its own root, not a child of the pivot - it must not turn with the car, and must not be
scaled by the focus view when a part is framed.

**Cost:** three generated assets, and a resting pitch that is per-exhibit state the other
two scenes now carry at zero.

## 2026-09-29 - The car's "open" state is an explode view with no poses in it

**Decision:** the Volvo gets an `EyeExplodePoseSet` containing nothing, and the tour
button drives `EyeExplodeView` exactly as the eye's expand button does.

**Why:** `EyeAnatomyController` gates the badges and the navigator on
`EyeExplodeView.IsExpanded`. On a model that comes apart those are the same state - the
eye is open, therefore its parts are labelled - and the car wants that second state
without the first: a car to look at, and a car with its tour running. An empty pose set
gives it for nothing. `ResolveParts` finds no parts and reports none missing, and
`SetExpanded` still runs its timer and raises `TransitionCompleted`, which is what brings
the badges up. The asset exists rather than being left null only because the view logs an
error without one.

The alternative was making `explodeView` optional on the controller, which is eight call
sites in a component two working exhibits depend on, to express something the component
can already say.

**Cost:** a pose set asset with nothing in it, which looks like an oversight until you
read why. Hence this entry.

## 2026-09-29 - Badges are pushed clear of the model, not of their own part

**Decision:** `EyeAnatomyController` gains `hotspotsOutsideModel`, default off. When on, a
badge is placed where the line from the model's centre through the part's centre leaves
the model's own bounding box, rather than just in front of the part's near face.

**Why:** the existing rule assumes the model comes apart. Once the eye is open every part
has clear air in front of it and a badge there belongs to that part. A car never opens, so
"just in front of the grille" is *inside the front bumper*, and five of the eight tour
stops would have been buried in bodywork.

The box is measured in the **model's own axes**, not the world's. The car rests at 45
degrees and its world-aligned box is 0.25 m square in plan where the car is 0.10 x 0.26 -
pushing to that box put the cabin's badge 68 mm off the back of the car, floating in
space. `EyePartBounds.TryGetLocal` measures in a given transform's axes and is what makes
the badges hug the bodywork instead.

**Cost:** a second bounds routine, and a slab test that has to convert the push direction
into the model's axes before it can use extents expressed in them.

## 2026-09-29 - A finish is a build variant, so it reuses the machinery interface

**Decision:** paint and interior trim are one component, `VehicleFinishSwatches`,
implementing `IExhibitMachinery` and driven by an unmodified `ExhibitFeaturePanel` - the
same pair that drives the engine's X-ray and build variants.

**Why:** a finish is a run of mutually exclusive variants with a name each, which is
precisely what that interface describes. One component serves both features because the
only difference between paint and trim is the data: paint is a colour with no texture,
trim is a texture with no colour, and a target carries an array of each.

`SetTransparent` is a documented no-op. A finish has no casing to see through, and the
panels built for these swatches are given no transparency button, so it is never called.

**Cost:** two new optional fields on `ExhibitFeaturePanel` - a marker per variant and a
label - because the panel marks the selected variant by rewriting its resting colour, and
a swatch's own colour *is* the thing being chosen. Where markers are supplied the
ColorBlock is left alone entirely. Both default to empty, so the engine is unchanged.

## 2026-09-29 - Swatch materials are per-renderer copies, and the focus view is told

**Decision:** `VehicleFinishSwatches` writes through `Renderer.materials`, matches its
targets by material *name* rather than by reference, and calls a new
`EyeFocusView.RefreshCachedMaterials` once its copies are in place.

**Why:** three things collide on the same renderers, and each one alone is fine.

`Renderer.materials` instantiates on first access and returns those same copies
afterwards, so `VehicleLightRig` and the swatches end up writing to one set whichever ran
first - the cabin lamp and the interior trim share the `Shell` material on
`CeilingConsole`. But *because* either may have instantiated first, the renderer may
already report a copy, and a reference test against the source asset would miss it.
Matching on the name with `" (Instance)"` stripped is exact and order-independent.

`EyeFocusView` captures every renderer's materials in its own `Awake` to restore them
after ghosting. Whatever it captured there, the renderers now carry copies - so without
being told, the first trip through a focus would put the project's own untouched
materials back over every swatch. `RefreshCachedMaterials` re-reads the materials only,
deliberately not re-scanning the hierarchy: by the time it is called the controller has
spawned the hotspot badges under the model, and a re-scan would sweep those in and ghost
them with the geometry.

Writing to the shared assets instead would have been simpler and is what the light rig's
own comment warns against: the project's `.mat` files would be left repainted after play,
and one material covers the whole body.

## 2026-09-29 - The car rests three-quarter on, which is what its scale was chosen for

**Decision:** `VolvoExhibitBuilder` puts a 225 degree yaw on the model pivot, applied
last, after every step that measures the car in world space.

**Why:** `TargetLength` is 0.26 m and its own comment says that fills about three quarters
of the screen *broadside*. The model's length runs down +Z and the rig's resting view
looks straight along +Z, so the exhibit actually opened on the car's back end, 0.105 m
across a screen 0.345 m wide. At 45 degrees the silhouette measures
0.26|sin| + 0.105|cos| = 0.258 m, which is the figure that constant was picked for, and it
is the angle a car is photographed from for the same reason.

Applied to the pivot rather than the car, and last, because the headlight beams and the
exhaust audio are placed from `carBounds.max.z` and would land on a flank if the car had
already turned. `EyeManipulator` captures the pivot's rotation as its rest pose, so Reset
View returns here rather than to zero.

**Cost:** the resting view changed, which is visible. It is one call to remove.

## 2026-09-29 - Reflections and the backdrop are set from different sources

**Decision:** the skybox stays the near-black gradient the whole exhibit is composed
against, and `RenderSettings.customReflectionTexture` is pointed at a generated cubemap
with three overhead softboxes in it.

**Why:** the two jobs are opposites. A backdrop wants to disappear; a reflection
environment wants structure. Car paint is the case that makes the difference obvious - a
clear coat at 0.69 smoothness is read almost entirely from what it reflects, and a smooth
gradient returns nearly the same value at every normal, which is why the body panels read
as flat shapes under the plain sky. Hard-edged bands give the long highlight that runs
down a wing and describes its curvature.

The eye never needed this. Near-white tissue at low smoothness is read from its diffuse
shading, so the gradient was the right answer there and still is.

**Cost:** an extra 64 px cubemap asset, and one more thing that differs between the three
exhibits' environments.

## 2026-09-29 - The engine note is synthesised, and cannot be a recording

**Decision:** `ProceduralAudio.CreateEngineIdle` is retuned towards the engine an S90
actually has - a two-litre turbocharged four, in every variant it was sold with - rather
than replaced with a recording of one.

**Why:** a recording of a real S90 is somebody's copyrighted audio, and there is no
licensed source for one here. What can be done honestly is to make the synthesis describe
the right engine. A large saloon's exhaust is muffled and low, so: harmonics falling as
1/n squared instead of 1/n, a softer pulse envelope, far less rasp, a component at half
the firing rate for the low beat once per crank revolution, and a four-element
per-cylinder gain table, because four cylinders are never quite matched and a perfectly
even pulse train sounds synthetic within a second.

Measured: 26 Hz dominant with 13 Hz beneath it and harmonics falling away smoothly to
130 Hz; the step across the loop point is 0.017 against a worst in-clip step of 0.074, so
it still loops without a click.

**Cost:** it is a good synthetic four, not an S90. `VehicleIgnition.idleOverride` and
`startOverride` take a clip and bypass the synthesis entirely, so a licensed recording
can be dropped in with no code change.

## 2026-09-29 - An existing panel is repaired by the build step, not skipped

**Decision:** `VolvoExhibitBuilder.BuildDoor` no longer returns early when a door pivot
already exists. It compares the door against the current spec and adds whatever is
missing. A hinge that has been withdrawn from the spec is taken down by
`RemoveRetiredPanels`, which hands the geometry back to the car first, and `BuildUi`
destroys the button of any group that no longer exists.

**Why:** find-before-create was being read as leave-alone-if-present, and that is only
safe while the spec never changes. It did change - the mirror indicators were added to
the door spec after the doors had been built - and the result was worse than a stale
door. `RemoveSourcePanels` cuts the two-sided source out of the car whether or not the
halves were placed, so the mirror indicators disappeared from the model altogether and
the indicator channel quietly ran on half its meshes. Nothing errored.

An idempotent build step has to converge on the spec from wherever the scene happens to
be, not just from empty. The same rule is what lets the hood and trunk be withdrawn
without anyone opening the scene to delete their pivots by hand.

**Cost:** the repair path has to shut the door before reparenting, because a part added
while the panel is open is fixed at that angle relative to the rest of the door.

## 2026-09-29 - The mesh split is safe because of clearance, not symmetry

**Decision:** `VehicleMeshSplitter` continues to accept any panel whose halves both come
out non-empty. It does not require the two halves to match.

**Why:** the first nine panels each divided into two exactly equal triangle counts, and
the docs recorded that as the property that made the cut trustworthy. It is not. The
front door's interior card splits 76,512 / 56,224, because the driver's door carries
window and mirror switchgear the passenger's door does not - and it is still a perfectly
exact cut. What makes the cut exact is that no triangle straddles the centreline, which
on this model is true by a wide margin: the nearest triangle to x = 0 on any panel cut so
far is 5.7 mm away, and on the interior cards it is 43 mm.

Requiring symmetry would have rejected a panel that splits perfectly well. The check that
matters - both halves non-empty - already catches the failure it was meant to catch, a
panel that is not mirrored at all.

## 2026-09-29 - The interface is pinned by UIScaler, not made a ScreenSpaceOverlay canvas

**Decision:** the engine exhibit's canvas stays world-space and carries the SDK's
`UIScaler`, which rewrites its pose and size every frame from the rig's screen plane.
It is also handed the rig camera as its event camera.

**Why not a real overlay, which is what was asked for:** `VRRenderer` renders side by
side - it gives the left eye the viewport `(0, 0, 0.5, 1)` and the right eye
`(0.5, 0, 0.5, 1)`. A `ScreenSpaceOverlay` canvas ignores camera viewports entirely and
is drawn once across the whole framebuffer, so it would span both eye images and never
fuse on the hardware. `ScreenSpaceCamera` has the same problem in reverse: it would be
drawn per eye but has no depth to sit at.

`UIScaler` gives the behaviour the request was actually about - square to the viewer,
fixed on the panel, clickable - and it is what the eye exhibit already uses. Anything
that wants a true overlay should be a separate canvas that is not part of the stereo
content.

**Why the event camera matters as much as the scaler:** a world-space `GraphicRaycaster`
with no camera assigned falls back to `Camera.main`, and this rig has none - the SDK
disables the rig camera's own `Camera` component and renders through `left` and `right`
sub-cameras it creates at runtime. With the field empty, every button on the canvas was
unclickable. That is a trap for any new scene built on this rig, not just this one.

## 2026-09-29 - The engine's own features are reached through an interface, not a type

**Decision:** `IExhibitMachinery` is declared in `KmaxDisplayExample` and implemented by
`Enginei4`. `ExhibitFeaturePanel` drives see-through and build variants through it.

**Why:** `Enginei4` is in `Assembly-CSharp`, which already references
`KmaxDisplayExample`. The exhibit can never name that type without closing a reference
cycle. An interface declared on the exhibit side and implemented on the model side runs
with the dependency rather than against it, and costs `Enginei4` nothing but the
declaration - it already had every method.

This is the same constraint `ExhibitMachineryGate` solved by holding a plain
`MonoBehaviour` and toggling `enabled`. That works when the only thing needed is a
property every behaviour has; an interface is what it takes to call something specific.
Unity will not serialise a bare interface field, so the reference is still stored as a
`MonoBehaviour` and cast once in `Awake`.

## 2026-09-29 - ApplyVariation is new rather than SetVariation being reused

**Decision:** `Enginei4` gained `ApplyVariation(int)`. `SetVariation(int)` keeps its
exact previous behaviour and delegates the part-swapping half to it.

**Why the old one could not be called:** it returns early unless the matching toggle in
`allTogglesType` is on, and those toggles live on the canvas the exhibit disables, so no
caller outside that canvas can satisfy it. It also has an early-out when the requested
variation is already current, which makes it impossible to apply variation 0 from a cold
start.

**And why it must not be called even if those were worked around:** it calls
`ActivateAllObjects`, which enables every object carrying a `MeshRenderer` anywhere under
the model. The hotspot badges are three MeshRenderers each, parented to the parts they
label, and are meant to stay hidden until the engine is open. One variation switch would
have revealed all twelve. `ApplyVariation` only ever hides and shows variation parts,
which is sufficient because nothing else is ever hidden by a variation.

## 2026-09-29 - Overlapping transparency fades are cancelled

**Decision:** `Enginei4` tracks its transparency coroutines and stops any still running
before starting new ones.

**Why:** each part's fade owns that part's material for the ten frames it runs. Toggling
see-through off and straight back on left two fades racing over the same renderers, and
the older fade-in finished last - putting the opaque material back while the interface
believed the engine was see-through. A double-click was enough to reach it. Only
transparency coroutines are tracked, so nothing else is affected, and cancelling
mid-fade is safe: both coroutines read whatever material is on the renderer when they
start, so they pick up correctly from a partial state.

## 2026-09-29 - Button colours live in the ColorBlock, not on the graphic

**Decision:** `BuildButton` leaves the button's `Image` white and puts every colour in
the button's own `ColorBlock`. `ExhibitFeaturePanel` marks the selected build by writing
`normalColor` and `selectedColor`, not `Image.color`.

**Why:** a `Selectable` with a colour transition drives the target graphic's canvas
renderer on every state change. Anything written to `Image.color` survives only until
the pointer next touches that button, so the selected build lost its highlight as soon
as any build was hovered.

It also fixes the hover the first build shipped: with `normalColor` white and
`highlightedColor` at 1.25 white, hovering crossfaded the button to flat white and lost
the dark base entirely.

## 2026-09-29 - The engine reuses the eye's runtime rather than getting its own

**Decision:** `VirtualExhibition WR.unity` is built on the eye's components verbatim.
`EyeAnatomyController`, `EyeExplodeView`, `EyeFocusView`, `EyeManipulator`,
`ViewerFlyController`, `ExhibitAttractMode`, `AnatomyStylusInput`,
`ExhibitPostProcessing`, `EyeHotspot.prefab`, `EyeGhost.mat` and `AnatomyPostFX.asset`
are shared, not copied. Only the values differ.

**Why:** none of them names the eye in anything but its type name. They work off a
catalogue, a pose set and a model root - all injected. The engine needed no runtime
change beyond one new gate component, and a shared fix now lands in both exhibits.

**What this costs:** the type names read oddly against an engine, and the eye's
assembly cannot reference `Enginei4`, which is in `Assembly-CSharp` and already
references it the other way. Renaming the components to something model-neutral is
worth doing if a third exhibit appears; with two it is churn.

**Sixteen build steps on `EyeAnatomySceneUpgrader` went from `private` to `internal`**
so the engine builder can call them instead of duplicating about 400 lines. Visibility
only - no behaviour changed. What stayed private is what is genuinely eye-specific:
the light intensities calibrated against near-white tissue, the focus-view and
scale-box tuning, and the navigator UI that clones an existing button.

## 2026-09-29 - The engine teardown is authored, not baked

**Decision:** the 19 explode poses are authored as a direction-and-distance table in
`EngineExhibitBuilder.GetTeardown`, in the model's native metres, and converted to each
part's parent space at build time.

**Why not baked, as the eye's are:** there is nothing to bake. `Models/Enginei4.FBX`
imports with `animationType=None` and no clips; `Engine_opt.FBX` has one `Take 001`
that is two keyframes over 0.033 s on the root, animating rotation and scale only, and
the scene does not use that FBX anyway. Every moving thing in this model is procedural
code in `Enginei4.Update`. `EyeAnatomyPoseBaker` has no input here.

**Why parent space, via world:** `EngineBlock` and `CylinderHead` both carry a 270
degree rotation about X. A local -Y offset on the oil pan would send it out of the side
of the engine rather than off the bottom. The conversion goes root space -> world ->
parent space; the exhibit's own scale appears on both sides and cancels, so the table
stays in native metres and survives any later change to the exhibit scale.

**Why the table is bigger than the catalogue:** 19 poses, 12 labels. The valves and
springs must travel with the camshafts that open them, the plug leads with the plugs,
and the timing belt off the front, but none of those is an assembly a viewer would ask
about. Twelve badges on a model this busy is already a lot; nineteen is a thicket.

## 2026-09-29 - The teardown is re-centred on the origin, and the block loses its anchor

**Decision:** the builder measures how far the pulled-apart engine's bounds centre
moves away from the assembled centre, and subtracts that drift from every top-level
offset before baking.

**Why:** an engine does not come apart symmetrically. Far more of it lifts off the top
than drops out of the bottom, and the gearbox alone travels half a model-length back.
Measured on the first build, that carried the whole model 0.046 m up and 0.049 m back
at exhibit scale - enough to push the cam cover 0.04 m above a virtual screen only
0.194 m tall, with the engine still comfortably small enough to fit. The extent was
never the problem; where it sat was. `ViewerFlyController` orbits the origin and resets
`focalCenter` to `Vector3.zero`, so an off-centre teardown orbits about empty space.

**Solved, not measured once:** the bounds are defined by whichever parts are furthest
out, and shifting everything can hand that job to a different part. The loop runs to a
0.1 mm residual.

**Applied only to groups directly under the model root.** The correction is a rigid
shift of the whole teardown, so nested steps inherit it through the hierarchy. Applying
it to a nested step as well moves that part twice - the oil pan once with the block it
hangs off and again on its own account - and the solve ends up chasing a target it is
itself moving. That was the bug behind an 0.8 mm overflow that would not close.

**What it costs:** the engine block is no longer a fixed anchor; it sinks slightly as
the engine opens. That is a fair trade for a teardown that stays in frame, and it means
the offsets can be re-authored freely without the framing being re-derived by hand.

## 2026-09-29 - The machinery is frozen while the engine is apart

**Decision:** `ExhibitMachineryGate` disables `Enginei4` on the frame an explode starts
and re-enables it only once the engine is fully reassembled.

**Why:** the procedural animation and the explode do not actually collide - the driver
only ever writes rotations and the local positions of parts *below* the group nodes the
explode moves. What breaks is the connecting rods. `Rod1..4` aim at targets parented
under the pistons, so the moment the pistons travel away from the block the rods swing
across the gap to keep pointing at them, and the teardown reads as broken geometry
rather than as an exploded view.

Freezing also means `Enginei4.Start` - which caches every piston and rod rest position -
can only ever run with the engine assembled. Started while apart, it would cache the
exploded pose as the rest pose.

**Why the driver is held as a plain `MonoBehaviour`:** `Enginei4` is in
`Assembly-CSharp`, which references `KmaxDisplayExample`. Naming the type in the gate
would close a reference cycle and neither assembly would compile. Toggling `enabled`
needs no type knowledge.

**`EyeExplodeView` gained a `TransitionStarted` event** for this. `TransitionCompleted`
is too late - it fires once the parts have already flown apart. The event is additive
and nothing else subscribes.

## 2026-09-29 - The engine's original interface is disabled, not deleted

**Decision:** `Canvas - Engine (1)` and the scene's single point light are deactivated.
The builder also clears `Enginei4`'s `RPMSlider` and `ZoomSlider` references and sets
`RPM` directly.

**Why disabled rather than deleted:** that canvas is the only wiring for the
transparency X-ray over 23 parts, the four tuning variations and the twelve part
toggles. None of those is exposed by the exhibit interface yet, and deleting the canvas
would throw away the only reference to them.

**Why the slider references must be cleared, not just deactivated:** a slider on a
deactivated object is still a live reference. `Enginei4` writes its parent's local scale
from `ZoomSlider` every frame, and with the object merely inactive the null check passes
and the write goes ahead: on the first frame of play the wrapper went back to the
slider's value of 1, and the pulled-apart engine measured 2.24 m across a screen
0.345 m wide. `Enginei4.Update` now treats both sliders as optional so clearing them is
safe; with them assigned its behaviour is unchanged.

## 2026-09-25 - Scale goes through the manipulator's zoom, not the transform

**Decision:** `EyeScaleBox` puts a billboarded frame with four corner handles around
the model. Dragging a corner scales it uniformly, through
`EyeManipulator.SetZoom` rather than by writing to a transform.

**Why not the transform:** `EyeManipulator.ApplyTransform` runs every frame and
rewrites `pivot.localScale` from its own `_currentZoom`. Anything written to the
pivot directly is gone by the next frame. Routing through the manipulator also means
Reset View restores the scale along with rotation and pan for free - verified:
`ResetTransform` takes zoom 1.8 back to 1.0.

`SetZoom` writes both the current and target values, not just the target.
`ApplySmoothing` only runs on the pointer-manipulation path, which is switched off
while `ViewerFlyController` owns navigation, so setting the target alone would never
reach the model.

**Why four corners on a plane, not eight on a box:** a wireframe cube in stereo is a
thicket of lines that reads as clutter and hides the anatomy behind it. Four corners
billboarded to the viewer are unambiguous from any angle, and they match what the
gesture actually means - dragging a corner outward is a screen-space idea, not a
volumetric one. The rectangle hugs the silhouette by projecting the bounds' eight
corners onto the camera's right and up axes, rather than being a loose square sized
to the longest diagonal.

**The drag centre is frozen at grab time.** Read live it moves as the model grows,
which feeds the scale back into its own input.

## 2026-09-25 - `IsPointerOverGameObject` was suppressing orbit over the model

**Finding, and a regression I introduced.** `ViewerFlyController.IsPointerOverUI`
used `EventSystem.IsPointerOverGameObject()`, which reports **any** object the event
system hit. Once the eye gained mesh colliders and the camera a `KmaxPhysicRaycaster`,
that became true whenever the pointer was anywhere on the model - so mouse drag
stopped orbiting over the very thing it is meant to turn.

It now raycasts and returns true only for hits under a `Canvas`. Scale handles are
excluded separately, through `EyeScaleBox.SuppressViewDrag`, because they are 3D and
would otherwise fall on the orbit side of that test.

`SuppressViewDrag` is static, which is the honest shape for it: this is input
arbitration between systems with no reason to hold references to each other - the fly
controller and the stylus both have to stand down, and neither should have to know
what a scale box is. It covers hover as well as drag, because the orbit's own drag
threshold is smaller than the EventSystem's - without that, the view would start
turning before the drag was ever recognised and the grab would be lost.

## 2026-09-25 - The pad was inaudible because of its pitch, not its level

**Finding.** Reported from the device as "BG music is not audible". The level was
low (0.16), but the real problem was that the pad was built on a 110 Hz root - a low
A, below what a display's own panel speakers reproduce. It was playing correctly and
simply could not be heard.

Root moved to 196 Hz and the voicing reweighted towards the octave and fifth above
it, so the chord sits in a band small speakers carry. Measured on the generated clip:
dominant energy moved from ~110 Hz to **~414 Hz**, and effective output roughly 2.8x
higher at the new 0.45 level.

The fade-in also came down from 3.5 s to 1.5 s. Long enough not to announce itself,
short enough that someone checking whether there is music does not conclude there is
none.

## 2026-09-25 - Stylus vibration is rate-limited, not just weaker

**Finding.** Reported from the device as too strong. Strength was part of it (hit
pulse 22, reset 40, now 8 and 18), but the bigger problem was frequency: every
structure carries its own collider, so sweeping the beam across the eye crosses a
boundary every few frames and fired a pulse each time. The pen buzzed continuously
rather than ticking on arrival.

`minVibrationInterval` (0.25 s) puts a floor between pulses. Feedback on arrival is
the intent; a constant buzz is noise.

## 2026-09-25 - Pop-out is a camera distance, not a part position

**Decision:** `EyeFocusView.focusPopOut` (0.10 m) sets how far a focused part floats
in front of the display glass, and the controller flies the camera to
`EyeFocusView.FocusCameraDistance` rather than to a distance of its own.

**Why it has to work this way:** the obvious approach - move `FocusAnchor` toward
the viewer - does nothing at all. `ViewerFlyController` places the rig at
`focalCenter + forward * (0.5 - distance)`, and the rig transform *is* the virtual
screen. Move the focal centre and the screen plane moves with it, so the parallax
between them never changes. The gap between the two is exactly `0.5 - distance`,
which makes the camera distance the only lever there is.

**The catch that comes with it:** framing is computed against `XRRig.ViewSize`, which
assumes the part sits at the screen plane. Flying closer magnifies it by
`screenDistance / cameraDistance`, so the framing scale is multiplied by the
reciprocal. Without that compensation, raising the pop-out silently zooms in as
well and `framingRatio` stops meaning anything.

This is the one feature that uses what the hardware is for. Content at zero parallax
sits inside the panel like any other screen; only content in front of it reads as
reaching out of the box.

## 2026-09-25 - The pen turns the view one for one

**Decision:** `AnatomyStylusInput.orbitMode` defaults to `WristTurn`, which applies
the pen's own change in aim angle to the view at a gain of 1.35. `ScreenDrag`, the
previous pixel-based behaviour, is kept as the other option.

**Why:** screen-space dragging scales by the projection, so the same hand movement
rotates by a different amount depending on how far the camera is dollied. An angle
is an angle at any distance. It is also simply the gesture a tracked wand invites -
take hold of the thing and turn your wrist - and that is the interaction people
remember from a fish-tank display.

The angles are measured in **rig space**, not world space. The pen hangs off the rig,
so its world rotation turns as the view orbits; measuring there would feed the orbit
back into its own input and the model would keep spinning after the hand stopped.

Direction matches the mouse by default rather than the "held object" convention, so
switching input device does not reverse the controls. `invertWristTurn` flips it.

## 2026-09-25 - The exhibit demonstrates itself when nobody is there

**Decision:** `ExhibitAttractMode` opens the eye and tours its structures after 30 s
without input, with a prompt inviting whoever walks past to take over. Any input -
mouse, key, scroll, pen button or a deliberate pen movement - hands control back.

**Why:** a standing exhibit showing a static model reads as a screensaver from three
metres away. Slow orbital motion is also the only thing that conveys stereo depth to
someone not yet close enough to be head-tracked, so the attract loop is what gets
them within range of the effect the hardware exists for.

**Two details that are deliberate:**

- **Waking does not reset the view.** Snapping home the moment someone touches the
  pen would take away the exact thing that drew them over. They continue from
  wherever the tour had reached.
- **It does not open the eye itself**, even though that was the first thing it did.
  `SelectNextPart` already opens the eye and queues the selection until the explode
  settles - and that completion is also when the controller caches the per-part view
  directions the camera flight needs. Expanding first makes the controller take its
  immediate path instead, the directions are not ready, and **every camera flight is
  silently skipped**. That was caught in Play mode: the tour ran but the camera never
  moved, sitting at 0.500 instead of the 0.400 the pop-out asks for.

The pen wake threshold cannot be zero. A tracked pen is never perfectly still, and
without a deliberate-movement threshold the exhibit could never go idle at all with a
pen lying on the desk beside it.

## 2026-09-25 - Post-processing never ran, because the stereo cameras have no camera data

**Finding, and the second half of the lighting story.** After the renderer was
corrected the model was lit but ugly: one side blown to flat white, the other
crushed. Measured, **26.4% of the subject was clipped above 0.97 luminance.**

Two causes, found in order:

1. **The specular point light.** It sat 0.13 m off-axis from a model about 0.1 m
   across, so inverse-square falloff made it a blowtorch on the near side.
   Disabling it alone took clipping from **26.4% to 0.1%**. It is kept in the
   scene, disabled, rather than deleted. A directional already gives a specular
   that slides as the camera orbits, because specular is view-dependent - the
   point light was never needed for that.

2. **No tonemapper.** With the point light gone the rig had to be run so dark
   that everything read muddy and desaturated, because the model's near-white
   albedo clips the moment lighting is strong enough to read. Adding a
   `Volume` with Tonemapping had already been tried in this session and appeared
   to do nothing, which is why the earlier entry below says the stereo path
   cannot be tonemapped. **That was wrong.** The real reason is that
   `VRRenderer` builds the `left` and `right` cameras at runtime **without a
   `UniversalAdditionalCameraData`**, so URP falls back to defaults where
   `renderPostProcessing` is false. The only two cameras that draw anything were
   skipping every volume in the scene. Setting it on the authored root camera in
   the inspector does nothing either, because the SDK disables that camera's own
   `Camera` component.

`ExhibitPostProcessing` now adds the component and enables post-processing and
HDR on the sub-cameras once they exist, re-checking when the camera count
changes. With Neutral tonemapping plus gentle bloom the rig runs at a normal
exposure - key 1.05, fill 0.50, rim 0.60, ambient 0.55 - and measures **0.00%
clipped** with a subject mean of 0.37.

Key to fill is held near 2:1. Wider crushes the shadow side of a sphere, which
is most of what the eye is. Tonemapping is Neutral rather than ACES: ACES pushes
saturation and contrast in a way that suits film, not an anatomical reference
where the tissue colours are the content.

## 2026-09-25 - The pipeline was using the 2D renderer, so nothing was ever lit

**Finding, and the most consequential one in this repo.** `URPAsset`'s
`m_RendererDataList[0]` pointed at a **`Renderer2DData`** - URP's 2D renderer,
which only handles `Light2D` and silently discards every directional and point
light in the scene. The whole exhibit had been rendering as albedo times
ambient.

Prompted by the observation "I think directional light has no effect on model".
It was measured rather than argued: the camera was rendered to a texture with
every light on and then every light off, and the mean luminance was identical to
four decimal places - `0.3247` both ways, for every light individually as well.
The measurement was validated by swapping the background colour, which moved it
from `0.0461` to `0.9535`, so the instrument was working. A stock URP Lit sphere
dropped into the scene rendered as a **flat white disc with no terminator**,
which ruled out the model's own materials.

After replacing the renderer with a `UniversalRendererData`, the same
measurement gives a delta of `0.0443` and the probe sphere shades correctly.

**What this explains, retroactively:**

- The model looked flat, and the focused `Lens` had no terminator no matter how
  it was lit. Earlier in this same session that was diagnosed as a geometry
  problem - a biconvex disc seen down its own axis - and a translucent stand-in
  was chosen partly on that basis. The geometry reading was correct but it was
  never the binding constraint; **the lights were simply not being applied.**
  With the renderer fixed the lens shades properly.
- Almost certainly the mystery recorded under *"Hotspot markers use a stock URP
  shader, not a custom one"*: a hand-written URP SubShader that compiled cleanly,
  reported `isSupported == true`, was correctly selected, and drew nothing. A 3D
  lit SubShader under the 2D renderer would do exactly that. That note should be
  revisited before anyone concludes custom shaders are unusable here.

**Consequence for every light value in the scene:** none of them had ever been
calibrated, because raising them did nothing. Once lighting applied, the
original intensities blew the model out to white.

> [!NOTE]
> The first attempt at this dropped the whole rig to about a third of its
> energy, on the reading that the stereo path could not be tonemapped. That
> reading was wrong - see the entry above - and the dark rig it produced looked
> muddy. The tonemapper is now running and the intensities are back at normal
> levels.

`EyeAnatomySceneUpgrader.UpgradeRenderPipeline` now checks for this and swaps
the renderer, so a re-run cannot silently go back to the unlit state.

## 2026-09-25 - Two stylus pointers sharing one id

**Finding.** After the first setup run the scene held two pens: the SDK's own
`pen.prefab` and this module's `AnatomyPen`. Every `KmaxStylus` registers under
`KmaxStylus.UniqueId` (1000), and `KmaxInputModule.ProcessStylusEvent` iterates
**every** registered `KmaxPointer` - so each press was raycast and dispatched
twice, from two different poses, under the same pointer ids.

It was also throwing. The console carried a `NullReferenceException` at
`KmaxStylus.UpdateState` line 280 - `pen.UpdatePose(stylus)` - on **every**
`EventSystem.Update`, because the second stylus had no resolved `IStylus`.
There was no matching "Can not find IStylus" error, which places it before that
component's `Start` rather than after it.

`EyeAnatomySceneUpgrader.RemoveDuplicatePens` now deletes any stylus under the
rig that is not `AnatomyPen`, and says so in the console. Afterwards the console
stays clean through a full pass over all 18 parts, both navigator directions,
reset and every audio cue. The per-frame click guards in `EyeHotspot` and
`EyePartPicker` remain, because the driver's mouse emulation is a separate
source of the same duplication.

## 2026-09-25 - The interface is taken out of the depth test

**Decision:** `UiAlwaysOnTop` gives every graphic on the canvas its own material
with `unity_GUIZTestMode` set to `Always` and a render queue of 4000.

**Why:** the SDK's `UIScaler` pins the canvas to the rig's screen plane every
frame, which is always exactly 0.5 m from the viewer. The model sits at the
orbit centre, `distance` away. Whenever the camera is dollied closer than 0.5 m
- and the default is 0.42 m - the model is physically in front of the interface
and occludes it. That is what was cutting the info panel's text off behind a
rectus muscle.

**Why not move the canvas forward:** the dolly range is 0.12 m to 1.2 m. An
offset big enough to clear the model at the near end would put the panel about
4 cm from the viewer's face, and no fixed offset covers the whole range anyway.
Depth cannot solve a conflict whose sign changes.

`unity_GUIZTestMode` is declared outside the shader's Properties block, so
`Material.HasProperty` returns false for it. That is expected and is not a
reason to skip the write - both `UI/Default` and the TextMeshPro distance-field
shaders read it. This was confirmed in Play mode before being committed.

## 2026-09-25 - Transparent parts get an opaque-enough stand-in

**Decision:** `EyeFocusView` swaps a focused part onto `FocusHighlight.mat` when
every one of its own materials is below `transparentAlphaThreshold` (0.75).
Today that catches exactly `Lens` and `Tear film`, which share the model's
`Mat.1` - a 91% grey at 58% alpha.

**Why this rather than lighting:** the obvious theory was that the parts had no
environment to reflect. Adding one did not fix them, and a grey background made
them *worse*, because a 58%-alpha grey over grey has no contrast at all. The
alpha is the problem, and only replacing it fixes it. ai_handoff.md item 0 had
been carrying this as a decision someone needed to make; this is that decision.

**Why the stand-in is translucent, not opaque:** an opaque version is certainly
visible but reads as a flat pale sticker. The lens is a biconvex disc viewed
down its own axis, so nearly every normal you can see points at the camera and
diffuse shading has nothing to grade across. Raising smoothness makes it worse,
not better - a near-mirror reflecting a nearly uniform grey sky returns the same
value at every normal. Letting the structures behind show through is what
actually reads as an optical body, and it stays closer to the source art. All of
this was measured in Play mode: the mesh has 2345 normals spanning 175 degrees,
so the geometry was never the limitation.

## 2026-09-25 - Grey background, but a dark one, and a sky that is never seen

**Decision:** cameras clear to `RGB(0.165, 0.175, 0.205)`. A grey gradient
skybox is assigned for ambient and reflections only - the cameras keep clearing
to a solid colour, so it never appears on screen. Ambient sits at 0.42 and
`EyeGhost.mat` drops to 0.028 alpha.

**Why the grey is dark:** a focused part is seen through about twenty ghost
shells, and lifting the background much past this accumulates them into a milky
haze that flattens the whole model. This is the point where the grey reads as
grey and the anatomy still has something to sit against.

**Why the sky is invisible:** the eye's wet surfaces need an environment to
reflect or they render dead matte, but a visible sky raises average screen
brightness and with it the crosstalk between the two eyes on a stereo panel.
Ambient is kept low for the same reason it is kept off the lens: an even fill
lights every surface to nearly the same value and erases the model's form.

## 2026-09-25 - The scene had no stylus at all

**Finding, not a decision.** "Stylus drag rotation is not working" had a simpler
cause than the name suggests: there was no stylus.

`XRRig.prefab` contains `Camera`, `left` and `right` and nothing else. The SDK's
`pen.prefab` - which carries `PenTracker`, `KmaxStylus`, `StylusRay` and a
`LineRenderer` - was never instanced into the scene, and a search of
`EyeAnatomy.unity` for those three script GUIDs returned zero hits. The
`EventSystem` was also running a stock `StandaloneInputModule`, not
`KmaxInputModule`.

That combination explains every reported symptom at once:

- **UI buttons worked with the pen** because the Kmax driver moves the OS mouse
  cursor. `StandaloneInputModule` plus the canvas `GraphicRaycaster` is enough
  to press a button from an emulated cursor, and this was confirmed on device.
- **Dragging never orbited**, because `ViewerFlyController.UpdateMouseOrbit`
  reads `Input.GetMouseButton(0)` and `(1)`, which the driver's cursor
  emulation does not reliably set.
- **There was no beam and no tip**, because there was no pointer to draw one for.
- **The ray had nothing to hit** either way: the model arrives from glTFast as
  mesh filters and renderers with no colliders, so the only colliders in the
  scene were the badges' own spheres.

Anything in this repo's history describing `XRRig/pen` describes a state that
was never in the scene file. architecture.md is corrected.

## 2026-09-25 - Stylus button mapping: select/orbit, reset, dolly

**Decision:** the pen's three buttons are mapped front to back as select and
drag-to-orbit, tap-to-reset, and hold-to-dolly. `KmaxStylus.PrimaryKey` is set
to `Left`.

**Why:** `IStylus.GetButton` takes `0..2` and `PenTracker` labels them 左 / 右 /
中 - front, rear, centre - so three is the whole budget and each should earn its
place.

- **0, front** carries select *and* orbit because it is the button a hand
  reaches first, and because a viewer's instinct with a wand is to press and
  drag as one gesture. Click and drag are separated by a travel threshold,
  exactly as the mouse path does it.
- **1** is reset. The request was for a button matching the Reset control, and
  tying the recovery action to its own key means a lost viewer never has to find
  a UI button while disoriented. It is edge-triggered on release with a tap
  timeout, so resting a thumb on it does nothing.
- **2, centre** is a push-pull dolly. Of everything left unmapped, zoom is the
  one thing a stylus user otherwise cannot do at all - the scroll wheel is a
  mouse-only affordance - and it is the control an anatomy exhibit needs most.

`PrimaryKey = Left` matters more than it looks: `KmaxStylus.StateOf` swaps index
0 with the primary's index whenever the primary is not `Left`. Leaving it on the
SDK default of `Middle` would mean `AnatomyStylusInput`'s button 0 and the input
module's "left button" referred to different physical keys.

## 2026-09-25 - Orbit from a fixed-distance aim point, not the hit point

**Decision:** `AnatomyStylusInput` derives its drag delta by projecting a point
a fixed `RayLength` along the pen's ray, rather than using
`KmaxStylus.ScreenPosition`.

**Why:** `ScreenPosition` projects `PointerPosition`, which is the *hit* point -
it collapses onto whatever the ray lands on. Crossing the silhouette edge of a
part changes the hit distance discontinuously, and the resulting jump in screen
position would fling the view. Projecting at a constant distance gives a signal
that only tracks where the pen is aimed, so the drag stays in screen pixels and
matches the mouse's feel without inheriting its geometry.

It also sidesteps a feedback loop: the pen is parented under `XRRig`, so both
the pen and the camera move together as the rig orbits and a stationary hand
produces a stationary aim point.

## 2026-09-25 - Colliders fitted at runtime, with a box fallback

**Decision:** `EyeAnatomyController` fits colliders to each catalogued part as
it builds that part's badge, via `EyePartColliders`. Mesh colliders are used
where the mesh is readable; where it is not, a box sized to the mesh bounds
stands in.

**Why runtime rather than baked into the scene:** the controller already walks
every catalogued part to place badges, so this is the one place that already
knows the answer. It also means a re-exported model needs no scene surgery.

**Why the fallback:** `MeshCollider.sharedMesh` cannot bake a mesh imported
without Read/Write access, and the glTFast importer settings on
`Model/EyeAnatomy.glb` do not enable it. Rather than have the feature fail
silently on the device, an unreadable mesh gets a bounding box - the beam still
stops on the eye and the part is still pickable, just less precisely - and one
warning names the importer setting that would fix it.

## 2026-09-25 - Next and Back navigate the catalog, and fly the camera

**Decision:** added `Next` and `Back` buttons that step through the catalog and
ease the camera to a viewpoint on the selected structure's own side of the eye.

**Why:** in the exploded view the nested shells occlude each other, and several
badges cannot be reached from most angles - roadmap.md already recorded six of
them clustering on one horizontal line. Without a navigator those structures are
unreachable unless the viewer happens to find an angle that exposes them, which
is not an interaction so much as a puzzle.

The flight angle is computed rather than authored per part: the rig sits at
`focalCenter + rot * (0, 0, -distance)`, so aiming the camera's offset direction
along the part's own direction from the eye's centre puts the camera on that
side. Pitch is damped to 60% because adopting the full elevation of a structure
high above the optical axis is disorienting. Directions are sampled from the
*exploded* pose - assembled, the parts are nested shells sharing one centre and
the directions carry no information.

## 2026-09-25 - Audio is synthesised, not shipped

**Decision:** `ProceduralAudio` builds the ambient pad and every interface cue
as `AudioClip`s at startup. `AnatomyAudioDirector` exposes an override slot for
each one.

**Why:** the module ships no audio assets and carries no licence question about
them, which matters for something meant to run in a public exhibit. The pad's
partials and tremolo rates are snapped to whole cycles across the loop so it
wraps without a click - that snap is the only non-obvious part of the synthesis
and the reason a 16 s loop does not tick.

The override slots mean replacing any of it with recorded audio is an inspector
edit, so this is a default rather than a constraint.

## 2026-09-25 - Scene changes go through a menu command

**Decision:** `Kmax/Eye Anatomy/Set Up Interaction Upgrades` authors the stylus,
the navigator, the audio rig and the extra particle layers into the scene, and
wires every reference. Every step finds before it creates.

**Why:** `EyeAnatomy.unity` is 12,000 lines of YAML keyed by file IDs, and the
changes needed span five roots, a prefab and two new materials. Hand-editing
that is neither reviewable nor repeatable. A command is idempotent, survives a
model re-import, records the intent in code next to the components it wires, and
can be re-run after a half-finished attempt without cleaning up first.

## 2026-09-21 - Globe-centered pivot rather than total bounds center

**Decision:** `EyeAnatomy` is positioned inside `EyeModelPivot` with local offset `(0.015024, 0.000562, 0.011255)`, aligning the visual center of the eyeball globe (sclera + cornea + lens) with the world origin `(0, 0, 0)`.

**Why:** The model includes long posterior optic nerve and muscle meshes extending ~12 cm back into +Z. Placing the center of the total renderer bounds at `(0, 0, 0)` placed the eyeball globe 4.2 cm in front of the rotation pivot. When rotated, this created an eccentric swing of over 8 cm across the screen. Centering on the eyeball globe keeps the primary viewing subject directly in place during rotation.

## 2026-09-21 - Turntable orbit with pitch clamping rather than free-flying camera

**Decision:** Replaced free Euler/quaternion rotation in `EyeManipulator` with true turntable orbit (yaw around world Up, pitch around camera Right), clamped pitch to `[-75°, +75°]`, and disabled `ViewerFlyController` camera flying by default.

**Why:** Free flying the `XRRig` on a stereoscopic fish-tank display breaks the head-tracked zero-parallax screen window calibration. Furthermore, unclamped rotation allowed users to flip the model upside-down, which inverted drag controls and caused disorientation. The turntable model provides an intuitive, tactile object-examination UX matching physical display expectations.

## 2026-09-21 - Invariant focal point rotation when inspecting focused parts

**Decision:** When a part is focused, `EyeManipulator` dynamically shifts its orbit pivot to the focused part at `FocusAnchor`.

**Why:** In focus mode, the model root is scaled and translated so the selected part sits at `FocusAnchor`. Rotating about `(0, 0, 0)` caused the focused part to swing in a wide arc off the screen. Applying position compensation `P_new = F - deltaRot * (F - P_old)` ensures the focused part spins in place on its own center.

## 2026-09-21 - Unified animated reset via UI button and 'R' shortcut

**Decision:** Added an on-screen "Reset View" button and mapped `KeyCode.R` to `EyeAnatomyController.ResetToHome()`, smoothly animating rotation, pan, and zoom back to default forward-facing overview while clearing focus and restoring materials.

**Why:** Previously, reset was an undocumented key that only reset `XRRig` and `EyeModelPivot` without clearing focus or restoring materials, and was completely inaccessible on touch/stylus without a physical keyboard.

## 2026-09-21 - Vendor both SDKs under `Assets/`, not as UPM packages

**Decision:** Copy both SDKs into
`Assets/Games/kmax-display-example/Plugins/Kmax/` rather than adding them to
`Packages/manifest.json` or embedding them in `Packages/`.

**Why:** The base project is read-only, so `Packages/manifest.json` cannot be
edited and no folder can be created under `Packages/`. Unity compiles assembly
definitions from `Assets/` exactly as it does from a package, so nothing is
lost except the Package Manager UI entry and the "Import Sample" button.

## 2026-09-21 - One define symbol, not two

**Decision:** Mutual exclusion is driven by a single symbol, `KMAX_AIO_K1`.
XR Core assemblies constrain on `!KMAX_AIO_K1`; AIO assemblies constrain on
`KMAX_AIO_K1`.

**Why:** Two symbols (`KMAX_XR_CORE` / `KMAX_AIO_K1`) allow "both defined" and
"neither defined" - the first reintroduces the `CS0433` collisions the
constraints exist to prevent, the second silently compiles no Kmax runtime at
all. With one symbol both states are unrepresentable. It also means a fresh
clone compiles against XR Core without anyone having to set a project setting
first, which matters because `ProjectSettings/` is read-only.

## 2026-09-21 - Re-GUID the AIO copies, not the XR Core copies

**Decision:** The five `.meta` GUIDs shared by both SDKs were reassigned on the
AIO side. XR Core keeps every GUID exactly as shipped.

**Why:** Duplicate GUIDs under `Assets/` make Unity reassign one side at import
and break whatever referenced it. XR Core is the default backend and the one
Kmax's own documentation, prefabs and future package updates are written
against, so it keeps its identities. Nothing inside the AIO SDK referenced the
five affected scripts, so only the `.meta` files changed.

The five files (XR Core forked from AIO, which is why they collided):

| XR Core | AIO K1 |
|---|---|
| `Runtime/Scripts/Utility/StylusDragable.cs` | `Runtime/Scripts/StylusDragable.cs` |
| `Runtime/Scripts/Input/KmaxUIRaycaster.cs` | `Runtime/Scripts/Input/KmaxUIFacade.cs` |
| `Runtime/Scripts/Input/KmaxInputModule.cs` | `Runtime/Scripts/Input/KmaxInputModule.cs` |
| `Runtime/Scripts/Input/KmaxPointer.cs` | `Runtime/Scripts/Input/KmaxPointer.cs` |
| `Runtime/Scripts/Input/KmaxStylus.cs` | `Runtime/Scripts/Input/KmaxStylus.cs` |

## 2026-09-21 - The switcher lives in a zero-reference assembly

**Decision:** `KmaxDisplayExample.Editor.asmdef` declares no references and
sets `autoReferenced: false`, and `KmaxSdkBackend.cs` uses no SDK type.

**Why:** If the switcher compiled into `Assembly-CSharp-Editor` it would
auto-reference the active SDK. A compile error in that SDK would then remove
the very menu needed to switch away from it.

## 2026-09-21 - Did not rename the AIO `Runtime/Resources` folder

**Decision:** `com.kmax.xr.aio/Runtime/Resources/` stays as-is, so
`StylusLine.prefab` and `KmaxPenOne.asset` are included in every build even
when the AIO backend is compiled out.

**Why:** `Runtime/Scripts/Frame/Stylus.cs` calls `Resources.Load("StylusLine")`.
Renaming the folder would break the AIO stylus beam at runtime, which is a
worse outcome than two small assets of dead weight. `KmaxPenOne.asset` has an
unresolvable script reference while XR Core is active; Unity does not warn
about it at import time. Revisit only if it shows up in a build report.

## 2026-09-21 - AIO HTML docs hidden, XR Core samples imported

**Decision:** `com.kmax.xr.aio/docs` was renamed to `Documentation~`;
`com.kmax.xr.core/Samples~` was renamed to `Samples`.

**Why:** The AIO docs are ~200 HTML, font and image files with no use inside
the Editor - importing them as textures and text assets is pure AssetDatabase
weight, and `~` is the UPM convention for exactly this. The XR Core samples are
the opposite case: under `Assets/` a `~` folder is invisible, and there is no
Package Manager "Import Sample" button for a vendored package, so the scenes
had to be un-hidden to be usable. Their scripts needed
`KmaxXR.Core.Samples.asmdef` so they do not land in `Assembly-CSharp`, where
they would break whenever the AIO backend is selected.

## 2026-09-21 - This module's docs live in the module

**Decision:** The AGENTS.md section 16 document set for this module is
`Assets/Games/kmax-display-example/docs/`, not the base project's `docs/`.

**Why:** The base project is read-only. The base `docs/` set still describes
the template and its analytics work and is unchanged.

## 2026-09-21 - The render pipeline changed mid-session, Built-in -> URP

**Finding, not a decision.** Early in the session
`GraphicsSettings.currentRenderPipeline` was null: `GraphicsSettings.asset`
referenced a URP asset by GUID `9fb93e134785e584698b58912ee0b588` that no asset
in the project carried, so Unity fell back to Built-in RP. A URP asset was then
added at `URPAssets/URPAsset.asset` and the project switched to URP.

**Consequence:** any earlier note claiming this project runs Built-in RP is out
of date. What it changed:

- glTFast reimported `EyeAnatomy.glb` onto URP shader-graph materials by
  itself. Nothing to do.
- The AIO K1 SDK's two CG shaders are now a real limitation of that backend,
  though it is not the default.
- Hand-written Built-in CG shaders silently draw nothing, which cost the custom
  hotspot shader (below).
- `Camera.Render()` stopped working for offscreen capture; SRP needs
  `RenderPipeline.SubmitRenderRequest`. Tooling only.

## 2026-09-21 - Baked two explode poses instead of playing the clips

**Decision:** `Kmax/Eye Anatomy/Bake Explode Poses` samples the model's merged
position curves, picks the most-packed and most-spread sample times, and writes
them to `EyeExplodePoses.asset`. `EyeExplodeView` lerps between those two poses.

**Why:** the 23 imported clips are a *pulse*, not an open/close - measured
spread over the 2.53 s runs 0.287 -> 0.179 -> 0.286 -> 0.179. Playing them
would make the eye breathe rather than open. They also bind to 23 separate
paths, and one `Animator` state can only drive one clip. Baking the two
extremes gives a deterministic closed default, a reversible scrubbable
transition, no `Animator` asset, and no per-frame allocation. The bake is a
menu item rather than a one-off so a re-exported model can be re-baked.

## 2026-09-21 - Focusing moves the model, and hides everything else

**Decision:** `EyeFocusView` scales and repositions the model root rather than
moving the camera, and hides all other parts while one is focused.

**Why:** the XRRig camera is head-tracked - moving it fights the tracker and
breaks the stereo geometry, so content comes to the viewer instead. The
isolation is not cosmetic: scaling the model ~4.5x so the lens fills half the
view also scales the sclera and muscles, which then cover the whole screen and
bury the part being explained. This was visible in testing before isolation was
added. Toggle it with `isolateFocusedPart` if you want context kept.

## 2026-09-21 - Hotspot markers use a stock URP shader, not a custom one

**Decision:** markers use `Universal Render Pipeline/Unlit`. The custom
`KmaxDisplayExample/HotspotMarker` shader was written, tested and deleted.

**Why:** the intent was `ZTest Always` so a marker on a part hidden behind
another part stayed visible. Under Built-in RP that worked. After the pipeline
switch, a hand-written URP SubShader compiled cleanly, reported
`isSupported == true`, was correctly selected as the material's only pass - and
drew nothing, at any render queue, with a constant-colour fragment, before and
after a forced synchronous reimport. A stock URP Unlit material in the same
frame on the same object rendered fine. Rather than keep debugging a shader
that is incidental to the feature, the markers were moved to the stock shader
and the dead one deleted.

> [!NOTE]
> **Revisit this.** The 2026-09-25 entry at the top of this file found that the
> pipeline was using URP's **2D renderer**, which discards 3D lights and would
> plausibly make a hand-written 3D lit SubShader compile, report `isSupported`
> and draw nothing - exactly the symptom below. The conclusion that custom
> shaders are unusable in this project was drawn under that renderer and may
> simply be wrong.

**Cost:** markers are depth-tested, so one can be occluded by geometry in front
of it. Mitigated by placing each marker in front of its part's **front face**
(`hotspotFrontGap`) rather than at its centre, which is also more robust than a
fixed offset - a marker at the centre of the 67 mm-deep sclera shell would sit
inside it. If always-on-top is needed later, the supported URP route is a
Render Objects renderer feature on a dedicated layer with Depth Test = Always;
`URPAssets/URPAsset_Renderer.asset` is in this module and writable, but the
layer would have to be added in the read-only base project.

## 2026-09-21 - Anatomy labels are inferred, and two of them are unverified

**Decision:** the 18 labels in `EyeAnatomyCatalog.asset` were derived from the
model's mesh names plus measured geometry, not from a source of truth shipped
with the model.

**Why / what to check:** most are unambiguous - the superior and inferior recti
and obliques were identified from their measured position and shape in the
assembled pose. **`Medial rectus` and `Lateral rectus` are a coin flip**: which
is which depends on whether the model is a left or a right eye, and that could
not be determined. The usual tell, the optic nerve head sitting nasal to the
posterior pole, measured only -0.0017 m against a ~0.015 m globe radius - about
6 degrees, inside modelling noise, and the two obliques point the other way.
`Anterior chamber` and `Tear film` are also readings of the mesh names
`cornea_inside` and `eye_over_cornea` rather than confirmed identifications.

Because the catalog is a data asset, correcting any of these is an inspector
edit with no code change - which is exactly why it is a data asset.

## 2026-09-21 - Fitted the eye model by derivation, not by eye

**Decision:** `EyeAnatomy`'s transform in the scene is computed from the
virtual screen rather than hand-placed: uniform scale
`screenHeight * 0.70 / rawBoundsHeight`, rotation `(0, 180, 0)`, and a position
that puts the renderer bounds centre on the rig origin. architecture.md records
the arithmetic.

**Why:** The model is authored ~12 m across, roughly 60x too large for a
0.345 m virtual screen, and faces away from the viewer. Recording the
derivation rather than a magic transform means the next person can refit it for
a different `ScreenType` or a different framing fraction without reverse
engineering three float triples. Centring on the rig origin puts the model at
zero parallax, which is the neutral starting point for tuning pop-out.

## 2026-09-21 - Left the model's animation unwired

**Decision:** The 23 imported `AnimationClip`s are left as imported sub-assets.
The root `Animator` has no controller, so nothing animates at runtime.

**Why:** The clips are one-per-part (t = 0 exploded, t = 2.53 assembled) and
all bind to distinct paths under the same root, so a single Animator state can
only ever move one part. Making them play together needs a choice that is a
product decision, not a mechanical one:

1. **Merge into one clip** - copy all 23 clips' curve bindings into a single
   `AnimationClip` asset, drive it from one Animator state. Lossless, because
   no two clips touch the same binding. Best fit if the explode/assemble is a
   single timeline.
2. **One Animator layer per clip** - 23 layers at weight 1. Faithful to the
   import but unwieldy.
3. **Drive it from code** - sample the clips directly against a normalised
   time, e.g. from a slider or the stylus, for scrubbable anatomy.

Option 1 is the obvious default, but the playback behaviour it implies
(autoplay? loop? ping-pong? stylus-triggered?) had not been specified, so
nothing was built. See roadmap.md.
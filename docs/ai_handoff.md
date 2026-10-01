# AI Handoff

## Session 2026-10-01 - Cross-Scene Suite, Launcher, Unified Audio/Stylus/UI & Automotive Lighting Fix

**Build index order in Build Settings:**
1. `Scenes/Launcher.unity` (0)
2. `Scenes/EyeAnatomy.unity` (1)
3. `Scenes/VirtualExhibition WR.unity` (2)
4. `Scenes/VolvoS90.unity` (3)

### How to test the suite
1. Open `Scenes/Launcher.unity` and enter Play mode.
2. The launcher displays 3 interactive cards (`Eye Anatomy`, `i4 Engine`, `Volvo S90`) with custom thumbnail art.
3. Click any card: the background dynamically loads that scene's 3D model, floating at stereo pop-out depth (+0.08 m) and rotating smoothly at 12°/s. Click "Load Scene" to transition into the selected exhibit.
4. In any exhibit scene, test the "Next Scene" button in the lower right to cycle smoothly through all four scenes.
5. Notice the soothing ambient music streams continuously without interruptions or restarts across all scene switches.
6. Test stylus pointer feedback: observe cyan beam at rest, amber when pressing back/reset, purple for tertiary, and vibrant emerald green when aiming at buttons/hotspots while pressing select.
7. In `VolvoS90.unity`, press "Start the car" and "Lights on": the cabin displays soft, authentic warm courtesy lighting, the digital dashboard LCDs glow with crisp modern backlighting, and the headlights throw focused beams onto the floor with zero blown-out glare.

### Traps & Solutions Discovered
- **`VolumeProfile` components created in editor scripts need `AssetDatabase.AddObjectToAsset`**: Calling `profile.Add<T>()` attaches the component in memory, but fails to serialize it to the `.asset` YAML file, resulting in `{fileID: 0}` null references. Without persistent Tonemapping, URP clips all HDR values > 1.0 to blinding white. Always use `AssetDatabase.AddObjectToAsset(component, profile)` followed by `AssetDatabase.SaveAssets()`.
- **UI Occlusion by 3D Colliders**: Pointer rays from `KmaxInputModule` hit 3D model colliders before reaching World Space UI canvases if sorting orders are default. `UiAlwaysOnTop` places all canvases on an overlay layer with explicit sorting orders, guaranteeing UI buttons always receive clicks.
- **Temporary Ghost Materials can leak into Scenes**: If play mode stops while a part is focused, `renderer.sharedMaterials` can retain the `EyeGhost` material asset. Added `OnDisable()` in `EyeFocusView` and a corruption check in `VolvoExhibitBuilder.EnsureCar()` to automatically restore clean materials.
- **Model-Scale Lighting Inverse-Square Blowout**: Scaled-down miniature models (e.g., car cabin 3 cm tall) place surfaces 1–2 cm away from point lights. An intensity of `0.012f` at `0.02m` produces illuminance 25x brighter than sunlight. Miniature cabin courtesy lights must be calibrated to `~0.0008f` with tightly bounded ranges (`~0.039m`).

### Reusable Components Added
- `ExhibitSceneSwitcher.cs`: Cyclical cross-scene switcher with clean audio persistence.
- `ExhibitLauncherController.cs`: 3D launcher with card selection, animated background model preview, and pop-out depth.
- `LauncherSceneBuilder.cs`: Idempotent menu builder for the Launcher scene (`Kmax/Launcher/Set Up Launcher Scene`).
- `UiButtonMotion.cs`: Physics-style spring hover (1.04x) and press absorption (0.94x) animations.
- `UiAlwaysOnTop.cs`: Automated canvas sorting to prevent 3D geometry from intercepting UI input.
- `AnatomyStylusBeam.cs`: 4-color dynamic stylus beam feedback.
- `ProceduralAudio.cs`: Mathematical zero-allocation audio synthesis for UI clicks, selection bursts, and engine loops.

### Next Recommended Tasks
- Test on physical Kmax stereoscopic display hardware to verify 3D pop-out depth and stylus tracking accuracy.
- Tune stylus haptic feedback parameters based on physical pen test results.

## Session 2026-09-30 (M4) - Probe

**Re-run `Kmax/Showcase/Set Up Probe`.** Authors `Scenes/Showcase/Probe.unity` from nothing,
idempotent at 5 roots and 18 transforms.

### Trying it without hardware

Play. The tip follows the **mouse**, the **scroll wheel** moves it in depth. Put it in the tunnel
mouth - the end nearest you, in front of the glass - and thread it to the far end without leaving
the lumen. The tunnel flashes red on contact and green on completion. **F9** comfort volume,
**F10** audit.

### The number that decides whether this scene works

`ProbePath.lumenOfHeight` and `lumenHardScale` give a lumen of 16.5 mm at easiest and 7.4 mm at
hardest, and **both were chosen against no measurement of how accurately the pen tracks.** Probe
is the most sensitive scene in the suite to that: it is a continuous tolerance test. If `S0-2`
finds the tip is out by more than a few millimetres, the hard end is not playable and
`lumenHardScale` must rise. Do not tune it by feel in the editor - the mouse fallback has perfect
accuracy and will tell you nothing.

### Two bugs worth knowing about

**A lifecycle that auto-resets conflates "task finished" with "visitor left".** Probe's difficulty
scaling was completely dead because finishing a run resolved the scene, the shared lifecycle
auto-reset to Attract a few seconds later, and the game reads Attract as a new visitor - so it
cleared the difficulty and the best time every single time. Probe now sets
`autoResetWhenResolved` false and handles its own replay. **M5 should think about which of the
two it means** before taking the default.

**Anything a component only positions at runtime saves into the scene wherever it was left.** The
attract marker went in as a one-metre sphere at the origin, outside the depth budget and breaking
the window. The build step now places and sizes it too. The comfort audit from M1 caught this
without being asked, which is the first time that tooling has earned its keep.

### Reusable pieces added

`ProbePath.Sweep` builds a tube of any polygonal cross-section along any centreline, open or
closed, with parallel-transport frames - rails and hoops both come from it. Anything needing real
swept geometry instead of a billboarding `LineRenderer` should start there.

### Next recommended task

`M5 - Reef`, the last scene and the only one with an art pipeline - budget for it to be the long
pole. Or `M0` the moment a device is free: it is still the gate on the whole suite, and it would
now clear `S2-8`, `S3-4`, `S3-10` and `S4-9` in one sitting, plus settle Probe's lumen sizes.

## Session 2026-09-30 (M3) - Stack

**Re-run `Kmax/Showcase/Set Up Stack`.** Authors `Scenes/Showcase/Stack.unity` from nothing and
is idempotent - 6 roots and 32 transforms before and after.

### Trying it without hardware

Play. The tip follows the **mouse**, the **scroll wheel** moves it in depth, the **left button**
grabs. Pull a block forward off the bench and set it on the platform. **F9** draws the comfort
volume, **F10** audits it.

### The one number that still needs a hand

`StylusGrab.followTime`, at 0.04 s on no evidence. It is the spring lag between the hand and the
held block: too low and the block feels weightless and stuck to the pen, too high and it is
visibly not where your hand is, which destroys the co-location the suite exists to demonstrate.
**Tune this first on hardware.** Everything else in the scene is measured.

### Three traps this session paid for

**`Transform.InverseTransformPoint` divides by localScale.** If you store an offset with it and
put it back with rotation alone, the scale never cancels. Every Stack block is a unit cube scaled
to 23 mm, so the grab offset came back about forty times too large. Use
`Quaternion.Inverse(t.rotation) * (point - t.position)` for a scale-independent local offset. The
bug hides completely if you test by grabbing things at their centre.

**The panel size is not a project constant.** The SDK prefab and VolvoS90 are 15.6 inches
(345 x 194 mm); EyeAnatomy overrides `screen.screenType` to 27 (598 x 336 mm). **Never author a
layout in metres.** Stack derives every dimension from `StereoVolume`, and M4 and M5 must too.
Worth deciding which panel the suite actually targets - it affects the older exhibits as well.

**Unity 6 renamed `PhysicMaterial` to `PhysicsMaterial` but left the extension alone.** The asset
must be written as `.physicMaterial`. `.physicsMaterial` writes a file that looks right, keeps
working for the rest of the session because the live object is still referenced, and loads back
as a `DefaultAsset` afterwards - so the friction silently reverts the next time the scene opens.

### Shared code

`ShowcaseBuildUtility` now owns the rig, the event system, the tip, the diagnostics and the
serialized-field helpers. Bloom was refactored onto it and verified unchanged. M4 and M5 should
use it rather than copy it.

### Next recommended task

`M4 - Probe`, which needs `StereoVolume.ProjectedMargin` for its path generator - already built
and verified for Bloom. Or `M0` the moment a device is free: it remains the gate on whether tip
co-location is accurate enough for Stack and Probe to work as designed, and it would clear
`S3-4`, `S2-8` and `S3-10` in one sitting.

## Session 2026-09-30 (M2) - Bloom

**Re-run `Kmax/Showcase/Set Up Bloom`.** It authors `Scenes/Showcase/Bloom.unity` from nothing,
including the Kmax rig, and it is idempotent - re-running on the finished scene changes neither
the root count nor the transform count.

### Trying it without hardware

Enter play mode. The tip follows the **mouse**, the **scroll wheel** moves it in depth, and motes
burst when it touches them. **F9** draws the comfort volume, **F10** writes a comfort audit to
the console. `StylusTip.IsTracked` is false the whole time - that is the flag to gate scoring on.

### Two traps this session paid for

**The SDK throws if you vibrate a pen that is not there.** `KmaxStylus` is in the scene whether
or not hardware is connected, so `pen != null` passes and then `PenTracker.Vibrate` reaches a
`PNClient` with no connection and throws a NullReferenceException from inside vendor code. Guard
on `KmaxStylus.Visible`, which is what `StylusHaptics` now does for every cue including `Stop`.
It first showed up on play-mode exit, via `OnDisable`.

**URP re-derives `_SrcBlend` and `_DstBlend` from `_Surface` and `_Blend`.** Writing the blend
factors directly is silently overwritten on import - the same shape as the clear-coat trap. Write
the toggles. And URP's additive is `SrcAlpha, One`, not `One, One`; test the **destination**
factor, or a correct material reads as broken.

### The design change worth knowing

`S2-2` was planned as a lifetime tuned so motes expire before reaching the frame edge. It is
built as a continuous fade on `StereoVolume.ProjectedMargin` instead, because the premise was
wrong: **a mote drifting straight at the viewer runs out of window margin without moving
sideways**, since projection from the eye magnifies anything in front of the glass. Probe will
want the same function for `S4-2`.

### What has and has not been judged

Measured: the field crosses the glass (10 in front, 38 behind, spanning -87 to +209 mm), zero
window violations, ninety bursts at fourteen particles each, and the full lifecycle round trip.

Not judged at all: the chime and the haptic tick were called but never heard or felt, and nothing
in this scene has been seen in stereo. The trails, the depth grading and the grid are the three
things carrying the scene's weakness - loose points read pop-out well and depth badly - and all
three have only been seen flat.

### Next recommended task

`M3 - Stack`, which is the headline scene and the first real test of `StylusGrab`. Or `M0` the
moment a device is free - it is still the gate on whether Stack and Probe work as designed, and
`S2-8` can be cleared in the same sitting.

## Session 2026-09-30 (M1) - the shared core exists

**No build command.** M1 is library code: ten types in `Scripts/Showcase/Runtime` and
`Scripts/Showcase/Editor`, in the new `KmaxShowcase` and `KmaxShowcase.Editor` assemblies.
Nothing is wired into a scene yet, which is M2's job.

### Read first

`StereoVolume` is the piece everything else leans on, and it is a **static class**, so there is
nothing to add to a scene and nothing to configure. It reads the live `XRRig`. If it returns
zeros, the scene has no rig.

### What is true, and what is only written

Verified against a live rig, not asserted:

- **130.0 mm of pop-out, 300.0 mm of depth.** Derived from `StereoCamera.DefaultDistance` and
  the comfort edges in `XRRig.DrawFrustum`, then read back to confirm.
- **`ViolatesWindow` is correct**, over six cases including the one that matters - the same X
  violates when popped forward and passes on the glass. Hand-checked.
- `ExceedsComfortDepth`, `Contains`, `ClampToComfort` behave at the limits. All seven components
  instantiate. The audit menu item is registered.

Not verified, because it needs the hardware:

- **`StylusTip.tipOffset` is zero.** That is almost certainly wrong, and it is the single number
  every co-located interaction in the suite depends on. `S0-2` measures it. Until then, tip
  grabbing is approximately right at best.
- **`StylusGrab.followTime` is 0.04 s on no evidence.** It is the spring lag between the hand and
  the held object: too low reads as weightless, too high destroys co-location. First thing to
  tune on a device, and it will not be right on the first guess.
- Neither the gizmo nor `ComfortOverlay` has been seen in stereo.

### Two things that changed from the plan

**`S1-7` shipped both selection modes** rather than waiting for `S0-7`. `StylusGrab.Selection`
switches between tip proximity and ray; everything downstream is identical. The gate is now a
field change, not a rewrite, so M0 can no longer invalidate M1.

**There is a mouse fallback.** With no pen tracked, the tip follows the mouse (scroll wheel for
depth) and grabs on left click, so the scenes can be built and debugged before the hardware
arrives. It is not co-location and proves nothing about feel. `StylusTip.IsTracked` is false
while it drives - gate any scoring on that.

### The number that moved

**The rig is set to 27 inches, not 15.6.** Working window **598 x 336 mm**. The design doc had
been quoting the 15.6 in figure, which is nearly half the width, and scene layouts drawn against
it would have been far more conservative than necessary. Check `VirtualScreen.ScreenType` before
trusting any layout number.

### Traps that still apply

The five from the Volvo sessions are unchanged and all still relevant - see the session below.
The one that bit hardest is worth repeating in this context: **verify a written value, never the
finished log.** Everything claimed above was read back after the fact, which is why the 27-inch
discovery surfaced at all.

One new one: a full `AssetDatabase.Refresh(ForceUpdate)` now also reimports the 1.5 GB
`Assets/ThirdParty` KB3D kit. Prefer a plain refresh unless a force is genuinely needed.

### Next recommended task

`M2 - Bloom`, which is buildable now on the mouse fallback. Or `M0` the moment a device is free;
it is still the gate on whether Stack and Probe work as designed, and it is half a day.

## Session 2026-09-30 (planning) - four new scenes, nothing built yet

**There is no build command for this work and no code to re-run.** This session produced a
plan. Start at **[showcase-suite.md](showcase-suite.md)**; everything below is only the
context that document assumes.

### What changed in direction

The Volvo exhibit is finished. The next work is a **new suite of four scenes** sharing one
direct-manipulation core, with nothing carried over from any existing exhibit. It serves three
purposes at once - client capability demo, production exhibit, internal tech reference - and
the split between them is written down in showcase-suite.md so they do not quietly trade off
against each other.

### The one thing to read before touching anything

**M0 is a gate, and it needs the hardware.** The suite assumes the tracked pen tip and the
rendered geometry occupy the same physical point to within a few millimetres. Nothing in this
project has ever needed that to be true - all three exhibits use the stylus as a ray-caster,
which forgives a centimetre of offset that a grab radius does not.

If M0 fails, Stack and Probe do not survive as designed and the suite becomes ray-based. Do
not build M1's `StylusGrab` before `S0-7` is answered. Half a day of measurement saves a week.

### The numbers, and where they come from

Do not re-derive these, and do not hard-code them either - `StereoVolume` exists to own them:

- `StereoCamera.DefaultDistance = 0.5f` - camera to screen plane
- `XRRig.DrawFrustum` draws the comfort zone at 0.37 and 0.80 from the camera
- therefore **0.13 m of pop-out, 0.30 m of depth**, and a 0.345 x 0.194 m window at 15.6 in
- all of it scales with `XRRig.ViewScale`, which raises `OnViewScaleChanged`

### What carries over from the old stack, and what does not

**Carries over:** `ProceduralAudio`, `UiAlwaysOnTop`, `UiButtonMotion`, `ExhibitPostProcessing`,
the `ExhibitAttractMode` pattern, and the Kmax plumbing - world-space canvas with `UIScaler`
plus an explicit event camera, `KmaxInputModule` on the `EventSystem`, no `Camera.main`, no
`ScreenSpaceOverlay`. That plumbing took three sessions to get right.

**Does not:** `EyeAnatomyController`, `EyeFocusView`, `EyeExplodeView`, `EyeAnatomyCatalog`
and everything shaped around them. They are a good abstraction for inspect-and-explode and
the wrong one for direct manipulation. decisions.md has the argument.

### Five traps already paid for, which apply to the new scenes too

Carried from the Volvo sessions, all of which failed silently:

1. `refresh_unity` can report success without recompiling - verify a written value, never the
   "finished" log
2. `Material.EnableKeyword(string)` does nothing for a non-overridable local keyword; use the
   typed `LocalKeyword` overload
3. `Mathf.SmoothStep` is not GLSL smoothstep - it interpolates between its first two
   arguments and is useless as a mask
4. A frozen `AudioSettings.dspTime` means a stalled audio device, not broken audio code;
   `AudioSettings.Reset(AudioSettings.GetConfiguration())` recovers it
5. Every build step must **converge on the spec from wherever the scene is**, never return
   early because the object already exists. This cost two sessions on the Volvo, twice.

Rate-limited haptics is a sixth, learned from device feedback: roughly one pulse per 0.25 s,
or the pen buzzes continuously and becomes unpleasant.

### Two stereo questions the Volvo left open

Both are folded into `S0-6`, because both need the hardware and neither was ever checkable in
the editor: the **wallpaper effect** on the car's regular tile grid (the eyes lock onto the
wrong tile and the surface jumps in depth), and whether **semi-transparent surfaces over
solid ones** fuse at all - the car's glass and the engine's 0.20-alpha casing are both this.
The answers constrain what the new scenes may use.

### Next recommended task

`S0-1` through `S0-7`. They need the device.

## Session 2026-09-29 (Volvo, presentation) - the showroom, the icons, and the real engine

**Re-run `Kmax/Volvo Exhibit/Set Up Volvo Exhibit`.** Built, saved, exercised in play mode,
scene left clean and closed. Everything below is authored by that one command.

### The exhibit now

A showroom: the car stands on a lit floor under a window gobo, casts a real shadow, and
wears clear-coated paint that reflects a studio. The interface is icons and colour swatches.
The engine and the music are real recordings. See decisions.md for each of these; the notes
below are only the things that will waste your time if you do not know them.

### Five silent failures, all found the same way

None of these errored. Each was found by **checking a value after the build claimed success**,
and that habit is the single most useful thing to carry forward here.

1. **`refresh_unity` can report success without recompiling.** The build step then runs
   against the previous assembly and skips whatever field you just added, while logging
   "finished". Force it with `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` +
   `CompilationPipeline.RequestScriptCompilation()`, then prove the new code is loaded
   before running the menu item.
2. **`Material.EnableKeyword(string)` does nothing for a non-overridable local keyword.**
   It writes the name into the material's keyword list, where it serialises and reads back
   convincingly, while `IsKeywordEnabled` stays false. Use the typed `LocalKeyword` overload.
   `EnableLocalKeyword` in the builder does it properly and warns if it did not take.
3. **URP `Lit` has no clear coat at all.** The properties are on the material because they
   are part of the shared URP property block and the shader ignores them. Clear coat is in
   **Complex Lit**. And `_ClearCoat` (the toggle) and `_ClearCoatMask` (the strength) are
   different properties - URP re-validates on import and rebuilds the keyword from the
   toggle, so setting only the mask had the keyword switched straight back off.
4. **`Mathf.SmoothStep` is not a GLSL smoothstep.** It interpolates *between* its first two
   arguments, so `Mathf.SmoothStep(0.55f, 0.95f, x)` never leaves 0.55-0.95 and is useless
   as a mask. Used as one, it lit the whole upper hemisphere of the studio cubemap. There is
   a `Step` with real edges in `EyeAnatomyAssetFactory` - and it, in turn, had to be fixed to
   handle a *falling* edge, which is how the window gobo is defined.
5. **`BuildSoloPanel` returned an existing hinge untouched**, so changing the sunroof angle
   in the table did nothing. Same mistake `BuildDoor` was fixed for a session earlier. Every
   build step here has to converge on the spec from wherever the scene is.

### Two things that were not code

**The audio device stalled.** `AudioSettings.dspTime` froze - identical across separate calls
and across a play-mode restart - so every source reported `isPlaying` true with `timeSamples`
stuck at 0 and nothing audible. Not a project setting: `m_DisableAudio` was false and the
editor was not muted. `AudioSettings.Reset(AudioSettings.GetConfiguration())` recovered it.
Worth trying that before looking for a bug in the audio wiring.

**The scene was not ghosted.** Every renderer reading `EyeGhost` in edit mode meant the
editor was in play mode with a part focused, not that the ghost had been saved. The scene on
disk carries exactly one reference to that material.

### Generated assets, and how to change them

Everything the exhibit is lit and dressed with is drawn by the build step, so **changing one
means deleting the asset and re-running** - the factories return what already exists:

| Asset | Drawn by |
|---|---|
| `Materials/StudioReflection.cubemap` | `GetOrCreateStudioReflection` - bright floor, dark ceiling, window bands |
| `Materials/StudioWindowCookie.png` | `GetOrCreateKeyCookie` - the gobo on the key light |
| `Materials/ShowroomFloorGradient.png` | `GetOrCreateFloorGradient` - albedo, tiles, rim fade |
| `Materials/ShowroomFloorSurface.png` | `GetOrCreateFloorSurface` - smoothness falloff |
| `Model/ShowroomFloor.asset` | `GetOrCreateFloorMesh` |
| `Materials/Icons/Icon*.png` | `ExhibitIconFactory` - six SDF glyphs |

The floor's brightness and the key's intensity are **set against a clipping measurement**,
not by eye: 0.54 albedo and 1.15 intensity give 4.29% of frame clipped, against 14.43% at
0.72 and 1.5. If either moves, re-measure. The remaining 4% is the lamps and the paint's
highlights, which should be at the top of the range.

### The supplied recordings

`Model/VOLVO/Music/VOLVO-S90-Start.wav` (5.5 s) plays once, then
`VOLVO-S90-Loop.wav` (14.5 s) loops under it. `VehicleIgnition` already sequenced this; the
only subtlety is `catchDelay`, which is derived from the start clip's length rather than
typed in, so a different recording needs no second edit. `Music/Dark-Times.mp3` is the
background track at 0.30, streamed.

### Next recommended task

- **The gobo is still coarse.** It reads as light through a window, but the panes resolve at
  roughly tile scale on the floor. `cookieSize2D` is 0.15 m; smaller tiles or a larger cookie
  would sharpen it.
- **Exercise all of this on the Kmax hardware.** None of the stereo-specific judgements have
  been checked on the device - the floor was kept to 0.45 m radius specifically to avoid a
  window violation, and that is a guess until someone looks at it in stereo.
- Move `Volvo S90.blend` (626 MB) out of `Assets/`. Still outstanding from three sessions ago.

## Session 2026-09-29 (Volvo, last) - the three remaining features, then a presentation pass

**Re-run `Kmax/Volvo Exhibit/Set Up Volvo Exhibit`.** Built, saved and exercised in play
mode. The exhibit is feature-complete against the roadmap.

### The scenes moved. Both builders were pointing at the old paths

`VolvoS90.unity` and `VirtualExhibition WR.unity` are now under `Scenes/` with the eye's,
committed in `e689e0a` mid-session. `VolvoExhibitBuilder.ScenePath` and
`EngineExhibitBuilder.ScenePath` were still pointing at `Model/VOLVO/` and
`CarEngineAnimated - i4/`, and both failed with "Scene file not found" until they were
corrected. Only the scenes moved; the models, materials and textures stayed put.

### What was built

1. **Interior focus.** An `Interior` node under the car holding 31 cabin meshes, created
   at the car's origin with every world pose preserved. It is a catalogue entry like any
   other, so `EyeFocusView` frames it and ghosts the 119 renderers that are not under it.
   The door cards stay on their hinges and are ghosted with the bodywork - they have to
   swing with their doors, and a ghosted door reads as one you can see through.
2. **Paint and trim swatches.** `VehicleFinishSwatches` (new), used twice, on
   `VolvoExhibit/Paint` and `VolvoExhibit/Interior Trim`. Driven by `ExhibitFeaturePanel`
   through `IExhibitMachinery`, both unchanged in shape.
3. **Guided tour.** Eight stops in `Data/VolvoCatalog.asset`, numbered badges,
   Next/Back, info panel, attract loop. `Data/VolvoTourPoses.asset` is an explode pose
   set with **nothing in it** - that is deliberate and decisions.md says why.

### Four traps, all of which cost time

**`refresh_unity` can report success without recompiling.** A build step then runs against
a stale editor assembly and silently skips whatever field you just added - `partNoun` was
written by the code on disk and not by the assembly Unity was running, while
`framingRatio` from an edit one minute earlier applied fine. Nothing errors. Force it with
`AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` +
`CompilationPipeline.RequestScriptCompilation()`, and **verify a value you just wrote**
rather than trusting that the build logged "finished".

**Bounds in world axes are wrong for a turned model.** The car now rests at 225 degrees, so
its world-aligned box is 0.25 m square in plan where the car is 0.10 x 0.26. Anything that
means "the model's own silhouette" has to use `EyePartBounds.TryGetLocal`.

**Find-before-create has to undo the old role too.** The swatches used to be named text
buttons; the chips reuse those objects by name, and the old `Label` child and background
`Image` sat on top of the colour until `BuildSwatchChip` was made to remove them.

**Three components fight over the same materials.** `VehicleLightRig`,
`VehicleFinishSwatches` and `EyeFocusView` all touch the renderers' material arrays in
`Awake`, in undefined order. Resolved by all of them going through `Renderer.materials`
(which instantiates once and returns the same copies after), by matching targets on the
material *name* with `" (Instance)"` stripped rather than by reference, and by the
swatches calling `EyeFocusView.RefreshCachedMaterials` when their copies are in. Read that
decisions.md entry before touching any of the three.

### The presentation pass that followed, on request

- Cabin lamp 0.09 -> **0.012**, range 0.45 -> 0.26 car-lengths, interior emission
  2.6/2.0/1.3 -> 1.5/1.15/0.75. The interior was clipping to white with the lights on.
- Swatches are colour chips with a selection ring, not named buttons. Nine full-width
  buttons became two rows 368 px wide. The car's controls shrank to 300x72.
- Info panel labels auto-size, so a long description shrinks instead of running off its
  background.
- `RenderSettings.customReflectionTexture` points at a **generated studio cubemap**
  (`Materials/StudioReflection.cubemap`) while the skybox stays the near-black gradient.
  This is the single biggest change to how the car looks.
- The car rests at a 225 degree yaw on the pivot, applied last in the build because the
  headlight beams and exhaust audio are placed from `carBounds.max.z`.
- The idle is retuned towards a two-litre turbo four. It is **synthesised, not a
  recording** - see decisions.md. `VehicleIgnition.idleOverride` takes a clip if a
  licensed recording ever turns up.

### Files this session

New runtime: `VehicleFinishSwatches`.
Modified runtime: `EyeAnatomyController` (`hotspotsOutsideModel`, `partNoun`,
`HotspotPosition`), `EyeFocusView` (`RefreshCachedMaterials`), `EyePartBounds`
(`TryGetLocal`), `ExhibitFeaturePanel` (variant markers and label),
`ProceduralAudio` (idle retune, `SnapToLoop` multiple overload).
Modified editor: `VolvoExhibitBuilder` (the bulk), `ExhibitUiFactory` (info panel, swatch
chips, button font size), `EyeAnatomyAssetFactory` (`LoadOrCreate`, studio cubemap),
`EngineExhibitBuilder` (scene path, delegates its info panel and asset helper to the
shared ones).
New assets: `Data/VolvoCatalog.asset`, `Data/VolvoTourPoses.asset`,
`Materials/StudioReflection.cubemap`.

### The showroom, added after the above

The car now stands on a generated floor (`Showroom/Floor`, 0.45 m radius, its own scene
root) and casts a real shadow onto it. Four things had to be true at once and each was
wrong first:

- Shadows were a **no-op project-wide** - 50 m shadow distance over a 2048 map, 24 mm per
  texel on a car 260 mm long. `UpgradeRenderQuality` now sets 2 m, soft shadows and 4x
  MSAA on the shared URP asset, which changes the eye and the engine too.
- The floor rendered to **within 0.004 of the backdrop** and was invisible. Backdrop
  measures sRGB 0.169, 0.176, 0.208; the gradient is authored against that.
- The resting view was **level with the car's centre**, and a horizontal disc seen from
  dead level is a horizontal line. `ViewerFlyController.homePitch` is new, 14 degrees here
  and 0 on the other two exhibits.
- The disc was **wound backfacing**. Correctly placed, correctly coloured, invisible.

The disc's edge is hidden by a **smoothness falloff**, not by the albedo fade: at the rim
the floor is seen at a grazing angle and was mirroring the studio softboxes at sRGB 0.47
against a backdrop of 0.17. Smoothness rides in the alpha of
`Materials/ShowroomFloorSurface.png`, which must import with sRGB **off**.

If the floor ever needs resizing, note why it is only 0.45 m: this is a stereo display and
a floor running off the frame in front of the screen plane is a window violation.

### Next recommended task

Three things were asked for and are **not** started, all raised with the user:

1. **Icon glyphs on the car's own controls.** The swatch chips answered most of "too many
   buttons"; the doors, sunroof, tour, lights, ignition and reset are still text. The user
   has chosen **generating them in the build step** over bringing in a licensed set, so
   this means a small signed-distance rasteriser writing PNGs next to the floor's textures
   - circle, rounded box, capsule and arc are enough for all six glyphs.
2. **Scene VFX.** The mote layers from `AnatomyParticleDirector` and the showroom floor
   are all there is. No package has been installed. The agreed direction is **studio, not
   spectacle** - keep anything added restrained enough to sell a car.
3. **Third-party assets.** Nothing has been downloaded and nothing needs to be: the
   studio cubemap, the floor and its two maps are all generated by the build step.

Also still open from before: move `Volvo S90.blend` (626 MB) out of `Assets/`.

## Session 2026-09-29 (Volvo, later) - door interiors in, hood and trunk out

**Re-run `Kmax/Volvo Exhibit/Set Up Volvo Exhibit`.** Built, saved, exercised in play
mode, scene left clean and closed.

Two changes, both asked for:

- The door **interior cards** (`Plane.057` front, `Plane.027` rear - Blender default
  names the modeller never changed) are split and hinged with their doors. They used to
  sit at the car root, so a door opened and left its own inside face behind.
- The **hood and trunk no longer open**. Their hinges are out of the spec, and a
  retired hinge is taken down by `RemoveRetiredPanels`, which returns the geometry to
  the car rather than deleting it. Two groups remain: Doors and Sunroof.

### The thing to know if you touch the build step

`BuildDoor` used to return early when a door pivot already existed. That is not safe
here, because `RemoveSourcePanels` removes the two-sided source from the car whether or
not the halves were placed. When the mirror indicators were added to the door spec after
the doors had been built, those four meshes were cut out of the car and never put back -
silently, with the indicator channel running on four renderers instead of eight ever
since. It is fixed, and both the doors and the indicators came back in the same pass.

**The rule: a build step here converges on the spec from wherever the scene is, not just
from empty.** If you add a part to `GetDoorParts`, an existing door will pick it up. If
you withdraw a hinge, `RemoveRetiredPanels` takes it down. If you withdraw a group,
`BuildUi` destroys its button. Keep all three honest.

### One correction to the note below

The splitter does **not** require the halves to match, and the claim that every panel
divides into exactly equal triangle counts is no longer true: the front interior card
splits 76,512 / 56,224, because the driver's door has switchgear the passenger's does
not. It is still an exact cut. What makes it exact is that no triangle straddles the
centreline - 5.7 mm clearance at worst on the body panels, 43 mm on the cards.

### Still not built

Unchanged from below: the interior focus view, the paint and trim swatches, and the
guided tour. The order and the design notes in `roadmap.md` still stand.

### Files this session

`Scripts/Editor/Anatomy/VolvoExhibitBuilder.cs` only, plus four new split meshes under
`Model/VOLVO/Split/` and the scene.

## Session 2026-09-29 (Volvo) - the car exhibit, partially built

**Run `Kmax/Volvo Exhibit/Set Up Volvo Exhibit`.** Same contract as the other two: finds
before it creates, refuses to run in play mode. Built, saved and exercised in play mode.

Doors, hood, trunk and sunroof open; the car starts, lights up and idles. Paint and trim
swatches, the interior focus view and the guided tour are **not built** - see below.
(The hood and trunk were withdrawn afterwards - see the section above.)

### Read this first - the doors only exist because the meshes were cut

Every two-sided panel on this car is one mesh. The four doors come from two of them.
`VehicleMeshSplitter` cuts each into halves saved under `Model/VOLVO/Split/`, and the
build reparents the halves onto pivots at the hinge lines. **If you re-import the FBX
with different settings, or the split assets go missing, the doors vanish** - the build
logs an error naming the mesh it could not load and carries on without that door.

The cut is exact and stays exact only because nothing straddles the centreline (nearest
triangle 5.7 mm away, every panel splitting into equal halves). If a future model is not
mirrored that cleanly, the splitter refuses rather than producing a lopsided half.

The model must be **Read/Write enabled** for any of this. The build turns it on itself.

### Read this second - the camera cannot go inside the car

At exhibit scale the cabin is about 0.08 m across and `ViewerFlyController` will not come
closer than 0.14 m, so "fly into the interior" is not available as stated. The way this
rig shows an interior is the way `EyeFocusView` already works: scale the model up around
the cabin so it fills the view and bring it to the anchor, with the bodywork ghosted.
That is also the stereo-correct answer - on a head-tracked rig the camera belongs to the
viewer, which is why the whole exhibit moves content rather than cameras.

### Read this third - light intensities are scale-dependent

A lamp inside a car 0.26 m long is centimetres from everything it lights, and
inverse-square falloff makes anything near intensity 1 a blowtorch. The cabin lamp runs
at **0.09**. `VehicleLightRig` captures each light's authored intensity at Awake and
scales that, so the figures live on the lights where they can be seen, not in code.

This only became visible once the glass was made transparent. All the glass on this model
imported **opaque**, which also meant there was no interior to see at all; the build now
converts those materials.

### Files this session

New runtime: `VehiclePanel`, `VehiclePanelController`, `VehicleLightRig`,
`VehicleIgnition`, `VehicleExhibitControls`.
New editor: `VehicleMeshSplitter`, `VolvoExhibitBuilder`, `ExhibitUiFactory`.
Modified: `ProceduralAudio` (engine start and idle synthesis),
`EngineExhibitBuilder` (its button and label helpers now delegate to `ExhibitUiFactory`).

### Two things about the workspace

`Volvo S90.blend` is 626 MB and sits **inside `Assets/`**. Unity has registered it and
failed to import it, and will retry on every refresh - which means launching Blender
headlessly against a file you have open. Move it out of `Assets/`, or into a folder whose
name ends in `~`, which Unity ignores.

The Blender MCP bridge was up (addon running, port 9876 listening, connections
established) but its server layer returned "unavailable" to every call this session, with
three stale `blender-mcp.exe` processes present. Nothing here needed it - the split is
done in Unity and leaves the FBX untouched - but it is worth restarting if you want it.

### Next recommended task

In order:

1. **Interior focus.** A catalogue entry whose bounds are the cabin, plus a body ghost
   over shell, roof and glass. `EyeFocusView` already does the framing; what is new is
   deciding which meshes count as "body".
2. **Paint and trim swatches.** `Car Paint` is a single material at base colour black, so
   paint is one colour swap. The interior ships Black / Blue / Brown / Tan textures for
   the dashboard, console, door panels and seats, so trim is a texture swap.
   `ExhibitFeaturePanel` and `IExhibitMachinery` already exist for exactly this shape of
   feature and should be reused rather than rebuilt.
3. **Guided tour.** The catalogue, hotspots, navigator and attract mode are all model
   agnostic; this is a catalogue of car features plus a call to the shared wiring.

## Session 2026-09-29 (later) - engine features, and the first build's faults

Re-run `Kmax/Engine Exhibit/Set Up Engine Exhibit`. The scene is built, saved and
exercised in play mode with screenshots at each state.

The engine's see-through and build variants are now on the exhibit interface, down the
top-left edge: **See inside**, then **BUILD** with Stock / Sport / Throttle bodies /
Turbo. Both features already existed on `Enginei4` and were reachable only from the
screen-space canvas it shipped with, which the exhibit disables.

### Three things that will trip you up on this rig

**A world-space canvas needs its event camera set explicitly.** Left empty it falls back
to `Camera.main`, and this rig has none - the SDK disables the rig camera's own `Camera`
component and renders through the `left` and `right` sub-cameras it creates at runtime.
With the field empty **not one button on the canvas can be clicked**, and nothing in the
console says so. The builder now assigns it.

**A world-space canvas also needs the SDK's `UIScaler`.** Without it the canvas just sits
at the world origin with no rotation and tilts away as soon as the rig orbits. The scaler
rewrites its pose and size every frame from the rig's screen plane. This is what gives
the interface its overlay-like behaviour, and it is stereo-correct - a real
`ScreenSpaceOverlay` canvas cannot be used, because `VRRenderer` renders side by side and
an overlay ignores camera viewports. See decisions.md.

**`manage_camera screenshot` pauses the editor.** Anything measured straight after one
looks frozen: the explode transition sat at 0.036 for several minutes of wall clock and
looked like a bug in the explode view. Unpause first. Attract mode also fires after 30 s
idle and will expand and tour the engine between MCP round trips - disable it for any
deterministic test.

### Calling into the model from the exhibit

`Enginei4` is in `Assembly-CSharp`, which references `KmaxDisplayExample`, so the exhibit
can never name that type. Two patterns are in use:

- `ExhibitMachineryGate` holds a plain `MonoBehaviour` and toggles `enabled` - enough
  when all you need is something every behaviour has.
- `IExhibitMachinery` is declared on the exhibit side and implemented by `Enginei4` -
  what it takes to call something specific. Unity will not serialise a bare interface
  field, so the reference is stored as a `MonoBehaviour` and cast once in `Awake`.

Do not call `Enginei4.SetVariation` from the exhibit. It early-returns on the disabled
canvas's toggles, and it calls `ActivateAllObjects`, which enables every object with a
MeshRenderer under the model - **including all twelve hotspot badges**, which are meant
to stay hidden until the engine is open. Use `ApplyVariation`.

### Files this session

New:
- `Scripts/Runtime/IExhibitMachinery.cs`
- `Scripts/Runtime/ExhibitFeaturePanel.cs`

Modified:
- `Scripts/Runtime/EyeFocusView.cs` - added the `FocusChanged` event (additive)
- `Scripts/Editor/Anatomy/EyeAnatomySceneUpgrader.cs` - particle systems created from
  scratch now get the mote material, plus `RepairParticleMaterials` for ones an earlier
  run already left without one; `MoteMaterialPath` on the factory made `internal`
- `Scripts/Editor/Anatomy/EngineExhibitBuilder.cs` - `UIScaler`, event camera, the
  feature controls, variant names, fade alpha, and button colours through the ColorBlock
- `CarEngineAnimated - i4/Scripts/Enginei4.cs` - implements `IExhibitMachinery`;
  `ApplyVariation`, `SetTransparent`, `VariationCount`, `GetVariationName`; transparency
  coroutines are now cancelled before new ones start
- `CarEngineAnimated - i4/VirtualExhibition WR.unity`

### Not verified

Still everything that needs the hardware: the stylus beam on engine geometry, the wrist
turn, and whether 0.100 m of pop-out is comfortable over a long session. Also unverified
is how the see-through casing reads **in stereo** - a 0.20-alpha shell in front of solid
internals is exactly the kind of surface that can be hard to fuse, and it has only been
looked at flat.

### Next recommended task

The twelve per-part toggles are the last feature still stranded on the disabled canvas.
They are a different shape from the other two - twelve booleans rather than one state -
so they probably want a scrolling list or a second panel rather than twelve more buttons
down the edge. Worth asking whether they earn their place at all now that the engine
pulls apart, since hiding a part and pulling it away solve much the same problem.

## Session 2026-09-29 - the i4 engine exhibit

**Run `Kmax/Engine Exhibit/Set Up Engine Exhibit` if you touch that scene.** Same
contract as the eye's command: it finds before it creates, so a re-run changes nothing
and a half-finished run can be repeated. It **refuses to run in play mode**, because
the assembled pose is read from the scene and in play mode the parts are wherever the
transition left them.

`CarEngineAnimated - i4/VirtualExhibition WR.unity` is now the same exhibit as the eye,
on the same runtime components. It has been built, saved and exercised in play mode.

### Read this first - the engine has no animation clips

If you go looking for the explode animation, there isn't one. `Models/Enginei4.FBX`
imports with `animationType=None` and no clips; the one `Take 001` in `Engine_opt.FBX`
is a two-key, 0.033 s stub on the root, and the scene does not use that FBX. Every
moving part is procedural code in `Enginei4.Update`, driven by a single `RPM` float.

The teardown is therefore **authored**, as a table in
`EngineExhibitBuilder.GetTeardown`, in the model's native metres. To change how the
engine comes apart, edit that table and re-run the command. The framing re-solves
itself - do not hand-correct the offsets to keep it on screen.

### Read this second - two things that will bite

**`Enginei4` will throw the exhibit scale away if its `ZoomSlider` is ever reassigned.**
It writes its parent's local scale from that slider every frame. A slider on a
*deactivated* canvas is still a live reference, so hiding the old UI is not enough - the
builder clears both slider references outright. If the pulled-apart engine ever appears
about 9x too big (it measured 2.24 m across a 0.345 m screen), this is why.

**The teardown drift correction must only touch top-level groups.** `SolveTeardownDrift`
re-centres the pulled-apart engine on the origin the rig orbits. Applying its correction
to a nested step as well as to its parent shifts that part twice and the solve chases
itself; that showed up as an 0.8 mm overflow that would not close. `Correct` guards this
with `part.parent != modelRoot`.

### Structure

```
EngineExhibit          EyeAnatomyController, EyeExplodeView, EyeFocusView, EyeManipulator,
                       ViewerFlyController, ExhibitAttractMode, AnatomyStylusInput,
                       ExhibitPostProcessing, ExhibitMachineryGate
  EngineModelPivot     EyeManipulator's pivot; never scaled
    Enginei4 (1)       EyeFocusView's modelRoot - scaled 0.10696 to fit, moved to frame parts
      Enginei4         explode/catalogue root, carries the Enginei4 driver
  FocusAnchor
UI                     world-space canvas, 1920x1080 at 0.00017989584 = the virtual screen
XRRig / EventSystem / Ambience / Audio / PostFX / Key+Fill+Rim Light
Engine                 the original root: legacy canvas and point light, both disabled
```

### Measured

Assembled 0.137 x 0.077 x 0.160 m; pulled apart 0.240 x 0.155 x 0.218 m. Both centred
on the origin to four decimals. The teardown uses 69% of the screen width and 80% of its
height. 12 parts, 12 hotspots, 12 pickers, 65 mesh colliders. Focus distance 0.400 m, so
the 0.100 m pop-out is intact. Machinery stops on the frame the explode starts and
restarts only once fully reassembled - confirmed by the crank moving again.

### Not verified

Anything needing the hardware: the stylus beam landing on engine geometry, the wrist
turn, and whether a 0.16 m engine at 0.100 m pop-out is comfortable over a long session.
The eye's pop-out was chosen on geometry rather than comfort and this inherits that
number unchanged, on a model with far more high-contrast detail. It wants a pass with a
real viewer.

### Files this session

New:
- `Scripts/Runtime/ExhibitMachineryGate.cs`
- `Scripts/Editor/Anatomy/EngineExhibitBuilder.cs`
- `Data/EngineCatalog.asset`, `Data/EngineExplodePoses.asset`

Modified:
- `Scripts/Runtime/EyeExplodeView.cs` - added the `TransitionStarted` event (additive)
- `Scripts/Editor/Anatomy/EyeAnatomySceneUpgrader.cs` - 16 members `private` -> `internal`,
  no behaviour change
- `CarEngineAnimated - i4/Scripts/Enginei4.cs` - both sliders are now optional
- `CarEngineAnimated - i4/VirtualExhibition WR.unity` - rebuilt as the exhibit

### Next recommended task

Expose the three features the engine already has that the exhibit interface does not:
the transparency X-ray over 23 parts, the four tuning variations (Tuning 4 adds the
whole turbo assembly), and the twelve part toggles. All of it is still wired on the
disabled `Canvas - Engine (1)` - the methods are `Enginei4.EnableTransparency`,
`DisableTransparency` and `SetVariation(int)`. The X-ray in particular is the natural
companion to the teardown: pull apart to see what the pieces are, go transparent to see
where they sit inside the block. Note `SetVariation` currently early-returns unless the
matching toggle in `allTogglesType` is on, so it needs a small refactor to be driven
from anything but that canvas.

## Session 2026-09-25 - stylus, navigator, audio

**Run `Kmax/Eye Anatomy/Set Up Interaction Upgrades` before anything else.**
The scene objects this session added are authored by that menu command, not
committed as YAML. It is idempotent. Without it the new scripts are present but
unwired and the exhibit behaves as it did before.

What changed, and why the headline bug was what it was:

- **There was no stylus in the scene.** Not misconfigured - absent. `XRRig.prefab`
  has only `Camera`, `left` and `right`, and the SDK's `pen.prefab` was never
  instanced. The `EventSystem` was also on a stock `StandaloneInputModule`.
  UI buttons worked with the pen only because the Kmax driver moves the OS
  cursor. decisions.md has the full diagnosis.
- **The pen now exists** as `XRRig/AnatomyPen`, with `KmaxInputModule` on the
  `EventSystem`. Its three buttons map to select/orbit, reset and dolly.
- **The model now has colliders**, fitted at runtime per catalogued part. The
  beam lands on the eye and each structure is directly selectable.
- **Next / Back navigator** steps the catalog and flies the camera.
- **Audio** is synthesised at startup, with an override slot per sound.
- **Particles** gained near and far depth layers, a popup ring and rise sparks.

## Read this first - the renderer

`URPAsset`'s renderer **must** be `URPAssets/URPAsset_ForwardRenderer.asset`
(a `UniversalRendererData`). It shipped pointing at `URPAsset_Renderer.asset`,
which is a **`Renderer2DData`** - URP's 2D renderer, which discards every 3D
light. The entire exhibit was rendering unlit, as albedo times ambient.

If the model ever goes flat again, check this first. The setup command checks it
on every run.

## Read this second - post-processing

`VRRenderer` builds the `left` and `right` cameras at runtime **without a
`UniversalAdditionalCameraData`**, so URP defaults them to
`renderPostProcessing = false` and they skip every volume in the scene. Setting
it on the authored root camera does nothing, because the SDK disables that
camera's own `Camera` component.

`ExhibitPostProcessing` on `EyeAnatomyExhibit` fixes this at runtime. **If the
image ever goes flat-white and over-contrasty again, check that component is
present and that the sub-cameras report `renderPostProcessing = true`.** Without
the tonemapper the model's near-white albedo clips as soon as the lighting is
strong enough to read, and the only way to avoid that is a rig so dark
everything looks muddy.

The light rig - key 1.05, fill 0.50, rim 0.60, ambient 0.55 - is calibrated
against the tonemapper. `Specular Point` and `Front Fill` are deliberately left
in the scene **disabled**; re-enabling the point light alone clips a quarter of
the frame, because it sits 0.13 m from a model 0.1 m across.

## Session 2026-09-25 (latest) - engagement features

Three additions aimed at giving the exhibit a reason to hold attention:

- **`ExhibitAttractMode`** tours the structures after 30 s idle and yields on any
  input. It deliberately does not reset the view on wake, and deliberately does not
  expand the eye itself - see the warning in architecture.md, because doing so
  silently kills every camera flight.
- **Stereo pop-out**: `EyeFocusView.focusPopOut` floats a focused part 0.10 m in
  front of the glass. Driven by camera distance, not part position - moving
  `FocusAnchor` does nothing, because the rig transform is the screen plane.
- **Wrist turn**: `AnatomyStylusInput.orbitMode` defaults to `WristTurn`, applying
  the pen's own rotation to the view one for one.

**Measured:** camera distance 0.400 during focus, a 0.100 m pop-out. Attract tour
confirmed stepping 1/18 then 2/18 with the camera flying to each.

**Not verified:** the wrist turn and the pen-movement wake, both of which need a
tracked pen. If the wrist turn feels reversed on device, flip `invertWristTurn`; if
it feels heavy, lower `wristTurnGain`. The 0.10 m pop-out was chosen on geometry, not
on comfort - too much pop-out causes eye strain over a long session, so it wants a
pass with a real viewer at the real viewing distance.

## Session 2026-09-25 (later) - verified in Play mode

The setup command has now been run and the scene saved, and the whole flow was
exercised in Play mode over the Unity MCP bridge. What that turned up and fixed:

- **Two pens.** The SDK's `pen.prefab` had also been dropped into the rig, so two
  `KmaxStylus` instances were registered under the same pointer id and every
  press dispatched twice. The setup command now removes any stylus that is not
  `AnatomyPen`.
- **The info panel was being cut off** by the model, because the canvas is pinned
  0.5 m from the viewer while the camera sits 0.42 m from the model.
  `UiAlwaysOnTop` takes the interface out of the depth test.
- **`Lens` and `Tear film` are fixed** with a translucent stand-in material.
  Lighting alone did not do it, and a grey background made them worse.
- **Environment**: grey background, an invisible gradient sky for ambient and
  reflections, two added lights, lower ambient, fainter ghosts.

Confirmed on screen: interface draws over the anatomy, the Lens reads clearly,
the navigator and counter work, all four mote layers populate (~800 motes), and
the burst, ring and sparks all fire on selection. The audio pad loops and a
selection cue reaches the one-shot source.

> [!NOTE]
> Driving the Editor headlessly over MCP leaves it unfocused, so frames do not
> tick and animated transitions stall part-way. `Time.deltaTime` reads 0. This
> is an artefact of remote driving, not a bug - transitions run normally when
> the Game view has focus. Snap transforms directly if you need a settled frame
> for a screenshot.

**Still not run on Kmax hardware.** `KmaxStylus.Visible` is false without a
tracked pen, so the beam, the tip and all three buttons remain unexercised.

## Earlier state (2026-09-21)

`Scenes/EyeAnatomy.unity` is a working interactive exhibit on the Kmax stereo
rig: the eye loads assembled and centered at `(0, 0, 0)`, a button explodes it,
18 numbered badges (1 to 18) appear perfectly billboarded facing the viewer, and
clicking one triggers a radiant particle burst and frames that part on its own with
a name and a short description. Over 220 ambient motes float in 3D stereoscopic depth,
and the badges gently breathe to give the display an organic living presence.

Navigation controls:
- **Spherical Orbit Camera**: Powered by `ViewerFlyController.cs`. The camera always
  flies and rotates in orbit around the eye, locking the focal center at the dead center
  of the screen (`viewport = (0.5000, 0.5000)`) across all angles and distances.
  - **Mouse Drag (Left or Right Click)**: Orbits yaw and pitch with smooth damping; pitch
    clamped to `[-80°, +80°]`.
  - **W / S**: Flies forward / backward in orbit (dollies toward or away from center).
  - **A / D**: Flies in an orbital circle left / right around the eye.
  - **Q / E**: Flies in an orbital arc down / up around the eye.
  - **Arrow Keys**: Orbit yaw and pitch.
  - **Left Shift**: 2.5x speed boost.
  - **Scroll Wheel**: Smoothly dollies in / out toward/away from center.
- **Centered Part Focus**: When any part is clicked, `EyeFocusView.cs` frames it at
  the exact center of the screen `(0, 0, 0)`, and the camera orbit remains centered on it.
- **Starting Pose Reset on Back**: Pressing "Back" exits focus and smoothly animates both
  the camera and model back to their exact 1st default starting position `(0, 0, 0)` over 0.45s.
- **Unified Reset**: Dedicated "Reset View" UI button and 'R' key smoothly restore starting view.

Active SDK backend: **Kmax XR Core 2.5.2** (`KMAX_AIO_K1` undefined).
Render pipeline: **URP**, via `URPAssets/URPAsset.asset`.

**Nothing has run on Kmax hardware.**

## Where things are

| Path | What |
|---|---|
| `Plugins/Kmax/com.kmax.xr.core/` | XR Core 2.5.2, default backend |
| `Plugins/Kmax/com.kmax.xr.aio/` | AIO K1 1.2.0, opt-in backend |
| `Scripts/Runtime/` | the exhibit (assembly `KmaxDisplayExample`) |
| `Scripts/Editor/KmaxSdkBackend.cs` | `Kmax/SDK Backend` menu, zero-reference assembly |
| `Scripts/Editor/Anatomy/EyeAnatomyPoseBaker.cs` | `Kmax/Eye Anatomy/Bake Explode Poses` |
| `Scripts/Editor/Anatomy/EyeAnatomySceneUpgrader.cs` | `Kmax/Eye Anatomy/Set Up Interaction Upgrades` - **run this first** |
| `Scripts/Editor/Anatomy/EyeAnatomyAssetFactory.cs` | stylus tip mesh + beam/tip materials, created on demand |
| `Data/EyeAnatomyCatalog.asset` | **the labels and descriptions - edit here** |
| `Data/EyeExplodePoses.asset` | baked assembled/exploded poses |
| `Model/EyeAnatomy.glb` | the model, imported by glTFast |
| `Prefabs/`, `Materials/` | hotspot marker |
| `Scenes/EyeAnatomy.unity` | the scene |

## Read this before changing anything

- **The catalog is the content.** Relabelling a part or rewording a description
  is an inspector edit on `EyeAnatomyCatalog.asset`. No code change.
- **Two labels are unverified.** `Medial rectus` / `Lateral rectus` may be
  swapped - it depends on whether the model is a left or a right eye, which the
  geometry does not settle. decisions.md has the measurements.
- **Scene coordinates.** The XRRig transform is the virtual screen. Viewer at
  Z `-0.5` looking along +Z, so **negative Z pops out of the display**. The rig
  is set to `Screen27` / 16:9, so the screen is **0.5977 x 0.3362 m**. Framing
  reads `XRRig.ViewSize` at runtime and follows this automatically; the one
  thing tied to it is the model's resting scale (`0.026037`), which must be
  re-derived if `ScreenType` changes. See architecture.md.
- **Badges stay up while a part is focused** and hold a constant 0.01 m
  on-screen size by counter-scaling against the model. Clicking another badge
  switches straight to that part.
- **`XRRig.ViewSize` follows the Game view aspect at runtime.** In a portrait
  editor Game view it reports portrait, and the focus framing adapts to it.
  That is correct behaviour, but it means editor framing will not match the
  device unless the Game view is 16:9.
- **Do not hand-tune the model transform or the poses.** Both are derived -
  the fit arithmetic is in architecture.md, the poses come from the bake menu.
- **Without hardware the pen is dormant, and that is correct.**
  `AnatomyStylusInput` gates on `KmaxStylus.Visible`, which is false when the
  tracker sees no pen, so the beam and its buttons do nothing in the editor and
  the mouse path drives everything. It is not evidence of a broken stylus.
- **A press can dispatch twice on the device.** The Kmax driver emulates the OS
  cursor *and* there is now a stylus pointer. `EyeHotspot` and `EyePartPicker`
  each drop a repeat click in the same frame; anything new that handles clicks
  needs the same guard.
- **Offscreen rendering needs the SRP path.** `Camera.Render()` draws nothing
  under URP; use `RenderPipeline.SubmitRenderRequest`.
- **Switching SDK backends** writes `KMAX_AIO_K1` into
  `ProjectSettings/ProjectSettings.asset` in the read-only base project.
  Selecting XR Core again removes it and restores the file exactly.

## What still needs a human

1. **Verify the stylus on the device.** The scene side is now confirmed in Play
   mode - one pointer, beam and tip built, colliders fitted - but the pen itself
   has still not met hardware, because `KmaxStylus.Visible` is false without a
   tracked pen. The three things most likely to need a tweak:
   - **Button order.** `PenTracker` labels the buttons 左/右/中 and this maps
     them front/rear/centre. If the physical order differs, the three indices on
     `AnatomyStylusInput` are inspector fields - swap them, no code change.
   - **Double-fire.** The driver emulates the mouse *and* there is now a stylus
     pointer, so a press can dispatch twice. `EyeHotspot` and `EyePartPicker`
     both drop a repeat click in the same frame, but if anything else in the
     scene turns out to double-fire, that is the cause.
   - **Orbit gain.** `orbitSpeed` is set to 0.25 deg/pixel to match the mouse.
     A wand held at arm's length may want less.
2. ~~Enable Read/Write on `Model/EyeAnatomy.glb`~~ - **not needed.** Measured in
   Play mode: 21 mesh colliders, 0 box fallbacks. The model imports readable and
   picking is already per-triangle. The fallback path in `EyePartColliders`
   stays for a future re-export that is not.

3. ~~Decide about `Lens` and `Tear film`~~ - **decided and done.** They are
   swapped onto `Materials/FocusHighlight.mat` while focused, a translucent
   glassy stand-in, because their own 58%-alpha grey cannot be made to read by
   any lighting. Verified in Play mode. decisions.md records why a translucent
   stand-in beat an opaque one.
4. **Review the two rectus labels** (above).
5. **Run it on a Kmax device.** Stereo output, head tracking and stylus input
   are all unverified end to end.
6. **Wire the scene into the build pipeline.** A `GameInfoSO.asset` appeared in
   this module during development but nothing references it, and the scene is
   not in `EditorBuildSettings`. Both live in the read-only base project.
7. **Decide whether the markers need to be always-on-top.** They are
   depth-tested today; roadmap.md has the supported URP route.

## If you add a game assembly definition

Reference the SDK by assembly name (`KmaxXR.Core`, `Kmax.InputModule`) and give
that asmdef the same `"!KMAX_AIO_K1"` define constraint, as
`KmaxDisplayExample` does - otherwise it stops compiling the moment someone
selects the AIO backend.

## Next recommended task

Review the catalog labels, then get it in front of the hardware.
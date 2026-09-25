# Tasks

## Session 12 (2026-09-25) - the other half of the lighting bug

Session 11 made the lights work; the result was ugly. One side blown to flat
white, the other crushed. Measured: **26.4% of the subject clipped above 0.97**.

### Two causes

- **The specular point light.** 0.13 m off-axis from a model about 0.1 m across,
  so inverse-square falloff made it a blowtorch on the near side. Disabling it
  alone took clipping from **26.4% to 0.1%**. It is kept in the scene disabled -
  a directional already gives a specular that slides as the camera orbits,
  because specular is view-dependent.
- **No tonemapper, for a reason session 11 got wrong.** With the point light
  gone the rig had to run so dark everything read muddy. Session 11 had tried a
  tonemapping `Volume`, seen no change, and concluded the SDK's stereo path
  could not be tonemapped. The real cause: **`VRRenderer` creates the `left` and
  `right` cameras at runtime without a `UniversalAdditionalCameraData`**, so URP
  defaults them to `renderPostProcessing = false` and they skip every volume.
  Setting it on the authored root camera does nothing either, because the SDK
  disables that camera's own `Camera` component.

`ExhibitPostProcessing` now adds the component and enables post-processing and
HDR on the sub-cameras once they exist. HDR matters as much as the flag - without
an HDR buffer, values clip before the tonemapper sees them.

### Result

Neutral tonemapping plus gentle bloom, and the rig back at normal exposure:
key 1.05, fill 0.50, rim 0.60, ambient 0.55, key:fill near 2:1. Measures
**0.00% clipped** with a subject mean of 0.37, against 26.4% clipped before.

The exploded view now reads properly - shaded sclera shells, warm interior,
vivid retinal vessels - and the rescued lens reads as a glassy body with a soft
halo rather than a flat sticker. Full pass over all 18 parts, both navigator
directions, reset and every audio cue with a clean console.

## Session 11 (2026-09-25) - the renderer was 2D, so nothing was ever lit

Prompted by "I think directional light has no effect on model". It does not, and
the cause turned out to be the largest single issue in this repo.

### Measured, not argued

Rendered the camera to a texture with every light on, then every light off.
Mean luminance was **identical to four decimal places - 0.3247 both ways** - and
the same for each light individually. Validated the instrument by swapping the
background colour, which moved it 0.0461 -> 0.9535. Dropped a stock URP Lit
sphere into the scene: it rendered as a **flat white disc with no terminator**,
ruling out the model's materials.

`URPAsset`'s `m_RendererDataList[0]` was a **`Renderer2DData`**. URP's 2D
renderer only handles `Light2D`; every directional and point light in the scene
was being discarded. The exhibit had been rendering as albedo times ambient.

Replaced it with a `UniversalRendererData`. The same measurement now gives a
delta of 0.0443 and the probe sphere shades correctly.

### What that invalidated

- **The lens diagnosis from session 10 was half right.** It was flat, and the
  geometry reading - a biconvex disc seen down its own axis - was accurate, but
  that was never the binding constraint. The lights were simply not applied.
  With the renderer fixed the lens shades properly and now reads as a glassy
  body with a real terminator. The translucent stand-in is kept; it looks right
  and stays closer to the source art.
- **Every light value in the scene was uncalibrated**, because raising a light
  had never done anything. Once lighting applied, the existing intensities blew
  the model out to solid white. Rebalanced to roughly a third of the previous
  total: key 0.24, fill 0.07, rim 0.13, specular point 0.12, ambient 0.24, with
  the front fill disabled outright. Budget the whole rig to about 0.7 total -
  the albedo is near-white and URP clips past 1.0.
- **Post-processing does not help.** Enabled HDR, assigned the missing
  `PostProcessData`, opted the stereo cameras in and added a global `Volume`
  with Neutral tonemapping. It changed nothing on the SDK's stereo path, so the
  volume was removed rather than left implying it works.
- **decisions.md's "custom shaders draw nothing" note is now suspect** and has
  been flagged in place. A 3D lit SubShader under the 2D renderer would produce
  exactly that symptom.

`EyeAnatomySceneUpgrader.UpgradeRenderPipeline` now checks the pipeline on every
run and swaps the renderer back if it regresses.

### Verified

Full pass over all 18 parts, both navigator directions, reset and every audio
cue with a clean console. One stylus pointer, ~825 motes across four layers,
interface drawing over the anatomy, and the Lens showing genuine 3D form.

## Session 10 (2026-09-25) - first Play-mode pass, with the Editor driven over MCP

The Unity MCP bridge became available this session, so for the first time the
work could be run and looked at rather than only compiled.

### What the running scene showed

Session 9's output was all present and working: audio looping, 725 motes across
three layers, all five buttons carrying `UiButtonMotion`, 18 badges with their
haloes, `AnatomyStylusInput` live. Two things were wrong, and one prediction in
the docs turned out to be false.

- **Two pens.** The SDK's `pen.prefab` had also been added to the rig, so two
  `KmaxStylus` instances were registered under pointer id 1000 and
  `KmaxInputModule` - which iterates every registered pointer - dispatched every
  press twice from two different poses. It was also throwing a
  `NullReferenceException` at `KmaxStylus.UpdateState` line 280 on every
  `EventSystem.Update`, because the second stylus had no resolved `IStylus`.
  The setup command now removes any stylus that is not `AnatomyPen`, and the
  console is clean afterwards.
- **The info panel was cut off** by any structure scaled up in front of it. The
  cause is geometric: `UIScaler` pins the canvas to the rig's screen plane,
  always 0.5 m from the viewer, while the camera orbits 0.42 m from the model.
- **Correction:** session 9 predicted the model's meshes were not marked
  Read/Write and that colliders would fall back to boxes. Measured: **21 mesh
  colliders, 0 fallbacks.** Picking is already per-triangle. The docs said
  otherwise and have been fixed.

### Lens and Tear film

The brief said these were invisible and guessed the cause was missing
environment reflections. Adding an environment did not fix them, and making the
background grey made them *worse* - a 58%-alpha grey over grey has no contrast
at all. The alpha is the problem, so `EyeFocusView` now swaps any part whose
materials are all under 0.75 alpha onto `Materials/FocusHighlight.mat`.

Getting that material right took measuring rather than guessing. An opaque
version was visible but read as a flat pale sticker, and neither lowering
ambient, re-aiming the lights, raising smoothness nor hiding the ghost shells
produced a terminator on it. Dumping the mesh settled it: 2345 normals spanning
175 degrees, so the geometry was never the limitation - the lens is a biconvex
disc seen down its own axis, where almost every visible normal points at the
camera. High smoothness made it worse, because a near-mirror reflecting a nearly
uniform grey sky returns the same value at every normal. A translucent stand-in
reads correctly and stays closer to the source art.

### Also changed

- Grey background at `RGB(0.165, 0.175, 0.205)`, kept dark because a focused
  part is seen through ~20 ghost shells that otherwise accumulate into haze.
- A grey gradient skybox for ambient and reflections that is never rendered, so
  the wet surfaces have something to reflect without raising stereo crosstalk.
- `Front Fill` and `Specular Point` added; ambient dropped to 0.42 and the
  ghosts to 0.028 alpha. The fill is faint and off-axis on purpose - the first
  version aimed it down the view axis and flattened everything facing the viewer.
- Button lift on hover and a press flash, on top of the existing punch.
- A fourth `MoteField_Foreground` layer well in front of the display: sparse and
  large, it contributes more parallax per particle than anything at model depth.

### Verified on screen

Interface drawing over the anatomy with the panel's full text readable, the Lens
clearly legible, navigator and counter working, ~800 motes across four layers,
and burst, ring and sparks all firing on selection. The pad loops and a
selection cue reaches the one-shot source.

**Not verified:** anything requiring a tracked pen. `KmaxStylus.Visible` is
false without hardware, so the beam, the tip and all three buttons are still
unexercised.

One gotcha worth knowing: driving the Editor headlessly leaves it unfocused, so
frames do not tick, `Time.deltaTime` reads 0 and animated transitions stall
part-way. That is remote driving, not a bug.

## Session 9 (2026-09-25) - the stylus, the navigator, audio and depth

### Diagnosed first

The brief opened with "stylus drag rotation is not working". It was not a
tuning problem. Searching `EyeAnatomy.unity` for the GUIDs of `KmaxStylus`,
`PenTracker` and `StylusRay` returned **zero hits in all three cases**, and
`XRRig.prefab` turned out to contain only `Camera`, `left` and `right` - the
SDK's `pen.prefab` was never instanced. The `EventSystem` was also running a
stock `StandaloneInputModule`, not `KmaxInputModule`.

The user confirmed on device that UI buttons *are* clickable with the pen, which
fits: the Kmax driver moves the OS cursor, and a cursor plus
`StandaloneInputModule` is enough to press a button but never produces a 3D
pointer. `ViewerFlyController.UpdateMouseOrbit` reads `Input.GetMouseButton`,
which that emulation does not reliably set - hence no orbit, no beam, nothing to
raycast. decisions.md records the full diagnosis; architecture.md's claim that
`XRRig/pen` existed has been corrected.

### Built

- **The stylus.** `XRRig/AnatomyPen` carrying `PenTracker` + `KmaxStylus`, with
  `KmaxInputModule` swapped onto the `EventSystem` (it derives from
  `StandaloneInputModule`, so the mouse path is untouched).
- **`AnatomyStylusInput`** maps the pen's three buttons - the whole budget, read
  as `IStylus.GetButton(0..2)`: front selects and orbits on a drag, rear taps to
  reset, centre holds to push-pull dolly. Drag deltas come from a point
  projected at a *fixed* distance along the ray, not from the hit point, which
  would jump at every silhouette edge.
- **`AnatomyStylusBeam`** replaces the SDK's `StylusRay`: a tapered
  `LineRenderer` ending in a cone whose apex sits on the hit point and whose
  body stands out along the surface normal, recolouring idle/hit/press, with a
  haptic pulse on hit-enter.
- **`EyePartColliders` + `EyePartPicker`** fit colliders to every catalogued
  structure at build time and make the geometry itself selectable. Mesh
  colliders where the mesh is readable, bounding boxes where it is not - the
  glTFast import does not enable Read/Write, so today it is boxes and one
  warning says so.
- **Next / Back navigator** with a `n / 18` counter, stepping the catalog and
  flying the camera to a viewpoint on each structure's own side of the eye.
  `ViewerFlyController` gained `FlyTo`, `AddOrbitDelta` and `AddDollyDelta`, and
  its reset was generalised into one eased flight that any input can interrupt.
- **Badge motion**: staggered pop-in with overshoot, hover lift and scale, press
  punch, and a selection halo pinging outward on a loop.
- **`UiButtonMotion`** on every UGUI button - on a stereo panel scale is the only
  hover cue that survives being looked at from an angle with two eyes.
- **Particles**: near and far mote layers for genuine parallax depth, a popup
  ring and rise sparks on selection, and a brief swell of the whole ambient
  field so the volume acknowledges an interaction.
- **Audio**: `ProceduralAudio` synthesises a seamless 16 s pad plus cues for
  hover, select, navigate, back, expand, collapse and reset;
  `AnatomyAudioDirector` plays them with ducking and an override slot each.
- **`Kmax/Eye Anatomy/Set Up Interaction Upgrades`** authors all of the scene
  side idempotently, because the alternative was hand-editing 12,000 lines of
  YAML keyed by file IDs.

### Verified

Both assemblies compiled against the project's real Unity reference assemblies
and `Library/ScriptAssemblies` - `KmaxDisplayExample` and
`KmaxDisplayExample.Anatomy.Editor`, clean, no warnings.

**Not verified:** nothing in this session has been run in Play mode or on
hardware. The menu command has not been executed. ai_handoff.md lists the three
settings most likely to need adjusting once it meets a real pen.

## Session 8 (2026-09-21) - badges persist while focused, ghost contrast, doc reconciliation

### Verified first, before changing anything

Re-ran the whole flow in Play mode rather than trusting the session 4-7 notes.
Console clean, 10 scene roots, rig on `Screen27` (`ViewSize 0.5977 x 0.3362`),
model at scale `0.026037`, closed by default, `MoteField` at 223 particles.
Focusing Sclera gave **opaque = 1, ghosted = 20, hidden = 0** with the part
centred at exactly `(0, 0, 0)` and a `TextMeshProUGUI` panel - so translucency,
TMP, centring and particles are all genuinely live.

### Fixed & Improved

- **Badges no longer vanish when a part is focused.** This was the last open
  item in roadmap.md's "Possible improvements": switching parts previously
  meant a trip through Back.
  - `EyeAnatomyController.OnExplodeTransitionCompleted` now keys badge
    visibility off `expanded` alone, not `expanded && !IsFocused`.
  - `OnHotspotClicked` deselects the previously selected badge and no longer
    hides the set, so clicking a second badge switches straight to it.
- **Badges hold a constant on-screen size.** The roadmap noted this needed
  per-frame counter-scaling, since focusing scales the model. Added
  `maintainWorldSize` + `worldDiameter` to `EyeHotspot`, which counter-scales
  against its parent's `lossyScale` each frame. The controller now calls
  `ConfigureSize(...)` instead of setting `localScale` once at build time.
- **Ghost contrast.** Roughly twenty ghost layers overlap and their alpha
  accumulates, so the ghosts were reading brighter than the focused part.
  `EyeGhost.mat` alpha `0.075` -> `0.040`, and a slightly darker, cooler tint.

### Verified in Play mode

- Badges active while focused: **18/18** (was 0).
- Badge world diameter `0.01000` m both before focus (model scale `0.026037`)
  and during it (`0.038`) - counter-scaling holds.
- Exactly one badge carries the gold selected colour at a time.
- Clicked Lens directly while Sclera was focused: lens re-centred to
  `(0.0000, 0.0000, 0.0000)`, panel switched to "Lens", opaque = 1 (lens),
  ghosted = 20, Sclera deselected. No Back needed.
- Console clean, scene saved.

### Found, not fixed

- `Lens` and `Tear film` both use the model's `Mat.1` - a textureless 91% grey
  at 58% alpha - so when focused they are nearly invisible whatever the ghost
  alpha is. This is the source art, not the isolation system; opaque parts like
  Sclera read clearly. Recorded in ai_handoff.md as a decision for a human.

### Documentation reconciled

`architecture.md` had drifted several sessions behind and actively contradicted
the build: it described a 15.6" screen, parts being *hidden* on focus, legacy
`Text` labels, a four-root scene, `FocusAnchor` at `x -0.07` and model scale
`0.015043`. Updated the scene table, coordinate convention, model fit, component
table, flow and focus sections to match what is actually in the scene, and
fixed the stale screen size in `ai_handoff.md`.

## Session 7 (2026-09-21) - camera orbit flight, centered part framing, and starting pose reset

Implemented spherical orbit camera flight locking the eye model at the dead center of the screen, centered all selected parts at (0, 0, 0), and added starting pose restoration upon pressing Back.

### Fixed & Improved

- **Continuous Camera Orbit Centering**:
  - Replaced FPS-style camera translation and in-place look with a spherical Orbit Camera model in `ViewerFlyController.cs`.
  - Math formula locks camera gaze directly at `focalCenter` (world `(0, 0, 0)`) across all angles:
    $$\mathbf{P}_{\text{rig}} = \mathbf{C}_{\text{focal}} + \mathbf{R} \times (0, 0, 0.50 - d)$$
    $$\mathbf{R}_{\text{rig}} = \text{Quaternion.Euler}(\text{pitch}, \text{yaw}, 0)$$
  - Verified mathematically: viewport point of the eye center is exactly `(0.5000, 0.5000)` and `Dot(camera.forward, toCenter) == 1.000000` at all orbit angles and distances.
  - Controls:
    - Mouse drag (Left-click or Right-click): Orbits yaw and pitch with velocity damping; pitch clamped to `[-80°, +80°]`.
    - W / S: Flies forward / backward in orbit (dollies toward/away from center).
    - A / D: Flies in an orbital circle left / right around the eye.
    - Q / E: Flies in an orbital arc down / up around the eye.
    - Arrow keys: Orbit yaw and pitch.
    - Left Shift: 2.5x speed boost.
    - Mouse scroll wheel: Dollies in / out.
- **Centered Part Framing**:
  - Re-anchored `FocusAnchor` in `EyeAnatomy.unity` from `(-0.125, 0, -0.02)` to `(0, 0, 0)`.
  - In `EyeFocusView.cs`, framing now brings the focused part's world bounds center to `(0.00, 0.00, 0.00)`, verified at viewport `(0.5000, 0.5000)`.
  - Resized `InfoPanel` from `(800, 300)` to `(650, 260)` and positioned it at `(-25, 0)`, leaving the entire central 65% of the screen unobstructed for the focused part.
  - Coordinated `flyController.SetFocalCenter(anchor)` so camera orbiting remains centered on the selected part.
- **Starting Pose Reset on Back**:
  - Updated `EyeAnatomyController.ReturnToOverview()` (invoked by clicking "Back") to call `flyController.ResetView(true)` and `manipulator.ResetTransform(true)`.
  - Smoothly restores camera rig position to `(0, 0, 0)`, rotation to `Quaternion.identity`, and distance to `0.5m` over 0.45s, returning the display to the exact 1st position when the app started.
- **Input Conflict Prevention**:
  - Added `disablePointerManipulation = true` to `EyeManipulator.cs` to prevent conflicting dual-rotation between camera rig and model pivot.

### Verified in Play Mode

- Overview camera orbit: Tested at multiple yaw (0°, 45°, 180°), pitch (-60°, 0°, +60°), and distance (0.35m, 0.5m, 0.8m) settings: viewport point of `(0, 0, 0)` is strictly `(0.5000, 0.5000)` and forward dot product is `1.000000`.
- Part selection: Selected Hotspot 4 (Lens); world bounds center landed at `(0.00, 0.00, -0.02)` with viewport `(0.5000, 0.5000)`.
- Back button: Clicked Back; camera returned to `(0.00, 0.00, -0.50)` with `(0, 0, 0)` rotation, rig to `(0, 0, 0)`, and pivot to `(0, 0, 0)`.
- Console completely clean with 0 errors.

Resolved camera flight controls with mouse and keyboard, and fixed numbered interaction badges so they always billboard upright facing the camera rather than facing downwards.

### Fixed & Improved

- **Upright Hotspot Billboarding**:
  - Diagnosed that `Camera.main` returned `null` because the root `Camera` object on `XRRig` is disabled by the Kmax SDK in favour of the active rendering camera `left`.
  - Because `_targetCamera` was null, `visualRoot.rotation` was never updated, causing badges to inherit their parent GLB bone/mesh pitch rotation of ~90° (`worldEuler ≈ (89.98, 180, 0)`), lying flat horizontally facing downwards towards the floor.
  - Added robust dynamic camera resolver `ResolveCamera()` in `EyeHotspot.cs` and `EyeAnatomyController.cs` that queries `Camera.main` and falls back to active/enabled cameras in `Camera.allCameras` (resolving `left`).
  - Added default fallback `visualRoot.rotation = Quaternion.identity` in `Awake()` and billboarding so markers never inherit parent bone tilt.
  - Verified in Play Mode: all 18 numbered badges billboard perfectly upright (`facingDot = 1.000`, `upDot = 1.000`) directly facing the camera.
- **Mouse & Keyboard Fly Camera**:
  - Enabled `ViewerFlyController` by default (`enableFly = true` in script and serialized `enableFly: 1` in `EyeAnatomy.unity`).
  - Unchained `Move()` in `ViewerFlyController.cs` so WASD and Q/E continuously fly the camera rig through 3D space whether right-click is held or not.
  - Added Left Shift key boost for 3x flying speed.
  - Added `CaptureAngles()` synchronization on `Input.GetMouseButtonDown(1)` and mouse position reset on `GetMouseButtonDown(2)` to eliminate look/pan angle jumps.
  - Separated mouse inputs: removed right-click and middle-click capture from `EyeManipulator.cs` so right-click is exclusively for fly-look and middle-click for fly-pan, while left-click remains dedicated to turntable model orbit.
  - Mapped scroll wheel dolly to right-click hold, preventing conflict with model zoom.
  - Connected `ViewerFlyController.ResetView()` into `EyeAnatomyController.ResetToHome()` so clicking "Reset View" or pressing 'R' restores both the camera rig and the model to their authored poses.

### Verified in Play Mode

- Expanding the eye shows all 18 numbered badges upright facing the camera with 1.000 alignment.
- WASD and Q/E fly the camera in 3D space; Left Shift boosts speed 3x.
- Right-click drag smoothly pitches and yaws the camera.
- Left-click drag orbits the model around its visual center.
- Resetting via 'R' or the "Reset View" button smoothly restores both camera and model to default home state.
- Console completely clean with 0 errors.

Replaced cyan plastic spheres with subtle numbered badges (1 to 18), added interaction particle bursts, and brought the 3D stereoscopic display alive.

### Fixed & Improved

- **Numbered & Subtle Interaction Badges**:
  - Replaced the sky-color spheres with circular badges featuring a dark translucent medical slate disc (`HotspotBadge.png`), soft cyan rim, and bold white `TextMeshPro` number (1 to 18).
  - Added camera billboarding so the numbers always directly face the viewer regardless of orbit angle.
  - Added subtle organic breathing pulse (`pulseAmplitude = 0.035f`, phase offset by `partIndex * 0.45f`) so the badges gently breathe, making the 3D display feel alive without being flashy or distracting.
  - Smooth hover scale expansion (1.18x) and gentle cyan highlight; warm golden highlight on selection.
- **Particle Burst on Any Interaction**:
  - Enhanced `AnatomyParticleDirector.PlayInteractionBurst(worldPosition)` with `burst.Emit(45)` for instantaneous, deterministic emission of 45 radiant golden stardust particles right at the clicked interaction point.
  - Wired in `EyeAnatomyController.OnHotspotClicked` to burst directly at `hotspot.transform.position`.
- **Living 3D Depth Ambience**:
  - Enabled `prewarm = true` on `MoteField` so over 220 delicate floating motes drift in stereoscopic depth (in front of, at, and behind the zero-parallax plane) from the moment the scene opens.

### Verified in Play Mode

- 18 numbered badges appear upon expanding, clearly numbered 1 ("Cornea") through 18 ("Inferior oblique").
- Badges billboard smoothly to face the camera during orbit and gently breathe.
- Clicking any badge instantly fires a 45-particle radiant burst at the tapped position and frames the part at `FocusAnchor`.
- 220+ ambient motes float smoothly in 3D stereoscopic space.
- Console clean with 0 errors and 0 warnings.

## Session 4 (2026-09-21) - rotation centering, turntable orbit, and unified reset

Fixed rotation drift, flipping/upside-down controls, focus pivot drift, and missing/inconsistent reset behavior.

### Fixed & Improved

- **Center of Rotation**:
  - Offset `EyeAnatomy` inside `EyeModelPivot` to `(0.015024, 0.000562, 0.011255)` so the visual center of the eyeball globe (sclera + cornea + lens) lands at exactly `(0, 0, 0)`.
  - In overview / exploded mode, rotating `EyeModelPivot` now spins the eyeball directly in place with zero drift.
- **Focused Part Invariant Orbit**:
  - `EyeManipulator` dynamically tracks active focal points. When a part is focused, the orbit pivot shifts to the focused part at `FocusAnchor` (`(-0.125, 0, -0.02)`), rotating the inspected part in place without it flying off-screen.
- **Turntable Orbit with Pitch Clamping**:
  - Constrained pitch between `-75°` and `+75°` so the model can never flip upside-down.
  - Implemented true turntable yaw/pitch rotation with smooth velocity damping.
- **Smooth Damped Pan & Zoom**:
  - Added middle/right-click dragging to pan within bounded screen extents.
  - Added scroll wheel to dolly/scale the model between 0.6x and 2.0x.
- **Unified Animated Reset**:
  - Added `ResetTransform(bool animated = true)` on `EyeManipulator` that smoothly lerps rotation, pan, and zoom back to default forward-facing view over 0.45s.
  - Added `ResetToHome()` on `EyeAnatomyController` that cleanly exits focus, restores all materials, clears the info panel, re-enables hotspots, and resets model transform.
  - Added on-screen `ResetButton` ("Reset View") on the UI canvas next to `ExpandButton`.
  - Bound `KeyCode.R` as an instant shortcut for `ResetToHome()`.
- **Disabled Free-Fly Camera by Default**:
  - In `ViewerFlyController`, set `enableFly = false` by default so right-click/WASD no longer flies the camera into the dark void away from the stereo display window.

### Verified in Play Mode

- Overview mode: model orbits smoothly in center; pitch clamps at ±75° without flipping.
- Exploded mode: 18 hotspots active; parts rotate symmetrically around center.
- Focus mode: selecting a hotspot (e.g. Lens) frames it at `FocusAnchor`; rotating spins the lens in place (distance drift = 0.00000).
- Reset: clicking "Reset View" button or pressing 'R' smoothly restores overview, clears focus, restores materials, and returns globe to `(0, 0, 0)`.
- Console clean throughout.

## Session 3 (2026-09-21) - the interactive exhibit

Closed by default, expand on a button, 18 clickable points, per-part zoom with
a name and a short description.

### Added

- `Scripts/Runtime/` (new assembly `KmaxDisplayExample`, constrained
  `!KMAX_AIO_K1` like the rest of the module):
  - `EyeAnatomyCatalog.cs` - `EyePartDefinition` + the catalog ScriptableObject.
  - `EyeExplodePoseSet.cs` - `EyePartPose` + the baked pose ScriptableObject.
  - `EyeExplodeView.cs` - interpolates parts between assembled and exploded.
  - `EyeFocusView.cs` - frames one part and isolates it.
  - `EyeHotspot.cs` - clickable marker.
  - `AnatomyInfoPanel.cs` - name/description presentation.
  - `EyeAnatomyController.cs` - the flow.
  - `EyePartBounds.cs` - measures a part while excluding its marker.
- `Scripts/Editor/Anatomy/EyeAnatomyPoseBaker.cs` in its own assembly, so the
  SDK backend switcher's zero-reference assembly stays that way.
- `Data/EyeExplodePoses.asset` - 23 parts baked.
- `Data/EyeAnatomyCatalog.asset` - 18 labelled parts.
- `Prefabs/EyeHotspot.prefab`, `Materials/HotspotMarker.mat`.
- Scene: world-space `UI` canvas (`KmaxUIRaycaster` + `GraphicRaycaster` +
  `UIScaler`), Expand/Back buttons, info panel, `FocusAnchor`,
  `EyeAnatomyExhibit`.

### Verified in Play mode

The stylus follows the mouse without hardware and fires its own clicks, so the
`EventSystem` was disabled and events dispatched with `ExecuteEvents` to get a
deterministic run.

- Start: `Expansion` 0, 18 hotspots inactive, panel and Back hidden, label
  "Expand eye".
- Expand: `Expansion` 1, bounds 0.084 -> 0.177 m, all 18 hotspots active.
- Every probed hotspot maps to the right entry - index, `selectedIndex` and
  panel title agreed for Cornea, Lens, Optic nerve, Superior rectus and
  Inferior oblique.
- Focus: model scaled 0.0150 -> 0.0671, part centred on the anchor at exactly
  50% of the view width, 20 of 21 part renderers hidden.
- Back: all 21 renderers restored, panel and Back hidden, hotspots returned.
- Close: `Expansion` 0, bounds back to 0.084 m, hotspots inactive.
- Console clean throughout.

### Bugs found and fixed while testing

1. **Info panel never appeared.** The panel starts inactive, so Unity deferred
   `AnatomyInfoPanel.Awake` until the first `Show()` - and `Awake` called
   `Hide()`, undoing the activation that had just triggered it. Setup is now
   lazy and nothing self-hides on wake.
2. **Focus framed the marker as well as the part.** Markers are parented to the
   part they label, so `GetComponentsInChildren<Renderer>` swept them in - the
   lens measured 165% deeper than it is. Extracted `EyePartBounds`, which skips
   markers, and used it in both call sites.
3. **Zoom buried the part it was zooming into.** Scaling the model so one part
   fills the view also scales its neighbours until they cover the screen.
   Focusing now hides everything else.
4. **Markers vanished after the pipeline switch.** See decisions.md - the
   custom shader was dropped for a stock URP one.

### Not a bug

An early test showed clicking `Hotspot_Lens` displaying "Sclera". With live
input disabled every hotspot mapped correctly; the mouse-driven stylus had
clicked a different marker between the dispatch and the read.

## Session 2 (2026-09-21) - first scene and eye model

- Created `Scenes/EyeAnatomy.unity`, added the `XRRig` via
  `GameObject/Kmax/Add XRRig`, an `EventSystem` with `KmaxInputModule`, and
  three directional lights.
- Examined `Model/EyeAnatomy.glb`: 21 meshes, 80,973 triangles, 9 materials,
  4 JPEG textures, 23 animation clips, no required extensions, authored
  11.79 x 9.04 x 11.99 m facing +Z.
- Fitted it: rotation `(0, 180, 0)`, uniform scale `0.015043`, bounds centred
  on the rig origin.

## Session 1 (2026-09-21) - SDK integration

- Vendored both Kmax SDKs under `Plugins/Kmax/`, reassigned five colliding
  `.meta` GUIDs on the AIO side, added `defineConstraints` to all six SDK
  assembly definitions and to `KMaxUnity.dll.meta`.
- Renamed `Samples~` to `Samples` (+ new asmdef) and AIO `docs` to
  `Documentation~`.
- Added `Scripts/Editor/KmaxSdkBackend.cs` and this `docs/` set.
- Verified both backends compile and that switching restores
  `ProjectSettings.asset` byte for byte.

## Next up

See roadmap.md. The first item needs a human: review the two rectus labels.
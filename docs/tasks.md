# Tasks

## Session 6 (2026-09-21) - fly camera controls and upright hotspot billboarding

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
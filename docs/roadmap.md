# Roadmap

## Done

- [x] **Volvo S90 Lighting & Post-Processing Overhaul.** Resolved interior blowout when starting
      the car or turning on lights. Scaled cabin courtesy light to 0.0008f intensity, separated
      warm ambient console glow from crisp LCD dashboard backlights, focused headlights to 44° cone
      angled downward, persisted Tonemapping & Bloom on `AnatomyPostFX.asset`, and added ghost material
      cleanup on exit.
- [x] **Unified 4-Scene Exhibit Suite & Interactive Launcher.** Built `Scenes/Launcher.unity` with
      interactive cards, dynamic rotating 3D background model preview, and pop-out depth. Added
      seamless cross-scene cyclical switcher (`ExhibitSceneSwitcher`) connecting all exhibits.
- [x] **Continuous Ambient Audio Stream.** Set up persistent `DontDestroyOnLoad` audio director,
      providing unbroken soothing background music across scene switches. Replaced outdated sound
      effects with modern synthesized micro-interaction cues.
- [x] **Glassmorphic UI Miniaturization & Non-Blocking Sorting.** Shrunk interfaces by 50%, pinned
      controls to top-left and info panels to top-right, added smooth hover/press spring motion
      (`UiButtonMotion`), and enforced `UiAlwaysOnTop` canvas rendering to eliminate raycast occlusions.
- [x] **4-State Stylus Interaction System.** Equipped Kmax Stylus with dynamic beam color states:
      Calm Cyan (primary laser), Amber Orange (Back/Reset), Purple Magenta (Tertiary), and Emerald Green
      (active interactive element confirmation).
- [x] **Hotspot Pinning & Double-Sided Rendering.** Anchored hotspot badges tight to part geometry
      (2–5 cm) without rotational swing, clamped badge height above the showroom floor, and enforced
      global double-sided rendering (`_Cull Off`) across all 3D assets.
- [x] **Volvo showroom and interface pass.** A lit floor with a window gobo on the key
      light, a real cast shadow, a studio cubemap for the paint to reflect, and clear-coated
      paint on Complex Lit. Six generated icon glyphs on the car's controls. The supplied
      S90 start and idle recordings, and a background track. Clipping measured down from
      14.43% of frame to 4.29%.
- [x] **Volvo interior focus.** 31 cabin meshes gathered under one `Interior` node that
      `EyeFocusView` frames and ghosts the rest of the car around. Focus-and-ghost, as
      planned - the camera cannot enter the cabin at exhibit scale.
- [x] **Volvo paint and interior trim swatches.** `VehicleFinishSwatches` on
      `ExhibitFeaturePanel` / `IExhibitMachinery`: five paints on one body material, four
      trim colourways across eight interior materials, written to per-renderer copies so
      the project's own `.mat` files are never repainted.
- [x] **Volvo guided tour.** Eight stops round the car, numbered badges pushed clear of
      the bodywork, Next/Back, info panel and idle attract loop - the shared runtime.
- [x] **Volvo presentation pass.** Cabin lamp brought down from a floodlight, swatches
      turned into colour chips, info panel text made to shrink to fit, a generated studio
      cubemap for the paint to reflect, the car turned three-quarter on to the viewer, and
      the idle retuned towards the engine an S90 actually has.

- [x] **Volvo S90 exhibit**: four doors cut from their mirrored meshes and hinged, plus
      sunroof; seven lamp channels; a staged ignition with synthesised
      start and idle audio; glass made transparent. 6.5M triangles across both eyes runs
      at 105-164 fps on an RTX 3060, so no decimation was needed.

- [x] **Expose the engine's see-through view and its four build variants** on the
      exhibit interface, through `IExhibitMachinery` / `ExhibitFeaturePanel`.
- [x] Fix the magenta particles, the floating canvas and the unclickable interface that
      the first engine build shipped with.

- [x] **Convert `CarEngineAnimated - i4/VirtualExhibition WR.unity` to the exhibit
      stack.** Same runtime components as the eye; 19 authored teardown poses over 12
      catalogued assemblies, XR rig, stylus, colliders, world-space UI and attract tour.
      `Kmax/Engine Exhibit/Set Up Engine Exhibit` authors all of it.
- [x] Re-centre the engine teardown on the origin the rig orbits, solved rather than
      hand-tuned, so the offsets can be re-authored without re-deriving the framing.
- [x] Freeze the engine's procedural animation while it is pulled apart, so the
      connecting rods stop chasing pistons that are no longer there.

- [x] Vendor Kmax XR Core 2.5.2 and Kmax AIO K1 1.2.0 into the writable module.
- [x] Resolve the five GUID collisions between the two SDKs.
- [x] Make the two SDKs mutually exclusive via the `KMAX_AIO_K1` constraint.
- [x] `Kmax/SDK Backend` editor switcher.
- [x] Import the XR Core sample scenes and give them an assembly definition.
- [x] Verify both backends compile in Unity 6000.3.9f1.
- [x] First scene `Scenes/EyeAnatomy.unity` with an `XRRig`, an `EventSystem`
      running `KmaxInputModule`, and a three-point light rig.
- [x] Import `Model/EyeAnatomy.glb` and fit it to the virtual screen.
- [x] Bake the assembled/exploded poses out of the model's animation clips.
- [x] Closed-by-default eye, expand button, 18 clickable hotspots, per-part
      framing with a name and description panel.
- [x] Exercise the whole flow in Play mode.
- [x] **Put a stylus in the scene at all** - `XRRig/AnatomyPen` with
      `PenTracker` + `KmaxStylus`, and `KmaxInputModule` on the `EventSystem`.
      There was none; see decisions.md.
- [x] Stylus beam: tapered `LineRenderer` ending in a pointed cone aligned to
      the surface normal, recolouring on hit, with haptics.
- [x] Colliders on the model, so the beam lands on the eye and each structure is
      directly selectable.
- [x] Map the pen's three buttons: select/orbit, reset, push-pull dolly.
- [x] Next / Back structure navigator with camera flight to each part.
- [x] Badge pop-in, hover lift, press punch and selection halo.
- [x] Near and far mote layers, popup ring and rise sparks.
- [x] Synthesised ambient pad and one cue per interaction, each overridable.
- [x] `Kmax/Eye Anatomy/Set Up Interaction Upgrades` to author all of the above.

- [x] **Fix the render pipeline**: it was using URP's 2D renderer, which discards
      every 3D light, so the exhibit rendered unlit. Swapped to a
      `UniversalRendererData` and rebalanced the whole light rig against it.
- [x] **Get post-processing running on the stereo cameras.** `VRRenderer` creates
      them without a `UniversalAdditionalCameraData`, so they skipped every
      volume. With Neutral tonemapping and bloom the rig runs at a normal
      exposure and clipping went from 26.4% to 0.00%.
- [x] Run the setup command and exercise the whole flow in Play mode.
- [x] Remove the duplicate stylus that made every press dispatch twice.
- [x] Take the interface out of the depth test so the anatomy cannot cover it.
- [x] Make `Lens` and `Tear film` readable when focused.
- [x] Grey background, an invisible gradient sky for ambient and reflections,
      and two more lights.
- [x] Button lift, press punch and press flash.
- [x] A fourth, foreground mote layer for stronger parallax.

- [x] **Attract loop** - the exhibit tours itself after 30 s idle and invites a
      passer-by to take over.
- [x] **Stereo pop-out** - a focused part now floats 0.10 m in front of the glass
      instead of sitting at zero parallax inside the panel.
- [x] **Wrist turn** - the pen rotates the view one for one instead of by pixels.

- [x] **Drag-to-scale bounding box** - billboarded frame with four corner handles.
- [x] Fixed mouse orbit being suppressed over the model, a regression from adding
      mesh colliders plus a physics raycaster.
- [x] Softened and rate-limited the stylus vibration (device feedback).
- [x] Moved the pad off a 110 Hz root so panel speakers can reproduce it, and raised
      its level (device feedback).

## Next

### Kmax Showcase Suite - four new scenes, from scratch

Full design, milestones and task tracker in **[showcase-suite.md](showcase-suite.md)**.
Not an extension of any existing exhibit; a separate direct-manipulation stack beside the
catalogue-driven one. Ordered so each scene proves one more thing about the hardware, which
is also the client-demo script and roughly the order of increasing risk.

- [ ] **M0 - Ground truth on the hardware.** A half-day spike, and a gate on everything
      else. Measures whether the tracked pen tip and the rendered geometry actually occupy
      the same point to within a few millimetres. Nothing in this project has ever needed
      that to be true - the existing exhibits use the stylus as a ray-caster - so it is an
      assumption. If it fails, Stack and Probe do not survive as designed.
- [x] **M1 - Shared core.** `StereoVolume` (the 0.13 m pop-out / 0.30 m depth budget as a
      first-class object, derived from the SDK rather than hard-coded), `StylusTip`,
      `StylusGrab`, the scene lifecycle and a comfort overlay. This is the internal-reference
      deliverable, more than the scenes are.
- [x] **M2 - Bloom.** Motes drift out through the screen plane and burst on the pen tip.
      Cheapest scene; exercises the tip end to end before the grab system is built on it.
- [x] **M3 - Stack.** Blocks picked up by the pen tip, lifted out through the window and
      stacked on a platform at zero parallax. The headline scene: you cannot judge the
      placement without stereo.
- [x] **M4 - Probe.** Thread the tip along a tube without touching the wall. The purest test
      of co-location.
- [ ] **M5 - Reef.** Fish and coral filling the volume, one nosing out through the glass.
      The art-heavy one; ships stylised first.
- [ ] **M6 - Hub, attract loops, analytics, unattended run, docs.**

Estimate 16-23 working days, provisional until M0 lands.

### Carried over


- [ ] **Sharpen the window gobo.** It reads as light through a window, but the panes
      resolve at roughly tile scale on the floor. `cookieSize2D` is 0.15 m; a smaller tile
      or a larger cookie texture would give a finer pattern.
- [ ] **Exercise the whole exhibit on the Kmax hardware.** None of the stereo-specific
      judgements here have been checked on the device. The showroom floor is kept to a
      0.45 m radius specifically to avoid a window violation, and that is reasoning rather
      than observation until someone looks at it in stereo.
- [ ] **Decide whether the exhibit wants ambience back.** The drifting mote layers are off
      on the car - the floor is a better depth cue and they read as speckle over it - so
      there is nothing moving while nobody is touching it apart from the attract tour.
- [ ] Move `Volvo S90.blend` out of `Assets/`. It is 626 MB, Unity has failed to import it
      and retries on every refresh, which launches Blender headlessly against a file that
      is open.

- [ ] Put the twelve per-part toggles somewhere, or decide they are redundant. They are
      the last feature still stranded on the disabled canvas, and they are a different
      shape from the other two - twelve booleans rather than one state - so they want a
      list rather than twelve more buttons down the edge. Worth asking whether hiding a
      part still earns its place now that the engine pulls apart.
- [ ] Look at the 0.20-alpha see-through casing **in stereo**. A translucent shell in
      front of solid internals is exactly the kind of surface that is hard to fuse, and
      it has only been judged flat.
- [ ] Decide whether the four missing prefabs in the engine scene (`Small_Room_02`,
      `FlatScreenTV`, `_Level`, four `FAQ Object` instances) should be restored or
      stripped. They are inert but they log on every scene open.

- [ ] **Exercise the stylus on Kmax hardware.** The scene side is verified, but
      `KmaxStylus.Visible` is false without a tracked pen, so the beam, the tip, all
      three buttons, the wrist-turn orbit mode and the attract loop's pen-movement
      wake are all still unexercised. ai_handoff.md lists the settings most likely
      to need a tweak. If the wrist turn feels reversed, flip `invertWristTurn`; if
      it feels heavy, lower `wristTurnGain`.
- [ ] **Confirm the audio and vibration on the device.** Both were changed in
      response to device feedback and both were measured in the editor - dominant pad
      energy at ~414 Hz, vibration capped to one pulse per 0.25 s - but a measurement
      of the signal is not a judgement of how it feels in the room. If the pad is
      still thin, raise `padRootHz` further rather than the volume.
- [ ] **Tune the pop-out against a real viewer.** 0.10 m was chosen on geometry, not
      on comfort. Too much pop-out causes eye strain over a long session, and the
      right number depends on the panel and the viewing distance.
- [ ] **Review the anatomy labels in `Data/EyeAnatomyCatalog.asset`**, in
      particular `Medial rectus` / `Lateral rectus` - these depend on whether
      the model is a left or a right eye, which could not be determined. See
      decisions.md.
- [ ] Register the scene with the base project's build pipeline
      (`ViitorCloudGameInfoSO` / `EditorBuildSettings`) - a `GameInfoSO.asset`
      now exists in this module but nothing here wires it up.
- [ ] Tune framing if wanted: the closed eye currently fills about 45% of the
      screen height, the exploded one 70%. Both follow from one number,
      `screenHeight * 0.70` in the model fit (architecture.md).

## Possible improvements

- [x] ~~Hotspots hidden while a part is focused~~ - done in session 8. Badges
      stay up and counter-scale to a constant 0.01 m on screen, so clicking
      another badge switches straight to that part.
- [ ] Markers are depth-tested and can be occluded. For always-on-top, add a
      URP Render Objects feature with Depth Test = Always on a dedicated layer
      (`URPAssets/URPAsset_ForwardRenderer.asset` is writable here; the layer is
      not). `UiAlwaysOnTop` does the equivalent for the canvas by forcing
      `unity_GUIZTestMode`, which may be the simpler route for markers too.
- [ ] **Retry the custom hotspot shader.** decisions.md concluded hand-written
      URP shaders draw nothing in this project, but that was measured under the
      2D renderer, which would produce exactly that symptom for a 3D lit
      SubShader. The conclusion may simply be wrong.
- [x] ~~Six badges cluster near the middle in the exploded view~~ - worked
      around in session 9 rather than fixed. The Next / Back navigator reaches
      every structure regardless of whether its badge is occluded. Per-part
      marker offsets in the catalog would still be the proper fix if the badges
      themselves need to be reachable by hand.
- [x] ~~`Lens` and `Tear film` are nearly invisible when focused~~ - done in
      session 10. Any part whose materials are all below 0.75 alpha is swapped
      onto `Materials/FocusHighlight.mat` while focused. The call went to a
      translucent stand-in rather than an opaque one; decisions.md has why.

## Conditional

- **If the AIO K1 backend is ever selected**, its `ARClip.shader` /
  `DepthRender.shader` are Built-in RP CG shaders and need a URP port. The
  project is on URP now. The XR Core backend has no such issue.

## Not planned

- Running both SDKs simultaneously. See decisions.md.
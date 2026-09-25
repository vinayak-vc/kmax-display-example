# Roadmap

## Done

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

## Next

- [ ] **Exercise the stylus on Kmax hardware.** The scene side is verified, but
      `KmaxStylus.Visible` is false without a tracked pen, so the beam, the tip
      and all three buttons remain unexercised. ai_handoff.md lists the three
      settings most likely to need a tweak.
- [ ] **Review the anatomy labels in `Data/EyeAnatomyCatalog.asset`**, in
      particular `Medial rectus` / `Lateral rectus` - these depend on whether
      the model is a left or a right eye, which could not be determined. See
      decisions.md.
- [ ] Confirm on real Kmax hardware. Everything so far has been driven with the
      stylus disabled and events dispatched in code, because without the device
      the stylus follows the mouse and fires its own clicks.
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
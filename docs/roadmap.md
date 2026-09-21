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

## Next

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
      (`URPAssets/URPAsset_Renderer.asset` is writable here; the layer is not).
- [ ] Six badges cluster near the middle in the exploded view, where the
      nested shells all sit on one horizontal line. Per-part marker offsets in
      the catalog would spread them.
- [ ] `Lens` and `Tear film` are nearly invisible when focused - both use the
      model's translucent `Mat.1`. Needs a call on whether to override those
      two with an opaque or emissive material. See ai_handoff.md item 0.

## Conditional

- **If the AIO K1 backend is ever selected**, its `ARClip.shader` /
  `DepthRender.shader` are Built-in RP CG shaders and need a URP port. The
  project is on URP now. The XR Core backend has no such issue.

## Not planned

- Running both SDKs simultaneously. See decisions.md.
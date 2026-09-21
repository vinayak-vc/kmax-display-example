# AI Handoff

## Current state (2026-09-21)

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
  Z `-0.5` looking along +Z, so **negative Z pops out of the display**. Screen
  is 0.3454 x 0.1943 m at the default `Screen15_6` / 16:9.
- **`XRRig.ViewSize` follows the Game view aspect at runtime.** In a portrait
  editor Game view it reports portrait, and the focus framing adapts to it.
  That is correct behaviour, but it means editor framing will not match the
  device unless the Game view is 16:9.
- **Do not hand-tune the model transform or the poses.** Both are derived -
  the fit arithmetic is in architecture.md, the poses come from the bake menu.
- **Testing interaction needs the stylus out of the way.** Without hardware the
  stylus tracks the mouse and generates its own clicks, which will silently
  interfere. Disable the `EventSystem` and dispatch with `ExecuteEvents`.
- **Offscreen rendering needs the SRP path.** `Camera.Render()` draws nothing
  under URP; use `RenderPipeline.SubmitRenderRequest`.
- **Switching SDK backends** writes `KMAX_AIO_K1` into
  `ProjectSettings/ProjectSettings.asset` in the read-only base project.
  Selecting XR Core again removes it and restores the file exactly.

## What still needs a human

1. **Review the two rectus labels** (above).
2. **Run it on a Kmax device.** Stereo output, head tracking and stylus input
   are all unverified end to end.
3. **Wire the scene into the build pipeline.** A `GameInfoSO.asset` appeared in
   this module during development but nothing references it, and the scene is
   not in `EditorBuildSettings`. Both live in the read-only base project.
4. **Decide whether the markers need to be always-on-top.** They are
   depth-tested today; roadmap.md has the supported URP route.

## If you add a game assembly definition

Reference the SDK by assembly name (`KmaxXR.Core`, `Kmax.InputModule`) and give
that asmdef the same `"!KMAX_AIO_K1"` define constraint, as
`KmaxDisplayExample` does - otherwise it stops compiling the moment someone
selects the AIO backend.

## Next recommended task

Review the catalog labels, then get it in front of the hardware.
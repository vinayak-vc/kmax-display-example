# AI Handoff

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
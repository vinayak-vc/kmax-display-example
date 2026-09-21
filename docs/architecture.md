# Architecture

## Folder layout

```
Assets/Games/kmax-display-example/
  Plugins/Kmax/
    com.kmax.xr.core/          Kmax XR Core SDK 2.5.2 (vendored)
      Editor/                  KmaxXR.Core.Editor
      Editor Resources/        XRRig.prefab, pen.prefab
      Runtime/Scripts/         KmaxXR.Core
      Runtime/Scripts/Input/   Kmax.InputModule
      Runtime/Plugins/         native libs (x64, Android, Linux, macOS, WebGL)
      Samples/                 KmaxXR.Core.Samples (was Samples~)
    com.kmax.xr.aio/           Kmax AIO K1 SDK 1.2.0 (vendored)
      Editor/                  Kmax.XR.Editor
      Editor Resources/        KmaxVR.prefab, KmaxAR.prefab
      Runtime/                 Kmax.XR
      Runtime/Resources/       StylusLine.prefab, KmaxPenOne.asset (Resources.Load target)
      Documentation~/          HTML API docs, hidden from the AssetDatabase
  Scripts/Editor/              KmaxDisplayExample.Editor - backend switcher
  Scenes/EyeAnatomy.unity      the module's first scene
  Model/EyeAnatomy.glb         anatomical eye model (glTFast ScriptedImporter)
  docs/                        this documentation set
```

## Render pipeline - URP

`URPAssets/URPAsset.asset` + `URPAssets/URPAsset_Renderer.asset` drive the
project, and `XRRig.IsSRP` is true.

This changed mid-development. Earlier in the same session
`GraphicsSettings.currentRenderPipeline` was **null**: `GraphicsSettings.asset`
referenced a URP asset by GUID that no asset in the project carried, so Unity
was silently falling back to Built-in. The asset was then added under this
module, and the project switched to URP. Anything in this repo's history that
claims Built-in RP describes that earlier state.

What the switch changed in practice:

- glTFast reimported `EyeAnatomy.glb` and swapped all 9 materials from
  `glTF/PbrMetallicRoughness` (Built-in) to
  `Shader Graphs/glTF-pbrMetallicRoughness` (URP). No action needed - the
  importer follows the active pipeline on its own.
- The AIO K1 SDK's `ARClip.shader` / `DepthRender.shader` are Built-in RP CG
  shaders. They are now genuinely a problem, but only if that backend is
  selected - it is not the default.
- `Camera.Render()` no longer works for offscreen capture. Under an SRP the
  editor-side render path is
  `RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt })`.
  This only matters for tooling and screenshots, not for the exhibit.
- Hand-written Built-in CG shaders silently draw nothing. See decisions.md for
  why the hotspot marker ended up on a stock URP shader.

## SDK backend selection

Both SDKs declare `namespace KmaxXR` and both define `KmaxInputModule`,
`KmaxMenu`, `KmaxPointer`, `KmaxStylus` and `StylusDragable`. Compiling both at
once produces `CS0433` ambiguity in every assembly that references them, and
registers duplicate `Kmax/...` menu item paths.

They are therefore made mutually exclusive through a **single scripting define
symbol**, `KMAX_AIO_K1`, applied as an assembly definition constraint:

| Assembly | Define constraint | Compiles when |
|---|---|---|
| `KmaxXR.Core` | `!KMAX_AIO_K1` | symbol absent |
| `Kmax.InputModule` | `!KMAX_AIO_K1` | symbol absent |
| `KmaxXR.Core.Editor` | `!KMAX_AIO_K1` | symbol absent |
| `KmaxXR.Core.Samples` | `!KMAX_AIO_K1` | symbol absent |
| `Kmax.XR` | `KMAX_AIO_K1` | symbol present |
| `Kmax.XR.Editor` | `KMAX_AIO_K1` | symbol present |
| `KMaxUnity.dll` (native) | `KMAX_AIO_K1` | symbol present |

One symbol rather than two makes "both on" and "neither on" unrepresentable,
and makes Kmax XR Core the backend a fresh clone compiles with no project
setting required.

## Backend switcher

`Scripts/Editor/KmaxSdkBackend.cs` exposes:

- `Kmax/SDK Backend/XR Core 2.5.2` - removes `KMAX_AIO_K1`
- `Kmax/SDK Backend/AIO K1 1.2.0` - adds `KMAX_AIO_K1`

The checked item reflects the active build target. The symbol is written to
`NamedBuildTarget.Standalone`, `Android`, `WebGL` and `WindowsStoreApps`, plus
the active build target if it is not one of those, then flushed with
`AssetDatabase.SaveAssets()` so the choice survives a crash.

Its assembly definition `KmaxDisplayExample.Editor` declares **no references**
and `autoReferenced: false`. It therefore always compiles, even when the
selected SDK backend has an error - otherwise a broken backend would remove the
menu needed to switch away from it.

## Assembly graph (XR Core backend active)

```
Assembly-CSharp            ->  KmaxXR.Core  ->  Kmax.InputModule
KmaxXR.Core.Samples        ->  KmaxXR.Core, Kmax.InputModule, UnityEngine.UI
KmaxXR.Core.Editor         ->  KmaxXR.Core, Kmax.InputModule
KmaxDisplayExample.Editor  ->  (nothing)
```

Both SDK runtime assemblies keep `autoReferenced: true`, so `Assembly-CSharp`
picks up whichever backend is active without any per-backend game asmdef.

## Scene: `Scenes/EyeAnatomy.unity`

Four roots:

| Root | Contents |
|---|---|
| `XRRig` | SDK prefab instance. `Camera` (+`HeadTracker`, `VRRenderer`, `PhysicsRaycaster`) at local Z `-0.5`, `left`/`right` stereo cameras, `pen` (`PenTracker` + `KmaxStylus`) with ray and pointer visuals |
| `EyeAnatomy` | prefab instance of the `.glb`, fitted to the virtual screen |
| `Key Light` / `Fill Light` / `Rim Light` | three directionals; only the key casts shadows |
| `EventSystem` | `EventSystem` + `KmaxInputModule` |

### Coordinate convention

The XRRig transform *is* the virtual screen: the screen lies in the rig's local
XY plane at Z = 0, and the viewer sits at Z = `-0.5` looking along **+Z**. So
negative Z is in front of the display (content pops out toward the viewer) and
positive Z is behind it (content sinks into the display).

With the default `Screen15_6` / 16:9 setting the virtual screen is
**0.3454 m x 0.1943 m**.

### How the model is fitted

`EyeAnatomy` is placed by three deliberate values rather than by eye:

- **Rotation `(0, 180, 0)`** - the model's optical axis (retina -> cornea)
  points +Z as authored, and the viewer looks along +Z, so unrotated the eye
  faces away. 180 degrees about Y turns the cornea toward the viewer; the
  measured axis is then `(-0.081, 0.038, -0.996)`.
- **Uniform scale `0.015043`** - derived as
  `screenHeight * 0.70 / rawBoundsHeight` = `0.1943 * 0.70 / 9.0394`. The model
  is authored at roughly 12 m across. Result: 0.1774 x 0.1360 x 0.1803 m, which
  covers 51% of the screen width and 70% of its height.
- **Position** - offset so the renderer bounds centre lands exactly on the rig
  origin, i.e. on the zero-parallax plane. The model then extends 0.090 m in
  front of the screen and 0.090 m behind it. To bias it further out of the
  screen, move it toward -Z; keep the pop-out under about half the screen
  width (0.17 m) to stay comfortable.

Cameras clear to a solid dark colour rather than a skybox (the scene has none),
which also keeps ghosting down on a stereo panel. Ambient is flat, not skybox.

## The anatomy exhibit

`EyeAnatomyExhibit` carries three components; `UI` carries the world-space
canvas. Responsibilities are kept apart so none of them needs to know the
whole flow:

| Component | Knows about | Does |
|---|---|---|
| `EyeExplodeView` | the model + a pose set | interpolates every part between its assembled and exploded position; exposes `Expansion`, `SetExpanded`, `TransitionCompleted` |
| `EyeFocusView` | the model + the XRRig | frames one part and hides the rest; `Focus` / `ClearFocus` |
| `EyeAnatomyController` | all of the above + the catalog + the UI | the only class that knows the actual flow |
| `AnatomyInfoPanel` | two `Text` fields | shows a name and description |
| `EyeHotspot` | nothing | a clickable marker that raises `Clicked` |
| `EyePartBounds` | - | static helper; measures a part while excluding its marker |

Data lives in two ScriptableObjects under `Data/`:

- `EyeAnatomyCatalog.asset` - 18 parts, each a display name, a description and
  a transform path. **Editing this asset is the whole job** for relabelling or
  rewording; no code involved.
- `EyeExplodePoses.asset` - the assembled and exploded local position of each
  of the 23 animated parts, baked from the model's clips.

### The flow

1. `Start` - eye assembled (`Expansion` 0), hotspots inactive, info panel and
   Back hidden, button reads "Expand eye".
2. **Expand** - `EyeExplodeView` interpolates to the exploded pose over 0.9 s.
   On `TransitionCompleted` the controller activates the 18 hotspots.
3. **Hotspot clicked** - `EyeFocusView` scales and moves the model so that part
   fills half the view at the focus anchor, hides every other part, and the
   info panel shows its name and description. Other hotspots are hidden and
   Back appears.
4. **Back** - framing and visibility restored, hotspots return.
5. **Close eye** - hotspots hidden, model interpolates back to assembled.

### Why the explode is baked, not played

The `.glb` ships 23 clips, one per part. They are not a one-shot explode: the
parts collapse, spread, and collapse again over 2.53 s, so playing them gives a
pulse rather than an open/close. A single `Animator` state can also only drive
one clip, and these bind to 23 different paths.

`Kmax/Eye Anatomy/Bake Explode Poses` therefore walks the merged position
curves, finds the sample times where the parts are most tightly packed and most
spread out, and writes those two poses to `EyeExplodePoses.asset`. The runtime
just lerps between them - no `Animator`, no clips, no per-frame allocation, and
the transition is scrubbable and reversible. Re-run the menu item if the model
is ever re-exported.

### Focusing scales the model, never the camera

On a head-tracked stereo rig the camera belongs to the viewer; moving it fights
the head tracker and breaks the stereo geometry. `EyeFocusView` instead moves
and scales `EyeAnatomy` so the chosen part lands on the focus anchor at the
right size.

That has one consequence worth knowing: scaling the whole model up so a small
part fills the view also blows up its neighbours until they swamp the screen.
So focusing also hides every other part (`isolateFocusedPart`, on by default).
The focus anchor sits at x `-0.07` so the framed part stays clear of the info
panel on the right.
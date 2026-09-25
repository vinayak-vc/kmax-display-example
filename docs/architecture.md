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

`URPAssets/URPAsset.asset` drives the project and `XRRig.IsSRP` is true.

> [!IMPORTANT]
> Its renderer **must** be `URPAssets/URPAsset_ForwardRenderer.asset`, a
> `UniversalRendererData`. It previously pointed at
> `URPAssets/URPAsset_Renderer.asset`, which is a **`Renderer2DData`** - URP's
> 2D renderer, which only handles `Light2D` and silently discards every
> directional and point light. Under it the whole exhibit rendered as albedo
> times ambient: measured mean luminance with all lights on versus all lights
> off was identical to four decimal places, and a stock URP Lit sphere rendered
> as a flat white disc.
>
> The 2D asset is left in place rather than deleted, because the module's
> history references it. `EyeAnatomySceneUpgrader.UpgradeRenderPipeline` checks
> the pipeline on every run and swaps it back if it ever regresses.
>
> **Every light intensity in the scene is calibrated against the corrected
> renderer and the tonemapper.** Without post-processing the model's near-white
> albedo clips the moment lighting is strong enough to read.

### Post-processing

`PostFX` holds a global `Volume` on `URPAssets/AnatomyPostFX.asset`: Neutral
tonemapping plus gentle bloom.

The volume alone does nothing, because **`VRRenderer` creates the `left` and
`right` cameras at runtime without a `UniversalAdditionalCameraData`**. URP then
falls back to defaults where `renderPostProcessing` is false, so the only two
cameras that draw anything skip every volume in the scene. Setting it on the
authored root camera does not help either - the SDK disables that camera's own
`Camera` component.

`ExhibitPostProcessing` on `EyeAnatomyExhibit` adds the component and enables
post-processing and HDR on the sub-cameras once they exist, re-checking whenever
the camera count changes. HDR matters as much as the flag: without an HDR colour
buffer, values clip before the tonemapper ever sees them and tonemapping becomes
a no-op.

`Specular Point` and `Front Fill` are left in the scene **disabled**. The point
light sat 0.13 m from a model 0.1 m across and inverse-square falloff made it
clip a quarter of the frame on its own; the front fill was aimed down the view
axis, which flattens whatever faces the viewer. Both are kept so the rig records
what was tried. decisions.md has the measurements.

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

Ten roots:

| Root | Contents |
|---|---|
| `XRRig` | SDK prefab instance. `Camera` (+`HeadTracker`, `VRRenderer`, `KmaxPhysicRaycaster`, `AudioListener`) at local Z `-0.5`, `left`/`right` stereo cameras, and `AnatomyPen`. **Driven in orbit by `ViewerFlyController`** |
| `XRRig/AnatomyPen` | `PenTracker` + `KmaxStylus`. Child `Stylus` carries `AnatomyStylusBeam`, whose own children are `Beam` (`LineRenderer`) and `Tip` (the cone) |
| `EyeModelPivot` | parent of the model; what `EyeManipulator` turns and pans. Never scaled |
| `EyeModelPivot/EyeAnatomy` | the `.glb` instance; what `EyeFocusView` moves and scales |
| `Key Light` / `Fill Light` / `Rim Light` | three directionals; only the key casts shadows |
| `EventSystem` | `EventSystem` + `KmaxInputModule` |
| `UI` | world-space canvas: `ExpandButton`, `BackButton`, `ResetButton`, `PreviousButton`, `NextButton`, `PartCounter`, `InfoPanel` |
| `FocusAnchor` | where a focused part is brought to. At the origin |
| `EyeAnatomyExhibit` | `EyeExplodeView`, `EyeFocusView`, `EyeAnatomyController`, `EyeManipulator`, `ViewerFlyController`, `AnatomyStylusInput` |
| `Ambience` | `AnatomyParticleDirector` + `MoteField` + `MoteField_Near` + `MoteField_Far` + `MoteField_Foreground` + `FocusBurst` + `PopupRing` + `RiseSparks` |
| `Audio` | `AnatomyAudioDirector` and its two `AudioSource`s |
| `Front Fill` / `Specular Point` | two added lights; see **Environment** below |

> [!IMPORTANT]
> The `AnatomyPen` subtree, the navigator buttons, the `Audio` root and the
> extra particle layers are all authored by
> **`Kmax/Eye Anatomy/Set Up Interaction Upgrades`**, not by hand. Re-run it
> after a fresh clone or if any of them go missing; it finds before it creates,
> so running it twice is a no-op. See `Scripts/Editor/Anatomy/EyeAnatomySceneUpgrader.cs`.

### Stylus input

The pen reports **three buttons**, read through `IStylus.GetButton(0..2)` and
labelled 左 / 右 / 中 by `PenTracker` - front, rear, centre.
`AnatomyStylusInput` maps them:

| Index | Constant | Role |
|:---:|---|---|
| 0 | `KmaxStylus.StylusButtnLeft` | Select on a tap; orbit the view on a drag |
| 1 | `KmaxStylus.StylusButtnRigth` | Tap to reset the view |
| 2 | `KmaxStylus.StylusButtnCenter` | Hold and push or pull the pen to dolly |

`KmaxStylus.PrimaryKey` is set to `Left` so that index 0 is also the button the
input module treats as a click. It is not the SDK default (`Middle`), and it is
not cosmetic: `KmaxStylus.StateOf` swaps index 0 with the primary's index
whenever the primary is not `Left`, so any other value makes
`AnatomyStylusInput`'s button numbering and the input module's disagree about
which physical key is which.

Two input paths run side by side and neither is authoritative. The mouse path in
`ViewerFlyController` is unchanged; the stylus path calls into it through
`AddOrbitDelta` / `AddDollyDelta`. Both cancel any camera flight in progress, so
reaching for the view always wins over an animation.

Because the Kmax driver also emulates the OS cursor with the pen, a single press
can arrive twice - once as a stylus pointer event and once as a mouse event.
`EyeHotspot` and `EyePartPicker` both drop a second click in the same frame.

### Colliders on the model

The `.glb` imports with no colliders, so before this the stylus ray had nothing
to land on anywhere in the scene except the badges' own spheres.
`EyeAnatomyController` now calls `EyePartColliders.Fit` on each catalogued part
as it builds that part's badge, and attaches an `EyePartPicker` so the geometry
itself is selectable.

Mesh colliders are used where `Mesh.isReadable` allows; where it does not, a
`BoxCollider` sized to the mesh bounds stands in and one warning names the
importer setting. **Measured in Play mode: 21 mesh colliders, 0 box fallbacks** -
`EyeAnatomy.glb` imports readable, so picking is per-triangle and no fallback is
in play. (An earlier revision of this document predicted otherwise.)

### The interface is drawn on top

`UiAlwaysOnTop` on the `UI` canvas gives every graphic its own material with
`unity_GUIZTestMode` set to `Always` and a render queue of 4000.

This is necessary because `UIScaler` pins the canvas to the rig's screen plane,
always 0.5 m from the viewer, while the model sits at the orbit centre
`distance` away - and the default distance is 0.42 m. Any time the camera is
dollied closer than 0.5 m the model is genuinely in front of the interface.
No depth offset fixes it, because the dolly range is 0.12 m to 1.2 m and the
sign of the conflict changes across it. decisions.md has the reasoning.

`Material.HasProperty("unity_GUIZTestMode")` returns false - the property is
declared outside the shader's Properties block. Write it anyway; both
`UI/Default` and the TextMeshPro distance-field shaders read it.

### Environment

The cameras clear to a solid grey, `RGB(0.165, 0.175, 0.205)`. A grey gradient
skybox (`Materials/AnatomyEnvironment.mat`) is assigned for ambient and
reflections **only** - it is never rendered, because a visible sky raises the
average screen brightness and with it the stereo crosstalk.

Ambient is deliberately low (0.42) and `Front Fill` is deliberately faint and
swung off-axis. Both are the same lesson: an even fill aimed at the viewer
lights every visible surface to nearly the same value, which erases the shading
that gives the model its form. `Specular Point` exists to put a highlight on the
wet surfaces that slides as the view orbits.

### Coordinate convention

The XRRig transform *is* the virtual screen: the screen lies in the rig's local
XY plane at Z = 0, and the viewer sits at Z = `-0.5` looking along **+Z**. So
negative Z is in front of the display (content pops out toward the viewer) and
positive Z is behind it (content sinks into the display).

The rig is set to **`Screen27` / 16:9**, so the virtual screen is
**0.5977 m x 0.3362 m**. It was `Screen15_6` (0.3454 x 0.1943) earlier in
development, and anything quoting those numbers predates the change. Nothing
reads the size as a constant: `EyeFocusView` asks `XRRig.ViewSize` at runtime,
so framing follows whatever the rig is set to. Only the model's resting fit
below is baked against a specific size.

### How the model is fitted

`EyeAnatomy` is placed by deliberate values rather than by eye:

- **Rotation `(0, 180, 0)`** - the model's optical axis (retina -> cornea)
  points +Z as authored, and the viewer looks along +Z, so unrotated the eye
  faces away. 180 degrees about Y turns the cornea toward the viewer; the
  measured axis is then `(-0.081, 0.038, -0.996)`.
- **Uniform scale `0.026037`** - derived as
  `screenHeight * 0.70 / rawBoundsHeight` = `0.3362 * 0.70 / 9.0394`. The model
  is authored roughly 12 m across. Exploded result: 0.3070 x 0.2354 x 0.3121 m,
  which covers 51% of the screen width and 70% of its height. **Re-derive this
  if the rig's `ScreenType` changes** - it is the one number tied to it.
- **Local position `(0.01502, 0.00056, 0.01126)` inside `EyeModelPivot`** -
  offset so the visual centre of the eyeball globe sits on the pivot origin.
  Turning the pivot then spins the eye in place with no drift.

Cameras clear to a solid dark colour rather than a skybox (the scene has none),
which also keeps ghosting down on a stereo panel. Ambient is flat, not skybox.

## The anatomy exhibit

`EyeAnatomyExhibit` carries three components; `UI` carries the world-space
canvas. Responsibilities are kept apart so none of them needs to know the
whole flow:

| Component | Knows about | Does |
|---|---|---|
| `EyeExplodeView` | the model + a pose set | interpolates every part between its assembled and exploded position; exposes `Expansion`, `SetExpanded`, `TransitionCompleted` |
| `EyeFocusView` | the model + the XRRig | frames one part and **ghosts** the rest; `Focus` / `ClearFocus` |
| `EyeManipulator` | the pivot | turntable orbit, pan and zoom of the model, with pitch clamps and damping |
| `ViewerFlyController` | the XRRig root | spherical orbit navigation of the viewer; owns the `R` reset key and `FlyTo` |
| `AnatomyStylusInput` | the stylus + the fly controller | maps the pen's three buttons onto orbit, reset and dolly |
| `AnatomyStylusBeam` | the stylus | `IPointerVisualize`: draws the beam and places the tip on the hit surface |
| `EyeAnatomyController` | all of the above + the catalog + the UI | the only class that knows the actual flow |
| `AnatomyInfoPanel` | two `TextMeshProUGUI` fields | shows a name and description |
| `AnatomyParticleDirector` | the particle systems | layered depth motes + burst, ring and sparks on selection |
| `AnatomyAudioDirector` | two `AudioSource`s | the ambient pad and one cue per interaction |
| `ProceduralAudio` | - | static; synthesises the pad and the cues |
| `UiButtonMotion` | its own `RectTransform` | hover, press and idle motion for one UGUI button |
| `EyeHotspot` | nothing | a numbered, billboarded badge that raises `Clicked` and `HoverChanged`; holds its own on-screen size |
| `EyePartPicker` | nothing | raises `Picked` when one structure's geometry is touched |
| `EyePartBounds` | - | static helper; measures a part while excluding its badge |
| `EyePartColliders` | - | static helper; fits mesh or box colliders to a part |

Data lives in two ScriptableObjects under `Data/`:

- `EyeAnatomyCatalog.asset` - 18 parts, each a display name, a description and
  a transform path. **Editing this asset is the whole job** for relabelling or
  rewording; no code involved.
- `EyeExplodePoses.asset` - the assembled and exploded local position of each
  of the 23 animated parts, baked from the model's clips.

### The flow

1. `Start` - eye assembled (`Expansion` 0), badges inactive, info panel and
   Back hidden, button reads "Expand eye".
2. **Expand** - `EyeExplodeView` interpolates to the exploded pose over 0.9 s.
   On `TransitionCompleted` the controller activates the 18 numbered badges.
3. **Badge clicked** - a particle burst fires at the badge, `EyeFocusView`
   scales and moves the model so that part lands on the focus anchor, every
   other part is swapped to the ghost material, and the info panel shows the
   name and description. Back appears.
4. **Another badge clicked** - badges stay up while focused, so this switches
   straight to the new part: the old badge deselects, the new one goes gold,
   and the framing and panel follow. No trip through Back.
5. **Next / Back (navigator)** - steps through the catalog with wraparound and
   flies the camera to a viewpoint on that structure's own side of the eye. The
   counter between them reads `n / 18`. Pressing either with the eye closed
   opens it first, rather than appearing to do nothing.
6. **A structure touched directly** - `EyePartPicker` routes it to the same
   `SelectPart`, so pointing the pen at the sclera selects the sclera.
7. **Back** - materials and framing restored, camera and model animate back to
   the starting pose over 0.45 s.
8. **Close eye** - badges hidden, model interpolates back to assembled.
9. **Reset View / `R` / stylus button 1** - same restoration as Back, at any time.

All four routes into a selection converge on
`EyeAnatomyController.SelectPart(index, burstOrigin)`. The optional origin is
the difference between them: a badge or a structure the viewer touched fires the
full burst at that point, while a navigator press fires only the quieter ring -
a burst at a point nobody pressed reads as a glitch.

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
the head tracker and breaks the stereo geometry. `EyeFocusView` moves and
scales `EyeAnatomy` so the chosen part lands on `FocusAnchor` (the origin) at
the right size, and `ViewerFlyController` orbits the rig *around* that point
rather than translating it freely.

Two consequences shaped the design:

- **Zoom is capped.** Scaling the model so a small part fills the view also
  scales its neighbours until they swamp the screen. `maxZoomMultiplier`
  (2.5x the resting scale) bounds it, which keeps the rest of the eye on
  screen as context.
- **Other parts are ghosted, not hidden.** Every non-focused renderer is
  swapped to `Materials/EyeGhost.mat` (URP Lit, transparent, alpha `0.040`)
  and restored on `ClearFocus`. The alpha is deliberately very low because
  about twenty ghost layers overlap and their alpha accumulates.

**Known limitation:** two of the eighteen parts - `Lens` and `Tear film` -
use the model's own `Mat.1`, a textureless 91% grey at 58% alpha. Focused,
they are nearly invisible against the dark background no matter how faint the
ghosts are. The gold badge and the info panel still identify them. Fixing it
properly means giving those two parts an opaque or emissive material, which is
a change to the source art's look rather than to this code.
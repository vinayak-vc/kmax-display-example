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
  Scenes/EyeAnatomy.unity      the eye anatomy exhibit
  Model/EyeAnatomy.glb         anatomical eye model (glTFast ScriptedImporter)
  Data/                        catalogues and explode pose sets for both exhibits
  CarEngineAnimated - i4/      the i4 engine exhibit: scene, FBXs, materials, textures,
                               and Enginei4.cs, its procedural animation driver
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

### Scaling the model

`EyeScaleBox` drives scale through `EyeManipulator.SetZoom`, **never** by writing to a
transform. `EyeManipulator.ApplyTransform` rewrites `pivot.localScale` from its own
`_currentZoom` every frame, so a direct write is gone by the next one. Going through
the manipulator also means Reset View restores the scale along with rotation and pan.

> [!IMPORTANT]
> `ViewerFlyController.IsPointerOverUI` must test for a `Canvas`, not call
> `EventSystem.IsPointerOverGameObject()`. That reports any object the event system
> hit, and since the eye gained mesh colliders and the camera a physics raycaster it
> is true over the whole model - which silently stops mouse drag orbiting the model.
>
> Scale handles are 3D, so they fall on the orbit side of that test and are suppressed
> separately by the static `EyeScaleBox.SuppressViewDrag`. It covers hover as well as
> drag, because the orbit's drag threshold is smaller than the EventSystem's.

### Stereo pop-out

`EyeFocusView.focusPopOut` (0.10 m) sets how far a focused part floats in front of
the display glass. The controller flies the camera to
`EyeFocusView.FocusCameraDistance` rather than to a distance of its own.

**Pop-out cannot be produced by moving the part.** `ViewerFlyController` places the
rig at `focalCenter + forward * (0.5 - distance)`, and the rig transform *is* the
virtual screen. Moving `FocusAnchor` moves the screen plane with it, so the parallax
never changes. The gap between the two is exactly `0.5 - distance`, which makes the
camera distance the only lever.

Framing is compensated by the reciprocal of that ratio. It is computed against
`XRRig.ViewSize` on the assumption the part sits at the screen plane, so flying
closer magnifies it by `screenDistance / cameraDistance` - without the compensation,
raising the pop-out silently zooms in as well and `framingRatio` stops meaning
anything.

### Attract mode

`ExhibitAttractMode` opens the eye and tours its structures after 30 s idle, then
yields on any input without resetting the view.

> [!IMPORTANT]
> It must **not** expand the eye itself before calling `SelectNextPart`.
> `StepSelection` already opens the eye and queues the selection until the explode
> completes, and that completion is also when `CacheViewDirections` runs. Expanding
> first makes the controller take its immediate path, the directions are not ready,
> and `FlyToPart` returns early - so the tour advances but **the camera never
> moves**. This was the failure mode caught in Play mode on the first version.

The pen wake threshold cannot be zero: a tracked pen is never perfectly still, so
without a deliberate-movement threshold the exhibit could never go idle with a pen
lying on the desk beside it.

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

## Scene: `CarEngineAnimated - i4/VirtualExhibition WR.unity`

The i4 engine exhibit, built on the same runtime components as the eye by
`Kmax/Engine Exhibit/Set Up Engine Exhibit`.

```
EngineExhibit          EyeAnatomyController, EyeExplodeView, EyeFocusView, EyeManipulator,
                       ViewerFlyController, ExhibitAttractMode, AnatomyStylusInput,
                       ExhibitPostProcessing, ExhibitMachineryGate
  EngineModelPivot     EyeManipulator's pivot; never scaled
    Enginei4 (1)       EyeFocusView's modelRoot - scaled and moved to frame a part
      Enginei4         explode and catalogue root; carries the Enginei4 driver
  FocusAnchor
UI                     world-space canvas, 1920x1080 at 0.00017989584 = the virtual screen
XRRig / EventSystem / Ambience / Audio / PostFX / Key + Fill + Rim Light
Engine                 the original root - legacy canvas and point light, both disabled
```

Nothing under `Scripts/Runtime` is eye-specific: every component works off an injected
catalogue, pose set and model root. The engine needed one addition,
`ExhibitMachineryGate`, and sixteen build steps on `EyeAnatomySceneUpgrader` made
`internal` so the engine builder calls them rather than copying them.

### The engine has no animation clips

`Models/Enginei4.FBX` imports with `animationType=None` and no clips. The one
`Take 001` in `Engine_opt.FBX` is two keyframes over 0.033 s on the root, animating
rotation and scale only - an empty take, on an FBX the scene does not use. Everything
that moves is procedural code in `Enginei4.Update`, slaved to a single `RPM` float.

So `EyeAnatomyPoseBaker` has no input here and the teardown is **authored** instead, as
a direction-and-distance table in `EngineExhibitBuilder.GetTeardown`, written in the
model's native metres. 19 poses drive 12 catalogued assemblies - the valves travel with
the camshafts that open them and the plug leads with the plugs, but neither is an
assembly worth a badge.

> [!IMPORTANT]
> Offsets are converted root space -> world -> the part's parent space, never applied
> directly. `EngineBlock` and `CylinderHead` both carry a 270 degree rotation about X,
> so a local -Y offset on the oil pan sends it out of the *side* of the engine rather
> than off the bottom. Both conversions run through the same scale chain above the model
> root, so the exhibit's scale cancels and the table stays in native metres.

### The teardown is re-centred on the origin

An engine does not come apart symmetrically. Far more of it lifts off the top than drops
out of the bottom, and the gearbox travels half a model-length back, so the authored
offsets carry the whole model up and back as it opens - measured at 0.046 m up and
0.049 m back, enough to push the cam cover above a screen only 0.194 m tall while the
engine was still small enough to fit easily. `ViewerFlyController` orbits `focalCenter`
and resets it to `Vector3.zero`, so an off-centre teardown orbits empty space.

`SolveTeardownDrift` measures that drift and subtracts it. Two details are load-bearing:

- It **solves** rather than measuring once. The bounds are defined by whichever parts
  are furthest out, and shifting everything can hand that job to a different part.
- The correction is applied **only to groups directly under the model root**. It is a
  rigid shift, so nested steps inherit it through the hierarchy; applying it to a nested
  step as well moves that part twice - the oil pan once with the block it hangs off and
  again on its own account - and the solve chases a target it is itself moving.

The cost is that the engine block is no longer a fixed anchor and sinks slightly as the
engine opens. That buys a teardown that stays in frame and offsets that can be
re-authored without re-deriving the framing by hand.

Measured: assembled 0.137 x 0.077 x 0.160 m, pulled apart 0.240 x 0.155 x 0.218 m, both
centred on the origin to four decimals, using 69% of the screen width and 80% of its
height.

### The machinery freezes while the engine is apart

`ExhibitMachineryGate` disables `Enginei4` on the frame an explode starts and re-enables
it only once the engine is fully reassembled.

The procedural animation and the explode do not really collide - the driver only writes
rotations and the local positions of parts *below* the group nodes the explode moves.
What breaks is the connecting rods: `Rod1..4` aim at targets parented under the pistons,
so the moment the pistons travel away from the block the rods swing across the gap to
keep pointing at them.

Freezing also guarantees `Enginei4.Start` - which caches every piston and rod rest
position - can only run with the engine assembled. Started while apart, it would cache
the exploded pose as the rest pose.

> [!IMPORTANT]
> The gate holds the driver as a plain `MonoBehaviour`, not as `Enginei4`. That type is
> in `Assembly-CSharp`, which already references `KmaxDisplayExample`; naming it here
> would close a reference cycle and neither assembly would compile. Toggling `enabled`
> needs no type knowledge.

`EyeExplodeView.TransitionStarted` exists for this. `TransitionCompleted` is too late -
it fires once the parts have already flown apart.

### The original interface is disabled, not deleted

`Canvas - Engine (1)` is the only wiring for the transparency X-ray over 23 parts, the
four tuning variations and the twelve part toggles, none of which the exhibit interface
exposes yet. It is deactivated so that wiring survives.

> [!IMPORTANT]
> Deactivating it is **not** enough on its own. `Enginei4` writes its parent's local
> scale from `ZoomSlider` every frame, and a slider on a deactivated object is still a
> live reference - the null check passes and the write goes ahead. On the first frame of
> play the wrapper went back to the slider's value of 1 and the pulled-apart engine
> measured 2.24 m across a screen 0.345 m wide. The builder clears both slider
> references outright, and `Enginei4.Update` now treats them as optional.

### The interface is pinned, not overlaid

The canvas is world-space and carries the SDK's `UIScaler`, which rewrites its pose and
size every frame from the rig's screen plane. That is what makes it behave like an
overlay - square to the viewer, fixed on the panel - while staying geometry both eye
cameras render.

> [!IMPORTANT]
> A true `ScreenSpaceOverlay` canvas cannot be used on this rig. `VRRenderer` renders
> side by side, giving the left eye the viewport `(0, 0, 0.5, 1)` and the right eye
> `(0.5, 0, 0.5, 1)`; an overlay canvas ignores camera viewports and is drawn once across
> the whole framebuffer, so it would span both eye images and never fuse.
>
> The canvas must also be handed an **event camera**. Left empty, a world-space
> `GraphicRaycaster` falls back to `Camera.main`, and this rig has none - the SDK
> disables the rig camera's own `Camera` component and renders through the `left` and
> `right` sub-cameras it creates at runtime. Without it not one button is clickable, and
> nothing is logged.

### See-through and build variants

Both belong to `Enginei4` and were reachable only from the canvas it shipped with.
`ExhibitFeaturePanel` puts them on the exhibit interface, reaching the model through
`IExhibitMachinery`.

That interface is declared on the exhibit side and implemented on the model side because
`Enginei4` is in `Assembly-CSharp`, which already references `KmaxDisplayExample` - the
exhibit can never name that type. It is the heavier sibling of the trick
`ExhibitMachineryGate` uses, which only needs `enabled` and so can hold a plain
`MonoBehaviour`.

> [!IMPORTANT]
> The exhibit calls `ApplyVariation`, never `SetVariation`. The latter returns early
> unless the matching toggle in `allTogglesType` is on - and those toggles are on the
> disabled canvas - and it calls `ActivateAllObjects`, which enables every object with a
> MeshRenderer under the model. The hotspot badges are three MeshRenderers each and are
> meant to stay hidden until the engine is open, so one variation switch would reveal all
> twelve.

Focusing a part stands see-through down. `EyeFocusView` ghosts every part except the
focused one and restores the originals on the way out, so the casing ends up solid
whatever the panel does - only the flag and the label have to catch up. `FocusChanged`
exists for this.

Overlapping fades are cancelled. Each part's fade owns that part's material for the ten
frames it runs, so toggling off and straight back on left the older fade-in finishing
last and putting the opaque material back while the interface believed otherwise.

### Particles

Systems created from scratch are given `AnatomyMote.mat` explicitly. A `ParticleSystem`
added by `AddComponent` has no material and Unity draws that as solid magenta; the eye
never hit it because its scene already had a `MoteField` to clone. `RepairParticleMaterials`
also fixes any system an earlier run left without one, because every build step finds
before it creates and would otherwise skip them forever.

### Button colours

Buttons leave their `Image` white and take every colour from their own `ColorBlock`, and
the selected build variant is marked by writing `normalColor`. A `Selectable` with a
colour transition drives the target graphic's canvas renderer on each state change, so
anything written to `Image.color` survives only until the pointer next touches it.

### Lighting

Driven harder than the eye's - key 1.5, fill 0.7, rim 0.95 - because cast iron,
aluminium and blued steel are far darker and more specular than near-white tissue. The
rim matters more here than it does on the eye: it is what separates one dark metal part
from the dark metal part behind it once the engine is open and the silhouettes overlap.

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
| `UI` | world-space canvas: `ExpandButton`, `BackButton`, `ResetButton`, `PreviousButton`, `NextButton`, `PartCounter`, `AttractPrompt`, `InfoPanel` |
| `FocusAnchor` | where a focused part is brought to. At the origin |
| `EyeAnatomyExhibit` | `EyeExplodeView`, `EyeFocusView`, `EyeAnatomyController`, `EyeManipulator`, `ViewerFlyController`, `AnatomyStylusInput`, `ExhibitAttractMode`, `ExhibitPostProcessing` |
| `Ambience` | `AnatomyParticleDirector` + `MoteField` + `MoteField_Near` + `MoteField_Far` + `MoteField_Foreground` + `FocusBurst` + `PopupRing` + `RiseSparks` |
| `Audio` | `AnatomyAudioDirector` and its two `AudioSource`s |
| `EyeScaleBox` | `EyeScaleBox`; builds its frame and four handles at runtime |
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
| `AnatomyStylusInput` | the stylus + the fly controller | maps the pen's three buttons onto orbit, reset and dolly; orbit is wrist-turn by default |
| `ExhibitAttractMode` | the controller + the fly controller | tours the exhibit when idle, and gets out of the way on any input |
| `EyeScaleBox` | the manipulator + the model bounds | billboarded frame with four drag-to-scale corners |
| `EyeScaleHandle` | its owning box | one corner; reports hover and drag |
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

---

## Shared Suite Architecture

### 1. Unified Glassmorphic UI & Motion System

All scenes share a standardized visual design language and layout hierarchy:
* **Geometry & Sizing**: Interfaces are compact (scaled 50% down from initial wireframes) to prevent occluding the 3D focal assets. Controls are anchored to the top-left; contextual informational panels are anchored to the top-right; global scene switches and resets live in the lower corners.
* **Aesthetic**: Deep translucent slate glass backing (`#0f172a` at 70-85% alpha) with crisp cyan/white vector glyphs (`ExhibitIconFactory`), high-legibility TextMeshPro labels, and 1px borders.
* **Motion (`UiButtonMotion`)**: Buttons feature smooth cubic/elastic hover expansion (1.04x scale) and press absorption (0.94x scale with subtle Z depression), accompanied by procedural click and select audio feedback.
* **Non-Blocking Raycasts (`UiAlwaysOnTop`)**: All UI canvases render in screen-space / camera overlay with an explicit sort order above 3D geometry. This ensures pointer rays interact with UI without being intercepted by underlying 3D model colliders.

### 2. Audio Pipeline & Procedural Synthesis (`AnatomyAudioDirector`)

* **Persistent Music (`DontDestroyOnLoad`)**: The audio director instantiates as a singleton root (`AudioRoot`) and marks itself persistent via `Object.DontDestroyOnLoad()`. When switching between Launcher, Eye, Engine, and Volvo scenes, the soothing background music streams seamlessly without stutter, clicks, or resets.
* **Procedural Synthesis (`ProceduralAudio`)**: Button clicks, hover ticks, part selection bursts, expand/collapse hums, and automotive starters/idles are synthesized algorithmically via math equations (sine/square bursts, filtered noise bursts, low-pass sweeps) with subtle randomized pitch variation (±5%). This gives instant zero-latency feedback without bloat from loose WAV files.
* **Audio Ducking**: The audio director automatically ducks background music volume during speech, interaction bursts, or engine start sequences.

### 3. 4-State Stylus Interaction System (`AnatomyStylusBeam`)

The 3D stylus pointer provides continuous visual feedback through a dynamic `LineRenderer` beam and glowing tip:
1. **Resting / Button 0 (Primary)**: Calm cyan beam (`#2EA3FF`), indicating standard laser pointing.
2. **Button 1 (Back / Reset)**: Amber / orange beam (`#FFAA22`), indicating a back or reset action.
3. **Button 2 (Tertiary / Option)**: Purple / magenta beam (`#B844FF`), indicating an alternate tool or mode toggle.
4. **Interactable Selection (Active Hover / Press)**: Bright emerald green (`#33FF88`) whenever the stylus beam is aimed at a clickable button or 3D hotspot badge and the user initiates a selection. This gives immediate visual confirmation that the element under the crosshair is interactive.

### 4. Cross-Scene Navigation (`ExhibitSceneSwitcher`)

* Every scene contains a "Next Scene" navigation button built with `ExhibitUiFactory`.
* `ExhibitSceneSwitcher` loops cyclically through the build settings: `Launcher` → `EyeAnatomy` → `VirtualExhibition WR` (Engine) → `VolvoS90` → `Launcher`.
* Audio playback survives the scene load cleanly, and render pipeline parameters remain locked across scene transitions.

### 5. Interactive 3D Launcher (`ExhibitLauncherController`)

* `Scenes/Launcher.unity` hosts three interactive exhibit cards (Eye Anatomy, i4 Engine, Volvo S90) with high-resolution thumbnail artwork.
* **Live 3D Background**: Selecting any tile dynamically instantiates and animates that exhibit's 3D model in the background behind the menu.
* **Stereo Pop-Out**: The preview model rotates slowly (12°/s) and floats at a comfortable pop-out depth (+0.08 m) in front of the screen plane. The background preview ignores direct raycasts so only the launcher UI receives clicks.
* A prominent **"Load Scene"** button emerges directly below the selected tile to enter the chosen exhibit.

### 6. Double-Sided Material Pipeline (`ExhibitPostProcessing`)

* In stereo cutaway and exploded views, single-sided meshes leave unnatural black holes on reverse faces.
* `ExhibitPostProcessing.ApplyDoubleSidedMaterials()` walks all active renderers and persistent materials across the project, setting `_Cull = Off`, `_BUILTIN_CullMode = Off`, `_CullMode = Off`, and `doubleSidedGI = true`.
* Executed at scene awake and as a permanent editor utility in `EyeAnatomySceneUpgrader.UpgradeMaterialsDoubleSided()`.

### 7. Automotive Lighting & Shader Architecture (`VolvoExhibitBuilder`)

* **Paint**: Body panels run on `Universal Render Pipeline/Complex Lit` with `_ClearCoat = 1`, `_ClearCoatMask = 1`, and `_ClearCoatSmoothness = 0.96`, reflecting a high-dynamic-range studio cubemap.
* **Cabin Courtesy Lighting**: Point light inside the cabin is scaled strictly to model dimensions (`intensity = 0.0008f`, `range = 0.039m`, warm tungsten `RGBA(1.0, 0.92, 0.82)`) to prevent internal blowout.
* **Dashboard Cockpit Displays**: Gauges and infotainment screens use dedicated emission channels (`Color(0.55f, 0.62f, 0.72f)`) simulating modern backlit LCD panels, separated from the soft overhead console lighting.
* **Headlights**: Dual forward spot lights are focused to a 44° cone with 22° inner spot, throwing 0.22m at `0.018f` intensity, angled 4° downward onto the showroom floor.
* **Post-Processing Persistence**: Neutral Tonemapping and Bloom components are registered via `AssetDatabase.AddObjectToAsset` on `AnatomyPostFX.asset`, preventing blown-out highlights and clipping.
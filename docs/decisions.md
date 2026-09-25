# Decisions

## 2026-09-25 - Post-processing never ran, because the stereo cameras have no camera data

**Finding, and the second half of the lighting story.** After the renderer was
corrected the model was lit but ugly: one side blown to flat white, the other
crushed. Measured, **26.4% of the subject was clipped above 0.97 luminance.**

Two causes, found in order:

1. **The specular point light.** It sat 0.13 m off-axis from a model about 0.1 m
   across, so inverse-square falloff made it a blowtorch on the near side.
   Disabling it alone took clipping from **26.4% to 0.1%**. It is kept in the
   scene, disabled, rather than deleted. A directional already gives a specular
   that slides as the camera orbits, because specular is view-dependent - the
   point light was never needed for that.

2. **No tonemapper.** With the point light gone the rig had to be run so dark
   that everything read muddy and desaturated, because the model's near-white
   albedo clips the moment lighting is strong enough to read. Adding a
   `Volume` with Tonemapping had already been tried in this session and appeared
   to do nothing, which is why the earlier entry below says the stereo path
   cannot be tonemapped. **That was wrong.** The real reason is that
   `VRRenderer` builds the `left` and `right` cameras at runtime **without a
   `UniversalAdditionalCameraData`**, so URP falls back to defaults where
   `renderPostProcessing` is false. The only two cameras that draw anything were
   skipping every volume in the scene. Setting it on the authored root camera in
   the inspector does nothing either, because the SDK disables that camera's own
   `Camera` component.

`ExhibitPostProcessing` now adds the component and enables post-processing and
HDR on the sub-cameras once they exist, re-checking when the camera count
changes. With Neutral tonemapping plus gentle bloom the rig runs at a normal
exposure - key 1.05, fill 0.50, rim 0.60, ambient 0.55 - and measures **0.00%
clipped** with a subject mean of 0.37.

Key to fill is held near 2:1. Wider crushes the shadow side of a sphere, which
is most of what the eye is. Tonemapping is Neutral rather than ACES: ACES pushes
saturation and contrast in a way that suits film, not an anatomical reference
where the tissue colours are the content.

## 2026-09-25 - The pipeline was using the 2D renderer, so nothing was ever lit

**Finding, and the most consequential one in this repo.** `URPAsset`'s
`m_RendererDataList[0]` pointed at a **`Renderer2DData`** - URP's 2D renderer,
which only handles `Light2D` and silently discards every directional and point
light in the scene. The whole exhibit had been rendering as albedo times
ambient.

Prompted by the observation "I think directional light has no effect on model".
It was measured rather than argued: the camera was rendered to a texture with
every light on and then every light off, and the mean luminance was identical to
four decimal places - `0.3247` both ways, for every light individually as well.
The measurement was validated by swapping the background colour, which moved it
from `0.0461` to `0.9535`, so the instrument was working. A stock URP Lit sphere
dropped into the scene rendered as a **flat white disc with no terminator**,
which ruled out the model's own materials.

After replacing the renderer with a `UniversalRendererData`, the same
measurement gives a delta of `0.0443` and the probe sphere shades correctly.

**What this explains, retroactively:**

- The model looked flat, and the focused `Lens` had no terminator no matter how
  it was lit. Earlier in this same session that was diagnosed as a geometry
  problem - a biconvex disc seen down its own axis - and a translucent stand-in
  was chosen partly on that basis. The geometry reading was correct but it was
  never the binding constraint; **the lights were simply not being applied.**
  With the renderer fixed the lens shades properly.
- Almost certainly the mystery recorded under *"Hotspot markers use a stock URP
  shader, not a custom one"*: a hand-written URP SubShader that compiled cleanly,
  reported `isSupported == true`, was correctly selected, and drew nothing. A 3D
  lit SubShader under the 2D renderer would do exactly that. That note should be
  revisited before anyone concludes custom shaders are unusable here.

**Consequence for every light value in the scene:** none of them had ever been
calibrated, because raising them did nothing. Once lighting applied, the
original intensities blew the model out to white.

> [!NOTE]
> The first attempt at this dropped the whole rig to about a third of its
> energy, on the reading that the stereo path could not be tonemapped. That
> reading was wrong - see the entry above - and the dark rig it produced looked
> muddy. The tonemapper is now running and the intensities are back at normal
> levels.

`EyeAnatomySceneUpgrader.UpgradeRenderPipeline` now checks for this and swaps
the renderer, so a re-run cannot silently go back to the unlit state.

## 2026-09-25 - Two stylus pointers sharing one id

**Finding.** After the first setup run the scene held two pens: the SDK's own
`pen.prefab` and this module's `AnatomyPen`. Every `KmaxStylus` registers under
`KmaxStylus.UniqueId` (1000), and `KmaxInputModule.ProcessStylusEvent` iterates
**every** registered `KmaxPointer` - so each press was raycast and dispatched
twice, from two different poses, under the same pointer ids.

It was also throwing. The console carried a `NullReferenceException` at
`KmaxStylus.UpdateState` line 280 - `pen.UpdatePose(stylus)` - on **every**
`EventSystem.Update`, because the second stylus had no resolved `IStylus`.
There was no matching "Can not find IStylus" error, which places it before that
component's `Start` rather than after it.

`EyeAnatomySceneUpgrader.RemoveDuplicatePens` now deletes any stylus under the
rig that is not `AnatomyPen`, and says so in the console. Afterwards the console
stays clean through a full pass over all 18 parts, both navigator directions,
reset and every audio cue. The per-frame click guards in `EyeHotspot` and
`EyePartPicker` remain, because the driver's mouse emulation is a separate
source of the same duplication.

## 2026-09-25 - The interface is taken out of the depth test

**Decision:** `UiAlwaysOnTop` gives every graphic on the canvas its own material
with `unity_GUIZTestMode` set to `Always` and a render queue of 4000.

**Why:** the SDK's `UIScaler` pins the canvas to the rig's screen plane every
frame, which is always exactly 0.5 m from the viewer. The model sits at the
orbit centre, `distance` away. Whenever the camera is dollied closer than 0.5 m
- and the default is 0.42 m - the model is physically in front of the interface
and occludes it. That is what was cutting the info panel's text off behind a
rectus muscle.

**Why not move the canvas forward:** the dolly range is 0.12 m to 1.2 m. An
offset big enough to clear the model at the near end would put the panel about
4 cm from the viewer's face, and no fixed offset covers the whole range anyway.
Depth cannot solve a conflict whose sign changes.

`unity_GUIZTestMode` is declared outside the shader's Properties block, so
`Material.HasProperty` returns false for it. That is expected and is not a
reason to skip the write - both `UI/Default` and the TextMeshPro distance-field
shaders read it. This was confirmed in Play mode before being committed.

## 2026-09-25 - Transparent parts get an opaque-enough stand-in

**Decision:** `EyeFocusView` swaps a focused part onto `FocusHighlight.mat` when
every one of its own materials is below `transparentAlphaThreshold` (0.75).
Today that catches exactly `Lens` and `Tear film`, which share the model's
`Mat.1` - a 91% grey at 58% alpha.

**Why this rather than lighting:** the obvious theory was that the parts had no
environment to reflect. Adding one did not fix them, and a grey background made
them *worse*, because a 58%-alpha grey over grey has no contrast at all. The
alpha is the problem, and only replacing it fixes it. ai_handoff.md item 0 had
been carrying this as a decision someone needed to make; this is that decision.

**Why the stand-in is translucent, not opaque:** an opaque version is certainly
visible but reads as a flat pale sticker. The lens is a biconvex disc viewed
down its own axis, so nearly every normal you can see points at the camera and
diffuse shading has nothing to grade across. Raising smoothness makes it worse,
not better - a near-mirror reflecting a nearly uniform grey sky returns the same
value at every normal. Letting the structures behind show through is what
actually reads as an optical body, and it stays closer to the source art. All of
this was measured in Play mode: the mesh has 2345 normals spanning 175 degrees,
so the geometry was never the limitation.

## 2026-09-25 - Grey background, but a dark one, and a sky that is never seen

**Decision:** cameras clear to `RGB(0.165, 0.175, 0.205)`. A grey gradient
skybox is assigned for ambient and reflections only - the cameras keep clearing
to a solid colour, so it never appears on screen. Ambient sits at 0.42 and
`EyeGhost.mat` drops to 0.028 alpha.

**Why the grey is dark:** a focused part is seen through about twenty ghost
shells, and lifting the background much past this accumulates them into a milky
haze that flattens the whole model. This is the point where the grey reads as
grey and the anatomy still has something to sit against.

**Why the sky is invisible:** the eye's wet surfaces need an environment to
reflect or they render dead matte, but a visible sky raises average screen
brightness and with it the crosstalk between the two eyes on a stereo panel.
Ambient is kept low for the same reason it is kept off the lens: an even fill
lights every surface to nearly the same value and erases the model's form.

## 2026-09-25 - The scene had no stylus at all

**Finding, not a decision.** "Stylus drag rotation is not working" had a simpler
cause than the name suggests: there was no stylus.

`XRRig.prefab` contains `Camera`, `left` and `right` and nothing else. The SDK's
`pen.prefab` - which carries `PenTracker`, `KmaxStylus`, `StylusRay` and a
`LineRenderer` - was never instanced into the scene, and a search of
`EyeAnatomy.unity` for those three script GUIDs returned zero hits. The
`EventSystem` was also running a stock `StandaloneInputModule`, not
`KmaxInputModule`.

That combination explains every reported symptom at once:

- **UI buttons worked with the pen** because the Kmax driver moves the OS mouse
  cursor. `StandaloneInputModule` plus the canvas `GraphicRaycaster` is enough
  to press a button from an emulated cursor, and this was confirmed on device.
- **Dragging never orbited**, because `ViewerFlyController.UpdateMouseOrbit`
  reads `Input.GetMouseButton(0)` and `(1)`, which the driver's cursor
  emulation does not reliably set.
- **There was no beam and no tip**, because there was no pointer to draw one for.
- **The ray had nothing to hit** either way: the model arrives from glTFast as
  mesh filters and renderers with no colliders, so the only colliders in the
  scene were the badges' own spheres.

Anything in this repo's history describing `XRRig/pen` describes a state that
was never in the scene file. architecture.md is corrected.

## 2026-09-25 - Stylus button mapping: select/orbit, reset, dolly

**Decision:** the pen's three buttons are mapped front to back as select and
drag-to-orbit, tap-to-reset, and hold-to-dolly. `KmaxStylus.PrimaryKey` is set
to `Left`.

**Why:** `IStylus.GetButton` takes `0..2` and `PenTracker` labels them 左 / 右 /
中 - front, rear, centre - so three is the whole budget and each should earn its
place.

- **0, front** carries select *and* orbit because it is the button a hand
  reaches first, and because a viewer's instinct with a wand is to press and
  drag as one gesture. Click and drag are separated by a travel threshold,
  exactly as the mouse path does it.
- **1** is reset. The request was for a button matching the Reset control, and
  tying the recovery action to its own key means a lost viewer never has to find
  a UI button while disoriented. It is edge-triggered on release with a tap
  timeout, so resting a thumb on it does nothing.
- **2, centre** is a push-pull dolly. Of everything left unmapped, zoom is the
  one thing a stylus user otherwise cannot do at all - the scroll wheel is a
  mouse-only affordance - and it is the control an anatomy exhibit needs most.

`PrimaryKey = Left` matters more than it looks: `KmaxStylus.StateOf` swaps index
0 with the primary's index whenever the primary is not `Left`. Leaving it on the
SDK default of `Middle` would mean `AnatomyStylusInput`'s button 0 and the input
module's "left button" referred to different physical keys.

## 2026-09-25 - Orbit from a fixed-distance aim point, not the hit point

**Decision:** `AnatomyStylusInput` derives its drag delta by projecting a point
a fixed `RayLength` along the pen's ray, rather than using
`KmaxStylus.ScreenPosition`.

**Why:** `ScreenPosition` projects `PointerPosition`, which is the *hit* point -
it collapses onto whatever the ray lands on. Crossing the silhouette edge of a
part changes the hit distance discontinuously, and the resulting jump in screen
position would fling the view. Projecting at a constant distance gives a signal
that only tracks where the pen is aimed, so the drag stays in screen pixels and
matches the mouse's feel without inheriting its geometry.

It also sidesteps a feedback loop: the pen is parented under `XRRig`, so both
the pen and the camera move together as the rig orbits and a stationary hand
produces a stationary aim point.

## 2026-09-25 - Colliders fitted at runtime, with a box fallback

**Decision:** `EyeAnatomyController` fits colliders to each catalogued part as
it builds that part's badge, via `EyePartColliders`. Mesh colliders are used
where the mesh is readable; where it is not, a box sized to the mesh bounds
stands in.

**Why runtime rather than baked into the scene:** the controller already walks
every catalogued part to place badges, so this is the one place that already
knows the answer. It also means a re-exported model needs no scene surgery.

**Why the fallback:** `MeshCollider.sharedMesh` cannot bake a mesh imported
without Read/Write access, and the glTFast importer settings on
`Model/EyeAnatomy.glb` do not enable it. Rather than have the feature fail
silently on the device, an unreadable mesh gets a bounding box - the beam still
stops on the eye and the part is still pickable, just less precisely - and one
warning names the importer setting that would fix it.

## 2026-09-25 - Next and Back navigate the catalog, and fly the camera

**Decision:** added `Next` and `Back` buttons that step through the catalog and
ease the camera to a viewpoint on the selected structure's own side of the eye.

**Why:** in the exploded view the nested shells occlude each other, and several
badges cannot be reached from most angles - roadmap.md already recorded six of
them clustering on one horizontal line. Without a navigator those structures are
unreachable unless the viewer happens to find an angle that exposes them, which
is not an interaction so much as a puzzle.

The flight angle is computed rather than authored per part: the rig sits at
`focalCenter + rot * (0, 0, -distance)`, so aiming the camera's offset direction
along the part's own direction from the eye's centre puts the camera on that
side. Pitch is damped to 60% because adopting the full elevation of a structure
high above the optical axis is disorienting. Directions are sampled from the
*exploded* pose - assembled, the parts are nested shells sharing one centre and
the directions carry no information.

## 2026-09-25 - Audio is synthesised, not shipped

**Decision:** `ProceduralAudio` builds the ambient pad and every interface cue
as `AudioClip`s at startup. `AnatomyAudioDirector` exposes an override slot for
each one.

**Why:** the module ships no audio assets and carries no licence question about
them, which matters for something meant to run in a public exhibit. The pad's
partials and tremolo rates are snapped to whole cycles across the loop so it
wraps without a click - that snap is the only non-obvious part of the synthesis
and the reason a 16 s loop does not tick.

The override slots mean replacing any of it with recorded audio is an inspector
edit, so this is a default rather than a constraint.

## 2026-09-25 - Scene changes go through a menu command

**Decision:** `Kmax/Eye Anatomy/Set Up Interaction Upgrades` authors the stylus,
the navigator, the audio rig and the extra particle layers into the scene, and
wires every reference. Every step finds before it creates.

**Why:** `EyeAnatomy.unity` is 12,000 lines of YAML keyed by file IDs, and the
changes needed span five roots, a prefab and two new materials. Hand-editing
that is neither reviewable nor repeatable. A command is idempotent, survives a
model re-import, records the intent in code next to the components it wires, and
can be re-run after a half-finished attempt without cleaning up first.

## 2026-09-21 - Globe-centered pivot rather than total bounds center

**Decision:** `EyeAnatomy` is positioned inside `EyeModelPivot` with local offset `(0.015024, 0.000562, 0.011255)`, aligning the visual center of the eyeball globe (sclera + cornea + lens) with the world origin `(0, 0, 0)`.

**Why:** The model includes long posterior optic nerve and muscle meshes extending ~12 cm back into +Z. Placing the center of the total renderer bounds at `(0, 0, 0)` placed the eyeball globe 4.2 cm in front of the rotation pivot. When rotated, this created an eccentric swing of over 8 cm across the screen. Centering on the eyeball globe keeps the primary viewing subject directly in place during rotation.

## 2026-09-21 - Turntable orbit with pitch clamping rather than free-flying camera

**Decision:** Replaced free Euler/quaternion rotation in `EyeManipulator` with true turntable orbit (yaw around world Up, pitch around camera Right), clamped pitch to `[-75°, +75°]`, and disabled `ViewerFlyController` camera flying by default.

**Why:** Free flying the `XRRig` on a stereoscopic fish-tank display breaks the head-tracked zero-parallax screen window calibration. Furthermore, unclamped rotation allowed users to flip the model upside-down, which inverted drag controls and caused disorientation. The turntable model provides an intuitive, tactile object-examination UX matching physical display expectations.

## 2026-09-21 - Invariant focal point rotation when inspecting focused parts

**Decision:** When a part is focused, `EyeManipulator` dynamically shifts its orbit pivot to the focused part at `FocusAnchor`.

**Why:** In focus mode, the model root is scaled and translated so the selected part sits at `FocusAnchor`. Rotating about `(0, 0, 0)` caused the focused part to swing in a wide arc off the screen. Applying position compensation `P_new = F - deltaRot * (F - P_old)` ensures the focused part spins in place on its own center.

## 2026-09-21 - Unified animated reset via UI button and 'R' shortcut

**Decision:** Added an on-screen "Reset View" button and mapped `KeyCode.R` to `EyeAnatomyController.ResetToHome()`, smoothly animating rotation, pan, and zoom back to default forward-facing overview while clearing focus and restoring materials.

**Why:** Previously, reset was an undocumented key that only reset `XRRig` and `EyeModelPivot` without clearing focus or restoring materials, and was completely inaccessible on touch/stylus without a physical keyboard.

## 2026-09-21 - Vendor both SDKs under `Assets/`, not as UPM packages

**Decision:** Copy both SDKs into
`Assets/Games/kmax-display-example/Plugins/Kmax/` rather than adding them to
`Packages/manifest.json` or embedding them in `Packages/`.

**Why:** The base project is read-only, so `Packages/manifest.json` cannot be
edited and no folder can be created under `Packages/`. Unity compiles assembly
definitions from `Assets/` exactly as it does from a package, so nothing is
lost except the Package Manager UI entry and the "Import Sample" button.

## 2026-09-21 - One define symbol, not two

**Decision:** Mutual exclusion is driven by a single symbol, `KMAX_AIO_K1`.
XR Core assemblies constrain on `!KMAX_AIO_K1`; AIO assemblies constrain on
`KMAX_AIO_K1`.

**Why:** Two symbols (`KMAX_XR_CORE` / `KMAX_AIO_K1`) allow "both defined" and
"neither defined" - the first reintroduces the `CS0433` collisions the
constraints exist to prevent, the second silently compiles no Kmax runtime at
all. With one symbol both states are unrepresentable. It also means a fresh
clone compiles against XR Core without anyone having to set a project setting
first, which matters because `ProjectSettings/` is read-only.

## 2026-09-21 - Re-GUID the AIO copies, not the XR Core copies

**Decision:** The five `.meta` GUIDs shared by both SDKs were reassigned on the
AIO side. XR Core keeps every GUID exactly as shipped.

**Why:** Duplicate GUIDs under `Assets/` make Unity reassign one side at import
and break whatever referenced it. XR Core is the default backend and the one
Kmax's own documentation, prefabs and future package updates are written
against, so it keeps its identities. Nothing inside the AIO SDK referenced the
five affected scripts, so only the `.meta` files changed.

The five files (XR Core forked from AIO, which is why they collided):

| XR Core | AIO K1 |
|---|---|
| `Runtime/Scripts/Utility/StylusDragable.cs` | `Runtime/Scripts/StylusDragable.cs` |
| `Runtime/Scripts/Input/KmaxUIRaycaster.cs` | `Runtime/Scripts/Input/KmaxUIFacade.cs` |
| `Runtime/Scripts/Input/KmaxInputModule.cs` | `Runtime/Scripts/Input/KmaxInputModule.cs` |
| `Runtime/Scripts/Input/KmaxPointer.cs` | `Runtime/Scripts/Input/KmaxPointer.cs` |
| `Runtime/Scripts/Input/KmaxStylus.cs` | `Runtime/Scripts/Input/KmaxStylus.cs` |

## 2026-09-21 - The switcher lives in a zero-reference assembly

**Decision:** `KmaxDisplayExample.Editor.asmdef` declares no references and
sets `autoReferenced: false`, and `KmaxSdkBackend.cs` uses no SDK type.

**Why:** If the switcher compiled into `Assembly-CSharp-Editor` it would
auto-reference the active SDK. A compile error in that SDK would then remove
the very menu needed to switch away from it.

## 2026-09-21 - Did not rename the AIO `Runtime/Resources` folder

**Decision:** `com.kmax.xr.aio/Runtime/Resources/` stays as-is, so
`StylusLine.prefab` and `KmaxPenOne.asset` are included in every build even
when the AIO backend is compiled out.

**Why:** `Runtime/Scripts/Frame/Stylus.cs` calls `Resources.Load("StylusLine")`.
Renaming the folder would break the AIO stylus beam at runtime, which is a
worse outcome than two small assets of dead weight. `KmaxPenOne.asset` has an
unresolvable script reference while XR Core is active; Unity does not warn
about it at import time. Revisit only if it shows up in a build report.

## 2026-09-21 - AIO HTML docs hidden, XR Core samples imported

**Decision:** `com.kmax.xr.aio/docs` was renamed to `Documentation~`;
`com.kmax.xr.core/Samples~` was renamed to `Samples`.

**Why:** The AIO docs are ~200 HTML, font and image files with no use inside
the Editor - importing them as textures and text assets is pure AssetDatabase
weight, and `~` is the UPM convention for exactly this. The XR Core samples are
the opposite case: under `Assets/` a `~` folder is invisible, and there is no
Package Manager "Import Sample" button for a vendored package, so the scenes
had to be un-hidden to be usable. Their scripts needed
`KmaxXR.Core.Samples.asmdef` so they do not land in `Assembly-CSharp`, where
they would break whenever the AIO backend is selected.

## 2026-09-21 - This module's docs live in the module

**Decision:** The AGENTS.md section 16 document set for this module is
`Assets/Games/kmax-display-example/docs/`, not the base project's `docs/`.

**Why:** The base project is read-only. The base `docs/` set still describes
the template and its analytics work and is unchanged.

## 2026-09-21 - The render pipeline changed mid-session, Built-in -> URP

**Finding, not a decision.** Early in the session
`GraphicsSettings.currentRenderPipeline` was null: `GraphicsSettings.asset`
referenced a URP asset by GUID `9fb93e134785e584698b58912ee0b588` that no asset
in the project carried, so Unity fell back to Built-in RP. A URP asset was then
added at `URPAssets/URPAsset.asset` and the project switched to URP.

**Consequence:** any earlier note claiming this project runs Built-in RP is out
of date. What it changed:

- glTFast reimported `EyeAnatomy.glb` onto URP shader-graph materials by
  itself. Nothing to do.
- The AIO K1 SDK's two CG shaders are now a real limitation of that backend,
  though it is not the default.
- Hand-written Built-in CG shaders silently draw nothing, which cost the custom
  hotspot shader (below).
- `Camera.Render()` stopped working for offscreen capture; SRP needs
  `RenderPipeline.SubmitRenderRequest`. Tooling only.

## 2026-09-21 - Baked two explode poses instead of playing the clips

**Decision:** `Kmax/Eye Anatomy/Bake Explode Poses` samples the model's merged
position curves, picks the most-packed and most-spread sample times, and writes
them to `EyeExplodePoses.asset`. `EyeExplodeView` lerps between those two poses.

**Why:** the 23 imported clips are a *pulse*, not an open/close - measured
spread over the 2.53 s runs 0.287 -> 0.179 -> 0.286 -> 0.179. Playing them
would make the eye breathe rather than open. They also bind to 23 separate
paths, and one `Animator` state can only drive one clip. Baking the two
extremes gives a deterministic closed default, a reversible scrubbable
transition, no `Animator` asset, and no per-frame allocation. The bake is a
menu item rather than a one-off so a re-exported model can be re-baked.

## 2026-09-21 - Focusing moves the model, and hides everything else

**Decision:** `EyeFocusView` scales and repositions the model root rather than
moving the camera, and hides all other parts while one is focused.

**Why:** the XRRig camera is head-tracked - moving it fights the tracker and
breaks the stereo geometry, so content comes to the viewer instead. The
isolation is not cosmetic: scaling the model ~4.5x so the lens fills half the
view also scales the sclera and muscles, which then cover the whole screen and
bury the part being explained. This was visible in testing before isolation was
added. Toggle it with `isolateFocusedPart` if you want context kept.

## 2026-09-21 - Hotspot markers use a stock URP shader, not a custom one

**Decision:** markers use `Universal Render Pipeline/Unlit`. The custom
`KmaxDisplayExample/HotspotMarker` shader was written, tested and deleted.

**Why:** the intent was `ZTest Always` so a marker on a part hidden behind
another part stayed visible. Under Built-in RP that worked. After the pipeline
switch, a hand-written URP SubShader compiled cleanly, reported
`isSupported == true`, was correctly selected as the material's only pass - and
drew nothing, at any render queue, with a constant-colour fragment, before and
after a forced synchronous reimport. A stock URP Unlit material in the same
frame on the same object rendered fine. Rather than keep debugging a shader
that is incidental to the feature, the markers were moved to the stock shader
and the dead one deleted.

> [!NOTE]
> **Revisit this.** The 2026-09-25 entry at the top of this file found that the
> pipeline was using URP's **2D renderer**, which discards 3D lights and would
> plausibly make a hand-written 3D lit SubShader compile, report `isSupported`
> and draw nothing - exactly the symptom below. The conclusion that custom
> shaders are unusable in this project was drawn under that renderer and may
> simply be wrong.

**Cost:** markers are depth-tested, so one can be occluded by geometry in front
of it. Mitigated by placing each marker in front of its part's **front face**
(`hotspotFrontGap`) rather than at its centre, which is also more robust than a
fixed offset - a marker at the centre of the 67 mm-deep sclera shell would sit
inside it. If always-on-top is needed later, the supported URP route is a
Render Objects renderer feature on a dedicated layer with Depth Test = Always;
`URPAssets/URPAsset_Renderer.asset` is in this module and writable, but the
layer would have to be added in the read-only base project.

## 2026-09-21 - Anatomy labels are inferred, and two of them are unverified

**Decision:** the 18 labels in `EyeAnatomyCatalog.asset` were derived from the
model's mesh names plus measured geometry, not from a source of truth shipped
with the model.

**Why / what to check:** most are unambiguous - the superior and inferior recti
and obliques were identified from their measured position and shape in the
assembled pose. **`Medial rectus` and `Lateral rectus` are a coin flip**: which
is which depends on whether the model is a left or a right eye, and that could
not be determined. The usual tell, the optic nerve head sitting nasal to the
posterior pole, measured only -0.0017 m against a ~0.015 m globe radius - about
6 degrees, inside modelling noise, and the two obliques point the other way.
`Anterior chamber` and `Tear film` are also readings of the mesh names
`cornea_inside` and `eye_over_cornea` rather than confirmed identifications.

Because the catalog is a data asset, correcting any of these is an inspector
edit with no code change - which is exactly why it is a data asset.

## 2026-09-21 - Fitted the eye model by derivation, not by eye

**Decision:** `EyeAnatomy`'s transform in the scene is computed from the
virtual screen rather than hand-placed: uniform scale
`screenHeight * 0.70 / rawBoundsHeight`, rotation `(0, 180, 0)`, and a position
that puts the renderer bounds centre on the rig origin. architecture.md records
the arithmetic.

**Why:** The model is authored ~12 m across, roughly 60x too large for a
0.345 m virtual screen, and faces away from the viewer. Recording the
derivation rather than a magic transform means the next person can refit it for
a different `ScreenType` or a different framing fraction without reverse
engineering three float triples. Centring on the rig origin puts the model at
zero parallax, which is the neutral starting point for tuning pop-out.

## 2026-09-21 - Left the model's animation unwired

**Decision:** The 23 imported `AnimationClip`s are left as imported sub-assets.
The root `Animator` has no controller, so nothing animates at runtime.

**Why:** The clips are one-per-part (t = 0 exploded, t = 2.53 assembled) and
all bind to distinct paths under the same root, so a single Animator state can
only ever move one part. Making them play together needs a choice that is a
product decision, not a mechanical one:

1. **Merge into one clip** - copy all 23 clips' curve bindings into a single
   `AnimationClip` asset, drive it from one Animator state. Lossless, because
   no two clips touch the same binding. Best fit if the explode/assemble is a
   single timeline.
2. **One Animator layer per clip** - 23 layers at weight 1. Faithful to the
   import but unwieldy.
3. **Drive it from code** - sample the clips directly against a normalised
   time, e.g. from a slider or the stylus, for scrubbable anatomy.

Option 1 is the obvious default, but the playback behaviour it implies
(autoplay? loop? ping-pong? stylus-triggered?) had not been specified, so
nothing was built. See roadmap.md.
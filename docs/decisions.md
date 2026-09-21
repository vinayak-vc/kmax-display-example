# Decisions

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
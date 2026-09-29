# AI Handoff

## Session 2026-09-29 (Volvo, later) - door interiors in, hood and trunk out

**Re-run `Kmax/Volvo Exhibit/Set Up Volvo Exhibit`.** Built, saved, exercised in play
mode, scene left clean and closed.

Two changes, both asked for:

- The door **interior cards** (`Plane.057` front, `Plane.027` rear - Blender default
  names the modeller never changed) are split and hinged with their doors. They used to
  sit at the car root, so a door opened and left its own inside face behind.
- The **hood and trunk no longer open**. Their hinges are out of the spec, and a
  retired hinge is taken down by `RemoveRetiredPanels`, which returns the geometry to
  the car rather than deleting it. Two groups remain: Doors and Sunroof.

### The thing to know if you touch the build step

`BuildDoor` used to return early when a door pivot already existed. That is not safe
here, because `RemoveSourcePanels` removes the two-sided source from the car whether or
not the halves were placed. When the mirror indicators were added to the door spec after
the doors had been built, those four meshes were cut out of the car and never put back -
silently, with the indicator channel running on four renderers instead of eight ever
since. It is fixed, and both the doors and the indicators came back in the same pass.

**The rule: a build step here converges on the spec from wherever the scene is, not just
from empty.** If you add a part to `GetDoorParts`, an existing door will pick it up. If
you withdraw a hinge, `RemoveRetiredPanels` takes it down. If you withdraw a group,
`BuildUi` destroys its button. Keep all three honest.

### One correction to the note below

The splitter does **not** require the halves to match, and the claim that every panel
divides into exactly equal triangle counts is no longer true: the front interior card
splits 76,512 / 56,224, because the driver's door has switchgear the passenger's does
not. It is still an exact cut. What makes it exact is that no triangle straddles the
centreline - 5.7 mm clearance at worst on the body panels, 43 mm on the cards.

### Still not built

Unchanged from below: the interior focus view, the paint and trim swatches, and the
guided tour. The order and the design notes in `roadmap.md` still stand.

### Files this session

`Scripts/Editor/Anatomy/VolvoExhibitBuilder.cs` only, plus four new split meshes under
`Model/VOLVO/Split/` and the scene.

## Session 2026-09-29 (Volvo) - the car exhibit, partially built

**Run `Kmax/Volvo Exhibit/Set Up Volvo Exhibit`.** Same contract as the other two: finds
before it creates, refuses to run in play mode. Built, saved and exercised in play mode.

Doors, hood, trunk and sunroof open; the car starts, lights up and idles. Paint and trim
swatches, the interior focus view and the guided tour are **not built** - see below.
(The hood and trunk were withdrawn afterwards - see the section above.)

### Read this first - the doors only exist because the meshes were cut

Every two-sided panel on this car is one mesh. The four doors come from two of them.
`VehicleMeshSplitter` cuts each into halves saved under `Model/VOLVO/Split/`, and the
build reparents the halves onto pivots at the hinge lines. **If you re-import the FBX
with different settings, or the split assets go missing, the doors vanish** - the build
logs an error naming the mesh it could not load and carries on without that door.

The cut is exact and stays exact only because nothing straddles the centreline (nearest
triangle 5.7 mm away, every panel splitting into equal halves). If a future model is not
mirrored that cleanly, the splitter refuses rather than producing a lopsided half.

The model must be **Read/Write enabled** for any of this. The build turns it on itself.

### Read this second - the camera cannot go inside the car

At exhibit scale the cabin is about 0.08 m across and `ViewerFlyController` will not come
closer than 0.14 m, so "fly into the interior" is not available as stated. The way this
rig shows an interior is the way `EyeFocusView` already works: scale the model up around
the cabin so it fills the view and bring it to the anchor, with the bodywork ghosted.
That is also the stereo-correct answer - on a head-tracked rig the camera belongs to the
viewer, which is why the whole exhibit moves content rather than cameras.

### Read this third - light intensities are scale-dependent

A lamp inside a car 0.26 m long is centimetres from everything it lights, and
inverse-square falloff makes anything near intensity 1 a blowtorch. The cabin lamp runs
at **0.09**. `VehicleLightRig` captures each light's authored intensity at Awake and
scales that, so the figures live on the lights where they can be seen, not in code.

This only became visible once the glass was made transparent. All the glass on this model
imported **opaque**, which also meant there was no interior to see at all; the build now
converts those materials.

### Files this session

New runtime: `VehiclePanel`, `VehiclePanelController`, `VehicleLightRig`,
`VehicleIgnition`, `VehicleExhibitControls`.
New editor: `VehicleMeshSplitter`, `VolvoExhibitBuilder`, `ExhibitUiFactory`.
Modified: `ProceduralAudio` (engine start and idle synthesis),
`EngineExhibitBuilder` (its button and label helpers now delegate to `ExhibitUiFactory`).

### Two things about the workspace

`Volvo S90.blend` is 626 MB and sits **inside `Assets/`**. Unity has registered it and
failed to import it, and will retry on every refresh - which means launching Blender
headlessly against a file you have open. Move it out of `Assets/`, or into a folder whose
name ends in `~`, which Unity ignores.

The Blender MCP bridge was up (addon running, port 9876 listening, connections
established) but its server layer returned "unavailable" to every call this session, with
three stale `blender-mcp.exe` processes present. Nothing here needed it - the split is
done in Unity and leaves the FBX untouched - but it is worth restarting if you want it.

### Next recommended task

In order:

1. **Interior focus.** A catalogue entry whose bounds are the cabin, plus a body ghost
   over shell, roof and glass. `EyeFocusView` already does the framing; what is new is
   deciding which meshes count as "body".
2. **Paint and trim swatches.** `Car Paint` is a single material at base colour black, so
   paint is one colour swap. The interior ships Black / Blue / Brown / Tan textures for
   the dashboard, console, door panels and seats, so trim is a texture swap.
   `ExhibitFeaturePanel` and `IExhibitMachinery` already exist for exactly this shape of
   feature and should be reused rather than rebuilt.
3. **Guided tour.** The catalogue, hotspots, navigator and attract mode are all model
   agnostic; this is a catalogue of car features plus a call to the shared wiring.

## Session 2026-09-29 (later) - engine features, and the first build's faults

Re-run `Kmax/Engine Exhibit/Set Up Engine Exhibit`. The scene is built, saved and
exercised in play mode with screenshots at each state.

The engine's see-through and build variants are now on the exhibit interface, down the
top-left edge: **See inside**, then **BUILD** with Stock / Sport / Throttle bodies /
Turbo. Both features already existed on `Enginei4` and were reachable only from the
screen-space canvas it shipped with, which the exhibit disables.

### Three things that will trip you up on this rig

**A world-space canvas needs its event camera set explicitly.** Left empty it falls back
to `Camera.main`, and this rig has none - the SDK disables the rig camera's own `Camera`
component and renders through the `left` and `right` sub-cameras it creates at runtime.
With the field empty **not one button on the canvas can be clicked**, and nothing in the
console says so. The builder now assigns it.

**A world-space canvas also needs the SDK's `UIScaler`.** Without it the canvas just sits
at the world origin with no rotation and tilts away as soon as the rig orbits. The scaler
rewrites its pose and size every frame from the rig's screen plane. This is what gives
the interface its overlay-like behaviour, and it is stereo-correct - a real
`ScreenSpaceOverlay` canvas cannot be used, because `VRRenderer` renders side by side and
an overlay ignores camera viewports. See decisions.md.

**`manage_camera screenshot` pauses the editor.** Anything measured straight after one
looks frozen: the explode transition sat at 0.036 for several minutes of wall clock and
looked like a bug in the explode view. Unpause first. Attract mode also fires after 30 s
idle and will expand and tour the engine between MCP round trips - disable it for any
deterministic test.

### Calling into the model from the exhibit

`Enginei4` is in `Assembly-CSharp`, which references `KmaxDisplayExample`, so the exhibit
can never name that type. Two patterns are in use:

- `ExhibitMachineryGate` holds a plain `MonoBehaviour` and toggles `enabled` - enough
  when all you need is something every behaviour has.
- `IExhibitMachinery` is declared on the exhibit side and implemented by `Enginei4` -
  what it takes to call something specific. Unity will not serialise a bare interface
  field, so the reference is stored as a `MonoBehaviour` and cast once in `Awake`.

Do not call `Enginei4.SetVariation` from the exhibit. It early-returns on the disabled
canvas's toggles, and it calls `ActivateAllObjects`, which enables every object with a
MeshRenderer under the model - **including all twelve hotspot badges**, which are meant
to stay hidden until the engine is open. Use `ApplyVariation`.

### Files this session

New:
- `Scripts/Runtime/IExhibitMachinery.cs`
- `Scripts/Runtime/ExhibitFeaturePanel.cs`

Modified:
- `Scripts/Runtime/EyeFocusView.cs` - added the `FocusChanged` event (additive)
- `Scripts/Editor/Anatomy/EyeAnatomySceneUpgrader.cs` - particle systems created from
  scratch now get the mote material, plus `RepairParticleMaterials` for ones an earlier
  run already left without one; `MoteMaterialPath` on the factory made `internal`
- `Scripts/Editor/Anatomy/EngineExhibitBuilder.cs` - `UIScaler`, event camera, the
  feature controls, variant names, fade alpha, and button colours through the ColorBlock
- `CarEngineAnimated - i4/Scripts/Enginei4.cs` - implements `IExhibitMachinery`;
  `ApplyVariation`, `SetTransparent`, `VariationCount`, `GetVariationName`; transparency
  coroutines are now cancelled before new ones start
- `CarEngineAnimated - i4/VirtualExhibition WR.unity`

### Not verified

Still everything that needs the hardware: the stylus beam on engine geometry, the wrist
turn, and whether 0.100 m of pop-out is comfortable over a long session. Also unverified
is how the see-through casing reads **in stereo** - a 0.20-alpha shell in front of solid
internals is exactly the kind of surface that can be hard to fuse, and it has only been
looked at flat.

### Next recommended task

The twelve per-part toggles are the last feature still stranded on the disabled canvas.
They are a different shape from the other two - twelve booleans rather than one state -
so they probably want a scrolling list or a second panel rather than twelve more buttons
down the edge. Worth asking whether they earn their place at all now that the engine
pulls apart, since hiding a part and pulling it away solve much the same problem.

## Session 2026-09-29 - the i4 engine exhibit

**Run `Kmax/Engine Exhibit/Set Up Engine Exhibit` if you touch that scene.** Same
contract as the eye's command: it finds before it creates, so a re-run changes nothing
and a half-finished run can be repeated. It **refuses to run in play mode**, because
the assembled pose is read from the scene and in play mode the parts are wherever the
transition left them.

`CarEngineAnimated - i4/VirtualExhibition WR.unity` is now the same exhibit as the eye,
on the same runtime components. It has been built, saved and exercised in play mode.

### Read this first - the engine has no animation clips

If you go looking for the explode animation, there isn't one. `Models/Enginei4.FBX`
imports with `animationType=None` and no clips; the one `Take 001` in `Engine_opt.FBX`
is a two-key, 0.033 s stub on the root, and the scene does not use that FBX. Every
moving part is procedural code in `Enginei4.Update`, driven by a single `RPM` float.

The teardown is therefore **authored**, as a table in
`EngineExhibitBuilder.GetTeardown`, in the model's native metres. To change how the
engine comes apart, edit that table and re-run the command. The framing re-solves
itself - do not hand-correct the offsets to keep it on screen.

### Read this second - two things that will bite

**`Enginei4` will throw the exhibit scale away if its `ZoomSlider` is ever reassigned.**
It writes its parent's local scale from that slider every frame. A slider on a
*deactivated* canvas is still a live reference, so hiding the old UI is not enough - the
builder clears both slider references outright. If the pulled-apart engine ever appears
about 9x too big (it measured 2.24 m across a 0.345 m screen), this is why.

**The teardown drift correction must only touch top-level groups.** `SolveTeardownDrift`
re-centres the pulled-apart engine on the origin the rig orbits. Applying its correction
to a nested step as well as to its parent shifts that part twice and the solve chases
itself; that showed up as an 0.8 mm overflow that would not close. `Correct` guards this
with `part.parent != modelRoot`.

### Structure

```
EngineExhibit          EyeAnatomyController, EyeExplodeView, EyeFocusView, EyeManipulator,
                       ViewerFlyController, ExhibitAttractMode, AnatomyStylusInput,
                       ExhibitPostProcessing, ExhibitMachineryGate
  EngineModelPivot     EyeManipulator's pivot; never scaled
    Enginei4 (1)       EyeFocusView's modelRoot - scaled 0.10696 to fit, moved to frame parts
      Enginei4         explode/catalogue root, carries the Enginei4 driver
  FocusAnchor
UI                     world-space canvas, 1920x1080 at 0.00017989584 = the virtual screen
XRRig / EventSystem / Ambience / Audio / PostFX / Key+Fill+Rim Light
Engine                 the original root: legacy canvas and point light, both disabled
```

### Measured

Assembled 0.137 x 0.077 x 0.160 m; pulled apart 0.240 x 0.155 x 0.218 m. Both centred
on the origin to four decimals. The teardown uses 69% of the screen width and 80% of its
height. 12 parts, 12 hotspots, 12 pickers, 65 mesh colliders. Focus distance 0.400 m, so
the 0.100 m pop-out is intact. Machinery stops on the frame the explode starts and
restarts only once fully reassembled - confirmed by the crank moving again.

### Not verified

Anything needing the hardware: the stylus beam landing on engine geometry, the wrist
turn, and whether a 0.16 m engine at 0.100 m pop-out is comfortable over a long session.
The eye's pop-out was chosen on geometry rather than comfort and this inherits that
number unchanged, on a model with far more high-contrast detail. It wants a pass with a
real viewer.

### Files this session

New:
- `Scripts/Runtime/ExhibitMachineryGate.cs`
- `Scripts/Editor/Anatomy/EngineExhibitBuilder.cs`
- `Data/EngineCatalog.asset`, `Data/EngineExplodePoses.asset`

Modified:
- `Scripts/Runtime/EyeExplodeView.cs` - added the `TransitionStarted` event (additive)
- `Scripts/Editor/Anatomy/EyeAnatomySceneUpgrader.cs` - 16 members `private` -> `internal`,
  no behaviour change
- `CarEngineAnimated - i4/Scripts/Enginei4.cs` - both sliders are now optional
- `CarEngineAnimated - i4/VirtualExhibition WR.unity` - rebuilt as the exhibit

### Next recommended task

Expose the three features the engine already has that the exhibit interface does not:
the transparency X-ray over 23 parts, the four tuning variations (Tuning 4 adds the
whole turbo assembly), and the twelve part toggles. All of it is still wired on the
disabled `Canvas - Engine (1)` - the methods are `Enginei4.EnableTransparency`,
`DisableTransparency` and `SetVariation(int)`. The X-ray in particular is the natural
companion to the teardown: pull apart to see what the pieces are, go transparent to see
where they sit inside the block. Note `SetVariation` currently early-returns unless the
matching toggle in `allTogglesType` is on, so it needs a small refactor to be driven
from anything but that canvas.

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

## Session 2026-09-25 (latest) - engagement features

Three additions aimed at giving the exhibit a reason to hold attention:

- **`ExhibitAttractMode`** tours the structures after 30 s idle and yields on any
  input. It deliberately does not reset the view on wake, and deliberately does not
  expand the eye itself - see the warning in architecture.md, because doing so
  silently kills every camera flight.
- **Stereo pop-out**: `EyeFocusView.focusPopOut` floats a focused part 0.10 m in
  front of the glass. Driven by camera distance, not part position - moving
  `FocusAnchor` does nothing, because the rig transform is the screen plane.
- **Wrist turn**: `AnatomyStylusInput.orbitMode` defaults to `WristTurn`, applying
  the pen's own rotation to the view one for one.

**Measured:** camera distance 0.400 during focus, a 0.100 m pop-out. Attract tour
confirmed stepping 1/18 then 2/18 with the camera flying to each.

**Not verified:** the wrist turn and the pen-movement wake, both of which need a
tracked pen. If the wrist turn feels reversed on device, flip `invertWristTurn`; if
it feels heavy, lower `wristTurnGain`. The 0.10 m pop-out was chosen on geometry, not
on comfort - too much pop-out causes eye strain over a long session, so it wants a
pass with a real viewer at the real viewing distance.

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
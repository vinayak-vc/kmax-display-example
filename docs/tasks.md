# Tasks

## Session 18 (2026-09-29) - the door interiors, and the hood and trunk withdrawn

Two changes to the Volvo, both asked for directly.

### The interior cards now belong to the doors

`Plane.057` and `Plane.027` - Blender default names the modeller never changed, carrying
the `DoorPanelFront` and `DoorPanelRear` materials - are the inside faces of the doors:
the trim card, armrest, speaker grille and switch panel. They were sitting at the car
root, so every door opened and left its own interior behind, hanging in the doorway.

They are two-sided like every other panel on this car, so they go through the same
splitter and onto the same hinges. Both cut cleanly - nothing on either is within 43 mm
of the centreline.

The front card is the first panel on this model that is **not symmetric**: 76,512
triangles on the left against 56,224 on the right, because the driver's door carries the
window and mirror switchgear and the passenger's does not. That is fine and always was -
the splitter's real requirement is that no triangle straddles the centreline, not that
the halves match - but the note claiming every panel divides into exactly equal counts
was load-bearing enough to be worth correcting, here and in the splitter itself.

### The hood and the trunk are no longer openable

Withdrawn on request. Both hinges are gone from the spec, and `RemoveRetiredPanels`
takes down any that a previous build left in the scene, handing the geometry back to the
car on the way out rather than deleting it with the pivot. `BuildUi` now also destroys
any panel button left over from a group that no longer exists, which it previously
abandoned in the canvas.

Two panel groups remain: **Doors** and **Sunroof**.

### A missing part found on the way

The doors were built before the mirror indicators were added to the spec, and
`BuildDoor` returned early whenever a door pivot already existed - so those four meshes
were cut out of the car by `RemoveSourcePanels` and never put back. The mirror
indicators had been absent from the scene entirely, and the indicator channel had been
running on four renderers instead of eight.

`BuildDoor` now repairs an existing door instead of skipping it: it adds any piece the
door is missing and leaves alone anything already there. That is what an idempotent
build step should have been doing, and it is what fixed the cards and the indicators in
the same pass. It shuts the door before reparenting, because a piece added to a door
left ajar would be fixed at that angle relative to the rest of the door for good.

### Verified

- All four doors swing to 62 degrees and back with their cards attached; the front-left
  card travels from x -0.0416 to -0.0672 with its skin.
- Indicator channel drives 9 material instances and blinks - caught mid-rise at level
  0.33, emission RGBA(1.65, 0.59, 0.04).
- Two panel buttons in the canvas, "Open the doors" and "Tilt the sunroof", both wired.
- 3,273,682 active triangles, up 6,864 from the mirror indicators returning. Well inside
  the envelope measured last session.

## Session 17 (2026-09-29) - the Volvo S90 exhibit

Third exhibit, built by **`Kmax/Volvo Exhibit/Set Up Volvo Exhibit`** into
`Model/VOLVO/VolvoS90.unity`, which shipped with nothing in it but a camera.

### The geometry problem that shaped everything

Every panel that exists on both sides of the car is **one mesh**. `Door Front` is a
single 1.85 m-wide mesh holding both front doors; the same goes for the rear doors, the
handles, the door glass and the mirrors. Hinging one of those swings both doors as a
rigid body and the left one sweeps through the cabin.

`VehicleMeshSplitter` cuts them. The cut is exact rather than approximate: the nearest
triangle to the centreline on any of these panels is 5.7 mm away, so sorting whole
triangles by the sign of their centroid's x never crosses an edge and never needs a new
vertex. (Every panel known at the time also divided into two exactly equal triangle
counts. That turned out to be a coincidence of this set rather than a rule - see session
18 - and it is the clearance, not the symmetry, that makes the cut safe.) Submeshes are
preserved because the doors carry five each (paint, gloss black, chrome, rubber,
plastic) and the renderer's material array is indexed by submesh.

The source FBX is untouched - both halves are new assets under `Model/VOLVO/Split/`.

Hood, trunk and sunroof are already single centre-hinged panels and need no cutting.

### Built

- **Four doors on real hinges**, each carrying its own skin, handle, glass and - on the
  front - its mirror and the mirror's indicator. Pivots stand at the leading edge; the
  geometry is reparented onto them without moving.
- **Hood, trunk and sunroof** on their own hinges.
- **A full lamp rig**: interior, daytime running, headlights, fog, tail, reverse and
  blinking indicators, seven channels over about forty meshes. The modeller built every
  reflector and emitter as its own mesh, so nothing is approximated.
- **Ignition** staged over two seconds - cabin first while the starter turns, exterior
  lamps once it catches, then the idle fades in.
- **Synthesised engine audio**. `ProceduralAudio` gained `CreateEngineStart` and
  `CreateEngineIdle`; the idle is built from the firing rate (26 Hz is about 780 rpm on a
  four-stroke four) rather than from an engine note, and the per-cycle noise is generated
  once and replayed so the loop is seamless. The idle source sits at the tailpipe and the
  starter at the engine bay, both 3D.
- **Interface** on the shared world-space canvas: a button per panel group, ignition,
  lamps, reset.
- Fly camera, manipulator, focus view, stylus and post-processing all reused unchanged.

### Measured

- **3,266,818 triangles active, 6,533,636 across both eyes.** On an RTX 3060 that runs
  at **105-164 fps**, so the decimation that was held in reserve is not needed. This was
  the open question when the work started; it is now answered.
- Car 0.105 x 0.075 x 0.260 m, centred on the origin the rig orbits.
- Seven lamp channels; reverse correctly stays off with the car running.

### Three faults found and fixed while building

**The scene held two cars.** The FBX had been dragged in to look at, and the builder
added its own - 6.5 million triangles, one standing through the other. The build now
removes any loose instance of the model from the scene root. It also disables the stock
`Main Camera`, which was rendering the car a second time and holding the scene's only
`AudioListener`, leaving the rig without one.

**All the glass imported opaque.** Every glazed material came in as `_Surface` 0, alpha
1, queue 2000 - so the windows, sunroof and lamp lenses were solid panels and there was
no interior to see from outside at all. The build now makes them properly transparent.

**The cabin light was a blowtorch.** With the glass opaque this was invisible; the moment
the windows went transparent it washed out the whole car. The rig had been imposing one
hardcoded intensity per light type, which is wrong twice over - a headlight and a
courtesy lamp are nothing alike, and the right figure depends entirely on how big the
model is in the scene. A lamp inside a car 0.26 m long sits centimetres from everything
it lights. The rig now captures each light's authored intensity and scales that; the
cabin lamp is 0.09. This is the same inverse-square trap the eye's notes already warn
about, walked into again.

### Not done

The interior focus view, paint and trim swatches, and the guided tour were all agreed for
this pass and are **not built**. See roadmap.md - the interior focus is the one with a
design note attached, because the camera cannot simply fly into the cabin.

## Session 16 (2026-09-29) - the engine's own features, and three faults from the first build

Re-run `Kmax/Engine Exhibit/Set Up Engine Exhibit` to pick all of this up.

### Three faults the first build shipped with

**The particles were magenta.** `CloneOrCreate` falls back to `new GameObject` +
`AddComponent<ParticleSystem>()` when there is no system to clone, and a particle
system made that way has no material - which Unity draws as solid magenta. The eye
never hit that branch because its scene already had a `MoteField` to clone from. The
creation path now assigns `AnatomyMote.mat`, and `RepairParticleMaterials` fixes any
system an earlier run already left without one, since every step here finds before it
creates and would otherwise skip them forever.

**The interface floated in the scene at an angle.** The canvas was world-space at the
origin with no rotation, so it tilted away as soon as the rig orbited. The eye's canvas
carries the SDK's `UIScaler`, which rewrites the canvas pose and size every frame from
the rig's own screen plane; the engine's was built without it.

**Nothing on the canvas could be clicked.** A world-space `GraphicRaycaster` needs an
event camera to turn a pointer position into a ray. The field was empty, so it fell back
to `Camera.main` - and there is none, because the SDK disables the rig camera's own
`Camera` component and renders through the `left` and `right` sub-cameras it builds at
runtime. The canvas is now handed the rig camera. Verified: all ten buttons return
themselves from a raycast at their own screen position.

> The request was for an overlay canvas. `UIScaler` is what gives that behaviour here -
> square to the viewer, fixed on the panel - while staying stereo-correct. A true
> `ScreenSpaceOverlay` canvas would break on the hardware: `VRRenderer` renders side by
> side, giving each eye half the viewport, and an overlay ignores camera viewports and
> is drawn once across the whole framebuffer. See decisions.md.

### See-through and build variants

Both already existed on `Enginei4` and were reachable only from the canvas it shipped
with. They are now on the exhibit interface, down the top-left edge.

- `IExhibitMachinery` declares them on the exhibit side and `Enginei4` implements it.
  That is the direction the assembly dependency already runs: `Enginei4` is in
  `Assembly-CSharp`, which references `KmaxDisplayExample`, so the exhibit can never
  name that type directly.
- `ExhibitFeaturePanel` owns the buttons and the state.
- `Enginei4.ApplyVariation` is new. `SetVariation` could not be reused: it returns early
  unless the matching toggle in `allTogglesType` is on, and those toggles are on the
  disabled canvas. It also calls `ActivateAllObjects`, which shows **every** object with
  a MeshRenderer - including the hotspot badges, which are three MeshRenderers each and
  are meant to stay hidden until the engine is open. `SetVariation` keeps its exact old
  behaviour and now delegates the part-swapping half to `ApplyVariation`.
- Variants are labelled from what they actually swap in: Stock, Sport, Throttle bodies,
  Turbo.
- The fade alpha was **0**, which deleted the casing rather than making it see-through.
  Now 0.15, and measured settling at 0.20.

### Two more faults found while testing this

**Toggling see-through off and straight back on left the engine solid** with the button
insisting it was see-through. `Enginei4` never cancelled its fade coroutines, so two
overlapping fades fought over the same renderers and the older one finished last,
putting the opaque material back. It now tracks and cancels them. Verified with
on/off/on issued in a single frame: all casing ends on the fade material, state agrees.

**The selected build button lost its highlight.** The tint was written to `Image.color`,
but a `Selectable` with a colour transition drives the canvas renderer directly on every
state change, so hovering any button threw it away. The selection is now written into
the button's own `ColorBlock`, and `BuildButton` leaves the graphic white and puts all
its colours there too - which also fixes the hover, which had been crossfading to
1.25 white and losing the dark base.

### Measured

All ten buttons clickable. Canvas forward-dot against the camera 1.0000 - square on.
Five particle systems on `AnatomyMote`. Variants swap turbo, manifolds, head covers,
cams and filters, with the badges staying hidden. See-through puts all 23 casing parts
on the fade material at alpha 0.20 and leaves internals opaque. Focusing a part stands
see-through down and locks its button, because focus rewrites every material anyway.

### A note for whoever tests this next

`manage_camera screenshot` **pauses the editor**. A transition measured straight after
one looks stuck - the explode sat at 0.036 for several minutes of wall clock. Unpause
before concluding anything is broken. Attract mode also fires after 30 s idle and will
expand and tour the engine between round trips; disable it for deterministic tests.

## Session 15 (2026-09-29) - the i4 engine on the exhibit stack

`CarEngineAnimated - i4/VirtualExhibition WR.unity` now runs the same exhibit as the
eye. One command builds it: **`Kmax/Engine Exhibit/Set Up Engine Exhibit`**. It is
idempotent, and it refuses to run in play mode because the closed pose is read from
the scene.

### What the engine actually had

No animation clips at all. `Models/Enginei4.FBX` imports with `animationType=None`,
and the one `Take 001` in `Engine_opt.FBX` is a two-key, 0.033 s stub on the root -
an empty take, on an FBX the scene does not even use. Everything that moves is
procedural code in `Enginei4.Update`, all of it slaved to a single `RPM` float:
crank 1x, cams 1/2x, gearbox shafts -1x and 1.47x, five gear ratios, two starter
gears, turbo fan, distributor, three pulleys, 16 valves opening on cam-phase windows
with their springs compressing, four pistons driven off the rods' Y delta, and both
belts scrolling by UV offset. Plus three non-RPM features: a 23-part transparency
X-ray, four tuning variations, and twelve part toggles.

So unlike the eye - whose 23 clips the pose baker samples - the teardown had nothing
to bake from and is authored in `EngineExhibitBuilder.GetTeardown`.

### What was built

- **19 teardown poses, 12 catalogued assemblies.** The pose set moves more than the
  catalogue labels: the valves have to travel with the cams that open them and the
  plug leads with the plugs, but neither is an assembly worth a badge.
- **Reused the whole runtime.** `EyeAnatomyController`, `EyeExplodeView`,
  `EyeFocusView`, `EyeManipulator`, `ViewerFlyController`, `ExhibitAttractMode`,
  `AnatomyStylusInput`, `ExhibitPostProcessing`, the hotspot prefab, the ghost
  material and the post-FX profile are all the eye's, unchanged. Sixteen generic
  build steps on `EyeAnatomySceneUpgrader` went from `private` to `internal` and are
  called directly rather than copied.
- **One new runtime script**, `ExhibitMachineryGate`, and one new editor script,
  `EngineExhibitBuilder`.
- The scene had **no XR rig, no EventSystem and no colliders** - its UI could not be
  clicked at all. It now has the rig, a single `AnatomyPen`, `KmaxInputModule` and
  65 fitted mesh colliders (every mesh is readable, so no box fallbacks).

### Measured

- Assembled 0.137 x 0.077 x 0.160 m, centred on the origin to four decimal places.
- Pulled apart 0.240 x 0.155 x 0.218 m, also centred on the origin exactly - 69% of
  the 0.3454 m screen width and 80% of its 0.1943 m height, both in frame.
- Machinery stops on the frame the explode starts and restarts only once the engine
  is fully back together; the crank was confirmed moving again afterwards.
- 12 parts resolved, 12 hotspots, 12 pickers. Attract tour reached 11/12 with the
  camera flying to each. Focus distance 0.400 m, so the 0.100 m pop-out is intact.

### Two bugs this turned up

**The teardown walked out of frame.** An engine does not come apart symmetrically -
far more lifts off the top than drops out of the bottom, and the gearbox travels half
a model-length backwards. The authored offsets carried the whole model 0.046 m up and
0.049 m back, putting the cam cover above a screen 0.194 m tall while the engine was
still small enough to fit easily. The builder now measures that drift and takes it
back out. It has to be solved rather than measured once, because the correction moves
whichever part defines the bounds, and it must be applied only to the groups directly
under the model root - a nested step like the oil pan would otherwise be shifted twice,
once with the block it hangs off and again on its own account.

**The engine threw away its own scale.** `Enginei4` writes its parent's local scale
from `ZoomSlider` every frame. Deactivating the legacy canvas is not enough, because a
slider on a deactivated object is still a live reference: the null check passes and the
write goes ahead. On the first frame of play the wrapper went back to the slider's value
of 1 and the pulled-apart engine measured **2.24 m across a 0.345 m screen**. The builder
now clears both slider references outright.

### Not done

The transparency X-ray, the four tuning variations and the twelve part toggles are
still only reachable from the old screen-space canvas, which is left in the scene
**disabled** rather than deleted so that wiring survives. Exposing them through the
exhibit interface is the obvious next task - see roadmap.md.

## Session 14 (2026-09-25) - scale handles, and two device fixes

### Drag-to-scale bounding box

`EyeScaleBox` puts a billboarded frame with four corner handles around the model.
Dragging a corner scales it uniformly. Four corners on a plane rather than eight on a
cube: a wireframe box in stereo is clutter that hides the anatomy, and dragging a
corner is a screen-space gesture anyway.

Scale is driven through `EyeManipulator.SetZoom`, not a transform - `ApplyTransform`
rewrites `pivot.localScale` from its own zoom every frame, so a direct write is gone
by the next one. Routing through it also means Reset View restores the scale for free.

Measured: dragging a corner to 1.5x its grab radius gives zoom 1.500, to 0.7x gives
0.700, clamped to the manipulator's 0.6-2.0 range. `ResetTransform` takes 1.8 back
to 1.0. The frame tracks the model at any scale and hides while a part is focused.

### A regression this turned up

`ViewerFlyController.IsPointerOverUI` used `EventSystem.IsPointerOverGameObject()`,
which reports **any** object the event system hit. Once the eye gained mesh colliders
and the camera a physics raycaster - both added earlier in this run of sessions -
that became true whenever the pointer was over the model, so **mouse drag stopped
orbiting over the model itself**. It now returns true only for hits under a `Canvas`.

Scale handles are suppressed separately through `EyeScaleBox.SuppressViewDrag`, which
covers hover as well as drag: the orbit's drag threshold is smaller than the
EventSystem's, so without it the view would start turning before the drag was
recognised.

### Two fixes reported from the device

- **Vibration too strong.** Strength came down (hit 22 -> 8, reset 40 -> 18), but the
  real problem was frequency: every structure has a collider, so sweeping the beam
  fired a pulse every few frames and the pen buzzed continuously. Added a 0.25 s floor
  between pulses.
- **Music inaudible.** Not primarily a level problem. The pad was built on a 110 Hz
  root, below what panel speakers reproduce - it was playing correctly and could not
  be heard. Root moved to 196 Hz with the voicing reweighted upward; measured on the
  generated clip, dominant energy moved from ~110 Hz to **~414 Hz**. Level 0.16 ->
  0.45 and fade-in 3.5 s -> 1.5 s.

**Not verified:** audibility and vibration strength on the device - both are why they
were reported in the first place, and neither can be judged from the editor. The
numbers are measurements of the signal, not of what it sounds like in the room.

## Session 13 (2026-09-25) - engagement: attract loop, pop-out, wrist turn

Three changes aimed at the gap the polish could not close: the exhibit had no reason
to keep you looking, and nothing drew a passer-by to it at all.

### Attract loop

`ExhibitAttractMode` opens the eye and tours its structures after 30 s idle, camera
flying to each and drifting gently between, with "Touch a structure to explore"
breathing above the navigator. Any input hands control back - and deliberately does
**not** reset the view, because snapping home would remove the thing that drew the
person over.

A real bug fell out of Play-mode testing: the first version opened the eye itself
before calling `SelectNextPart`. That made the controller take its immediate path
rather than the queued one, so the per-part view directions had not been cached yet
and **every camera flight was silently skipped** - the tour advanced but the camera
never moved, sitting at 0.500 instead of 0.400. Letting the controller own the
expand fixed it.

### Stereo pop-out

`EyeFocusView.focusPopOut` (0.10 m) now floats the focused part in front of the
glass. This had to be done as a camera distance, not a part position: the rig
transform *is* the virtual screen, so moving `FocusAnchor` moves the screen plane
with it and the parallax never changes. The gap is exactly `0.5 - distance`.

Framing had to be compensated by the reciprocal, because it is computed against
`XRRig.ViewSize` on the assumption the part sits at the screen plane - flying closer
magnifies it and `framingRatio` would stop meaning anything.

### Wrist turn

`AnatomyStylusInput.orbitMode` defaults to `WristTurn`: the pen's own change in aim
angle drives the view one for one (gain 1.35). Screen dragging scales by the
projection, so the same hand movement rotates differently depending on the dolly
distance; an angle is an angle. Angles are measured in **rig space** - the pen hangs
off the rig, so world-space measurement would feed the orbit back into its own input.

### Verified

Attract loop confirmed on screen touring 1/18 Cornea then 2/18 Tear film, both of
which now read clearly. Camera distance measured at **0.400, a 0.100 m pop-out**.
Full manual cycle over all 18 parts on top of the attract component with a clean
console and one stylus pointer.

**Not verified:** the wrist turn itself, and the pen-movement wake path - both need a
tracked pen. The mouse, key and scroll wake paths are code-identical to the ones the
fly controller already uses.

## Session 12 (2026-09-25) - the other half of the lighting bug

Session 11 made the lights work; the result was ugly. One side blown to flat
white, the other crushed. Measured: **26.4% of the subject clipped above 0.97**.

### Two causes

- **The specular point light.** 0.13 m off-axis from a model about 0.1 m across,
  so inverse-square falloff made it a blowtorch on the near side. Disabling it
  alone took clipping from **26.4% to 0.1%**. It is kept in the scene disabled -
  a directional already gives a specular that slides as the camera orbits,
  because specular is view-dependent.
- **No tonemapper, for a reason session 11 got wrong.** With the point light
  gone the rig had to run so dark everything read muddy. Session 11 had tried a
  tonemapping `Volume`, seen no change, and concluded the SDK's stereo path
  could not be tonemapped. The real cause: **`VRRenderer` creates the `left` and
  `right` cameras at runtime without a `UniversalAdditionalCameraData`**, so URP
  defaults them to `renderPostProcessing = false` and they skip every volume.
  Setting it on the authored root camera does nothing either, because the SDK
  disables that camera's own `Camera` component.

`ExhibitPostProcessing` now adds the component and enables post-processing and
HDR on the sub-cameras once they exist. HDR matters as much as the flag - without
an HDR buffer, values clip before the tonemapper sees them.

### Result

Neutral tonemapping plus gentle bloom, and the rig back at normal exposure:
key 1.05, fill 0.50, rim 0.60, ambient 0.55, key:fill near 2:1. Measures
**0.00% clipped** with a subject mean of 0.37, against 26.4% clipped before.

The exploded view now reads properly - shaded sclera shells, warm interior,
vivid retinal vessels - and the rescued lens reads as a glassy body with a soft
halo rather than a flat sticker. Full pass over all 18 parts, both navigator
directions, reset and every audio cue with a clean console.

## Session 11 (2026-09-25) - the renderer was 2D, so nothing was ever lit

Prompted by "I think directional light has no effect on model". It does not, and
the cause turned out to be the largest single issue in this repo.

### Measured, not argued

Rendered the camera to a texture with every light on, then every light off.
Mean luminance was **identical to four decimal places - 0.3247 both ways** - and
the same for each light individually. Validated the instrument by swapping the
background colour, which moved it 0.0461 -> 0.9535. Dropped a stock URP Lit
sphere into the scene: it rendered as a **flat white disc with no terminator**,
ruling out the model's materials.

`URPAsset`'s `m_RendererDataList[0]` was a **`Renderer2DData`**. URP's 2D
renderer only handles `Light2D`; every directional and point light in the scene
was being discarded. The exhibit had been rendering as albedo times ambient.

Replaced it with a `UniversalRendererData`. The same measurement now gives a
delta of 0.0443 and the probe sphere shades correctly.

### What that invalidated

- **The lens diagnosis from session 10 was half right.** It was flat, and the
  geometry reading - a biconvex disc seen down its own axis - was accurate, but
  that was never the binding constraint. The lights were simply not applied.
  With the renderer fixed the lens shades properly and now reads as a glassy
  body with a real terminator. The translucent stand-in is kept; it looks right
  and stays closer to the source art.
- **Every light value in the scene was uncalibrated**, because raising a light
  had never done anything. Once lighting applied, the existing intensities blew
  the model out to solid white. Rebalanced to roughly a third of the previous
  total: key 0.24, fill 0.07, rim 0.13, specular point 0.12, ambient 0.24, with
  the front fill disabled outright. Budget the whole rig to about 0.7 total -
  the albedo is near-white and URP clips past 1.0.
- **Post-processing does not help.** Enabled HDR, assigned the missing
  `PostProcessData`, opted the stereo cameras in and added a global `Volume`
  with Neutral tonemapping. It changed nothing on the SDK's stereo path, so the
  volume was removed rather than left implying it works.
- **decisions.md's "custom shaders draw nothing" note is now suspect** and has
  been flagged in place. A 3D lit SubShader under the 2D renderer would produce
  exactly that symptom.

`EyeAnatomySceneUpgrader.UpgradeRenderPipeline` now checks the pipeline on every
run and swaps the renderer back if it regresses.

### Verified

Full pass over all 18 parts, both navigator directions, reset and every audio
cue with a clean console. One stylus pointer, ~825 motes across four layers,
interface drawing over the anatomy, and the Lens showing genuine 3D form.

## Session 10 (2026-09-25) - first Play-mode pass, with the Editor driven over MCP

The Unity MCP bridge became available this session, so for the first time the
work could be run and looked at rather than only compiled.

### What the running scene showed

Session 9's output was all present and working: audio looping, 725 motes across
three layers, all five buttons carrying `UiButtonMotion`, 18 badges with their
haloes, `AnatomyStylusInput` live. Two things were wrong, and one prediction in
the docs turned out to be false.

- **Two pens.** The SDK's `pen.prefab` had also been added to the rig, so two
  `KmaxStylus` instances were registered under pointer id 1000 and
  `KmaxInputModule` - which iterates every registered pointer - dispatched every
  press twice from two different poses. It was also throwing a
  `NullReferenceException` at `KmaxStylus.UpdateState` line 280 on every
  `EventSystem.Update`, because the second stylus had no resolved `IStylus`.
  The setup command now removes any stylus that is not `AnatomyPen`, and the
  console is clean afterwards.
- **The info panel was cut off** by any structure scaled up in front of it. The
  cause is geometric: `UIScaler` pins the canvas to the rig's screen plane,
  always 0.5 m from the viewer, while the camera orbits 0.42 m from the model.
- **Correction:** session 9 predicted the model's meshes were not marked
  Read/Write and that colliders would fall back to boxes. Measured: **21 mesh
  colliders, 0 fallbacks.** Picking is already per-triangle. The docs said
  otherwise and have been fixed.

### Lens and Tear film

The brief said these were invisible and guessed the cause was missing
environment reflections. Adding an environment did not fix them, and making the
background grey made them *worse* - a 58%-alpha grey over grey has no contrast
at all. The alpha is the problem, so `EyeFocusView` now swaps any part whose
materials are all under 0.75 alpha onto `Materials/FocusHighlight.mat`.

Getting that material right took measuring rather than guessing. An opaque
version was visible but read as a flat pale sticker, and neither lowering
ambient, re-aiming the lights, raising smoothness nor hiding the ghost shells
produced a terminator on it. Dumping the mesh settled it: 2345 normals spanning
175 degrees, so the geometry was never the limitation - the lens is a biconvex
disc seen down its own axis, where almost every visible normal points at the
camera. High smoothness made it worse, because a near-mirror reflecting a nearly
uniform grey sky returns the same value at every normal. A translucent stand-in
reads correctly and stays closer to the source art.

### Also changed

- Grey background at `RGB(0.165, 0.175, 0.205)`, kept dark because a focused
  part is seen through ~20 ghost shells that otherwise accumulate into haze.
- A grey gradient skybox for ambient and reflections that is never rendered, so
  the wet surfaces have something to reflect without raising stereo crosstalk.
- `Front Fill` and `Specular Point` added; ambient dropped to 0.42 and the
  ghosts to 0.028 alpha. The fill is faint and off-axis on purpose - the first
  version aimed it down the view axis and flattened everything facing the viewer.
- Button lift on hover and a press flash, on top of the existing punch.
- A fourth `MoteField_Foreground` layer well in front of the display: sparse and
  large, it contributes more parallax per particle than anything at model depth.

### Verified on screen

Interface drawing over the anatomy with the panel's full text readable, the Lens
clearly legible, navigator and counter working, ~800 motes across four layers,
and burst, ring and sparks all firing on selection. The pad loops and a
selection cue reaches the one-shot source.

**Not verified:** anything requiring a tracked pen. `KmaxStylus.Visible` is
false without hardware, so the beam, the tip and all three buttons are still
unexercised.

One gotcha worth knowing: driving the Editor headlessly leaves it unfocused, so
frames do not tick, `Time.deltaTime` reads 0 and animated transitions stall
part-way. That is remote driving, not a bug.

## Session 9 (2026-09-25) - the stylus, the navigator, audio and depth

### Diagnosed first

The brief opened with "stylus drag rotation is not working". It was not a
tuning problem. Searching `EyeAnatomy.unity` for the GUIDs of `KmaxStylus`,
`PenTracker` and `StylusRay` returned **zero hits in all three cases**, and
`XRRig.prefab` turned out to contain only `Camera`, `left` and `right` - the
SDK's `pen.prefab` was never instanced. The `EventSystem` was also running a
stock `StandaloneInputModule`, not `KmaxInputModule`.

The user confirmed on device that UI buttons *are* clickable with the pen, which
fits: the Kmax driver moves the OS cursor, and a cursor plus
`StandaloneInputModule` is enough to press a button but never produces a 3D
pointer. `ViewerFlyController.UpdateMouseOrbit` reads `Input.GetMouseButton`,
which that emulation does not reliably set - hence no orbit, no beam, nothing to
raycast. decisions.md records the full diagnosis; architecture.md's claim that
`XRRig/pen` existed has been corrected.

### Built

- **The stylus.** `XRRig/AnatomyPen` carrying `PenTracker` + `KmaxStylus`, with
  `KmaxInputModule` swapped onto the `EventSystem` (it derives from
  `StandaloneInputModule`, so the mouse path is untouched).
- **`AnatomyStylusInput`** maps the pen's three buttons - the whole budget, read
  as `IStylus.GetButton(0..2)`: front selects and orbits on a drag, rear taps to
  reset, centre holds to push-pull dolly. Drag deltas come from a point
  projected at a *fixed* distance along the ray, not from the hit point, which
  would jump at every silhouette edge.
- **`AnatomyStylusBeam`** replaces the SDK's `StylusRay`: a tapered
  `LineRenderer` ending in a cone whose apex sits on the hit point and whose
  body stands out along the surface normal, recolouring idle/hit/press, with a
  haptic pulse on hit-enter.
- **`EyePartColliders` + `EyePartPicker`** fit colliders to every catalogued
  structure at build time and make the geometry itself selectable. Mesh
  colliders where the mesh is readable, bounding boxes where it is not - the
  glTFast import does not enable Read/Write, so today it is boxes and one
  warning says so.
- **Next / Back navigator** with a `n / 18` counter, stepping the catalog and
  flying the camera to a viewpoint on each structure's own side of the eye.
  `ViewerFlyController` gained `FlyTo`, `AddOrbitDelta` and `AddDollyDelta`, and
  its reset was generalised into one eased flight that any input can interrupt.
- **Badge motion**: staggered pop-in with overshoot, hover lift and scale, press
  punch, and a selection halo pinging outward on a loop.
- **`UiButtonMotion`** on every UGUI button - on a stereo panel scale is the only
  hover cue that survives being looked at from an angle with two eyes.
- **Particles**: near and far mote layers for genuine parallax depth, a popup
  ring and rise sparks on selection, and a brief swell of the whole ambient
  field so the volume acknowledges an interaction.
- **Audio**: `ProceduralAudio` synthesises a seamless 16 s pad plus cues for
  hover, select, navigate, back, expand, collapse and reset;
  `AnatomyAudioDirector` plays them with ducking and an override slot each.
- **`Kmax/Eye Anatomy/Set Up Interaction Upgrades`** authors all of the scene
  side idempotently, because the alternative was hand-editing 12,000 lines of
  YAML keyed by file IDs.

### Verified

Both assemblies compiled against the project's real Unity reference assemblies
and `Library/ScriptAssemblies` - `KmaxDisplayExample` and
`KmaxDisplayExample.Anatomy.Editor`, clean, no warnings.

**Not verified:** nothing in this session has been run in Play mode or on
hardware. The menu command has not been executed. ai_handoff.md lists the three
settings most likely to need adjusting once it meets a real pen.

## Session 8 (2026-09-21) - badges persist while focused, ghost contrast, doc reconciliation

### Verified first, before changing anything

Re-ran the whole flow in Play mode rather than trusting the session 4-7 notes.
Console clean, 10 scene roots, rig on `Screen27` (`ViewSize 0.5977 x 0.3362`),
model at scale `0.026037`, closed by default, `MoteField` at 223 particles.
Focusing Sclera gave **opaque = 1, ghosted = 20, hidden = 0** with the part
centred at exactly `(0, 0, 0)` and a `TextMeshProUGUI` panel - so translucency,
TMP, centring and particles are all genuinely live.

### Fixed & Improved

- **Badges no longer vanish when a part is focused.** This was the last open
  item in roadmap.md's "Possible improvements": switching parts previously
  meant a trip through Back.
  - `EyeAnatomyController.OnExplodeTransitionCompleted` now keys badge
    visibility off `expanded` alone, not `expanded && !IsFocused`.
  - `OnHotspotClicked` deselects the previously selected badge and no longer
    hides the set, so clicking a second badge switches straight to it.
- **Badges hold a constant on-screen size.** The roadmap noted this needed
  per-frame counter-scaling, since focusing scales the model. Added
  `maintainWorldSize` + `worldDiameter` to `EyeHotspot`, which counter-scales
  against its parent's `lossyScale` each frame. The controller now calls
  `ConfigureSize(...)` instead of setting `localScale` once at build time.
- **Ghost contrast.** Roughly twenty ghost layers overlap and their alpha
  accumulates, so the ghosts were reading brighter than the focused part.
  `EyeGhost.mat` alpha `0.075` -> `0.040`, and a slightly darker, cooler tint.

### Verified in Play mode

- Badges active while focused: **18/18** (was 0).
- Badge world diameter `0.01000` m both before focus (model scale `0.026037`)
  and during it (`0.038`) - counter-scaling holds.
- Exactly one badge carries the gold selected colour at a time.
- Clicked Lens directly while Sclera was focused: lens re-centred to
  `(0.0000, 0.0000, 0.0000)`, panel switched to "Lens", opaque = 1 (lens),
  ghosted = 20, Sclera deselected. No Back needed.
- Console clean, scene saved.

### Found, not fixed

- `Lens` and `Tear film` both use the model's `Mat.1` - a textureless 91% grey
  at 58% alpha - so when focused they are nearly invisible whatever the ghost
  alpha is. This is the source art, not the isolation system; opaque parts like
  Sclera read clearly. Recorded in ai_handoff.md as a decision for a human.

### Documentation reconciled

`architecture.md` had drifted several sessions behind and actively contradicted
the build: it described a 15.6" screen, parts being *hidden* on focus, legacy
`Text` labels, a four-root scene, `FocusAnchor` at `x -0.07` and model scale
`0.015043`. Updated the scene table, coordinate convention, model fit, component
table, flow and focus sections to match what is actually in the scene, and
fixed the stale screen size in `ai_handoff.md`.

## Session 7 (2026-09-21) - camera orbit flight, centered part framing, and starting pose reset

Implemented spherical orbit camera flight locking the eye model at the dead center of the screen, centered all selected parts at (0, 0, 0), and added starting pose restoration upon pressing Back.

### Fixed & Improved

- **Continuous Camera Orbit Centering**:
  - Replaced FPS-style camera translation and in-place look with a spherical Orbit Camera model in `ViewerFlyController.cs`.
  - Math formula locks camera gaze directly at `focalCenter` (world `(0, 0, 0)`) across all angles:
    $$\mathbf{P}_{\text{rig}} = \mathbf{C}_{\text{focal}} + \mathbf{R} \times (0, 0, 0.50 - d)$$
    $$\mathbf{R}_{\text{rig}} = \text{Quaternion.Euler}(\text{pitch}, \text{yaw}, 0)$$
  - Verified mathematically: viewport point of the eye center is exactly `(0.5000, 0.5000)` and `Dot(camera.forward, toCenter) == 1.000000` at all orbit angles and distances.
  - Controls:
    - Mouse drag (Left-click or Right-click): Orbits yaw and pitch with velocity damping; pitch clamped to `[-80°, +80°]`.
    - W / S: Flies forward / backward in orbit (dollies toward/away from center).
    - A / D: Flies in an orbital circle left / right around the eye.
    - Q / E: Flies in an orbital arc down / up around the eye.
    - Arrow keys: Orbit yaw and pitch.
    - Left Shift: 2.5x speed boost.
    - Mouse scroll wheel: Dollies in / out.
- **Centered Part Framing**:
  - Re-anchored `FocusAnchor` in `EyeAnatomy.unity` from `(-0.125, 0, -0.02)` to `(0, 0, 0)`.
  - In `EyeFocusView.cs`, framing now brings the focused part's world bounds center to `(0.00, 0.00, 0.00)`, verified at viewport `(0.5000, 0.5000)`.
  - Resized `InfoPanel` from `(800, 300)` to `(650, 260)` and positioned it at `(-25, 0)`, leaving the entire central 65% of the screen unobstructed for the focused part.
  - Coordinated `flyController.SetFocalCenter(anchor)` so camera orbiting remains centered on the selected part.
- **Starting Pose Reset on Back**:
  - Updated `EyeAnatomyController.ReturnToOverview()` (invoked by clicking "Back") to call `flyController.ResetView(true)` and `manipulator.ResetTransform(true)`.
  - Smoothly restores camera rig position to `(0, 0, 0)`, rotation to `Quaternion.identity`, and distance to `0.5m` over 0.45s, returning the display to the exact 1st position when the app started.
- **Input Conflict Prevention**:
  - Added `disablePointerManipulation = true` to `EyeManipulator.cs` to prevent conflicting dual-rotation between camera rig and model pivot.

### Verified in Play Mode

- Overview camera orbit: Tested at multiple yaw (0°, 45°, 180°), pitch (-60°, 0°, +60°), and distance (0.35m, 0.5m, 0.8m) settings: viewport point of `(0, 0, 0)` is strictly `(0.5000, 0.5000)` and forward dot product is `1.000000`.
- Part selection: Selected Hotspot 4 (Lens); world bounds center landed at `(0.00, 0.00, -0.02)` with viewport `(0.5000, 0.5000)`.
- Back button: Clicked Back; camera returned to `(0.00, 0.00, -0.50)` with `(0, 0, 0)` rotation, rig to `(0, 0, 0)`, and pivot to `(0, 0, 0)`.
- Console completely clean with 0 errors.

Resolved camera flight controls with mouse and keyboard, and fixed numbered interaction badges so they always billboard upright facing the camera rather than facing downwards.

### Fixed & Improved

- **Upright Hotspot Billboarding**:
  - Diagnosed that `Camera.main` returned `null` because the root `Camera` object on `XRRig` is disabled by the Kmax SDK in favour of the active rendering camera `left`.
  - Because `_targetCamera` was null, `visualRoot.rotation` was never updated, causing badges to inherit their parent GLB bone/mesh pitch rotation of ~90° (`worldEuler ≈ (89.98, 180, 0)`), lying flat horizontally facing downwards towards the floor.
  - Added robust dynamic camera resolver `ResolveCamera()` in `EyeHotspot.cs` and `EyeAnatomyController.cs` that queries `Camera.main` and falls back to active/enabled cameras in `Camera.allCameras` (resolving `left`).
  - Added default fallback `visualRoot.rotation = Quaternion.identity` in `Awake()` and billboarding so markers never inherit parent bone tilt.
  - Verified in Play Mode: all 18 numbered badges billboard perfectly upright (`facingDot = 1.000`, `upDot = 1.000`) directly facing the camera.
- **Mouse & Keyboard Fly Camera**:
  - Enabled `ViewerFlyController` by default (`enableFly = true` in script and serialized `enableFly: 1` in `EyeAnatomy.unity`).
  - Unchained `Move()` in `ViewerFlyController.cs` so WASD and Q/E continuously fly the camera rig through 3D space whether right-click is held or not.
  - Added Left Shift key boost for 3x flying speed.
  - Added `CaptureAngles()` synchronization on `Input.GetMouseButtonDown(1)` and mouse position reset on `GetMouseButtonDown(2)` to eliminate look/pan angle jumps.
  - Separated mouse inputs: removed right-click and middle-click capture from `EyeManipulator.cs` so right-click is exclusively for fly-look and middle-click for fly-pan, while left-click remains dedicated to turntable model orbit.
  - Mapped scroll wheel dolly to right-click hold, preventing conflict with model zoom.
  - Connected `ViewerFlyController.ResetView()` into `EyeAnatomyController.ResetToHome()` so clicking "Reset View" or pressing 'R' restores both the camera rig and the model to their authored poses.

### Verified in Play Mode

- Expanding the eye shows all 18 numbered badges upright facing the camera with 1.000 alignment.
- WASD and Q/E fly the camera in 3D space; Left Shift boosts speed 3x.
- Right-click drag smoothly pitches and yaws the camera.
- Left-click drag orbits the model around its visual center.
- Resetting via 'R' or the "Reset View" button smoothly restores both camera and model to default home state.
- Console completely clean with 0 errors.

Replaced cyan plastic spheres with subtle numbered badges (1 to 18), added interaction particle bursts, and brought the 3D stereoscopic display alive.

### Fixed & Improved

- **Numbered & Subtle Interaction Badges**:
  - Replaced the sky-color spheres with circular badges featuring a dark translucent medical slate disc (`HotspotBadge.png`), soft cyan rim, and bold white `TextMeshPro` number (1 to 18).
  - Added camera billboarding so the numbers always directly face the viewer regardless of orbit angle.
  - Added subtle organic breathing pulse (`pulseAmplitude = 0.035f`, phase offset by `partIndex * 0.45f`) so the badges gently breathe, making the 3D display feel alive without being flashy or distracting.
  - Smooth hover scale expansion (1.18x) and gentle cyan highlight; warm golden highlight on selection.
- **Particle Burst on Any Interaction**:
  - Enhanced `AnatomyParticleDirector.PlayInteractionBurst(worldPosition)` with `burst.Emit(45)` for instantaneous, deterministic emission of 45 radiant golden stardust particles right at the clicked interaction point.
  - Wired in `EyeAnatomyController.OnHotspotClicked` to burst directly at `hotspot.transform.position`.
- **Living 3D Depth Ambience**:
  - Enabled `prewarm = true` on `MoteField` so over 220 delicate floating motes drift in stereoscopic depth (in front of, at, and behind the zero-parallax plane) from the moment the scene opens.

### Verified in Play Mode

- 18 numbered badges appear upon expanding, clearly numbered 1 ("Cornea") through 18 ("Inferior oblique").
- Badges billboard smoothly to face the camera during orbit and gently breathe.
- Clicking any badge instantly fires a 45-particle radiant burst at the tapped position and frames the part at `FocusAnchor`.
- 220+ ambient motes float smoothly in 3D stereoscopic space.
- Console clean with 0 errors and 0 warnings.

## Session 4 (2026-09-21) - rotation centering, turntable orbit, and unified reset

Fixed rotation drift, flipping/upside-down controls, focus pivot drift, and missing/inconsistent reset behavior.

### Fixed & Improved

- **Center of Rotation**:
  - Offset `EyeAnatomy` inside `EyeModelPivot` to `(0.015024, 0.000562, 0.011255)` so the visual center of the eyeball globe (sclera + cornea + lens) lands at exactly `(0, 0, 0)`.
  - In overview / exploded mode, rotating `EyeModelPivot` now spins the eyeball directly in place with zero drift.
- **Focused Part Invariant Orbit**:
  - `EyeManipulator` dynamically tracks active focal points. When a part is focused, the orbit pivot shifts to the focused part at `FocusAnchor` (`(-0.125, 0, -0.02)`), rotating the inspected part in place without it flying off-screen.
- **Turntable Orbit with Pitch Clamping**:
  - Constrained pitch between `-75°` and `+75°` so the model can never flip upside-down.
  - Implemented true turntable yaw/pitch rotation with smooth velocity damping.
- **Smooth Damped Pan & Zoom**:
  - Added middle/right-click dragging to pan within bounded screen extents.
  - Added scroll wheel to dolly/scale the model between 0.6x and 2.0x.
- **Unified Animated Reset**:
  - Added `ResetTransform(bool animated = true)` on `EyeManipulator` that smoothly lerps rotation, pan, and zoom back to default forward-facing view over 0.45s.
  - Added `ResetToHome()` on `EyeAnatomyController` that cleanly exits focus, restores all materials, clears the info panel, re-enables hotspots, and resets model transform.
  - Added on-screen `ResetButton` ("Reset View") on the UI canvas next to `ExpandButton`.
  - Bound `KeyCode.R` as an instant shortcut for `ResetToHome()`.
- **Disabled Free-Fly Camera by Default**:
  - In `ViewerFlyController`, set `enableFly = false` by default so right-click/WASD no longer flies the camera into the dark void away from the stereo display window.

### Verified in Play Mode

- Overview mode: model orbits smoothly in center; pitch clamps at ±75° without flipping.
- Exploded mode: 18 hotspots active; parts rotate symmetrically around center.
- Focus mode: selecting a hotspot (e.g. Lens) frames it at `FocusAnchor`; rotating spins the lens in place (distance drift = 0.00000).
- Reset: clicking "Reset View" button or pressing 'R' smoothly restores overview, clears focus, restores materials, and returns globe to `(0, 0, 0)`.
- Console clean throughout.

## Session 3 (2026-09-21) - the interactive exhibit

Closed by default, expand on a button, 18 clickable points, per-part zoom with
a name and a short description.

### Added

- `Scripts/Runtime/` (new assembly `KmaxDisplayExample`, constrained
  `!KMAX_AIO_K1` like the rest of the module):
  - `EyeAnatomyCatalog.cs` - `EyePartDefinition` + the catalog ScriptableObject.
  - `EyeExplodePoseSet.cs` - `EyePartPose` + the baked pose ScriptableObject.
  - `EyeExplodeView.cs` - interpolates parts between assembled and exploded.
  - `EyeFocusView.cs` - frames one part and isolates it.
  - `EyeHotspot.cs` - clickable marker.
  - `AnatomyInfoPanel.cs` - name/description presentation.
  - `EyeAnatomyController.cs` - the flow.
  - `EyePartBounds.cs` - measures a part while excluding its marker.
- `Scripts/Editor/Anatomy/EyeAnatomyPoseBaker.cs` in its own assembly, so the
  SDK backend switcher's zero-reference assembly stays that way.
- `Data/EyeExplodePoses.asset` - 23 parts baked.
- `Data/EyeAnatomyCatalog.asset` - 18 labelled parts.
- `Prefabs/EyeHotspot.prefab`, `Materials/HotspotMarker.mat`.
- Scene: world-space `UI` canvas (`KmaxUIRaycaster` + `GraphicRaycaster` +
  `UIScaler`), Expand/Back buttons, info panel, `FocusAnchor`,
  `EyeAnatomyExhibit`.

### Verified in Play mode

The stylus follows the mouse without hardware and fires its own clicks, so the
`EventSystem` was disabled and events dispatched with `ExecuteEvents` to get a
deterministic run.

- Start: `Expansion` 0, 18 hotspots inactive, panel and Back hidden, label
  "Expand eye".
- Expand: `Expansion` 1, bounds 0.084 -> 0.177 m, all 18 hotspots active.
- Every probed hotspot maps to the right entry - index, `selectedIndex` and
  panel title agreed for Cornea, Lens, Optic nerve, Superior rectus and
  Inferior oblique.
- Focus: model scaled 0.0150 -> 0.0671, part centred on the anchor at exactly
  50% of the view width, 20 of 21 part renderers hidden.
- Back: all 21 renderers restored, panel and Back hidden, hotspots returned.
- Close: `Expansion` 0, bounds back to 0.084 m, hotspots inactive.
- Console clean throughout.

### Bugs found and fixed while testing

1. **Info panel never appeared.** The panel starts inactive, so Unity deferred
   `AnatomyInfoPanel.Awake` until the first `Show()` - and `Awake` called
   `Hide()`, undoing the activation that had just triggered it. Setup is now
   lazy and nothing self-hides on wake.
2. **Focus framed the marker as well as the part.** Markers are parented to the
   part they label, so `GetComponentsInChildren<Renderer>` swept them in - the
   lens measured 165% deeper than it is. Extracted `EyePartBounds`, which skips
   markers, and used it in both call sites.
3. **Zoom buried the part it was zooming into.** Scaling the model so one part
   fills the view also scales its neighbours until they cover the screen.
   Focusing now hides everything else.
4. **Markers vanished after the pipeline switch.** See decisions.md - the
   custom shader was dropped for a stock URP one.

### Not a bug

An early test showed clicking `Hotspot_Lens` displaying "Sclera". With live
input disabled every hotspot mapped correctly; the mouse-driven stylus had
clicked a different marker between the dispatch and the read.

## Session 2 (2026-09-21) - first scene and eye model

- Created `Scenes/EyeAnatomy.unity`, added the `XRRig` via
  `GameObject/Kmax/Add XRRig`, an `EventSystem` with `KmaxInputModule`, and
  three directional lights.
- Examined `Model/EyeAnatomy.glb`: 21 meshes, 80,973 triangles, 9 materials,
  4 JPEG textures, 23 animation clips, no required extensions, authored
  11.79 x 9.04 x 11.99 m facing +Z.
- Fitted it: rotation `(0, 180, 0)`, uniform scale `0.015043`, bounds centred
  on the rig origin.

## Session 1 (2026-09-21) - SDK integration

- Vendored both Kmax SDKs under `Plugins/Kmax/`, reassigned five colliding
  `.meta` GUIDs on the AIO side, added `defineConstraints` to all six SDK
  assembly definitions and to `KMaxUnity.dll.meta`.
- Renamed `Samples~` to `Samples` (+ new asmdef) and AIO `docs` to
  `Documentation~`.
- Added `Scripts/Editor/KmaxSdkBackend.cs` and this `docs/` set.
- Verified both backends compile and that switching restores
  `ProjectSettings.asset` byte for byte.

## Next up

See roadmap.md. The first item needs a human: review the two rectus labels.
# Kmax Showcase Suite

Four new scenes, built from scratch, sharing one runtime core. Nothing in here depends on
the Volvo, the eye or the engine.

## The thesis

**Build something where depth perception is required to succeed.**

A scene that merely looks deep gets admired for five seconds. A task you cannot complete
without stereo - where you misjudge, fail, move your head, and then succeed - proves the
display in a way no screenshot or video can, because neither can carry it. Every design
decision below follows from that sentence.

The four scenes are ordered as an argument, and that order is also the demo script:

| | Scene | What it proves |
|---|---|---|
| 1 | **Bloom** | There is space in front of the glass |
| 2 | **Reef** | That space is deep, continuous and richly occluded |
| 3 | **Stack** | You can reach into it, and your hand is where the object is |
| 4 | **Probe** | And it is accurate enough to work in |

It is also roughly increasing technical risk, which is why it is the build order too.

## Three audiences, one suite

The suite was asked to serve as a client capability demo, a production exhibit and an
internal tech reference at once. Those pull in compatible directions if the split is
explicit:

- **Client demo** - the four scenes, run in order, narrated. Needs a hub, fast switching
  and no dead air. This is what drives the scene ordering above.
- **Production exhibit** - unattended operation. Needs attract loops, idle reset, analytics
  and no state that can get stuck.
- **Internal reference** - the shared core is the deliverable, not the scenes. It must be
  readable, documented and genuinely reusable on the next Kmax project. This is what
  justifies building `StereoVolume` properly rather than hard-coding 0.13 in four places.

---

## The stereo budget

Every scene is designed into the same volume. These numbers are derived from the SDK, not
chosen: `StereoCamera.DefaultDistance = 0.5f` puts the camera half a metre from the screen
plane, and `XRRig.DrawFrustum` draws its comfort gizmo at 0.37 and 0.80 from the camera.

| | |
|---|---|
| Pop-out, in front of glass | **0.13 m** |
| Depth, behind glass | **0.30 m** |
| Total comfort depth | 0.43 m |
| Window, 15.6 in / 24 in / 27 in | 0.345 x 0.194 / 0.531 x 0.299 / **0.598 x 0.336** m |

All of it scales with `XRRig.ViewScale`, which is settable at runtime and raises
`OnViewScaleChanged`.

**This project's rig is set to 27 inches**, so the working window is **598 x 336 mm** - read
off the live rig while verifying M1, not assumed. That is nearly twice the width of the
15.6 in figure, and it changes scene layout directly: content can sit much further off-centre
before it breaks the stereo window. Check `VirtualScreen.ScreenType` before trusting any
number laid out against a different panel.

Two rules fall out, and they constrain every scene:

1. **Most content lives behind the glass.** Pop-out is punctuation. Something parked at
   -0.13 m for a whole minute is tiring; something that crosses the plane and comes back is
   not.
2. **Anything at negative parallax must stay clear of all four frame edges.** Cut by the
   bezel, disparity says "in front" and occlusion says "behind", and that contradiction is
   what makes stereo feel broken rather than merely shallow. This is the most common way a
   pop-out effect fails, and it is why `StereoVolume.ViolatesWindow` exists.

A third, less obvious one: **motion through the screen plane is the cue that sells depth**,
far more than static disparity. Pop-out is a verb. Every scene has something crossing.

---

## Scene 1 - Bloom

*Pop-out, in its simplest possible form.*

Motes drift out of the volume, through the screen plane, into the viewer's space, and burst
on the pen tip.

**Layout.** Emitter at +0.15 m behind the glass. Motes drift forward on a slow curl, cross
the plane, and reach -0.08 m before fading. Lifetime is tuned so they fade *before* they can
reach the frame edges - the window-violation guard is a design constraint here, not a
runtime check.

**Interaction.** A sphere trigger at the pen tip. Contact bursts the mote into a sub-burst
with a haptic tick and a soft chime, pitch varying by depth.

**The known weakness.** Loose particles have no structure, so they show pop-out brilliantly
and depth weakly. Three mitigations, all cheap: a short ribbon trail on each mote so it has
extent rather than being a point; size graded with depth so there is a consistent gradient
to read; and a faint static grid *at* the screen plane, so the moment of crossing is legible
rather than merely happening.

**Why it is first.** It is the cheapest scene that exercises `StylusTip` end to end - tip
collider, haptics, audio, the comfort volume - without needing the grab system. It de-risks
everything from M3 onwards for about a day of work.

## Scene 2 - Reef

*Depth, at its richest.*

Fish and coral fill the 0.30 m behind the glass. One hero fish periodically noses out
through the window and darts away when touched.

**Layout.** Static coral and rock as occluders at varying depth - occlusion is the strongest
depth cue there is, and layered geometry at different distances is what makes an aquarium
read as a volume rather than a painting. Boids school through the gaps. The hero fish
approaches to -0.06 m, centre frame, then withdraws.

**Interaction.** Touch a fish with the tip and it darts, with a haptic flick. A button
releases food; the school converges on it. Deliberately light - this scene demonstrates
depth rather than requiring it, and that is its job in the sequence.

**Art.** The long pole. Sourcing via Poly Haven / Sketchfab through the Blender MCP; CC-BY
models carry an attribution obligation that has to be recorded. Ships stylised low-poly
first, realistic only if time allows.

**Caustics** via an animated light cookie. The gobo work on the Volvo transfers directly -
including the `Step` helper bug that made its edges hard and inverted.

## Scene 3 - Stack

*Co-location. The headline scene.*

Blocks sit in a bin behind the glass. The pen **tip** - not a ray - picks one up, lifts it
out through the window, and stacks it on a platform. Real physics, real shadows, real
toppling.

**Why it is the strongest.** You cannot judge the placement without stereo. Not "it is
harder without" - you genuinely cannot, and the failure is legible and funny rather than
frustrating. It is also the cheapest of the strong ideas: primitives and physics, no art
pipeline, dressable as crates or cargo or an assembly later.

**Layout, and the deliberate contrast.** Bin at +0.10 to +0.25 m. The stacking platform sits
**at the screen plane, zero parallax** - rock solid, the most comfortable place on the
display. The held block sits at **-0.05 to -0.08 m**. So the thing in your hand pops out and
the thing you are aiming at does not, which is the largest legible depth contrast the
hardware can produce, and it puts the stable reference exactly where the precision is
needed.

**Grab.** Nearest `Grabbable` within a radius of the tip on button 0. Not a raycast - the
whole point is that your hand and the block are in the same cubic centimetre.

**The one subtle tuning problem.** A block rigidly locked to the tip feels weightless and
reads as a rendering artefact stuck to the pen. A small spring lag gives it mass and makes
it read as a held object. Too much lag and co-location breaks, which is the entire premise.
Expect to tune this against real hands; it will not be right on the first guess.

**Shadows carry a lot of this scene.** Contact shadows tell you where a block is above the
platform when disparity alone is ambiguous. Shadow distance is already down at 2 m from the
Volvo work, which is correct for this scale.

## Scene 4 - Probe

*Precision. The hardest proof.*

A tube winds from +0.20 m behind the glass out to -0.10 m in front. Thread the pen tip along
it without touching the wall. Timed, scored, instantly repeatable.

**Interaction.** Tip inside the tube volume; contact with the wall triggers a haptic buzz, a
red flash and a time penalty. Procedurally generated, so difficulty scales and it is not an
art task.

**Why it is last, and why it matters.** It is the purest possible test of whether the tracked
tip and the rendered geometry actually occupy the same point in the room. If M0 shows the tip
is accurate to a couple of millimetres, this scene is remarkable. If it is out by two
centimetres, this scene is impossible - and so is Stack, and the whole suite becomes
ray-based. That is why M0 comes first.

**Guard.** The path must never approach the frame edges while at negative parallax. The
generator enforces this against `StereoVolume` rather than trusting the author.

---

## Shared core

New assembly `Scripts/Showcase/Runtime/KmaxShowcase.asmdef`, deliberately separate from
`KmaxDisplayExample`. The existing runtime is a catalogue-driven inspect-and-explode stack -
`EyeAnatomyController`, `EyeFocusView`, `EyeExplodeView`. This is a direct-manipulation
stack. They are different shapes, and merging them would damage both.

**Reused from the existing module:** `ProceduralAudio`, `UiAlwaysOnTop`, `UiButtonMotion`,
`ExhibitPostProcessing`, and the `ExhibitAttractMode` pattern. The Kmax plumbing -
world-space canvas with `UIScaler` and an explicit event camera, `KmaxInputModule` on the
`EventSystem` - carries over unchanged, and is the most expensive thing here not to have to
rediscover.

### `StereoVolume` - the budget as an object

The single most valuable reusable piece, and nothing in the SDK does it.

- Derives `PopOutLimit`, `DepthLimit` and `Window` from `XRRig.Screen`, `XRRig.ViewScale`
  and `StereoCamera.DefaultDistance`. No hard-coded 0.13.
- `Contains(Vector3)`, `ClampToComfort(Vector3)`
- **`ViolatesWindow(Bounds)`** - true when bounds carry negative parallax *and* cross a
  frustum edge. The failure mode that breaks pop-out, detectable in one call.
- Subscribes to `OnViewScaleChanged`
- Editor gizmo drawing the volume, so content can be authored against it

### `StylusTip` - the pen as a physical object

- Sphere collider at the tip, with enter/exit/stay events
- **Calibration offset** between the reported pose and the visible physical tip.
  Non-optional; M0 measures it.
- Velocity tracking, for throws and burst force
- Rate-limited haptics. The Volvo already learned this the hard way: cap to roughly one
  pulse per 0.25 s, or it buzzes continuously and becomes unpleasant.

### `StylusGrab` + `Grabbable`

Tip-proximity grab, never a raycast. Nearest grabbable within radius on press, spring-lagged
follow, release with velocity into physics. Haptic on both grab and release.

### `ShowcaseScene` + `ShowcaseHub`

Lifecycle `Attract -> Engaged -> Playing -> Resolved -> Reset`, with an idle timer that
returns the scene to attract after inactivity. The hub loads scenes additively and is the
client-demo driver.

### `ComfortOverlay`

Runtime diagnostic: draws the volume, flags anything violating it, reports where content
sits in the budget. Dev-only, key-toggled. This is a large part of what makes the suite a
credible internal reference rather than four demos.

---

## Milestones

Estimates are working days, and are **provisional until M0 lands** - M0 can invalidate the
interaction model for M3 and M4 entirely.

| | Milestone | Est. | Gate |
|---|---|---|---|
| **M0** | Ground truth on hardware | 0.5 | Is tip co-location accurate enough to build on? |
| **M1** | Shared core | 2-3 | `StereoVolume` + `StylusTip` proven in a test scene |
| **M2** | Bloom | 1-2 | Pop-out reads; tip collision works; comfortable over minutes |
| **M3** | Stack | 3-4 | Grab feels co-located; stacking is possible and fails legibly |
| **M4** | Probe | 2-3 | Precision task completable; contact detection trustworthy |
| **M5** | Reef | 4-6 | Art pipeline; schooling reads as depth |
| **M6** | Hub, attract, polish, docs | 3-4 | Runs unattended for a day without intervention |
| | **Total** | **16-23** | |

### M0 is a gate, not a formality

Everything after it assumes the tracked pen tip and the rendered geometry occupy the same
physical point to within a few millimetres. That is an assumption, not a measurement - the
existing exhibits use the stylus as a ray-caster, so nothing in this project has ever needed
it to be true.

If it fails, Stack and Probe do not survive in their current form, and the suite pivots to
ray-based manipulation. Better to learn that in half a day than in M3.

The same spike answers three other open questions cheaply: whether 0.13 m of pop-out is
actually comfortable on this panel, how head tracking behaves when a viewer leans or steps
away, and what the end-to-end latency is - tip-grab needs low latency or it stops feeling
co-located regardless of how spatially accurate it is.

---

## Task tracker

Status: `[ ]` todo, `[~]` in progress, `[x]` done, `[!]` blocked.

### M0 - Ground truth

- [ ] `S0-1` Spike scene: depth-graded markers at known Z, tip-tracked cursor, on-screen ruler
- [ ] `S0-2` Measure the offset between reported stylus pose and physical tip; record the number
- [ ] `S0-3` Measure end-to-end tip latency
- [ ] `S0-4` Judge 0.13 m pop-out for comfort over a 3-minute session
- [ ] `S0-5` Check head tracking under lean, step-back, and the 3 s eye-lost path
- [ ] `S0-6` Check the wallpaper effect on a regular tile grid, and fusion of semi-transparent
      surfaces over solid ones - both flagged on the Volvo, neither ever verified in stereo
- [ ] `S0-7` **Gate decision**: tip-grab or ray-grab. Record in decisions.md either way

### M1 - Shared core

**Built 2026-09-30.** Ten types in `Scripts/Showcase/`, compiling clean with 0 errors and
0 warnings. What is *verified* and what is merely *written* is split out below the list -
the distinction matters more here than usual, because half of this cannot be judged without
the hardware.

- [x] `S1-1` `KmaxShowcase.asmdef` and folder structure
- [x] `S1-2` `StereoVolume`, with limits derived from the SDK rather than hard-coded
- [x] `S1-3` `StereoVolume.ViolatesWindow(Bounds)`
- [x] `S1-4` `StereoVolume` editor gizmo - `StereoVolumeGizmo`
- [x] `S1-5` `StylusTip` with contact, events, and the calibration offset from `S0-2`
- [x] `S1-6` Rate-limited haptic helper - `StylusHaptics`
- [x] `S1-7` `Grabbable` + `StylusGrab` with spring-lagged follow
- [x] `S1-8` `ShowcaseScene` lifecycle + idle reset
- [x] `S1-9` `ComfortOverlay` runtime diagnostic
- [x] `S1-10` Editor validator - `Kmax/Showcase/Audit Comfort Volume`

**Verified by measurement, not by reading:**

- The budget derives correctly: **130.0 mm of pop-out and 300.0 mm of depth**, read back off a
  live rig rather than asserted.
- `ViolatesWindow` passes six cases, including the one that matters: a point at 0.9 of the
  half-window violates when fully popped out (67.0 mm overshoot) and does not violate at the
  same X on the glass. Hand-checked - the worst corner projects to 365.9 mm against a
  298.9 mm half-window.
- `ExceedsComfortDepth`, `Contains` and `ClampToComfort` all behave at the limits.
- All seven components instantiate; the audit menu item is registered.

**Written but not verified, and why:**

- Everything touching the pen. `StylusTip`, `StylusHaptics` and `StylusGrab` cannot be judged
  without hardware. `tipOffset` is still **zero**, which is almost certainly wrong - that is
  `S0-2`'s output.
- `followTime` is at 0.04 s on no evidence at all. It is the first thing to tune on a device.
- The gizmo and the overlay are drawn but have not been looked at in stereo.

**Deliberately ahead of the gate:** `S1-7` was listed as depending on `S0-7`, and instead
ships with **both** selection modes - tip proximity and ray - behind one enum. If M0 says the
tip cannot be trusted, that is a field change rather than a rewrite, and the rest of the grab
system is unaffected either way. Building both cost about an hour and removes the gate's
ability to invalidate M1.

**Not in the plan, added during the build:** a mouse fallback on `StylusTip` and `StylusGrab`,
so all four scenes can be exercised in the editor before the hardware is available. It is not
co-location and it cannot answer whether an interaction feels right - it only proves the logic
runs. `StylusTip.IsTracked` is false while it drives, so a scene can refuse to score a run
that was not done on the pen.

### M2 - Bloom

**Built 2026-09-30.** `Scenes/Showcase/Bloom.unity`, authored end to end by
`Kmax/Showcase/Set Up Bloom`. Compiles and runs clean: 0 errors, 0 warnings, including through
play-mode exit.

- [x] `S2-1` Mote sim with forward drift crossing the screen plane
- [x] `S2-2` Motes fade out before reaching the frame edges
- [x] `S2-3` Ribbon trails and depth-graded size
- [x] `S2-4` Screen-plane reference grid
- [x] `S2-5` Tip contact: burst, haptic tick, chime
- [x] `S2-6` Attract loop
- [x] `S2-7` `Kmax/Showcase/Set Up Bloom` idempotent build step
- [ ] `S2-8` Verify on hardware

**Verified in play mode:**

- **The field genuinely crosses the glass** - 48 motes, with 10 in front and 38 behind at one
  sample, spanning -87 to +209 mm against a -130 to +300 mm budget. That crossing is the whole
  claim of the scene, so it is the thing that had to be measured rather than eyeballed.
- **Zero window violations**, continuously, with every mote's bounds tested against
  `StereoVolume.ViolatesWindow` each sample.
- **The burst path works end to end** - 90 bursts in one run, each emitting exactly the
  configured 14 particles.
- **The lifecycle runs the whole way round**: Attract to Engaged on arrival, to Playing on the
  first burst with the clock starting, back to Attract on idle, with the score cleared.
- **The build step is idempotent** - re-run on the finished scene, 5 roots and 18 transforms
  before and after.
- The generated material is additive: `_DstBlend = One`, `_ZWrite = 0`, transparent queue.

**`S2-2` was built differently from the plan.** It was written as a tuned lifetime, and became a
continuous fade driven by `StereoVolume.ProjectedMargin`. The reason is worth keeping: a mote
drifting straight at the viewer **runs out of window margin without moving sideways at all**,
because projection from the eye magnifies anything in front of the glass. A lifetime tuned
against that is a guess that breaks the moment the view scale or the panel changes; a margin
test is exact and self-correcting.

**Not verified, and cannot be here:** the haptic tick and the chime were exercised as code paths
but neither was felt nor heard, and nothing has been judged in stereo. Trails, the depth-graded
size and the grid are all written and running but have only been seen flat.

### M3 - Stack

**Built 2026-09-30.** `Scenes/Showcase/Stack.unity`, authored by `Kmax/Showcase/Set Up Stack`.
0 errors, 0 warnings.

- [x] `S3-1` Bench, blocks, platform at the screen plane
- [x] `S3-2` Physics: rigidbodies, continuous collision, cast shadows
- [x] `S3-3` Tip grab and release with throw velocity
- [ ] `S3-4` Spring-lag tuning pass on real hands - **needs hardware**
- [x] `S3-5` Haptics: grab, release, block-on-block knock
- [x] `S3-6` Height scoring and topple detection
- [x] `S3-7` Reset
- [x] `S3-8` Attract loop
- [x] `S3-9` `Kmax/Showcase/Set Up Stack` build step
- [ ] `S3-10` Verify on hardware

**Verified in play mode:**

- **Grab works through the real code path.** The tip finds the block it is inside, takes hold,
  and the body stays **non-kinematic with gravity off** - which is the design decision the whole
  scene rests on, because a kinematic block would slide through the tower instead of knocking it.
- **The held block tracks the tip exactly**, and a deliberately off-centre grab of 16.3 mm is
  carried at 16.3 mm.
- **The comfort clamp holds.** Dragged to 300 mm in front of the glass, the block stops at
  exactly -130.0 mm, the pop-out limit.
- **Scoring is exact**: one block placed on the platform reads as 1 block and 23.3 mm, which is
  one block height.
- **Zero window violations** across every renderer, and nothing outside the depth budget.
- **The build step is idempotent** - 6 roots and 32 transforms before and after.

**`S3-1` deviates slightly from the plan, deliberately.** The platform was specified at exactly
zero parallax. It is built just behind the glass instead, at 12% of the depth budget, so that its
**whole footprint stays behind the screen plane**. Centred on zero it would have extended
forward of the glass, and a wide flat surface at negative parallax is precisely what the scope
guards rule out. The design intent survives intact: the target is effectively on the glass and
the only thing that ever pops out is the block in the viewer's hand.

**Not verified, and cannot be here:** `followTime` is still 0.04 s on no evidence. It is the
spring lag between hand and block, it decides whether the scene feels like holding an object or
like dragging a rendering artefact, and no amount of editor testing can judge it.

### M4 - Probe

**Built 2026-09-30.** `Scenes/Showcase/Probe.unity`, authored by `Kmax/Showcase/Set Up Probe`.
0 errors, 0 warnings.

- [x] `S4-1` Procedural path generator, crossing the screen plane
- [x] `S4-2` Generator respects the window at negative parallax
- [x] `S4-3` Wall contact detection
- [x] `S4-4` Buzz, flash, time penalty
- [x] `S4-5` Timer, scoring, best time
- [x] `S4-6` Difficulty scaling
- [x] `S4-7` Attract loop
- [x] `S4-8` `Kmax/Showcase/Set Up Probe` build step
- [ ] `S4-9` Verify on hardware

**Verified in play mode:**

- **The tunnel crosses the glass**: mouth at -94 mm, far end at +240 mm, length 346 mm.
- **`S4-2` tested across the generator's whole range, not one sample.** 200 tunnels over five
  difficulties: **zero break the stereo window.** The lumen ranges 16.5 mm down to 7.4 mm as
  difficulty rises.
- **Contact detection and the penalty cooldown are exact.** Held outside the lumen for ten
  seconds with a 0.5 s cooldown gave twenty contacts and forty seconds of penalty, and the clock
  matched elapsed plus penalties to the decimal.
- **The tunnel flashes red on contact** and green on completion, driven through a property block.
- **A full run works**: entered at the mouth, ran clean, finished, best time 5.68 s recorded,
  difficulty stepped to 0.20 and the next tunnel tightened from 16.5 mm to 14.7 mm - exactly the
  predicted value.
- **The attract marker travels the tunnel** while nobody is playing and hides the moment a run
  starts.
- **Zero window violations and nothing outside the depth budget**, and the build step is
  idempotent at 5 roots and 18 transforms.

**Two bugs found and fixed here, both by measurement rather than by reading:**

*Difficulty scaling did not work at all.* Finishing a run resolved the scene, which auto-reset to
Attract a few seconds later - and Attract means somebody new has arrived, so it cleared the
difficulty and the best time. Consecutive runs never got harder. Probe now manages its own
replay and only the idle timeout resets a player.

*The attract marker saved into the scene as a one-metre sphere at the origin*, because the game
only sizes it at runtime. Far outside the depth budget and breaking the window. The comfort audit
caught it - which is the first time the tooling built in M1 found something on its own.

### M5 - Reef

- [ ] `S5-1` Source fish and coral; record licences and CC-BY attributions
- [ ] `S5-2` Boids schooling inside the comfort volume
- [ ] `S5-3` Layered static occluders at varying depth
- [ ] `S5-4` Hero fish approach-and-withdraw behaviour
- [ ] `S5-5` Tip touch: dart + haptic flick
- [ ] `S5-6` Feeding
- [ ] `S5-7` Caustic light cookie
- [ ] `S5-8` Attract loop (this scene is its own attract loop)
- [ ] `S5-9` `Kmax/Showcase/Set Up Reef` build step
- [ ] `S5-10` Verify on hardware

### M6 - Suite

- [ ] `S6-1` `ShowcaseHub` with additive loading
- [ ] `S6-2` Scene selection UI - world-space canvas, `UIScaler`, explicit event camera
- [ ] `S6-3` Guided demo sequence in narration order
- [ ] `S6-4` Idle reset across the whole suite
- [ ] `S6-5` Analytics via the base project's `KioskAnalyticsEvents`
- [ ] `S6-6` Register scenes with `ViitorCloudGameInfoSO` / `EditorBuildSettings`
- [ ] `S6-7` Clipping measurement per scene, against the module's existing bar
- [ ] `S6-8` architecture.md, decisions.md, ai_handoff.md
- [ ] `S6-9` Unattended run for a full day without intervention

---

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Tip co-location inaccurate | Kills Stack and Probe as designed | `M0` gates everything; ray-based fallback |
| Latency too high for grab | Grab feels detached, not co-located | Measured in `S0-3`; spring lag masks a little, not a lot |
| 0.13 m pop-out uncomfortable in practice | Weakens every scene | `S0-4`; the budget scales with `ViewScale` |
| Reef art pipeline overruns | M5 slips | Stylised low-poly first, realistic as an upgrade |
| Wallpaper effect on regular patterns | Surfaces jump in depth | `S0-6`; avoid regular grids, or break them up |
| Semi-transparent over solid fails to fuse | Affects any glass | `S0-6`; avoid in all four scenes |

## Scope guards

Decided up front, so they are not relitigated mid-build:

- **No reliance on post-processing.** `VRRenderer`'s sub-cameras have no
  `UniversalAdditionalCameraData`, so Volumes never apply to them. Depth of field would be
  wrong in stereo anyway - it fights the viewer's own focus.
- **No text at negative parallax.** Reading at a different depth from the surrounding
  surface is the fastest route to eye strain. UI sits at or just behind the screen plane.
- **No fast motion in Z.** Vergence cannot track it, and it reads as a glitch.
- **No semi-transparent surfaces in front of solid ones.** Two depths at one pixel is not
  fusible.
- **No regular high-contrast repeating patterns.** Wallpaper effect.
- **Every scene gets an idempotent build step**, matching the module's existing pattern.
  Each converges on the spec from wherever the scene is - the mistake that cost two sessions
  on the Volvo was a build step that returned early instead of re-applying.

# 3D movement timing: regression and rollback

This note records the local Immersive UI investigation on 2026-11-08. It explains
why the 3D movement timing was rolled back, what remains in place, and what a
future attempt at frame-rate-independent movement would need to test. Expanded
was not part of this change.

## History

| Revision | Change |
| --- | --- |
| `5530e38c` | Added display interpolation for 2D exploration. No 3D translation interpolation yet. |
| `df508f72` | Added display interpolation for accepted 3D walking and strafing. Mouse and keyboard yaw remained immediate. This is the working comparison build before the timing change. |
| `fdc6d614` | Preserved fractional ticks in the general clocks, scheduled movement using elapsed time, and added temporary Shift sprint. Modern 3D movement became 60 logical steps per second, independently of update frequency. |
| `ea85f5f6` | Fixed 2D visual jumps when crossing adjacent world-map sections. This is a separate issue from the 3D regression. |
| Local rollback following `ea85f5f6` | Restored the earlier 3D movement cadence while retaining the independent 2D, clock, and input fixes. |

These revisions describe the Immersive UI branch. The startup choice between
Original and Advanced game data does not select a different camera scheduler.

## What the player observed

The player compared the installed build with the older `df508f72` build. While
circling a tree billboard or weaving between objects using mouse-look, the newer
build appeared to vibrate and stutter. The ground and walls also stuttered. The
older 3D build did not show that behavior in the same informal comparison.

The player had also tried an older build on a laptop with a 240 Hz display and
reported normal movement rates. Display refresh rate is not the same as game
update frequency, so that observation does not establish that the old movement
code is independent of hardware timing. It is useful evidence that the older
build was satisfactory on the two systems actually used.

## What the code and probe established

The `Camera3D` implementation is identical in `df508f72` and `fdc6d614`. The
regression was introduced by changing the timing of the movement samples sent to
that existing interpolator, rather than by changing billboard artwork or shaders.

Relevant code:

- [`Game/Movement.cs`](../Ambermoon.Core/Game/Movement.cs): movement scheduling and the duration supplied for visual interpolation.
- [`Game/Lifecycle.cs`](../Ambermoon.Core/Game/Lifecycle.cs): general clock advancement and update dispatch.
- [`Player3D.cs`](../Ambermoon.Core/Render/Player3D.cs): accepted movement, collision/event processing, and camera capture.
- [`Camera3D.cs`](../Ambermoon.Renderer.OpenGL/Camera3D.cs): the accepted path queue, playback speed, and render position.
- [`Billboard3DShader.cs`](../Ambermoon.Renderer.OpenGL/Billboard3DShader.cs): billboard centers use the same model-view transform as the scene.

The older scheduler rounded `60 * deltaTime` independently on each update and
discarded the remainder after a movement repeat. At an approximately 84 Hz update
cadence, a modern 3D movement step was normally delivered on every update.

The normalized scheduler instead delivered 60 equal movement steps per second.
At 84 updates per second, successive steps arrived after either one update
(approximately 11.9 ms) or two updates (approximately 23.8 ms).

`Camera3D.RecordMovement` estimates a batch's playback duration from the time
since the previous batch when that interval is within its acceptance bounds. It
also increases playback speed when needed to drain a queued path within a short
limit. With irregularly arriving fixed-size steps, these rules change display
speed repeatedly. Correct total distance over two seconds therefore does not
guarantee even motion from one rendered frame to the next.

A deterministic probe used the actual compiled `Camera3D` class, an injected
clock, 0.04-unit movement steps, and straight, circular, and slalom paths. It
sampled the render position at regular intervals, discarded the first second,
and measured the following five seconds. Selected straight-path results:

| Sample/update rate | Delivery pattern | Mean display speed | Display speed range | Relative speed standard deviation |
| --- | --- | --- | --- | --- |
| 60 Hz | Fixed 60 steps/s | 2.400 units/s | 2.400–2.400 | effectively zero |
| 75 Hz | Fixed 60 steps/s | 2.400 units/s | 0.600–3.000 | 38.7% |
| 84 Hz | Fixed 60 steps/s | 2.400 units/s | 1.440–3.360 | 35.5% |
| 120 Hz | Fixed 60 steps/s | 2.400 units/s | 2.400–2.400 | effectively zero |
| 144 Hz | Fixed 60 steps/s | 2.400 units/s | 1.920–2.880 | 20.0% |
| 240 Hz | Fixed 60 steps/s | 2.400 units/s | 2.400–2.400 | effectively zero |
| 75 Hz | One step on every update | 3.000 units/s | 3.000–3.000 | effectively zero |
| 84 Hz | One step on every update | 3.360 units/s | 3.360–3.360 | effectively zero |

Circular and slalom paths produced essentially the same speed variation at 75
and 84 Hz. There were no completely stationary frames in these probe cases: a
large repeated speed change can look like stuttering without a full stop.

The probe's `regular-every-update` cases are a regular-delivery reference. They
directly inject one movement step per update; they do **not** execute the old
game scheduler. In particular, their synthetic 144/240 Hz results must not be
read as measurements of how the old game would actually behave at those update
rates. The old independent rounding has its own high-frequency edge cases.

This establishes a camera pacing defect under the tested schedules. It strongly
supports the reported whole-scene vibration, but is not a recording of the
player's exact route, GPU presentation, mouse input, or physical monitor. The
additional effect of immediate yaw against uneven translation while circling is
an explanation consistent with the report, not a separate measured result.

## Rollback scope and tradeoff

The rollback restores a separate legacy timing path for 3D movement, including
its observed movement duration for the existing display interpolator. It does
not restore the old rounding behavior to all game clocks.

The following remain in place:

- 2D display interpolation, elapsed-time movement, and smooth adjacent world-map section crossings.
- General game, animation, popup, battle, and selector clocks that preserve fractional ticks.
- The saved Caps Lock walking/sprint selection, 60% fresh walking default, and temporary Shift sprint.
- Existing 3D translation interpolation, immediate mouse-look, collision checks, accepted corner paths, and event handling.
- Immersive UI, balance selection, save format, and other unrelated changes.

This prioritizes the earlier 3D behavior that the player found satisfactory. It
does not make 3D movement frame-rate-independent: the restored repeat cadence
still depends on update intervals. A 240 Hz monitor alone does not determine
those intervals. Different frame caps, update scheduling, load, or future window
library changes can affect the result. Preserving this limitation is deliberate
for this rollback, rather than a claim that the old timing is universally correct.

The implementation keeps a dedicated rounded `movement3DTicks` counter and a
short rolling observation of that counter's cadence. Only 3D repeat scheduling
and the camera's batch identifiers use it. Collision and event timestamps still
use the corrected shared clock. Passing the dedicated counter to the camera is
necessary: several old-style movement updates can otherwise share one corrected
game tick and be incorrectly grouped together. Current focus, menu, and input
reset guards are retained. This is a targeted restoration of the old movement
path, not a byte-for-byte rollback of the whole engine.

## Rollback verification

Native windowed tests compared 54 routes of 192 frames each with `df508f72`:
**10,368 logical and displayed camera samples matched exactly**. Routes included
forward/backward movement, strafing, turning, circles, slaloms, and collisions at
60, 75, 84, 119, and 120 Hz plus alternating 119/121 Hz updates. Exact-120 Hz
rounding behavior was intentionally compared with the old build, not treated as
a promise of refresh-independent movement. General clocks were checked
separately because their corrected behavior is intentionally retained.

The focused movement/control/clock suite passed 337 assertions. A separate 219
assertions verified 2D section crossings, world wrapping, long walks, logical
histories, and edge tiles: **556 assertions in total**. An initial combined
fixture run carried UI/input state between unrelated tests; running those checks
from clean fixture state resolved the failures without production changes.

The graphics check exercised all **45 combinations** of None/Smooth/Blur,
None/Lines/Grid/Scanline/CRT, and None/Grayscale/Sepia in a 1280×800 native window.
It rendered actual moving 3D geometry with intermediate camera positions and
captured screenshots for pixel checks. Checks covered visible filter/overlay
changes, complete scene output, equal grayscale channels, and sepia colour
ordering. This verifies functional compatibility, not GPU performance on every
machine or a cure for the original temporal pacing problem.

The diagnostic harness also reported OpenGL `InvalidOperation`/`InvalidValue`
flags during drawing. The unfiltered `df508f72` reference fixture produced the
same flags; images and movement still rendered. No renderer change was made to
address these pre-existing or harness-related flags. The graphics result must
not be described as a completely error-free OpenGL stream. Native presentation
and perceived smoothness on the player's two machines still merit a manual
comparison after installation.

Local detailed logs and screenshots are archived with the installed build under
`Verification/Rollback3D` and `Verification/GraphicsFilters`; the portable probe
below preserves the minimal camera reproduction in source control.

Follow-up on 2026-11-09: the exploration action grid is now explicitly non-modal.
Held keyboard/mouse-arrow movement and camera interpolation continue while it
is visible; only mouse-look capture is released for clicking its buttons. Its
selection survives actions and temporary UI screens. Options, dialogs, and
focus loss still block movement. Older tests that expected merely opening the
action grid to stop movement must use those actual blocking states instead.
This UI correction does not change the restored 3D cadence.

## Why earlier checks passed

The timing tests verified equal elapsed-time distance at many update rates,
clock accumulation, sprint input, collision/event counts, bounded catch-up, and
teleport resets. The camera tests verified intermediate positions, actual
model-view translation, immediate yaw, accepted corner paths, bounded lag, and
settling when interfaces or focus interrupt movement.

Those checks did not measure frame-to-frame display velocity when the new 60 Hz
movement schedule was combined with a different render/update rate. The earlier
jittered camera test also fed its own accepted-step sequence, rather than the new
schedule. Correct distances and collision paths therefore passed while this
perceptual defect remained. Future timing work needs both kinds of test.

## Reproducing and extending the comparison

For a manual comparison, use copies of the same save and settings in separate
directories. Run windowed, select the same Original/Advanced data, mouse-look,
and Caps Lock speed, and use the same graphics filter. Walk forward, strafe in a
circle around a nearby object, then weave between objects while turning with the
mouse. Include walls and ground in the view. Avoid changing graphics settings or
speed during a single comparison. The operating system's Caps Lock light does
not identify the game's saved speed selection.

For a deterministic regression test, exercise the actual scheduler and camera
together at 60, 75, 84, 120, 144, and 240 samples per second, plus jitter. Record
both logical and render positions, step arrival times, mouse yaw, and per-frame
velocity. Add different update/render rates, both movement speeds, diagonal
input, collision slides, stops, focus changes, teleports, and short stalls.
Verify accepted path boundaries as well as distance and frame pacing.

The repository includes a portable [camera timing probe](../tools/CameraTimingProbe/README.md)
with its invocation and interpretation. It reproduces the camera-only evidence
above against a published build without loading a save or opening a game window.
Its source also records the distinction between a regular-delivery reference
and the real legacy scheduler. Full scheduler and native-window tests are still
needed for any production timing change.

## Possible future approaches

The most direct future design is interpolation on a fixed simulation timeline:
timestamp accepted movement endpoints using their scheduled step times, retain
the scheduler's fractional phase, and sample a short interval behind that
timeline. Camera playback would then use simulation time instead of estimating
velocity from irregular arrival times. Multiple accepted legs within one step
must retain collision-resolved corners, and long stalls and interrupted input
still need bounded reset behavior. Extrapolation through unchecked space should
be avoided.

Another possibility is elapsed-time-scaled 3D translation on each update, with
bounded substeps for collision and events. That is a wider gameplay change:
interaction frequency, diagonal movement, mobile/legacy modes, and collision
behavior would need renewed verification. Merely fixing the interpolation
duration at 1/60 second is insufficient evidence of a fix, because an empty
queue can still finish early and wait for the next sample.

Graphics filters and post-processing address spatial appearance, not movement
sample timing. They can change how sharp or conspicuous motion looks, and an
expensive effect can affect frame time, but they do not repair the scheduling
mismatch established by the camera-only probe. Filter changes should be tested
separately from any future timing experiment.

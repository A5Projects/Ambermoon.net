# Camera timing probe

This small .NET 9 console program loads `Camera3D` from an existing published
Immersive UI build and supplies a deterministic clock. It needs no extra NuGet
packages, game data, save files, or graphics context. It writes only to the given
output directory and its own normal `bin`/`obj` build directories.

From the repository root:

```powershell
dotnet run --project tools/CameraTimingProbe -- "C:\path\to\published-build" "tools\CameraTimingProbe\results"
```

The build directory must contain `Ambermoon.Renderer.OpenGL.dll` and its managed
dependencies. Use a build containing the Immersive UI `Camera3D` interpolation
implementation, such as `df508f72` or the later timing experiment. Existing JSON
files with the same names in the output directory will be overwritten.

The probe runs 36 cases: two delivery patterns, six sample rates (60, 75, 84, 120,
144, 240 Hz), and three paths (straight, circle, slalom). Every case has a one-second
warm-up followed by five measured seconds. `summary.json` contains the display
speed range, mean, standard deviation, largest change in speed between samples,
and count of stationary samples. Separate JSON files contain the frame samples.

The delivery patterns are:

- `regular-every-update`: inject one 0.04-unit movement on every sampled update.
- `fixed-60`: accumulate time and inject 60 equal movements per second, as in the normalized modern 3D timing experiment.

Both patterns use the actual compiled camera's accepted-path interpolation and
immediate yaw. Neither runs the full game scheduler, collision handling, event
system, input, rendering, or display presentation. In particular,
`regular-every-update` does **not** emulate the old scheduler's per-update tick
rounding. Its 144/240 Hz results cannot establish how the old game would run at
those actual update rates. Synthetic sample rate is also not monitor refresh rate.

This is a diagnostic probe, not a pass/fail assertion that the current camera
must handle the abandoned fixed-60 schedule. The deliberate rollback changes
the game scheduler; the camera itself remains unchanged, so it should reproduce
the same problematic fixed-60 results until a future camera redesign addresses
that schedule. Compare total distance **and** display speed variation when
evaluating such a redesign.

See [the investigation note](../../docs/3d-movement-timing.md) for the measured
regression, rollback scope, limitations, and possible future work.

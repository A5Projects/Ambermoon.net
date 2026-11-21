using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CameraTimingProbe <published-build-directory> <output-directory>");
    return 2;
}

string build = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string rendererPath = Path.Combine(build, "Ambermoon.Renderer.OpenGL.dll");
if (!File.Exists(rendererPath))
{
    Console.Error.WriteLine($"Renderer assembly not found: {rendererPath}");
    return 2;
}

Directory.CreateDirectory(output);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(build, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};

var assembly = Assembly.LoadFrom(rendererPath);
var type = assembly.GetType("Ambermoon.Renderer.OpenGL.Camera3D", throwOnError: true)!;
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
object? Invoke(object camera, string name, params object?[] values)
    => type.GetMethod(name, flags)!.Invoke(camera, values);
double Value(object camera, string name)
    => Convert.ToDouble(type.GetProperty(name, flags)!.GetValue(camera));

static double AngleDiff(double from, double to)
{
    double diff = (to - from) % 360.0;
    if (diff > 180.0) diff -= 360.0;
    else if (diff < -180.0) diff += 360.0;
    return diff;
}

// 1. Standard rate / route / jitter sweep across delivery modes.
var reports = new List<object>();
foreach (string mode in new[] { "regular-every-update", "fixed-60", "timeline-60" })
foreach (double jitterMs in new[] { 0.0, 0.9 })
foreach (int hz in new[] { 60, 75, 84, 120, 144, 240 })
foreach (string route in new[] { "straight", "circle", "slalom", "diagonal", "keyturn" })
{
    // Keep legacy modes to the original three routes; timeline tests all five.
    if (mode != "timeline-60" && route is "diagonal" or "keyturn")
        continue;

    bool regularDelivery = mode == "regular-every-update";
    bool timelineDelivery = mode == "timeline-60";
    var random = new Random(1234 + hz);
    double now = 0, previousNow = 0, nextStep = double.NaN;
    var camera = Activator.CreateInstance(type, flags, null,
        new object?[] { null, (Func<double>)(() => now) }, null)!;
    Invoke(camera, "SetPosition", 0f, 0f, 0f);
    Invoke(camera, "TurnTowards", 0f);
    double accumulator = 0;
    var velocities = new List<double>();
    var angularVelocities = new List<double>();
    var samples = new List<object>();
    double previousX = 0, previousZ = 0, previousAngle = 0, maxDeltaVelocity = 0;
    double previousVelocity = double.NaN;

    for (int frame = 1; frame <= hz * 6; frame++)
    {
        now = frame / (double)hz + (jitterMs == 0 ? 0 : (random.NextDouble() * 2 - 1) * jitterMs / 1000.0);
        double deltaTime = Math.Max(1e-6, now - previousNow);
        previousNow = now;

        // Mouse-look routes rotate immediately outside timeline steps.
        if (route is "circle" or "slalom")
        {
            float angle = route == "circle"
                ? (float)(now * 90)
                : (float)(35 * Math.Sin(now * 2 * Math.PI / 2.5));
            Invoke(camera, "TurnTowards", angle);
        }

        accumulator += deltaTime;
        int steps = regularDelivery ? 1 : (int)Math.Floor((accumulator + 1e-9) * 60);
        if (timelineDelivery)
        {
            if (double.IsNaN(nextStep))
                nextStep = now;
            steps = 0;
            while (nextStep <= now + 1e-9 && steps < 3)
            {
                Invoke(camera, "BeginTimelineStep", nextStep, 1.0 / 60);
                if (route == "diagonal")
                {
                    Invoke(camera, "MoveLeft", .04f, false, false);
                    Invoke(camera, "MoveForward", .04f, false, false);
                }
                else if (route == "keyturn")
                {
                    Invoke(camera, "TurnRight", 2.0f);
                    Invoke(camera, "MoveForward", .04f, false, false);
                }
                else
                {
                    Invoke(camera, "MoveForward", .04f, false, false);
                }
                Invoke(camera, "EndTimelineStep");
                nextStep += 1.0 / 60;
                steps++;
            }
        }
        else
        {
            if (!regularDelivery)
                accumulator -= steps / 60.0;

            for (int step = 0; step < steps; step++)
            {
                uint tick = (uint)(regularDelivery ? frame : Math.Floor(now * 60 + 1e-9));
                Invoke(camera, "BeginMovementInterpolation", tick, Math.Max(1, steps) / 60.0);
                Invoke(camera, "MoveForward", .04f, false, false);
                Invoke(camera, "EndMovementInterpolation");
            }
        }

        Invoke(camera, "UpdateMovementInterpolation");
        double x = Value(camera, "RenderX"), z = Value(camera, "RenderZ");
        double renderAngle = Value(camera, "RenderAngle");
        double speed = Math.Sqrt((x - previousX) * (x - previousX) +
            (z - previousZ) * (z - previousZ)) / deltaTime;
        double angSpeed = Math.Abs(AngleDiff(previousAngle, renderAngle)) / deltaTime;
        if (frame > hz) // Discard one second of startup; measure five seconds.
        {
            velocities.Add(speed);
            angularVelocities.Add(angSpeed);
            if (!double.IsNaN(previousVelocity))
                maxDeltaVelocity = Math.Max(maxDeltaVelocity, Math.Abs(speed - previousVelocity));
            previousVelocity = speed;
            samples.Add(new
            {
                frame, time = now, x, z, renderAngle, speed, angSpeed,
                logicalX = Value(camera, "X"), logicalZ = Value(camera, "Z"),
                logicalAngle = Value(camera, "Angle"), steps
            });
        }
        previousX = x;
        previousZ = z;
        previousAngle = renderAngle;
    }

    double mean = velocities.Average();
    double standardDeviation = Math.Sqrt(velocities.Average(value => (value - mean) * (value - mean)));
    double angMean = angularVelocities.Average();
    double angStdDev = Math.Sqrt(angularVelocities.Average(value => (value - angMean) * (value - angMean)));
    string id = $"{mode}-j{jitterMs:0.0}-{hz}-{route}";
    var report = new
    {
        id, hz, route, mode, jitterMs, regularDelivery,
        minSpeed = velocities.Min(), maxSpeed = velocities.Max(), meanSpeed = mean,
        speedStdDev = standardDeviation, coefficientVariation = standardDeviation / mean,
        angMean, angCV = angMean > 1e-4 ? angStdDev / angMean : 0.0,
        maxDeltaVelocity, stationaryFrames = velocities.Count(value => value < .0001),
        frames = velocities.Count
    };
    reports.Add(report);
    Console.WriteLine(JsonSerializer.Serialize(report));
    File.WriteAllText(Path.Combine(output, id + ".json"), JsonSerializer.Serialize(samples));
}

// 2. Targeted edge-case assertions for timeline mode.
var assertions = new List<(string Name, bool Passed, string Detail)>();
void Assert(string name, bool condition, string detail)
{
    assertions.Add((name, condition, detail));
    Console.WriteLine($"[ASSERT] {(condition ? "PASS" : "FAIL")} {name}: {detail}");
}

// 2a. Mouse-look stays immediate (0 ms lag) during active timeline walk.
{
    double now = 0;
    var cam = Activator.CreateInstance(type, flags, null, new object?[] { null, (Func<double>)(() => now) }, null)!;
    Invoke(cam, "SetPosition", 0f, 0f, 0f);
    Invoke(cam, "TurnTowards", 0f);
    for (int i = 0; i < 10; i++)
    {
        now = i / 60.0;
        Invoke(cam, "BeginTimelineStep", now, 1.0 / 60);
        Invoke(cam, "MoveForward", 0.04f, false, false);
        Invoke(cam, "EndTimelineStep");
        Invoke(cam, "UpdateMovementInterpolation");
    }
    Invoke(cam, "TurnRight", 17.5f);
    double immediateAngle = Value(cam, "RenderAngle");
    double logicalAngle = Value(cam, "Angle");
    Invoke(cam, "UpdateMovementInterpolation");
    double afterAdvanceAngle = Value(cam, "RenderAngle");
    Assert("mouselook-immediate-during-walk",
        Math.Abs(immediateAngle - 17.5) < 1e-4 && Math.Abs(afterAdvanceAngle - 17.5) < 1e-4 && Math.Abs(logicalAngle - 17.5) < 1e-4,
        $"immediate={immediateAngle:F4}, afterAdvance={afterAdvanceAngle:F4}, logical={logicalAngle:F4}");
}

// 2b. Stop settling & zero extrapolation (never overshoots logical endpoint).
{
    double now = 0;
    var cam = Activator.CreateInstance(type, flags, null, new object?[] { null, (Func<double>)(() => now) }, null)!;
    Invoke(cam, "SetPosition", 0f, 0f, 0f);
    Invoke(cam, "TurnTowards", 0f);
    for (int i = 0; i < 30; i++)
    {
        now = i / 60.0;
        Invoke(cam, "BeginTimelineStep", now, 1.0 / 60);
        Invoke(cam, "MoveForward", 0.04f, false, false);
        Invoke(cam, "EndTimelineStep");
    }
    double finalLogicalZ = Value(cam, "Z");
    bool overshot = false;
    for (int f = 0; f < 20; f++)
    {
        now += 1.0 / 84.0;
        Invoke(cam, "UpdateMovementInterpolation");
        double rz = Value(cam, "RenderZ");
        // Moving forward at angle 0 moves Z positively (or negatively); check |rz| <= |finalLogicalZ| + eps.
        if (Math.Abs(rz) > Math.Abs(finalLogicalZ) + 1e-5)
            overshot = true;
    }
    double settledZ = Value(cam, "RenderZ");
    Assert("stop-no-extrapolation-and-settle",
        !overshot && Math.Abs(settledZ - finalLogicalZ) < 1e-5,
        $"overshot={overshot}, settledZ={settledZ:F6}, logicalZ={finalLogicalZ:F6}");
}

// 2c. Corner slide retains corner waypoint (does not cut diagonally across a 90-deg turn).
{
    double now = 0;
    var cam = Activator.CreateInstance(type, flags, null, new object?[] { null, (Func<double>)(() => now) }, null)!;
    Invoke(cam, "SetPosition", 0f, 0f, 0f);
    Invoke(cam, "TurnTowards", 0f);
    // 10 steps North, then 10 steps East (simulating corner slide).
    for (int i = 0; i < 10; i++)
    {
        now = i / 60.0;
        Invoke(cam, "BeginTimelineStep", now, 1.0 / 60);
        Invoke(cam, "MoveForward", 0.04f, false, false);
        Invoke(cam, "EndTimelineStep");
    }
    double cornerX = Value(cam, "X"), cornerZ = Value(cam, "Z");
    for (int i = 10; i < 20; i++)
    {
        now = i / 60.0;
        Invoke(cam, "BeginTimelineStep", now, 1.0 / 60);
        Invoke(cam, "MoveRight", 0.04f, false, false);
        Invoke(cam, "EndTimelineStep");
    }
    // Sample every 1 ms across the whole trajectory and check minimum distance to corner.
    double minDistToCorner = double.MaxValue;
    for (double t = 0; t <= 0.5; t += 0.001)
    {
        now = t;
        Invoke(cam, "UpdateMovementInterpolation");
        double dx = Value(cam, "RenderX") - cornerX;
        double dz = Value(cam, "RenderZ") - cornerZ;
        minDistToCorner = Math.Min(minDistToCorner, Math.Sqrt(dx * dx + dz * dz));
    }
    Assert("corner-slide-preserves-corner",
        minDistToCorner < 0.003,
        $"minDistToCorner={minDistToCorner:F6}");
}

// 2d. Reset/teleport snaps pose immediately.
{
    double now = 0;
    var cam = Activator.CreateInstance(type, flags, null, new object?[] { null, (Func<double>)(() => now) }, null)!;
    Invoke(cam, "SetPosition", 0f, 0f, 0f);
    Invoke(cam, "TurnTowards", 0f);
    for (int i = 0; i < 10; i++)
    {
        now = i / 60.0;
        Invoke(cam, "BeginTimelineStep", now, 1.0 / 60);
        Invoke(cam, "TurnRight", 2.0f);
        Invoke(cam, "MoveForward", 0.04f, false, false);
        Invoke(cam, "EndTimelineStep");
    }
    Invoke(cam, "SetPosition", 5f, 7f, 0f);
    Invoke(cam, "TurnTowards", 180f);
    Assert("teleport-snaps-immediately",
        Math.Abs(Value(cam, "RenderX") - (-5f)) < 1e-5 &&
        Math.Abs(Value(cam, "RenderZ") - 7f) < 1e-5 &&
        Math.Abs(Value(cam, "RenderAngle") - 180f) < 1e-5,
        $"Render=({Value(cam, "RenderX"):F2},{Value(cam, "RenderZ"):F2},{Value(cam, "RenderAngle"):F1})");
}

File.WriteAllText(Path.Combine(output, "summary.json"),
    JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));

return assertions.All(a => a.Passed) ? 0 : 1;

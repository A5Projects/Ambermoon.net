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

// This is a camera-only probe. It calls the real compiled Camera3D but supplies
// movement itself. It does not run GameCore, input, collision, OpenGL, or VSync.
// "regular-every-update" is NOT an emulation of the old rounded game scheduler:
// especially at synthetic 144/240 Hz, that scheduler could round ticks to zero.
var reports = new List<object>();
foreach (bool regularDelivery in new[] { true, false })
foreach (int hz in new[] { 60, 75, 84, 120, 144, 240 })
foreach (string route in new[] { "straight", "circle", "slalom" })
{
    double now = 0;
    // Null State is safe for these camera operations: none activates OpenGL.
    var camera = Activator.CreateInstance(type, flags, null,
        new object?[] { null, (Func<double>)(() => now) }, null)!;
    Invoke(camera, "SetPosition", 0f, 0f, 0f);
    Invoke(camera, "TurnTowards", 0f);
    double accumulator = 0;
    var velocities = new List<double>();
    var samples = new List<object>();
    double previousX = 0, previousZ = 0, maxDeltaVelocity = 0;
    double previousVelocity = double.NaN;

    for (int frame = 1; frame <= hz * 6; frame++)
    {
        double deltaTime = 1.0 / hz;
        now = frame * deltaTime;
        float angle = route switch
        {
            "circle" => (float)(now * 90),
            "slalom" => (float)(35 * Math.Sin(now * 2 * Math.PI / 2.5)),
            _ => 0
        };
        Invoke(camera, "TurnTowards", angle);
        accumulator += deltaTime;
        int steps = regularDelivery ? 1 : (int)Math.Floor((accumulator + 1e-9) * 60);
        if (!regularDelivery)
            accumulator -= steps / 60.0;

        for (int step = 0; step < steps; step++)
        {
            uint tick = (uint)(regularDelivery ? frame : Math.Floor(now * 60 + 1e-9));
            Invoke(camera, "BeginMovementInterpolation", tick, Math.Max(1, steps) / 60.0);
            Invoke(camera, "MoveForward", .04f, false, false);
            Invoke(camera, "EndMovementInterpolation");
        }

        Invoke(camera, "UpdateMovementInterpolation");
        double x = Value(camera, "RenderX"), z = Value(camera, "RenderZ");
        double speed = Math.Sqrt((x - previousX) * (x - previousX) +
            (z - previousZ) * (z - previousZ)) / deltaTime;
        if (frame > hz) // Discard one second of startup; measure five seconds.
        {
            velocities.Add(speed);
            if (!double.IsNaN(previousVelocity))
                maxDeltaVelocity = Math.Max(maxDeltaVelocity, Math.Abs(speed - previousVelocity));
            previousVelocity = speed;
            samples.Add(new
            {
                frame, time = now, x, z, speed,
                logicalX = Value(camera, "X"), logicalZ = Value(camera, "Z"), angle, steps
            });
        }
        previousX = x;
        previousZ = z;
    }

    double mean = velocities.Average();
    double standardDeviation = Math.Sqrt(velocities.Average(value => (value - mean) * (value - mean)));
    string id = $"{(regularDelivery ? "regular-every-update" : "fixed-60")}-{hz}-{route}";
    var report = new
    {
        id, hz, route, regularDelivery,
        minSpeed = velocities.Min(), maxSpeed = velocities.Max(), meanSpeed = mean,
        speedStdDev = standardDeviation, coefficientVariation = standardDeviation / mean,
        maxDeltaVelocity, stationaryFrames = velocities.Count(value => value < .0001),
        frames = velocities.Count
    };
    reports.Add(report);
    Console.WriteLine(JsonSerializer.Serialize(report));
    File.WriteAllText(Path.Combine(output, id + ".json"), JsonSerializer.Serialize(samples));
}

File.WriteAllText(Path.Combine(output, "summary.json"),
    JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
return 0;

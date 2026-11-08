using Ambermoon.Render;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Ambermoon.Renderer.OpenGL;

internal class Camera3D : ICamera3D
{
    const double AngleFactor = Math.PI / 180.0;
    const double QuarterTurnAngle = 0.5 * Math.PI;
    readonly State state;
    readonly Matrix4 currentMatrix;
    Matrix4 rotationMatrix = new Matrix4(Matrix4.Identity);
    Matrix4 translateMatrix = new Matrix4(Matrix4.Identity);
    float currentAngle = 0.0f;
    double currentAngleCos = 0.0;
    double currentAngleSin = -1.0;
    // The perpendicular value is -90° rotation (= turn left by a quarter)
    double currentPerpendicularAngleCos = -1.0;
    double currentPerpendicularAngleSin = 0.0;
    readonly Func<double> interpolationClock;
    readonly List<(float X, float Y, float Z)> movementTargets = new();
    bool capturingMovement;
    uint movementTick;
    double movementDuration;
    uint lastMovementTick;
    bool movementBatchRendered;
    double? lastMovementTime;
    double batchDistance;
    double batchDuration;
    double playbackSpeed;
    double lastInterpolationTime;

    public Camera3D(State state, Func<double> interpolationClock = null)
    {
        this.state = state;
        this.interpolationClock = interpolationClock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        currentMatrix = new Matrix4(Matrix4.Identity);
    }

    public float X { get; private set; } = 0.0f;
    public float Y { get; private set; } = 0.0f;
    public float Z { get; private set; } = 0.0f;
    public float GroundY { get; set; } = 0.0f;
    public float Angle => currentAngle;
    public float RenderX { get; private set; }
    public float RenderY { get; private set; }
    public float RenderZ { get; private set; }

    public void BeginMovementInterpolation(uint ticks, double duration)
    {
        capturingMovement = true;
        movementTick = ticks;
        movementDuration = Math.Clamp(duration, 1.0 / 240, 1.0 / 30);
    }

    public void EndMovementInterpolation() => capturingMovement = false;

    public void ResetMovementInterpolation()
    {
        capturingMovement = false;
        movementTargets.Clear();
        lastMovementTime = null;
        lastInterpolationTime = interpolationClock();
        if (RenderX != X || RenderY != Y || RenderZ != Z)
            SetRenderPosition(X, Y, Z);
    }

    void SetRenderPosition(float x, float y, float z)
    {
        RenderX = x;
        RenderY = y;
        RenderZ = z;
        translateMatrix = Matrix4.CreateTranslationMatrix(x, y, z);
        UpdateMatrix();
    }

    static double Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    void RecordMovement((float X, float Y, float Z) previous)
    {
        double now = interpolationClock();
        var end = (X, Y, Z);
        double distance = Distance(previous, end);
        if (distance == 0)
            return;

        // Advance the old path before appending this accepted movement. Each
        // collision-resolved endpoint stays in order, including corner slides.
        AdvanceMovementInterpolation(now);
        if (movementTargets.Count >= 64)
        {
            movementTargets.Clear();
            SetRenderPosition(previous.X, previous.Y, previous.Z);
            lastMovementTime = null;
        }
        if (lastMovementTime == null || lastMovementTick != movementTick || movementBatchRendered)
        {
            batchDuration = movementDuration;
            if (lastMovementTime is double lastTime)
            {
                double interval = now - lastTime;
                if (interval >= 1.0 / 240 && interval <= 1.5 * movementDuration)
                    batchDuration = interval;
            }
            batchDistance = 0;
            lastMovementTick = movementTick;
            lastMovementTime = now;
            movementBatchRendered = false;
        }
        movementTargets.Add(end);
        batchDistance += distance;

        double remainingDistance = 0;
        var point = (RenderX, RenderY, RenderZ);
        foreach (var target in movementTargets)
        {
            remainingDistance += Distance(point, target);
            point = target;
        }
        // Catch up along the accepted path instead of accumulating queued time
        // after frame stalls. Once movement stops this drains in at most 33 ms.
        double catchUpDuration = Math.Min(1.0 / 30, 1.25 * movementDuration);
        playbackSpeed = Math.Max(batchDistance / batchDuration, remainingDistance / catchUpDuration);
    }

    public void UpdateMovementInterpolation()
    {
        AdvanceMovementInterpolation(interpolationClock());
        movementBatchRendered = true;
    }

    void AdvanceMovementInterpolation(double now)
    {
        double distance = Math.Max(0, now - lastInterpolationTime) * playbackSpeed;
        lastInterpolationTime = now;
        while (movementTargets.Count != 0)
        {
            var start = (X: RenderX, Y: RenderY, Z: RenderZ);
            var end = movementTargets[0];
            double length = Distance(start, end);
            if (distance < length)
            {
                float factor = (float)(distance / length);
                SetRenderPosition(start.X + (end.X - start.X) * factor,
                    start.Y + (end.Y - start.Y) * factor, start.Z + (end.Z - start.Z) * factor);
                return;
            }
            SetRenderPosition(end.X, end.Y, end.Z);
            movementTargets.RemoveAt(0);
            distance -= length;
        }
    }

    Action<float> turnedHandler;

    public event Action<float> Turned
    {
        add
        {
            if (turnedHandler == null || !turnedHandler.GetInvocationList().Contains(value))
                turnedHandler += value;
        }
        remove
        {
            turnedHandler -= value;
        }
    }

    public void GetForwardPosition(float distance, out float x, out float z, bool noX, bool noZ)
    {
        x = noX ? X : X - (float)currentAngleCos * distance;
        z = noZ ? Z : Z - (float)currentAngleSin * distance;
    }

    public void GetBackwardPosition(float distance, out float x, out float z, bool noX, bool noZ)
    {
        x = noX ? X : X + (float)currentAngleCos * distance;
        z = noZ ? Z : Z + (float)currentAngleSin * distance;
    }

    public void GetLeftPosition(float distance, out float x, out float z, bool noX, bool noZ)
    {
        x = noX ? X : X - (float)currentPerpendicularAngleCos * distance;
        z = noZ ? Z : Z - (float)currentPerpendicularAngleSin * distance;
    }

    public void GetRightPosition(float distance, out float x, out float z, bool noX, bool noZ)
    {
        x = noX ? X : X + (float)currentPerpendicularAngleCos * distance;
        z = noZ ? Z : Z + (float)currentPerpendicularAngleSin * distance;
    }

    public void Activate()
    {
        UpdateMovementInterpolation();
        state.RestoreModelViewMatrix(Matrix4.Identity);
        state.PushModelViewMatrix(currentMatrix);
    }

    private void UpdateMatrix()
    {
        currentMatrix.Reset();
        currentMatrix.Multiply(rotationMatrix);
        currentMatrix.Multiply(translateMatrix);
    }

    private void Move(float x, float y, float z)
    {
        var previous = (X, Y, Z);
        X += x;
        Y += y;
        Z += z;
        if (capturingMovement && y == 0)
            RecordMovement(previous);
        else
            ResetMovementInterpolation();
    }

    private void Rotate(float angle)
    {
        rotationMatrix = Matrix4.CreateZRotationMatrix(angle);
        UpdateMatrix();
    }

    public void LevitateDown(float distance)
    {
        ResetMovementInterpolation();
        Move(0.0f, distance, 0.0f);
    }

    public void LevitateUp(float distance)
    {
        ResetMovementInterpolation();
        Move(0.0f, -distance, 0.0f);
    }

    public void MoveBackward(float distance, bool noX, bool noZ)
    {
        Move(noX ? 0.0f : (float)currentAngleCos * distance, 0.0f, noZ ? 0.0f : (float)currentAngleSin * distance);
    }

    public void MoveForward(float distance, bool noX, bool noZ)
    {
        Move(noX ? 0.0f : -(float)currentAngleCos * distance, 0.0f, noZ ? 0.0f : -(float)currentAngleSin * distance);
    }

    public void MoveLeft(float distance, bool noX, bool noZ)
    {
        Move(noX ? 0.0f : -(float)currentPerpendicularAngleCos * distance, 0.0f, noZ ? 0.0f : -(float)currentPerpendicularAngleSin * distance);
    }

    public void MoveRight(float distance, bool noX, bool noZ)
    {
        Move(noX ? 0.0f : (float)currentPerpendicularAngleCos * distance, 0.0f, noZ ? 0.0f : (float)currentPerpendicularAngleSin * distance);
    }

    public void UpdatePosition()
    {
        Y = GroundY;
        ResetMovementInterpolation();
        TurnTowards(currentAngle);
        UpdateMatrix();
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public void SetPosition(float x, float z, float? y = null)
    {
        X = -x;
        Y = y ?? GroundY;
        Z = z;
        ResetMovementInterpolation();
    }

    public void TurnLeft(float angle)
    {
        TurnTowards(currentAngle - angle);
    }

    public void TurnRight(float angle)
    {
        TurnTowards(currentAngle + angle);
    }

    public void TurnTowards(float angle)
    {
        while (angle < 0.0f)
            angle += 360.0f;
        while (angle >= 360.0f)
            angle -= 360.0f;

        currentAngle = angle;
        var radiant = AngleFactor * (currentAngle - 90.0f);
        currentAngleCos = Math.Cos(radiant);
        currentAngleSin = Math.Sin(radiant);
        currentPerpendicularAngleCos = Math.Cos(radiant - QuarterTurnAngle);
        currentPerpendicularAngleSin = Math.Sin(radiant - QuarterTurnAngle);
        Rotate(currentAngle);

        turnedHandler?.Invoke(currentAngle);
    }
}

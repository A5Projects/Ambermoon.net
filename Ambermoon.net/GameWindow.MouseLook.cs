using Ambermoon.Data;
using Silk.NET.Input;
using MousePosition = System.Numerics.Vector2;

namespace Ambermoon;

partial class GameWindow
{
    bool mouseLookCaptured;
    bool mouseLookWarpFallback;
    bool mouseLookIgnoreNextMove;
    bool mouseLookWindowFocused = true;
    MousePosition mouseLookLastPosition;

    void SetMouseLookCapture(bool captured, Position position)
    {
        if (mouse == null || cursor == null)
            return;

        mouse.MouseMove -= Mouse_MouseMove;
        try
        {
            mouseLookCaptured = captured;
            mouseLookWarpFallback = false;
            if (captured)
            {
                try { cursor.CursorMode = CursorMode.Disabled; }
                catch
                {
                    // Some backends lack an unbounded virtual cursor. Recenter
                    // after each event instead, never feeding the warp into look.
                    cursor.CursorMode = CursorMode.Hidden;
                    mouseLookWarpFallback = true;
                }
            }
            else
                cursor.CursorMode = CursorMode.Hidden;

            // Disabled mode already supplies an unbounded virtual position.
            if (mouseLookWarpFallback)
                mouse.Position = new MousePosition(window.Size.X / 2, window.Size.Y / 2);
            else if (!captured)
                mouse.Position = new MousePosition(position.X, position.Y);
            mouseLookLastPosition = mouse.Position;
            mouseLookIgnoreNextMove = captured;
        }
        finally { mouse.MouseMove += Mouse_MouseMove; }
    }

    void HandleMouseLookMove(MousePosition position)
    {
        if (mouseLookIgnoreNextMove)
        {
            // A queued event may still carry coordinates from before capture
            // or a deliberate cursor warp. Anchor to the device's live position.
            mouseLookLastPosition = mouse.Position;
            mouseLookIgnoreNextMove = false;
        }
        else
        {
            var delta = position - mouseLookLastPosition;
            mouseLookLastPosition = position;
            Game?.ApplyMouseLookDelta(delta.X, delta.Y);
        }

        if (mouseLookCaptured && mouseLookWarpFallback)
        {
            mouse.MouseMove -= Mouse_MouseMove;
            try
            {
                mouse.Position = new MousePosition(window.Size.X / 2, window.Size.Y / 2);
                mouseLookLastPosition = mouse.Position;
            }
            finally { mouse.MouseMove += Mouse_MouseMove; }
        }
    }

    void Window_FocusChanged(bool focused)
    {
        mouseLookWindowFocused = focused;
        Game?.SetMouseLookFocus(focused);
    }
}

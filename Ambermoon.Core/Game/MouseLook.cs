using System;
using Ambermoon.Data;

namespace Ambermoon;

partial class GameCore
{
    bool mouseLookFocused = true;
    bool mouseLookFullscreenChanging;
    bool mouseLookToggleHeld;
    Position? mouseLookReturnPosition;

    public bool MouseLookActive { get; private set; }
    public event Action<bool, Position>? MouseLookCaptureChanged;

    bool CanUseMouseLook => !CoreConfiguration.IsMobile && Ingame && Is3D &&
        mouseLookFocused && !mouseLookFullscreenChanging && !DisallowMoving() && !Fading && !layout.PopupActive &&
        !layout.ExplorationActionsOpen && !layout.OptionMenuOpen && !layout.IsDragging &&
        worldMapViewer == null && QuestLog?.Open != true && nextClickHandler == null &&
        !GameOverButtonsVisible && player3D != null;

    public void SetMouseLook3D(bool enabled)
    {
        if (CoreConfiguration.IsMobile)
            return;
        CoreConfiguration.MouseLook3D = enabled;
        clickMoveActive = false;
        UntrapMouse();
        ResetMoveKeys(true);
        UpdateMouseLookCapture();
        CoreConfiguration.RequestSave();
    }

    public void SetMouseLookFocus(bool focused)
    {
        mouseLookFocused = focused;
        RefreshSprintKeys(focused ? pressedKeyProvider?.Invoke() : null);
        if (!focused)
        {
            camera3D?.ResetMovementInterpolation();
            Array.Clear(keys, 0, keys.Length);
            mouseLookToggleHeld = false;
            clickMoveActive = false;
            ResetMoveKeys(true);
        }
        UpdateMouseLookCapture();
    }

    public void UpdateMouseLookCapture()
    {
        bool active = CoreConfiguration.MouseLook3D && CanUseMouseLook;
        if (active == MouseLookActive)
            return;
        if (!active)
        {
            ReleaseMouseLookCapture();
            return;
        }

        clickMoveActive = false;
        UntrapMouse();
        ResetMoveKeys(true);
        layout.CancelExplorationUiPress();
        mouseLookReturnPosition = new Position(lastMousePosition);
        lastMousePosition = renderView.GameToScreen(Map3DViewArea.Center);
        MouseLookActive = true;
        CursorType = CursorType.None;
        layout.HideTooltip();
        MouseLookCaptureChanged?.Invoke(true, new Position(lastMousePosition));
    }

    void ReleaseMouseLookCapture()
    {
        if (!MouseLookActive)
            return;
        MouseLookActive = false;
        clickMoveActive = false;
        ResetMoveKeys(true);
        if (mouseLookReturnPosition != null)
            lastMousePosition = mouseLookReturnPosition;
        mouseLookReturnPosition = null;
        MouseLookCaptureChanged?.Invoke(false, new Position(lastMousePosition));
        CursorType = CursorType.Sword;
        UpdateCursor();
    }

    /// <summary>Relative desktop mouse movement, in window pixels. Vertical movement is intentionally ignored.</summary>
    public void ApplyMouseLookDelta(float dx, float dy)
    {
        UpdateMouseLookCapture();
        if (!MouseLookActive || dx == 0 || !float.IsFinite(dx) || !float.IsFinite(dy))
            return;
        float sensitivity = Math.Clamp(CoreConfiguration.MouseLookSensitivity, 25, 300) / 100.0f;
        player3D!.TurnRight((dx * 0.15f * sensitivity) % 360.0f);
        CurrentSavegame!.CharacterDirection = player!.Direction = player3D.Direction;
    }
}

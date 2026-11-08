using Ambermoon.Data;
using Ambermoon.Render;
using TextColor = Ambermoon.Data.Enumerations.Color;

namespace Ambermoon.UI;

internal partial class Layout
{
    public static readonly Rect ExplorationActionsArea = new(
        Global.VirtualScreenWidth - 3 * Button.Width,
        Global.VirtualScreenHeight - 3 * Button.Height,
        3 * Button.Width,
        3 * Button.Height);
    public static readonly Rect ExplorationMenuButtonArea = new(282, 38, 36, 12);
    public bool IsExplorationLayout => Type is LayoutType.Map2D or LayoutType.Map3D;
    // Keep the user's selection while a spell, inventory, or options screen
    // temporarily replaces the exploration buttons.
    bool explorationActionsEnabled;
    public bool ExplorationActionsOpen => explorationActionsEnabled && IsExplorationLayout && !OptionMenuOpen;
    IColoredRect? explorationTopBackground;
    IColoredRect? explorationPortraitSeparator;
    IColoredRect? explorationActionBackground;
    UIText? explorationMenuLabel;
    bool explorationUiPress;
    bool explorationActionPress;

    IColoredRect CreateExplorationArea(Rect area, Color color, byte layer)
    {
        var result = RenderView.ColoredRectFactory.Create(area.Width, area.Height, color, layer);
        result.Layer = renderLayer;
        result.X = area.X;
        result.Y = area.Y;
        return result;
    }

    void EnsureExplorationActions()
    {
        if (explorationMenuLabel != null)
            return;
        // One backing keeps opacity uniform across the title, indicators and MENU.
        explorationTopBackground = CreateExplorationArea(new Rect(0, 38, Global.VirtualScreenWidth, 10),
            new Color(16, 15, 12, 95), 1);
        explorationPortraitSeparator = CreateExplorationArea(new Rect(0, 37, Global.VirtualScreenWidth, 1),
            new Color(0, 0, 0, 255), 1);
        // Some button icons contain transparent pixels. Cover the scene beneath
        // them completely, staying inside the grid's opaque outer frame.
        explorationActionBackground = CreateExplorationArea(ExplorationActionsArea.CreateModified(1, 1, -2, -2),
            new Color(16, 15, 12, 255), 1);
        explorationMenuLabel = new UIText(RenderView, game.UIPaletteIndex,
            RenderView.TextProcessor.CreateText("MENU"), new Rect(284, 40, 32, 8), 4,
            TextColor.BrightGray, true, TextAlign.Center);
        if (game.Map != null && !game.WindowActive)
            game.UpdateMapName();
    }

    public void ToggleExplorationActions()
    {
        if (!IsExplorationLayout || !game.InputEnable || PopupActive || OptionMenuOpen)
            return;
        SetExplorationActions(!ExplorationActionsOpen);
    }

    public void SetExplorationActions(bool open)
    {
        ReleaseButtons(true);
        explorationActionsEnabled = open && IsExplorationLayout;
        ButtonGridPage = ExplorationActionsOpen ? 1 : 0;
        buttonGrid.HideTooltips();
        UpdateLayoutButtons(ticksPerMovement);
        UpdateExplorationActionsVisibility();
    }

    public void UpdateExplorationActionsVisibility()
    {
        buttonGrid.SetPosition(IsExplorationLayout ? ExplorationActionsArea.X : Global.ButtonGridX,
            IsExplorationLayout ? ExplorationActionsArea.Y : Global.ButtonGridY);
        if (IsExplorationLayout)
            EnsureExplorationActions();
        bool exploring = IsExplorationLayout && !game.WindowActive;
        bool chrome = exploring && !PopupActive && !OptionMenuOpen;
        if (explorationMenuLabel != null)
        {
            explorationMenuLabel.Visible = chrome;
            explorationMenuLabel.PaletteIndex = game.UIPaletteIndex;
            explorationTopBackground!.Visible = chrome;
            explorationPortraitSeparator!.Visible = chrome;
        }
        if (IsExplorationLayout)
            buttonGrid.Visible = exploring && !PopupActive && (ExplorationActionsOpen || OptionMenuOpen);
        if (explorationActionBackground != null)
            explorationActionBackground.Visible = exploring && buttonGrid.Visible;
    }

    public bool IsExplorationUiAt(Position position) => IsExplorationLayout && !game.WindowActive && !PopupActive &&
        ((!OptionMenuOpen && ExplorationMenuButtonArea.Contains(position)) ||
         ((ExplorationActionsOpen || OptionMenuOpen) && ExplorationActionsArea.Contains(position)));

    public bool ExplorationUiMouseDown(Position position, MouseButtons buttons, uint ticks, out CursorType? cursorType)
    {
        cursorType = null;
        if (!IsExplorationUiAt(position))
            return false;
        explorationUiPress = true;
        explorationActionPress = false;
        if (!OptionMenuOpen && ExplorationMenuButtonArea.Contains(position))
        {
            if (buttons == MouseButtons.Left)
                ToggleExplorationActions();
        }
        else if (buttons == MouseButtons.Right && ExplorationActionsOpen)
            SetExplorationActions(false);
        else
            explorationActionPress = buttonGrid.MouseDown(position, buttons, out cursorType, ticks);
        return true;
    }

    public bool ExplorationUiMouseUp(Position position, MouseButtons buttons, uint ticks, out CursorType? cursorType)
    {
        cursorType = null;
        if (!explorationUiPress)
            return false;
        explorationUiPress = false;
        if (explorationActionPress)
            buttonGrid.MouseUp(position, buttons, out cursorType, ticks);
        explorationActionPress = false;
        return true;
    }

    public void WaitFromKeyboard() => OpenWaitPopup();

    public void CancelExplorationUiPress()
    {
        if (!explorationUiPress)
            return;
        explorationUiPress = explorationActionPress = false;
        for (int index = 0; index < 9; ++index)
            buttonGrid.GetButton(index).Release(true);
    }

    void ResetExplorationActions(bool keepSelection = false)
    {
        if (!keepSelection)
            explorationActionsEnabled = false;
        ButtonGridPage = ExplorationActionsOpen ? 1 : 0;
        explorationTopBackground?.Delete();
        explorationPortraitSeparator?.Delete();
        explorationActionBackground?.Delete();
        explorationMenuLabel?.Destroy();
        explorationTopBackground = explorationPortraitSeparator = explorationActionBackground = null;
        explorationMenuLabel = null;
        // Retain a pending mouse-up after an action opens a legacy window.
        // It must not click through into that new window.
    }
}

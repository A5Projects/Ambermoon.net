using Ambermoon.Data;
using Ambermoon.Render;
using TextColor = Ambermoon.Data.Enumerations.Color;

namespace Ambermoon.UI;

internal partial class Layout
{
    const int ExplorationActionsPadding = 4;
    public static readonly Rect ExplorationActionsArea = new(
        Global.VirtualScreenWidth - 3 * Button.Width - 2 * ExplorationActionsPadding,
        Global.VirtualScreenHeight - 3 * Button.Height - 2 * ExplorationActionsPadding,
        3 * Button.Width + 2 * ExplorationActionsPadding,
        3 * Button.Height + 2 * ExplorationActionsPadding);
    public static readonly Rect ExplorationMenuButtonArea = new(282, 39, 36, 12);
    public bool IsExplorationLayout => Type is LayoutType.Map2D or LayoutType.Map3D;
    public bool ExplorationActionsOpen { get; private set; }
    IColoredRect? explorationActionBackground;
    IColoredRect? explorationMenuBorder;
    IColoredRect? explorationMenuBackground;
    IColoredRect? explorationTitleBackground;
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
        explorationActionBackground = CreateExplorationArea(ExplorationActionsArea, new Color(24, 21, 16, 235), 1);
        explorationMenuBorder = CreateExplorationArea(ExplorationMenuButtonArea, new Color(139, 125, 94, 220), 1);
        explorationMenuBackground = CreateExplorationArea(new Rect(283, 40, 34, 10), new Color(24, 21, 16, 230), 2);
        explorationTitleBackground = CreateExplorationArea(new Rect(4, 39, 200, 10), new Color(16, 15, 12, 180), 1);
        explorationMenuLabel = new UIText(RenderView, game.UIPaletteIndex,
            RenderView.TextProcessor.CreateText("MENU"), new Rect(284, 41, 32, 8), 4,
            TextColor.BrightGray, true, TextAlign.Center);
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
        ExplorationActionsOpen = open && IsExplorationLayout;
        ButtonGridPage = ExplorationActionsOpen ? 1 : 0;
        buttonGrid.HideTooltips();
        UpdateLayoutButtons(ticksPerMovement);
        UpdateExplorationActionsVisibility();
    }

    public void UpdateExplorationActionsVisibility()
    {
        buttonGrid.SetPosition(IsExplorationLayout ? ExplorationActionsArea.X + ExplorationActionsPadding : Global.ButtonGridX,
            IsExplorationLayout ? ExplorationActionsArea.Y + ExplorationActionsPadding : Global.ButtonGridY);
        if (IsExplorationLayout)
            EnsureExplorationActions();
        bool exploring = IsExplorationLayout && !game.WindowActive;
        bool chrome = exploring && !PopupActive && !OptionMenuOpen;
        if (explorationMenuLabel != null)
        {
            explorationMenuLabel.Visible = chrome;
            explorationMenuLabel.PaletteIndex = game.UIPaletteIndex;
            explorationMenuBorder!.Visible = chrome;
            explorationMenuBackground!.Visible = chrome;
            explorationTitleBackground!.Visible = chrome;
            explorationActionBackground!.Visible = exploring && !PopupActive && (ExplorationActionsOpen || OptionMenuOpen);
        }
        if (IsExplorationLayout)
            buttonGrid.Visible = exploring && !PopupActive && (ExplorationActionsOpen || OptionMenuOpen);
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
        bool closeActions = ExplorationActionsOpen && explorationActionPress;
        if (explorationActionPress)
            buttonGrid.MouseUp(position, buttons, out cursorType, ticks);
        explorationActionPress = false;
        if (closeActions && IsExplorationLayout && !OptionMenuOpen)
            SetExplorationActions(false);
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

    void ResetExplorationActions()
    {
        ExplorationActionsOpen = false;
        ButtonGridPage = 0;
        explorationActionBackground?.Delete();
        explorationMenuBorder?.Delete();
        explorationMenuBackground?.Delete();
        explorationTitleBackground?.Delete();
        explorationMenuLabel?.Destroy();
        explorationActionBackground = explorationMenuBorder = explorationMenuBackground = explorationTitleBackground = null;
        explorationMenuLabel = null;
        // Retain a pending mouse-up after an action opens a legacy window.
        // It must not click through into that new window.
    }
}

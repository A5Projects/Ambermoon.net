using Ambermoon.Data;
using Ambermoon.Render;
using System;
using System.Collections.Generic;
using TextColor = Ambermoon.Data.Enumerations.Color;

namespace Ambermoon.UI;

internal partial class Layout
{
    internal static readonly Rect ExplorationCompassArea = new(128, 38, 64, 10);
    const int ExplorationCompassBearingSpacing = 24;
    readonly List<IColoredRect> explorationCompassFrame = [];
    readonly List<UIText> explorationCompassLabels = [];
    readonly List<IColoredRect> explorationCompassTicks = [];
    bool explorationCompassVisible;
    int? explorationCompassAngle;

    internal bool ExplorationCompassActive => explorationCompassFrame.Count != 0;

    void EnsureExplorationCompass()
    {
        if (ExplorationCompassActive)
            return;

        var area = ExplorationCompassArea;
        var interior = area.CreateModified(1, 1, -2, -2);

        void AddFrame(Rect bounds, Color color, byte layer = 3)
        {
            var rect = CreateExplorationArea(bounds, color, layer);
            rect.Visible = false;
            explorationCompassFrame.Add(rect);
        }

        // A black face with a one-pixel metal rim keeps the original compass's
        // restrained appearance without taking space away from the scene.
        AddFrame(area, new Color(5, 8, 9), 2);
        AddFrame(new Rect(area.X, area.Y, area.Width, 1), new Color(69, 80, 82));
        AddFrame(new Rect(area.X, area.Bottom - 1, area.Width, 1), new Color(35, 43, 46));
        AddFrame(new Rect(area.X, area.Y + 1, 1, area.Height - 2), new Color(46, 56, 60));
        AddFrame(new Rect(area.Right - 1, area.Y + 1, 1, area.Height - 2), new Color(27, 34, 37));
        int center = area.X + area.Width / 2;
        AddFrame(new Rect(center - 1, area.Y, 3, 1), new Color(191, 207, 203), 4);
        AddFrame(new Rect(center, area.Y + 1, 1, 1), new Color(191, 207, 203), 4);

        // Localized directions start with west and continue clockwise. Only
        // the first eight are needed; the original ribbon repeats its start.
        var directions = game.DataNameProvider.CompassDirections.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < 8; ++index)
        {
            string direction = directions[index].Replace("-", "");
            var label = new UIText(RenderView, game.UIPaletteIndex,
                RenderView.TextProcessor.CreateText(direction),
                new Rect(center, area.Y + 2, direction.Length * Global.GlyphWidth, 7),
                4, TextColor.BrightGray, false);
            label.Clip(interior);
            label.Visible = false;
            explorationCompassLabels.Add(label);

            var tick = CreateExplorationArea(new Rect(center, area.Y + 4, 1, 3), new Color(140, 162, 161), 3);
            tick.ClipArea = interior;
            tick.Visible = false;
            explorationCompassTicks.Add(tick);
        }

        UpdateExplorationCompass();
    }

    void UpdateExplorationCompass()
    {
        if (!ExplorationCompassActive)
            return;

        foreach (var label in explorationCompassLabels)
            label.PaletteIndex = game.UIPaletteIndex;

        int angle = (game.PlayerAngle % 360 + 360) % 360;
        if (explorationCompassAngle == angle)
            return;

        explorationCompassAngle = angle;
        var area = ExplorationCompassArea;
        int center = area.X + area.Width / 2;

        int Offset(float bearing)
        {
            // Wrap outside the visible strip, so turning through north never
            // resets the visible scale. One-pixel motion replaces 45-degree
            // text snapping while retaining the game's native pixel grid.
            float difference = (bearing - angle + 540.0f) % 360.0f - 180.0f;
            return Util.Round(difference * ExplorationCompassBearingSpacing / 45.0f);
        }

        for (int index = 0; index < 8; ++index)
        {
            int bearing = (index * 45 + 270) % 360;
            var label = explorationCompassLabels[index];
            int width = label.Area.Width;
            label.SetBounds(new Rect(center + Offset(bearing) - width / 2, area.Y + 2, width, 7));
            explorationCompassTicks[index].X = center + Offset(bearing + 22.5f);
        }
    }

    void SetExplorationCompassVisible(bool visible)
    {
        visible &= ExplorationCompassActive;
        if (explorationCompassVisible == visible)
            return;

        explorationCompassVisible = visible;
        foreach (var rect in explorationCompassFrame)
            rect.Visible = visible;
        foreach (var label in explorationCompassLabels)
            label.Visible = visible;
        foreach (var tick in explorationCompassTicks)
            tick.Visible = visible;
    }

    void ResetExplorationCompass()
    {
        foreach (var rect in explorationCompassFrame)
            rect.Delete();
        foreach (var label in explorationCompassLabels)
            label.Destroy();
        foreach (var tick in explorationCompassTicks)
            tick.Delete();
        explorationCompassFrame.Clear();
        explorationCompassLabels.Clear();
        explorationCompassTicks.Clear();
        explorationCompassVisible = false;
        explorationCompassAngle = null;
    }
}

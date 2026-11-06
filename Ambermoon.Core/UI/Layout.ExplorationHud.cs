using Ambermoon.Data;
using Ambermoon.Data.Enumerations;
using Ambermoon.Render;
using System;
using TextColor = Ambermoon.Data.Enumerations.Color;

namespace Ambermoon.UI;

internal partial class Layout
{
    Tooltip? explorationTitleTooltip;
    bool explorationTitleTruncated;
    IColoredRect? explorationCoordinatesBackground;
    bool explorationCoordinatesBelow;
    internal int ExplorationTitleWidth { get; private set; } = 200;
    // GetTextRect adds the font's vertical offset; keep the final title aligned
    // with the directly positioned indicator and MENU text at y=40.
    internal Rect ExplorationTitleTextArea => new(4, 40 - (Global.GlyphLineHeight - GlyphHeight), ExplorationTitleWidth - 4, 7);
    static readonly SpecialItemPurpose[] explorationHudOrder =
    [
        SpecialItemPurpose.Clock, SpecialItemPurpose.DayTime,
        SpecialItemPurpose.MonsterEye, SpecialItemPurpose.WindChain, SpecialItemPurpose.MapLocation
    ];

    string GetCompactMapLocation() => $"X{game.PartyPosition.X + 1} Y{game.PartyPosition.Y + 1}";

    internal string FitExplorationTitle(string mapName)
    {
        int maxCharacters = ExplorationTitleTextArea.Width / Global.GlyphWidth;
        explorationTitleTruncated = mapName.Length > maxCharacters;
        explorationTitleTooltip ??= AddTooltip(new Rect(4, 38, ExplorationTitleWidth - 4, 10), mapName, TextColor.BrightGray);
        explorationTitleTooltip.Area = new Rect(4, 38, ExplorationTitleWidth - 4, 10);
        explorationTitleTooltip.Text = mapName;
        explorationTitleTooltip.ShowBelow = true;
        explorationTitleTooltip.Enabled = explorationTitleTruncated && IsExplorationLayout && !game.WindowActive && !PopupActive && !OptionMenuOpen;
        return explorationTitleTruncated ? mapName[..(maxCharacters - 3)].TrimEnd() + "..." : mapName;
    }

    void AddExplorationSpecialItem(SpecialItemPurpose specialItem)
    {
        if (specialItem == SpecialItemPurpose.Compass)
        {
            EnsureExplorationCompass();
            ReflowExplorationHud();
            UpdateExplorationHudVisibility();
            return;
        }

        if (specialItemSprites.ContainsKey(specialItem) || specialItemTexts.ContainsKey(specialItem))
            return;

        void AddIcon(UIGraphic graphic, int sourceHeight = 32, int height = 12)
        {
            var icon = AddSprite(new Rect(4, 38, 12, height), Graphics.GetUIGraphicIndex(graphic), game.UIPaletteIndex, 3);
            icon.TextureSize = new Size(32, sourceHeight);
            specialItemSprites.Add(specialItem, icon);
        }

        switch (specialItem)
        {
            case SpecialItemPurpose.MonsterEye:
                AddIcon(game.MonsterSeesPlayer ? UIGraphic.MonsterEyeActive : UIGraphic.MonsterEyeInactive);
                break;
            case SpecialItemPurpose.DayTime:
                AddIcon(UIGraphic.Night + (int)game.GameTime!.GetDayTime());
                break;
            case SpecialItemPurpose.WindChain:
                AddIcon(UIGraphic.Windchain, 15, 6);
                break;
            case SpecialItemPurpose.MapLocation:
                specialItemTexts.Add(specialItem, AddText(new Rect(4, 40, 54, 7), GetCompactMapLocation(), TextColor.BrightGray));
                break;
            case SpecialItemPurpose.Clock:
                specialItemTexts.Add(specialItem, AddText(new Rect(4, 40, 30, 7), $"{game.GameTime!.Hour,2}:{game.GameTime.Minute:00}", TextColor.BrightGray));
                break;
            default:
                throw new AmbermoonException(ExceptionScope.Application, $"Invalid special item: {specialItem}");
        }

        ReflowExplorationHud();
        UpdateExplorationHudVisibility();
    }

    void ReflowExplorationHud()
    {
        static int ItemWidth(SpecialItemPurpose item) => item switch
        {
            SpecialItemPurpose.Clock => 34,
            SpecialItemPurpose.MapLocation => 58,
            _ => 16
        };

        int contentWidth = 0;
        foreach (var item in explorationHudOrder)
            if (specialItemSprites.ContainsKey(item) || specialItemTexts.ContainsKey(item))
                contentWidth += ItemWidth(item);

        // Keep the compass centered even as other indicators are acquired.
        // Coordinates need a second line only when the right-hand row is full.
        explorationCoordinatesBelow = ExplorationCompassActive && contentWidth > 82 &&
            specialItemTexts.ContainsKey(SpecialItemPurpose.MapLocation);
        if (explorationCoordinatesBelow)
        {
            contentWidth -= ItemWidth(SpecialItemPurpose.MapLocation);
            explorationCoordinatesBackground ??= CreateExplorationArea(new Rect(220, 51, 58, 9),
                new Render.Color(16, 15, 12, 95), 1);
        }

        int statusX = ExplorationMenuButtonArea.X - 4 - contentWidth;
        int x = statusX;
        foreach (var item in explorationHudOrder)
        {
            bool hasIcon = specialItemSprites.TryGetValue(item, out var icon);
            bool hasText = specialItemTexts.TryGetValue(item, out var text);
            if (!hasIcon && !hasText)
                continue;

            if (item == SpecialItemPurpose.MapLocation && explorationCoordinatesBelow)
            {
                text!.SetPosition(new Position(222, 52));
                continue;
            }

            if (hasIcon)
            {
                icon!.X = x;
                icon.Y = item == SpecialItemPurpose.WindChain ? 41 : 38;
            }
            if (hasText)
                text!.SetPosition(new Position(x, 40));

            x += ItemWidth(item);
        }

        ExplorationTitleWidth = ExplorationCompassActive ? 124 : Math.Min(200, statusX - 10);

        if (game.Map != null && !game.WindowActive)
            game.UpdateMapName();
    }

    internal void UpdateExplorationHudVisibility()
    {
        bool visible = IsExplorationLayout && !game.WindowActive && !PopupActive && !OptionMenuOpen;
        SetExplorationCompassVisible(visible);
        if (explorationCoordinatesBackground != null)
            explorationCoordinatesBackground.Visible = visible && explorationCoordinatesBelow;
        foreach (var icon in specialItemSprites.Values)
            icon.Visible = visible;
        foreach (var text in specialItemTexts.Values)
            text.Visible = visible;
        if (explorationTitleTooltip != null)
            explorationTitleTooltip.Enabled = visible && explorationTitleTruncated;

        // Battles retain their original spell row. All other full UI screens
        // hide the exploration row until the map is restored.
        bool spellsVisible = visible || Type == LayoutType.Battle;
        foreach (var icon in activeSpellSprites.Values)
            icon.Visible = spellsVisible;
        foreach (var background in activeSpellDurationBackgrounds.Values)
            background.Visible = spellsVisible;
        foreach (var bar in activeSpellDurationBars.Values)
            bar.Visible = spellsVisible;
    }

    internal void ResetExplorationHud()
    {
        ResetExplorationCompass();
        explorationCoordinatesBackground?.Delete();
        explorationCoordinatesBackground = null;
        explorationCoordinatesBelow = false;
        ExplorationTitleWidth = 200;
        if (explorationTitleTooltip != null)
            RemoveTooltip(explorationTitleTooltip);
        explorationTitleTooltip = null;
        explorationTitleTruncated = false;
    }
}

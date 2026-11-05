using Ambermoon.Data;
using Ambermoon.Data.Enumerations;
using Ambermoon.Render;
using System;
using TextColor = Ambermoon.Data.Enumerations.Color;

namespace Ambermoon.UI;

internal partial class Layout
{
    IColoredRect? explorationHudBackground;
    Tooltip? explorationTitleTooltip;
    bool explorationTitleTruncated;
    internal int ExplorationTitleWidth { get; private set; } = 200;
    internal Rect ExplorationTitleTextArea => new(8, 38, ExplorationTitleWidth - 8, 10);
    static readonly SpecialItemPurpose[] explorationHudOrder =
    [
        SpecialItemPurpose.Compass, SpecialItemPurpose.Clock, SpecialItemPurpose.DayTime,
        SpecialItemPurpose.MonsterEye, SpecialItemPurpose.WindChain, SpecialItemPurpose.MapLocation
    ];

    string GetCompactCompassString()
    {
        // The original localized ribbon starts at west and advances clockwise.
        var directions = game.DataNameProvider.CompassDirections.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int bearing = ((game.PlayerAngle % 360 + 360 + 22) / 45 + 2) % 8;
        return directions[bearing];
    }

    string GetCompactMapLocation() => $"X:{game.PartyPosition.X + 1,3}^Y:{game.PartyPosition.Y + 1,3}";

    internal string FitExplorationTitle(string mapName)
    {
        int maxCharacters = ExplorationTitleTextArea.Width / Global.GlyphWidth;
        explorationTitleTruncated = mapName.Length > maxCharacters;
        explorationTitleTooltip ??= AddTooltip(new Rect(4, 37, ExplorationTitleWidth, 10), mapName, TextColor.BrightGray);
        explorationTitleTooltip.Area = new Rect(4, 37, ExplorationTitleWidth, 10);
        explorationTitleTooltip.Text = mapName;
        explorationTitleTooltip.ShowBelow = true;
        explorationTitleTooltip.Enabled = explorationTitleTruncated && IsExplorationLayout && !game.WindowActive && !PopupActive && !OptionMenuOpen;
        return explorationTitleTruncated ? mapName[..(maxCharacters - 3)].TrimEnd() + "..." : mapName;
    }

    void AddExplorationSpecialItem(SpecialItemPurpose specialItem)
    {
        if (specialItemSprites.ContainsKey(specialItem) || specialItemTexts.ContainsKey(specialItem))
            return;

        void AddIcon(UIGraphic graphic, int sourceHeight = 32, int height = 12)
        {
            var icon = AddSprite(new Rect(4, 37, 12, height), Graphics.GetUIGraphicIndex(graphic), game.UIPaletteIndex, 3);
            icon.TextureSize = new Size(32, sourceHeight);
            specialItemSprites.Add(specialItem, icon);
        }

        switch (specialItem)
        {
            case SpecialItemPurpose.Compass:
                AddIcon(UIGraphic.Compass);
                specialItemTexts.Add(specialItem, AddText(new Rect(18, 39, 18, 7), GetCompactCompassString(), TextColor.BrightGray));
                break;
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
                specialItemTexts.Add(specialItem, AddText(new Rect(4, 38, 30, 14), GetCompactMapLocation(), TextColor.BrightGray));
                break;
            case SpecialItemPurpose.Clock:
                specialItemTexts.Add(specialItem, AddText(new Rect(4, 39, 30, 7), $"{game.GameTime!.Hour,2}:{game.GameTime.Minute:00}", TextColor.BrightGray));
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
            SpecialItemPurpose.Compass or SpecialItemPurpose.Clock or SpecialItemPurpose.MapLocation => 34,
            _ => 16
        };

        int contentWidth = 0;
        foreach (var item in explorationHudOrder)
            if (specialItemSprites.ContainsKey(item) || specialItemTexts.ContainsKey(item))
                contentWidth += ItemWidth(item);

        // The common compass + clock pair fits the original gap. As other
        // indicators are acquired, reserve room by shortening only the title.
        int backgroundWidth = contentWidth + 4;
        int backgroundX = ExplorationMenuButtonArea.X - 2 - backgroundWidth;
        int x = backgroundX + 2;
        foreach (var item in explorationHudOrder)
        {
            bool hasIcon = specialItemSprites.TryGetValue(item, out var icon);
            bool hasText = specialItemTexts.TryGetValue(item, out var text);
            if (!hasIcon && !hasText)
                continue;

            if (hasIcon)
            {
                icon!.X = x;
                icon.Y = item == SpecialItemPurpose.WindChain ? 40 : 37;
            }
            if (hasText)
                text!.SetPosition(new Position(x + (item == SpecialItemPurpose.Compass ? 14 : 0),
                    item == SpecialItemPurpose.MapLocation ? 38 : 39));

            x += ItemWidth(item);
        }

        ExplorationTitleWidth = contentWidth == 0 ? 200 : Math.Min(200, backgroundX - 8);
        explorationTitleBackground?.Resize(ExplorationTitleWidth, 10);
        if (contentWidth != 0)
        {
            int height = specialItemTexts.ContainsKey(SpecialItemPurpose.MapLocation) ? 16 : 12;
            explorationHudBackground ??= CreateArea(new Rect(backgroundX, 37, backgroundWidth, height), new Render.Color(16, 15, 12, 95), 1);
            explorationHudBackground.X = backgroundX;
            explorationHudBackground.Y = 37;
            explorationHudBackground.Resize(backgroundWidth, height);
        }

        if (game.Map != null && !game.WindowActive)
            game.UpdateMapName();
    }

    internal void UpdateExplorationHudVisibility()
    {
        bool visible = IsExplorationLayout && !game.WindowActive && !PopupActive && !OptionMenuOpen;
        foreach (var icon in specialItemSprites.Values)
            icon.Visible = visible;
        foreach (var text in specialItemTexts.Values)
            text.Visible = visible;
        if (explorationHudBackground != null)
            explorationHudBackground.Visible = visible && (specialItemSprites.Count != 0 || specialItemTexts.Count != 0);
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
        // The rectangle is tracked by filledAreas and deleted by Layout.Reset.
        explorationHudBackground = null;
        ExplorationTitleWidth = 200;
        if (explorationTitleTooltip != null)
            RemoveTooltip(explorationTitleTooltip);
        explorationTitleTooltip = null;
        explorationTitleTruncated = false;
    }
}

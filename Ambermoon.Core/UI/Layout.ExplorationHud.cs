using Ambermoon.Data;
using Ambermoon.Data.Enumerations;
using Ambermoon.Render;
using System;
using TextColor = Ambermoon.Data.Enumerations.Color;

namespace Ambermoon.UI;

internal partial class Layout
{
    IColoredRect? explorationHudBackground;
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

    string GetCompactMapLocation() => $"X:{game.PartyPosition.X + 1} Y:{game.PartyPosition.Y + 1}";

    void AddExplorationSpecialItem(SpecialItemPurpose specialItem)
    {
        if (specialItemSprites.ContainsKey(specialItem) || specialItemTexts.ContainsKey(specialItem))
            return;

        void AddIcon(UIGraphic graphic, int sourceHeight = 32, int height = 16)
        {
            var icon = AddSprite(new Rect(4, 182, 16, height), Graphics.GetUIGraphicIndex(graphic), game.UIPaletteIndex, 3);
            icon.TextureSize = new Size(32, sourceHeight);
            specialItemSprites.Add(specialItem, icon);
        }

        switch (specialItem)
        {
            case SpecialItemPurpose.Compass:
                AddIcon(UIGraphic.Compass);
                specialItemTexts.Add(specialItem, AddText(new Rect(22, 187, 18, 7), GetCompactCompassString(), TextColor.BrightGray));
                break;
            case SpecialItemPurpose.MonsterEye:
                AddIcon(game.MonsterSeesPlayer ? UIGraphic.MonsterEyeActive : UIGraphic.MonsterEyeInactive);
                break;
            case SpecialItemPurpose.DayTime:
                AddIcon(UIGraphic.Night + (int)game.GameTime!.GetDayTime());
                break;
            case SpecialItemPurpose.WindChain:
                AddIcon(UIGraphic.Windchain, 15, 8);
                break;
            case SpecialItemPurpose.MapLocation:
                specialItemTexts.Add(specialItem, AddText(new Rect(4, 187, 66, 7), GetCompactMapLocation(), TextColor.BrightGray));
                break;
            case SpecialItemPurpose.Clock:
                specialItemTexts.Add(specialItem, AddText(new Rect(4, 187, 30, 7), $"{game.GameTime!.Hour,2}:{game.GameTime.Minute:00}", TextColor.BrightGray));
                break;
            default:
                throw new AmbermoonException(ExceptionScope.Application, $"Invalid special item: {specialItem}");
        }

        ReflowExplorationHud();
        UpdateExplorationHudVisibility();
    }

    void ReflowExplorationHud()
    {
        int x = 4;
        foreach (var item in explorationHudOrder)
        {
            bool hasIcon = specialItemSprites.TryGetValue(item, out var icon);
            bool hasText = specialItemTexts.TryGetValue(item, out var text);
            if (!hasIcon && !hasText)
                continue;

            if (hasIcon)
            {
                icon!.X = x;
                icon.Y = item == SpecialItemPurpose.WindChain ? 186 : 182;
            }
            if (hasText)
                text!.SetPosition(new Position(x + (item == SpecialItemPurpose.Compass ? 18 : 0), 187));

            x += item switch
            {
                SpecialItemPurpose.Compass => 40,
                SpecialItemPurpose.Clock => 34,
                SpecialItemPurpose.MapLocation => 66,
                _ => 20
            };
        }

        if (x > 4)
        {
            explorationHudBackground ??= CreateArea(new Rect(2, 180, x, 20), new Render.Color(12, 12, 10, 180), 1);
            explorationHudBackground.Resize(x, 20);
        }
    }

    internal void UpdateExplorationHudVisibility()
    {
        bool visible = IsExplorationLayout && !PopupActive && !OptionMenuOpen;
        foreach (var icon in specialItemSprites.Values)
            icon.Visible = visible;
        foreach (var text in specialItemTexts.Values)
            text.Visible = visible;
        if (explorationHudBackground != null)
            explorationHudBackground.Visible = visible && (specialItemSprites.Count != 0 || specialItemTexts.Count != 0);

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
    }
}

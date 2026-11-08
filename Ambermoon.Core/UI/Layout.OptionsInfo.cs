using Ambermoon.Data;
using Ambermoon.Data.Enumerations;
using Ambermoon.Render;
using TextColor = Ambermoon.Data.Enumerations.Color;

namespace Ambermoon.UI;

internal partial class Layout
{
    void AddOptionMenuInfo()
    {
        string hints = game.GameLanguage switch
        {
            GameLanguage.German => "3D C/Mausrad-Klick: Mausmodus ändern^Caps: 60%/100%  Shift halten: 100%^Esc/Enter: Aktionsmenü öffnen^F11: Vollbild wechseln",
            GameLanguage.French => "3D C/clic molette: mode souris^Caps: 60%/100%  Maintenir Shift: 100%^Esc/Entrée: menu d'actions^F11: plein écran",
            GameLanguage.Polish => "3D C/klik rolki: tryb myszy^Caps: 60%/100%  Trzymaj Shift: 100%^Esc/Enter: menu akcji^F11: pełny ekran",
            GameLanguage.Czech => "3D C/stisk kolečka: režim myši^Caps: 60%/100%  Držet Shift: 100%^Esc/Enter: nabídka akcí^F11: celá obrazovka",
            _ => "3D C/wheel click: switch mouse mode^Caps: 60%/100%  Hold Shift: 100%^Esc/Enter: open action menu^F11: toggle fullscreen"
        };
        string content = $"Immersive UI V1^{game.GetFullVersion()}^^{hints}";
        const int boxWidth = 240;
        const int padding = 4;
        var text = RenderView.TextProcessor.WrapText(RenderView.TextProcessor.CreateText(content),
            new Rect(0, 0, boxWidth - 2 * padding, Global.VirtualScreenHeight),
            new Size(Global.GlyphWidth, Global.GlyphLineHeight));
        int textHeight = text.LineCount * Global.GlyphLineHeight;
        var availableArea = Type == LayoutType.Battle ? Global.CombatBackgroundArea :
            new Rect(0, Global.LayoutY, Global.VirtualScreenWidth, ExplorationActionsArea.Top - Global.LayoutY);
        var boxArea = Rect.Create(availableArea.Center, new Size(boxWidth, textHeight + 2 * padding));
        var box = AddSprite(boxArea, Graphics.GetCustomUIGraphicIndex(UICustomGraphic.BiggerInfoBox), game.UIPaletteIndex, 2);
        box.TextureSize = new Size(144, 40);
        // Keep the options menu's two-sprite/one-text lifetime and cleanup order.
        AddText(new Rect(boxArea.X + padding, boxArea.Y + padding, boxWidth - 2 * padding, textHeight),
            text, TextColor.BrightGray, TextAlign.Center, 3);
    }
}

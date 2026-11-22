using System;
using System.Linq;
using Ambermoon.Data;
using Ambermoon.Data.Enumerations;
using Ambermoon.Render;

namespace Ambermoon;

partial class GameCore
{
    const int RuneTableItemIndex = 145;

    readonly string gameVersionName;
    readonly string fullVersion;

    internal string GameVersionName => gameVersionName;
    internal GameLanguage GameLanguage { get; private set; }
    public ICoreConfiguration CoreConfiguration { get; private set; }
    public bool AutoDerune => TravelType == TravelType.Fly || // Superman mode can also read runes as text
        (CoreConfiguration.AutoDerune && PartyMembers.Any(p => p.HasItem(RuneTableItemIndex)));

    public delegate void FullscreenChangeHandler(WindowMode windowMode);
    public delegate void ResolutionChangeHandler(int? oldWidth);

    readonly FullscreenChangeHandler fullscreenChangeHandler;
    readonly ResolutionChangeHandler resolutionChangeHandler;

    public event Action<ICoreConfiguration, bool>? ConfigurationChanged;
    /// <summary>
    /// Optional provider returning the active monitor's refresh rate (Hz) so the Options UI
    /// can display the resolved frame rate for the "2x" frame limit preset.
    /// </summary>
    public Func<int?>? MonitorRefreshRateProvider { get; set; }
    /// <summary>
    /// Invoked when the user explicitly changes the in-game Frame limit option, allowing the
    /// window host to clear any non-persistent CLI --fps / --vsync overrides immediately.
    /// </summary>
    public Action? FrameLimitChanged { get; set; }
    /// <summary>
    /// Optional per-run override for the active 3D renderer (e.g. from CLI --renderer).
    /// </summary>
    public Renderer3DMode? Renderer3DOverride { get; set; }
    public Renderer3DMode ActiveRenderer3D => Renderer3DOverride ?? CoreConfiguration.Renderer3D;
    /// <summary>
    /// Optional per-run override for the Experimental 3D texture filter (e.g. from CLI --texture-filter).
    /// </summary>
    public Texture3DFilterMode? Texture3DFilterOverride { get; set; }
    public Texture3DFilterMode ActiveTexture3DFilter => Texture3DFilterOverride ?? CoreConfiguration.Texture3DFilter;
    /// <summary>
    /// Optional per-run override for Experimental 3D lighting (e.g. from CLI --lighting).
    /// </summary>
    public Lighting3DMode? Lighting3DOverride { get; set; }
    public Lighting3DMode ActiveLighting3D => Lighting3DOverride ?? CoreConfiguration.Lighting3D;

    public void RefreshRenderer3DMode()
    {
        renderView.Renderer3DMode = ActiveRenderer3D;
        renderView.Texture3DFilterMode = ActiveTexture3DFilter;
        renderView.Lighting3DMode = ActiveLighting3D;
    }

    internal void NotifyConfigurationChange(bool windowChange)
    {
        // Also apply while in 2D so the next 3D map uses the selected setting.
        renderView.SetSmoothDistantFloor(CoreConfiguration.SmoothDistantFloor);
        renderView.SetSmoothWalls(CoreConfiguration.SmoothWalls);
        RefreshRenderer3DMode();
        if (Is3D)
        {
            renderMap3D?.UpdateFloorAndCeilingVisibility(CoreConfiguration.ShowFloor, CoreConfiguration.ShowCeiling);
            renderMap3D?.SetFog(Map!, MapManager.GetLabdataForMap(Map));
        }

        ConfigurationChanged?.Invoke(CoreConfiguration, windowChange);

        // Ensure the music is updated
        UpdateMusic();

        if (windowChange && !Trapped)
        {
            trappedMousePositionOffset.X = 0;
            trappedMousePositionOffset.Y = 0;
            MouseTrappedChanged?.Invoke(false, lastMousePosition);
            UpdateCursor();
        }
    }
    internal void RequestFullscreenChange(WindowMode windowMode) => fullscreenChangeHandler?.Invoke(windowMode);
    internal void NotifyResolutionChange(int? oldWidth) => resolutionChangeHandler?.Invoke(oldWidth);
    public string GetFullVersion() => fullVersion;
    public void ExternalGraphicFilterChanged() => layout.OnExternalGraphicFilterChanged();
    public void ExternalGraphicFilterOverlayChanged() => layout.OnExternalGraphicFilterOverlayChanged();
    public void ExternalEffectsChanged() => layout.OnExternalEffectsChanged();
    public void ExternalBattleSpeedChanged()
    {
        SetBattleSpeed(CoreConfiguration.BattleSpeed);
        layout.OnExternalBattleSpeedChanged();
    }
    public void ExternalMusicChanged() => layout.OnExternalMusicChanged();
    public void ExternalVolumeChanged() => layout.OnExternalVolumeChanged();
    public void PreFullscreenChanged()
    {
        mouseLookFullscreenChanging = true;
        ReleaseMouseLookCapture();
        preFullscreenMousePosition = renderView.ScreenToGame(GetMousePosition(lastMousePosition));
        preFullscreenChangeTrapMouseArea = trapMouseGameArea;
        if (trapMouseGameArea != null)
            UntrapMouse();
    }
    public void PostFullscreenChanged()
    {
        if (preFullscreenMousePosition != null)
            lastMousePosition = renderView.GameToScreen(preFullscreenMousePosition);

        if (preFullscreenChangeTrapMouseArea != null)
        {
            TrapMouse(preFullscreenChangeTrapMouseArea);
            preFullscreenChangeTrapMouseArea = null;
        }
        else
        {
            MousePositionChanged?.Invoke(lastMousePosition);
        }

        if (layout?.OptionMenuOpen == true)
        {
            layout.UpdateFullscreenOption();
        }
        mouseLookFullscreenChanging = false;
        UpdateMouseLookCapture();
    }
}

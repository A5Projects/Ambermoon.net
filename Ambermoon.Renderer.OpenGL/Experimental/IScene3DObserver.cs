namespace Ambermoon.Renderer.OpenGL;

using Render;

/// <summary>
/// Receives lifecycle and property notifications from <see cref="Surface3D"/> instances
/// so the experimental 3D scene builder stays synchronized with Classic layers at all times,
/// enabling instantaneous runtime switching between Classic and Experimental 3D passes.
/// </summary>
internal interface IScene3DObserver
{
    void OnSurfaceAdded(Layer layer, ISurface3D surface);
    void OnSurfaceRemoved(Layer layer, ISurface3D surface);
    void OnSurfaceUpdated(Layer layer, ISurface3D surface);
    void OnTextureFactorChanged(Layer layer, uint textureFactor);
}

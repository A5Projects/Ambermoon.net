#if !GLES
using Silk.NET.OpenGL;
using System;

namespace Ambermoon.Renderer.OpenGL.Experimental;

/// <summary>
/// Owns the offscreen 3D render target, <see cref="Scene3DBuilder"/> meshes,
/// <see cref="ExperimentalTextureCache"/> RGBA texture arrays, and Experimental 3D shaders,
/// keeping the Experimental 3D pass isolated from Classic layers and 2D/UI compositing.
/// </summary>
internal sealed class Experimental3DRenderer : IDisposable
{
    readonly State state;
    readonly FrameBuffer sceneTarget;
    readonly ExperimentalSurfaceShader surfaceShader;
    readonly ExperimentalBillboardShader billboardShader;
    readonly ExperimentalCompositeShader compositeShader;
    uint compositeVao;
    bool disposed;

    public Scene3DBuilder Scene { get; }
    public ExperimentalTextureCache TextureCache { get; }
    public Experimental3DState Uniforms { get; } = new();
    public ulong FramesRendered { get; private set; }
    public Size LastTargetSize { get; private set; } = new(0, 0);

    public Experimental3DRenderer(State state)
    {
        this.state = state;
        Scene = new Scene3DBuilder(state);
        TextureCache = new ExperimentalTextureCache(state);
        sceneTarget = new FrameBuffer(state);
        surfaceShader = new ExperimentalSurfaceShader(state);
        billboardShader = new ExperimentalBillboardShader(state);
        compositeShader = new ExperimentalCompositeShader(state);
        compositeVao = state.Gl.GenVertexArray();
    }

    public void Begin3DPass(Rect viewport, Position backgroundOffset)
    {
        if (disposed)
            return;

        LastTargetSize = new Size(viewport.Width, viewport.Height);
        sceneTarget.Bind(viewport.Width, viewport.Height);
        state.Gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        state.Gl.Clear((uint)ClearBufferMask.ColorBufferBit | (uint)ClearBufferMask.DepthBufferBit);
        state.Gl.Viewport(backgroundOffset.X, backgroundOffset.Y, (uint)viewport.Width, (uint)viewport.Height);
    }

    public void RenderGeometryAndComposite(
        RenderLayer ceilingLayer,
        RenderLayer wallLayer,
        RenderLayer billboardLayer,
        Texture palette,
        Rect viewport,
        Rect mapViewArea,
        FrameBuffer destinationFrameBuffer,
        bool useEffectFrameBuffer)
    {
        if (disposed)
            return;

        var gl = state.Gl;
        TextureCache.BeginFrame();
        ulong frameIndex = FramesRendered + 1;

        ulong palReplaceHash = ExperimentalTextureCache.ComputePaletteReplacementHash(Uniforms.PaletteReplacement);
        byte skyKey = Uniforms.SkyColorIndex.HasValue && Uniforms.SkyColorIndex.Value <= 31u
            ? (byte)Uniforms.SkyColorIndex.Value
            : (byte)255;
        uint skyReplaceRgba = skyKey != 255 ? ExperimentalTextureCache.PackRgba(Uniforms.SkyReplaceColor) : 0u;

        // Draw 3D geometry into the exact Map3DViewArea sub-viewport inside sceneTarget.
        gl.Viewport(mapViewArea.X, viewport.Height - (mapViewArea.Y + mapViewArea.Height),
            (uint)mapViewArea.Width, (uint)mapViewArea.Height);
        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.CullFace);
        gl.Disable(EnableCap.DepthTest);

        if (ceilingLayer.Visible && ceilingLayer.Texture is Texture ceilingTexture && palette != null)
        {
            var ctx = new TextureCacheContext(ceilingTexture.Index, palette.Index, palReplaceHash, skyKey, skyReplaceRgba, Uniforms.FilterMode);
            surfaceShader.BindAndApply(Uniforms);
            Scene.FloorCeilingBatch.Draw(TextureCache, ceilingTexture, palette, Uniforms, ctx, frameIndex,
                bucket => surfaceShader.SetLayerTextureSize(bucket.Width, bucket.Height));
        }

        gl.Enable(EnableCap.DepthTest);

        if (wallLayer.Visible && wallLayer.Texture is Texture wallTexture && palette != null)
        {
            var ctx = new TextureCacheContext(wallTexture.Index, palette.Index, palReplaceHash, skyKey, skyReplaceRgba, Uniforms.FilterMode);
            surfaceShader.BindAndApply(Uniforms);
            Scene.WallBatch.Draw(TextureCache, wallTexture, palette, Uniforms, ctx, frameIndex,
                bucket => surfaceShader.SetLayerTextureSize(bucket.Width, bucket.Height));
        }

        gl.Disable(EnableCap.CullFace);

        if (billboardLayer.Visible && billboardLayer.Texture is Texture billboardTexture && palette != null)
        {
            var ctx = new TextureCacheContext(billboardTexture.Index, palette.Index, palReplaceHash, skyKey, skyReplaceRgba, Uniforms.FilterMode);
            billboardShader.BindAndApply(Uniforms);
            Scene.BillboardBatch.Draw(TextureCache, billboardTexture, palette, Uniforms, ctx, frameIndex,
                bucket => billboardShader.SetLayerTextureSize(bucket.Width, bucket.Height));
        }

        gl.ActiveTexture(GLEnum.Texture0);
        gl.BindTexture(TextureTarget.Texture2DArray, 0);

        // Composite the completed offscreen 3D scene back to the destination framebuffer
        // (either effectFrameBuffer or default FBO 0) so 2D/UI/GraphicFilter/Effects proceed identically.
        if (useEffectFrameBuffer && destinationFrameBuffer != null)
        {
            destinationFrameBuffer.Bind(viewport.Width, viewport.Height);
            gl.Viewport(0, 0, (uint)viewport.Width, (uint)viewport.Height);
        }
        else
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0u);
            gl.Viewport(viewport.X, viewport.Y, (uint)viewport.Width, (uint)viewport.Height);
        }

        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(false);

        compositeShader.Bind();
        sceneTarget.BindAsTexture();
        gl.BindVertexArray(compositeVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.BindVertexArray(0);
        gl.ActiveTexture(GLEnum.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        gl.DepthMask(true);
        gl.Enable(EnableCap.DepthTest);
        Texture.InvalidateActiveTexture();

        FramesRendered++;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Scene.Dispose();
        TextureCache.Dispose();
        sceneTarget.Dispose();
        surfaceShader.Dispose();
        billboardShader.Dispose();
        compositeShader.Dispose();
        if (compositeVao != 0)
        {
            state.Gl.DeleteVertexArray(compositeVao);
            compositeVao = 0;
        }
    }
}
#endif

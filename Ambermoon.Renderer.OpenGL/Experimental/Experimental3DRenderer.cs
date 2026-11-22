#if !GLES
using Silk.NET.OpenGL;
using System;
using System.Diagnostics;
using System.Numerics;

namespace Ambermoon.Renderer.OpenGL.Experimental;

using Render;
using Texture = Ambermoon.Renderer.OpenGL.Texture;

/// <summary>
/// Owns the HDR offscreen 3D render target (<c>RGBA16F</c> with <c>RGBA8</c> fallback),
/// <see cref="Scene3DBuilder"/> meshes, <see cref="ExperimentalTextureCache"/> RGBA texture arrays,
/// <see cref="SceneLightingEngine"/> AO/blob-shadow/point-light pipeline, and Experimental 3D shaders.
/// </summary>
internal sealed class Experimental3DRenderer : IDisposable
{
    readonly State state;
    readonly FrameBuffer sceneTarget;
    readonly ExperimentalSurfaceShader surfaceShader;
    readonly ExperimentalBillboardShader billboardShader;
    readonly ExperimentalCompositeShader compositeShader;
    readonly long startTimestamp;
    uint compositeVao;
    bool disposed;

    public Scene3DBuilder Scene { get; }
    public ExperimentalTextureCache TextureCache { get; }
    public SceneLightingEngine Lighting { get; }
    public Experimental3DState Uniforms { get; } = new();
    public ulong FramesRendered { get; private set; }
    public Size LastTargetSize { get; private set; } = new(0, 0);

    public Experimental3DRenderer(State state)
    {
        this.state = state;
        startTimestamp = Stopwatch.GetTimestamp();
        Scene = new Scene3DBuilder(state);
        TextureCache = new ExperimentalTextureCache(state);
        Lighting = new SceneLightingEngine(state);
        sceneTarget = new FrameBuffer(state, InternalFormat.Rgba16f);
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
        Camera3D camera,
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
        bool hasCeiling = Uniforms.Environment.HasCeiling;

        var ceilingTexture = ceilingLayer?.Texture as Texture;
        var wallTexture = wallLayer?.Texture as Texture;
        var billboardTexture = billboardLayer?.Texture as Texture;

        TextureCacheContext MakeContext(Texture tex) =>
            new(tex.Index, palette.Index, palReplaceHash, skyKey, skyReplaceRgba, Uniforms.FilterMode, hasCeiling);

        // Upload & analyze Wall + Billboard batches first so AO, blob shadows, and emissive point lights
        // reflect the exact current scene with zero 1-frame lag before drawing Floor/Ceiling.
        if (palette != null)
        {
            if (wallTexture != null)
                Scene.WallBatch.EnsureUploaded(TextureCache, wallTexture, palette, Uniforms, MakeContext(wallTexture));
            if (billboardTexture != null)
                Scene.BillboardBatch.EnsureUploaded(TextureCache, billboardTexture, palette, Uniforms, MakeContext(billboardTexture));
            if (ceilingTexture != null)
                Scene.FloorCeilingBatch.EnsureUploaded(TextureCache, ceilingTexture, palette, Uniforms, MakeContext(ceilingTexture));
        }

        Vector3 camPos = camera != null
            ? new Vector3(-camera.RenderX, -camera.RenderY, -camera.RenderZ)
            : new Vector3(0.0f, 0.35f, 0.0f);
        float elapsedSeconds = (float)(Stopwatch.GetTimestamp() - startTimestamp) / Stopwatch.Frequency;

        FrameLightingUniforms lightingUniforms = Uniforms.LightingMode == Lighting3DMode.Atmospheric
            ? Lighting.PrepareFrame(Scene.FloorCeilingBatch, Scene.WallBatch, Scene.BillboardBatch,
                TextureCache, Uniforms, camPos, elapsedSeconds)
            : default;

        // Draw 3D geometry into the exact Map3DViewArea sub-viewport inside sceneTarget.
        gl.Viewport(mapViewArea.X, viewport.Height - (mapViewArea.Y + mapViewArea.Height),
            (uint)mapViewArea.Width, (uint)mapViewArea.Height);
        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.CullFace);
        gl.Disable(EnableCap.DepthTest);

        if (Uniforms.LightingMode == Lighting3DMode.Atmospheric)
            Lighting.BindAoShadowMap(1);

        if (ceilingLayer != null && ceilingLayer.Visible && ceilingTexture != null && palette != null)
        {
            var ctx = MakeContext(ceilingTexture);
            surfaceShader.BindAndApply(Uniforms, lightingUniforms, Lighting);
            Scene.FloorCeilingBatch.Draw(TextureCache, ceilingTexture, palette, Uniforms, ctx, frameIndex,
                bucket => surfaceShader.SetLayerTextureSize(bucket.Width, bucket.Height));
        }

        gl.Enable(EnableCap.DepthTest);

        if (wallLayer != null && wallLayer.Visible && wallTexture != null && palette != null)
        {
            var ctx = MakeContext(wallTexture);
            surfaceShader.BindAndApply(Uniforms, lightingUniforms, Lighting);
            Scene.WallBatch.Draw(TextureCache, wallTexture, palette, Uniforms, ctx, frameIndex,
                bucket => surfaceShader.SetLayerTextureSize(bucket.Width, bucket.Height));
        }

        gl.Disable(EnableCap.CullFace);

        if (billboardLayer != null && billboardLayer.Visible && billboardTexture != null && palette != null)
        {
            var ctx = MakeContext(billboardTexture);
            billboardShader.BindAndApply(Uniforms, lightingUniforms, camPos.X, camPos.Y, camPos.Z);
            Scene.BillboardBatch.Draw(TextureCache, billboardTexture, palette, Uniforms, ctx, frameIndex,
                bucket => billboardShader.SetLayerTextureSize(bucket.Width, bucket.Height));
        }

        gl.ActiveTexture(GLEnum.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, 0);
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

        compositeShader.Bind(Uniforms.LightingMode);
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
        Lighting.Dispose();
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

#if !GLES
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ambermoon.Renderer.OpenGL.Experimental;

using Render;
using Texture = Ambermoon.Renderer.OpenGL.Texture;

internal readonly record struct StaticSceneLight(
    float X,
    float Y,
    float Z,
    float Radius,
    float R,
    float G,
    float B,
    float BaseIntensity,
    float FlickerAmount,
    float PhaseSeed,
    bool IsCoolMagic);

internal struct FrameLightingUniforms
{
    public Vector3 SunDir;
    public Vector3 SunColor;
    public Vector3 SkyAmbient;
    public Vector3 GroundAmbient;
    public Vector4 PlayerLightPosRadius;
    public Vector4 PlayerLightColorIntensity;
    public Vector3 ShadowTint;
    public Vector3 HighlightTint;
    public Vector4 EnvParams; // x = aoStrength, y = splitStrength, z = fogHeightFalloff, w = emissiveFogResist
    public float FogExpDensityScale;
    public int ActivePointLightCount;
    public float[] PointLightPosRadius;     // 16 * 4 floats
    public float[] PointLightColorIntensity; // 16 * 4 floats
}

/// <summary>
/// Manages baked wall/floor/ceiling contact AO, soft billboard floor shadows (<c>GL_RG8</c>),
/// automatic emissive point light extraction (torches, candles, lava, arcane runes, lit windows),
/// and per-environment atmospheric lighting + split-toning uniforms for the Experimental 3D pass.
/// </summary>
internal sealed class SceneLightingEngine : IDisposable
{
    public const int MaxShaderPointLights = 16;

    readonly State state;
    readonly List<StaticSceneLight> billboardLights = new(32);
    readonly List<StaticSceneLight> wallLights = new(32);
    readonly List<(StaticSceneLight Light, float DistSq)> candidateLights = new(64);
    readonly float[] pointPosRadius = new float[MaxShaderPointLights * 4];
    readonly float[] pointColorIntensity = new float[MaxShaderPointLights * 4];

    uint aoShadowTex;
    int aoWidth;
    int aoHeight;
    float aoMinX;
    float aoMinZ;
    float aoInvSpanX = 1.0f;
    float aoInvSpanZ = 1.0f;
    bool aoValid;

    byte[] northAo = [];
    byte[] southAo = [];
    byte[] eastAo = [];
    byte[] westAo = [];
    byte[] staticWallAo = [];
    byte[] dynamicShadow = [];
    byte[] uploadRg = [];

    int lastWallRebuild = -1;
    int lastBillboardRebuild = -1;
    Environment3DKind lastLightEnvKind = (Environment3DKind)(-1);
    float lastLightDaylight = -1.0f;
    ulong lastTextureEpoch = 0;

    bool disposed;

    public uint AoShadowTextureId => aoShadowTex;
    public Vector4 AoMapBounds => new(aoMinX, aoMinZ, aoInvSpanX, aoInvSpanZ);
    public Size AoMapSize => new(aoWidth, aoHeight);
    public int AoRebuilds { get; private set; }
    public int ExtractedBillboardLights => billboardLights.Count;
    public int ExtractedWallLights => wallLights.Count;
    public int ActivePointLightCount { get; private set; }

    public SceneLightingEngine(State state)
    {
        this.state = state;
        var gl = state.Gl;
        aoShadowTex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, aoShadowTex);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        byte[] white = [255, 255];
        unsafe
        {
            fixed (byte* ptr = white)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.RG8, 1, 1, 0, PixelFormat.RG, PixelType.UnsignedByte, ptr);
        }
        gl.BindTexture(TextureTarget.Texture2D, 0);
        Texture.InvalidateActiveTexture();
    }

    public FrameLightingUniforms PrepareFrame(
        SurfaceMeshBatch floorCeilingBatch,
        SurfaceMeshBatch wallBatch,
        BillboardMeshBatch billboardBatch,
        ExperimentalTextureCache textureCache,
        Experimental3DState uniforms,
        Vector3 cameraWorldPos,
        float elapsedSeconds)
    {
        var env = uniforms.Environment;
        bool wallsChanged = wallBatch.RebuildCount != lastWallRebuild;
        bool bbChanged = billboardBatch.RebuildCount != lastBillboardRebuild;
        bool epochChanged = textureCache.CacheEpoch != lastTextureEpoch;

        if (wallsChanged || bbChanged || !aoValid)
        {
            RebuildAoShadowMap(floorCeilingBatch, wallBatch, billboardBatch, env, forceStaticRebuild: wallsChanged || !aoValid);
            lastWallRebuild = wallBatch.RebuildCount;
            lastBillboardRebuild = billboardBatch.RebuildCount;
        }

        if (wallsChanged || bbChanged || epochChanged ||
            env.Kind != lastLightEnvKind || MathF.Abs(env.DaylightFactor - lastLightDaylight) > 0.20f)
        {
            ExtractStaticLights(wallBatch, billboardBatch, textureCache, env);
            lastLightEnvKind = env.Kind;
            lastLightDaylight = env.DaylightFactor;
            lastTextureEpoch = textureCache.CacheEpoch;
        }

        SelectFrameLights(cameraWorldPos, env, elapsedSeconds);
        return BuildFrameUniforms(uniforms, cameraWorldPos, elapsedSeconds);
    }

    public void BindAoShadowMap(int textureUnit = 1)
    {
        var gl = state.Gl;
        gl.ActiveTexture(GLEnum.Texture0 + textureUnit);
        gl.BindTexture(TextureTarget.Texture2D, aoShadowTex);
        Texture.InvalidateActiveTexture();
    }

    void RebuildAoShadowMap(
        SurfaceMeshBatch floorCeilingBatch,
        SurfaceMeshBatch wallBatch,
        BillboardMeshBatch billboardBatch,
        in Scene3DEnvironment env,
        bool forceStaticRebuild)
    {
        var bounds = !wallBatch.Bounds.IsEmpty
            ? wallBatch.Bounds
            : !floorCeilingBatch.Bounds.IsEmpty
                ? floorCeilingBatch.Bounds
                : new SceneBounds3D(0.0f, 0.0f, -env.MapHeight * env.BlockSize, env.MapWidth * env.BlockSize, env.WallHeight, 0.0f);

        float block = Math.Max(0.25f, env.BlockSize);
        float pad = block;
        float minX = bounds.MinX - pad;
        float minZ = bounds.MinZ - pad;
        float maxX = bounds.MaxX + pad;
        float maxZ = bounds.MaxZ + pad;
        float spanX = Math.Max(block, maxX - minX);
        float spanZ = Math.Max(block, maxZ - minZ);

        // 8 texels per block gives smooth sub-block contact AO gradients on normal maps (capped at 512).
        int desiredW = Math.Clamp((int)MathF.Ceiling(spanX / block * 8.0f), 16, 512);
        int desiredH = Math.Clamp((int)MathF.Ceiling(spanZ / block * 8.0f), 16, 512);

        bool sizeChanged = desiredW != aoWidth || desiredH != aoHeight;
        bool boundsChanged = MathF.Abs(minX - aoMinX) > 0.01f || MathF.Abs(minZ - aoMinZ) > 0.01f ||
            MathF.Abs((1.0f / spanX) - aoInvSpanX) > 1e-5f || MathF.Abs((1.0f / spanZ) - aoInvSpanZ) > 1e-5f;

        if (sizeChanged || boundsChanged || forceStaticRebuild)
        {
            aoWidth = desiredW;
            aoHeight = desiredH;
            aoMinX = minX;
            aoMinZ = minZ;
            aoInvSpanX = 1.0f / spanX;
            aoInvSpanZ = 1.0f / spanZ;

            int count = aoWidth * aoHeight;
            if (staticWallAo.Length < count)
            {
                northAo = new byte[count];
                southAo = new byte[count];
                eastAo = new byte[count];
                westAo = new byte[count];
                staticWallAo = new byte[count];
                dynamicShadow = new byte[count];
                uploadRg = new byte[count * 2];
            }

            BakeStaticWallAo(wallBatch.ActiveSurfaces, block);
        }

        BakeDynamicBillboardShadows(billboardBatch.ActiveSurfaces, env);

        int total = aoWidth * aoHeight;
        for (int i = 0; i < total; i++)
        {
            uploadRg[i * 2 + 0] = staticWallAo[i];
            uploadRg[i * 2 + 1] = dynamicShadow[i];
        }

        var gl = state.Gl;
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.ActiveTexture(GLEnum.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, aoShadowTex);
        unsafe
        {
            fixed (byte* ptr = uploadRg)
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.RG8,
                    (uint)aoWidth, (uint)aoHeight, 0, PixelFormat.RG, PixelType.UnsignedByte, ptr);
            }
        }
        gl.BindTexture(TextureTarget.Texture2D, 0);
        Texture.InvalidateActiveTexture();

        aoValid = true;
        AoRebuilds++;
    }

    void BakeStaticWallAo(IReadOnlyList<ISurface3D> wallSurfaces, float block)
    {
        int total = aoWidth * aoHeight;
        Array.Fill(northAo, (byte)255, 0, total);
        Array.Fill(southAo, (byte)255, 0, total);
        Array.Fill(eastAo, (byte)255, 0, total);
        Array.Fill(westAo, (byte)255, 0, total);

        float aoReach = 0.42f * block;
        float spanX = 1.0f / aoInvSpanX;
        float spanZ = 1.0f / aoInvSpanZ;
        float texelSizeX = spanX / aoWidth;
        float texelSizeZ = spanZ / aoHeight;
        float pad = aoReach + Math.Max(texelSizeX, texelSizeZ);

        for (int i = 0; i < wallSurfaces.Count; i++)
        {
            var wall = wallSurfaces[i];
            if (!wall.Visible || wall.Type != SurfaceType.Wall)
                continue;

            SurfaceMeshBatch.GetWallSegment(wall, out float e0x, out float e0z, out float e1x, out float e1z, out float nx, out float nz);
            float dx = e1x - e0x;
            float dz = e1z - e0z;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f)
                continue;

            float txDir = dx / len;
            float tzDir = dz / len;

            float reachMinX = Math.Min(Math.Min(e0x, e1x), Math.Min(e0x + nx * aoReach, e1x + nx * aoReach)) - texelSizeX;
            float reachMaxX = Math.Max(Math.Max(e0x, e1x), Math.Max(e0x + nx * aoReach, e1x + nx * aoReach)) + texelSizeX;
            float reachMinZ = Math.Min(Math.Min(e0z, e1z), Math.Min(e0z + nz * aoReach, e1z + nz * aoReach)) - texelSizeZ;
            float reachMaxZ = Math.Max(Math.Max(e0z, e1z), Math.Max(e0z + nz * aoReach, e1z + nz * aoReach)) + texelSizeZ;

            int tx0 = Math.Clamp((int)MathF.Floor((reachMinX - aoMinX) * aoInvSpanX * aoWidth), 0, aoWidth - 1);
            int tx1 = Math.Clamp((int)MathF.Ceiling((reachMaxX - aoMinX) * aoInvSpanX * aoWidth), 0, aoWidth - 1);
            int tz0 = Math.Clamp((int)MathF.Floor((reachMinZ - aoMinZ) * aoInvSpanZ * aoHeight), 0, aoHeight - 1);
            int tz1 = Math.Clamp((int)MathF.Ceiling((reachMaxZ - aoMinZ) * aoInvSpanZ * aoHeight), 0, aoHeight - 1);

            byte[] targetAxis = wall.WallOrientation switch
            {
                WallOrientation.Normal => northAo,
                WallOrientation.Rotated90 => eastAo,
                WallOrientation.Rotated180 => southAo,
                _ => westAo,
            };

            float baseAo = wall.Alpha ? 0.85f : 0.62f;

            for (int tz = tz0; tz <= tz1; tz++)
            {
                float wz = aoMinZ + (tz + 0.5f) / aoHeight * spanZ;
                int row = tz * aoWidth;
                float relZ = wz - e0z;

                for (int tx = tx0; tx <= tx1; tx++)
                {
                    float wx = aoMinX + (tx + 0.5f) / aoWidth * spanX;
                    float relX = wx - e0x;

                    float dN = relX * nx + relZ * nz;
                    if (dN < -0.05f * block || dN >= aoReach)
                        continue;

                    float dT = relX * txDir + relZ * tzDir;
                    float endDist = dT < 0.0f ? -dT : (dT > len ? dT - len : 0.0f);
                    if (endDist >= aoReach)
                        continue;

                    float perpDist = Math.Max(0.0f, dN);
                    float dist = MathF.Sqrt(perpDist * perpDist + endDist * endDist);
                    if (dist >= aoReach)
                        continue;

                    float t = dist / aoReach;
                    float smooth = t * t * (3.0f - 2.0f * t);
                    float aoVal = baseAo + (1.0f - baseAo) * smooth;
                    byte valByte = (byte)Math.Clamp((int)(aoVal * 255.0f + 0.5f), 0, 255);
                    int idx = row + tx;
                    if (valByte < targetAxis[idx])
                        targetAxis[idx] = valByte;
                }
            }
        }

        // Combine perpendicular wall directions multiplicatively without double-darkening colinear seams.
        const float inv255 = 1.0f / 255.0f;
        for (int i = 0; i < total; i++)
        {
            float f = (northAo[i] * inv255) * (southAo[i] * inv255) * (eastAo[i] * inv255) * (westAo[i] * inv255);
            staticWallAo[i] = (byte)Math.Clamp((int)(f * 255.0f + 0.5f), 0, 255);
        }
    }

    void BakeDynamicBillboardShadows(IReadOnlyList<ISurface3D> bbSurfaces, in Scene3DEnvironment env)
    {
        int total = aoWidth * aoHeight;
        Array.Fill(dynamicShadow, (byte)255, 0, total);

        float block = Math.Max(0.25f, env.BlockSize);
        float wallH = Math.Max(0.25f, env.WallHeight);
        float spanX = 1.0f / aoInvSpanX;
        float spanZ = 1.0f / aoInvSpanZ;

        for (int i = 0; i < bbSurfaces.Count; i++)
        {
            var surf = bbSurfaces[i];
            if (!surf.Visible || surf.Type != SurfaceType.Billboard)
                continue;

            float bottomY = surf.Y - surf.Height;
            // Skip overhead ceiling props (chandeliers/banners/stalactites) that don't touch the floor.
            if (bottomY > 0.22f * wallH)
                continue;
            if (surf.Width < 0.12f * block)
                continue;

            float radius = Math.Clamp(0.40f * surf.Width, 0.15f * block, 0.48f * block);
            float radiusSq = radius * radius;
            float depth = Math.Clamp(0.22f + 0.24f * (surf.Width / block), 0.22f, 0.44f);

            int tx0 = Math.Clamp((int)MathF.Floor((surf.X - radius - aoMinX) * aoInvSpanX * aoWidth), 0, aoWidth - 1);
            int tx1 = Math.Clamp((int)MathF.Ceiling((surf.X + radius - aoMinX) * aoInvSpanX * aoWidth), 0, aoWidth - 1);
            int tz0 = Math.Clamp((int)MathF.Floor((surf.Z - radius - aoMinZ) * aoInvSpanZ * aoHeight), 0, aoHeight - 1);
            int tz1 = Math.Clamp((int)MathF.Ceiling((surf.Z + radius - aoMinZ) * aoInvSpanZ * aoHeight), 0, aoHeight - 1);

            for (int tz = tz0; tz <= tz1; tz++)
            {
                float wz = aoMinZ + (tz + 0.5f) / aoHeight * spanZ;
                float dz = wz - surf.Z;
                int row = tz * aoWidth;

                for (int tx = tx0; tx <= tx1; tx++)
                {
                    float wx = aoMinX + (tx + 0.5f) / aoWidth * spanX;
                    float dx = wx - surf.X;
                    float distSq = dx * dx + dz * dz;
                    if (distSq >= radiusSq)
                        continue;

                    float q = 1.0f - distSq / radiusSq;
                    float val = 1.0f - depth * (q * q);
                    byte valByte = (byte)Math.Clamp((int)(val * 255.0f + 0.5f), 0, 255);
                    int idx = row + tx;
                    if (valByte < dynamicShadow[idx])
                        dynamicShadow[idx] = valByte;
                }
            }
        }
    }

    void ExtractStaticLights(
        SurfaceMeshBatch wallBatch,
        BillboardMeshBatch billboardBatch,
        ExperimentalTextureCache textureCache,
        in Scene3DEnvironment env)
    {
        billboardLights.Clear();
        wallLights.Clear();

        float block = Math.Max(0.25f, env.BlockSize);
        float wallH = Math.Max(0.25f, env.WallHeight);

        var bbSurfaces = billboardBatch.ActiveSurfaces;
        var bbKeys = billboardBatch.ActiveKeys;
        for (int i = 0; i < bbSurfaces.Count; i++)
        {
            var surf = bbSurfaces[i];
            if (!surf.Visible)
                continue;
            if (!textureCache.TryGetEmissive(bbKeys[i], out var em) || em.OpaqueCount <= 0)
                continue;

            float seed = MathF.Abs(MathF.Sin(surf.X * 12.9898f + surf.Z * 78.233f)) * 6.28318f;

            if (surf.Type == SurfaceType.BillboardFloor)
            {
                if (em.WarmFireCount >= 12)
                {
                    float cr = 0.68f + 0.32f * em.WarmR;
                    float cg = 0.34f * (0.5f + 0.5f * em.WarmG);
                    float cb = 0.10f * (0.5f + 0.5f * em.WarmB);
                    billboardLights.Add(new StaticSceneLight(
                        surf.X, Math.Max(0.18f * wallH, surf.Y + 0.15f * wallH), surf.Z,
                        3.4f * block, cr, cg, cb, 0.90f, 0.05f, seed, false));
                }
                else if (em.CoolBrightCount >= 14)
                {
                    billboardLights.Add(new StaticSceneLight(
                        surf.X, Math.Max(0.18f * wallH, surf.Y + 0.15f * wallH), surf.Z,
                        2.8f * block, 0.35f + 0.55f * em.CoolR, 0.55f + 0.45f * em.CoolG, 0.75f + 0.25f * em.CoolB,
                        0.75f, 0.07f, seed, true));
                }
                continue;
            }

            bool isCompactAnimatedFlame = surf.FrameCount >= 4 && surf.Width <= 0.24f * block
                && (em.WarmFireCount >= 2 || (em.WarmHotCount >= 16 && (float)em.WarmHotCount / em.OpaqueCount >= 0.10f));
            bool isAnimatedFireWithCore = surf.FrameCount > 1 && em.WarmFireCount >= 3 && em.WarmHotCount >= 2
                && (float)em.WarmFireCount / em.OpaqueCount <= 0.65f;
            bool isOverheadFlame = surf.FrameCount >= 4 && surf.Y >= 0.72f * wallH && surf.Height < 0.70f * wallH
                && (em.WarmFireCount >= 2 || (em.WarmHotCount >= 2 && em.OpaqueCount <= 80));
            bool isStaticFlame = surf.Width <= 0.24f * block && em.WarmFireCount >= 3 && em.WarmHotCount >= 3
                && (float)(em.WarmFireCount + em.WarmHotCount) / em.OpaqueCount <= 0.22f;

            if (isCompactAnimatedFlame || isAnimatedFireWithCore || isOverheadFlame || isStaticFlame)
            {
                bool overhead = surf.Y >= 0.72f * wallH && surf.Height < 0.7f * wallH;
                float lightY = Math.Clamp(surf.Y - surf.Height * em.WarmCentroidY, 0.15f * wallH, 0.90f * wallH);
                float cr = 0.80f + 0.20f * em.WarmR;
                float cg = 0.56f + 0.24f * em.WarmG;
                float cb = 0.22f + 0.14f * em.WarmB;
                float radius = overhead ? 3.8f * block : 3.2f * block;
                float intensity = overhead ? 0.88f : 0.92f;
                billboardLights.Add(new StaticSceneLight(
                    surf.X, lightY, surf.Z, radius, cr, cg, cb, intensity, 0.06f, seed, false));
                continue;
            }

            float coolFrac = (float)em.CoolBrightCount / em.OpaqueCount;
            bool isMagicGlow = ((surf.FrameCount > 1 && em.CoolBrightCount >= 6 && coolFrac <= 0.70f)
                || (em.CoolBrightCount >= 18 && coolFrac <= 0.28f))
                && em.CoolBrightCount > em.WarmFireCount + em.WarmHotCount;
            if (isMagicGlow)
            {
                float lightY = Math.Clamp(surf.Y - surf.Height * em.CoolCentroidY, 0.15f * wallH, 0.90f * wallH);
                billboardLights.Add(new StaticSceneLight(
                    surf.X, lightY, surf.Z, 3.0f * block,
                    0.30f + 0.55f * em.CoolR, 0.45f + 0.45f * em.CoolG, 0.78f + 0.22f * em.CoolB,
                    0.78f, 0.07f, seed, true));
            }
        }

        // Animated wall sconces and compact glowing night windows/lanterns (never earthy stone/cave/wood walls).
        var wallSurfaces = wallBatch.ActiveSurfaces;
        var wallKeys = wallBatch.ActiveKeys;
        bool allowStaticWallLights = env.Kind == Environment3DKind.OutdoorNight
            || env.Kind == Environment3DKind.OutdoorDawnDusk
            || env.Kind == Environment3DKind.IndoorLit;
        for (int i = 0; i < wallSurfaces.Count; i++)
        {
            var wall = wallSurfaces[i];
            if (!wall.Visible || wall.Type != SurfaceType.Wall)
                continue;
            if (!textureCache.TryGetEmissive(wallKeys[i], out var em) || em.OpaqueCount <= 0)
                continue;

            int totalWarm = em.WarmFireCount + em.WarmHotCount;
            float warmFrac = (float)totalWarm / em.OpaqueCount;
            bool animatedSconce = wall.FrameCount > 1 && em.WarmFireCount >= 6 && warmFrac <= 0.35f;
            bool staticWindowOrSconce = allowStaticWallLights && em.WarmHotCount >= 8 && em.WarmFireCount >= 3 && warmFrac <= 0.12f;

            if (animatedSconce || staticWindowOrSconce)
            {
                SurfaceMeshBatch.GetWallSegment(wall, out float e0x, out float e0z, out float e1x, out float e1z, out float nx, out float nz);
                float cx = 0.5f * (e0x + e1x) + nx * 0.35f * block;
                float cz = 0.5f * (e0z + e1z) + nz * 0.35f * block;
                float cy = Math.Clamp(wall.Y - wall.Height * em.WarmCentroidY, 0.20f * wallH, 0.85f * wallH);
                float seed = MathF.Abs(MathF.Sin(cx * 12.9898f + cz * 78.233f)) * 6.28318f;
                wallLights.Add(new StaticSceneLight(
                    cx, cy, cz, 2.75f * block,
                    0.74f + 0.26f * em.WarmR, 0.46f + 0.24f * em.WarmG, 0.15f + 0.12f * em.WarmB,
                    0.68f, 0.04f, seed, false));
            }
        }
    }

    void SelectFrameLights(Vector3 cameraWorldPos, in Scene3DEnvironment env, float elapsedSeconds)
    {
        candidateLights.Clear();
        float maxRange = 12.0f * Math.Max(0.25f, env.BlockSize);
        float maxRangeSq = maxRange * maxRange;

        float envScale = env.Kind switch
        {
            Environment3DKind.Dungeon => 1.05f,
            Environment3DKind.OutdoorNight => 0.90f,
            Environment3DKind.OutdoorDawnDusk => 0.60f,
            Environment3DKind.IndoorLit => 0.68f,
            _ => 0.18f,
        };

        void Collect(List<StaticSceneLight> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                var l = source[i];
                float dx = l.X - cameraWorldPos.X;
                float dz = l.Z - cameraWorldPos.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 <= maxRangeSq)
                    candidateLights.Add((l, d2));
            }
        }

        Collect(billboardLights);
        Collect(wallLights);

        if (candidateLights.Count > 1)
            candidateLights.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

        int count = Math.Min(MaxShaderPointLights, candidateLights.Count);
        ActivePointLightCount = count;

        for (int i = 0; i < count; i++)
        {
            var l = candidateLights[i].Light;
            float wave = 0.65f * MathF.Sin(elapsedSeconds * 5.2f + l.PhaseSeed)
                       + 0.35f * MathF.Sin(elapsedSeconds * 11.7f + l.PhaseSeed * 1.9f);
            float flicker = 1.0f + l.FlickerAmount * wave;
            float intensity = l.BaseIntensity * envScale * flicker;

            int o = i * 4;
            pointPosRadius[o + 0] = l.X;
            pointPosRadius[o + 1] = l.Y;
            pointPosRadius[o + 2] = l.Z;
            pointPosRadius[o + 3] = l.Radius;

            pointColorIntensity[o + 0] = l.R;
            pointColorIntensity[o + 1] = l.G;
            pointColorIntensity[o + 2] = l.B;
            pointColorIntensity[o + 3] = intensity;
        }
    }

    FrameLightingUniforms BuildFrameUniforms(Experimental3DState uniforms, Vector3 cameraWorldPos, float elapsedSeconds)
    {
        var env = uniforms.Environment;
        float block = Math.Max(0.25f, env.BlockSize);
        float wallH = Math.Max(0.25f, env.WallHeight);
        float lightScalar = uniforms.Light;

        Vector3 skyRgb = new(
            env.SkyHorizonColor.R / 255.0f,
            env.SkyHorizonColor.G / 255.0f,
            env.SkyHorizonColor.B / 255.0f);

        Vector3 sunDir;
        Vector3 sunColor;
        Vector3 skyAmbient;
        Vector3 groundAmbient;
        Vector4 playerPosRadius;
        Vector4 playerColorIntensity;
        Vector3 shadowTint;
        Vector3 highlightTint;
        float aoStrength;
        float splitStrength;
        float fogHeightFalloff;
        float emissiveFogResist;
        float fogExpScale;

        switch (env.Kind)
        {
            case Environment3DKind.OutdoorDay:
            {
                float dayAngle = Math.Clamp((env.TimeOfDayHours - 6.0f) / 12.0f, 0.0f, 1.0f) * MathF.PI;
                float sunX = -MathF.Cos(dayAngle) * 0.55f;
                float sunY = Math.Max(0.55f, MathF.Sin(dayAngle) * 0.85f);
                float sunZ = 0.42f;
                sunDir = Vector3.Normalize(new Vector3(sunX, sunY, sunZ));

                sunColor = new Vector3(0.24f, 0.22f, 0.19f);
                Vector3 skyTint = Vector3.Lerp(new Vector3(0.84f, 0.90f, 0.98f), new Vector3(0.82f, 0.92f, 1.04f) * Math.Max(0.6f, (skyRgb.X + skyRgb.Y + skyRgb.Z) * 0.45f + 0.55f), 0.35f);
                skyAmbient = new Vector3(0.84f, 0.88f, 0.94f) * 0.96f;
                skyAmbient = Vector3.Lerp(skyAmbient, skyTint * 0.88f, 0.25f);
                groundAmbient = new Vector3(0.80f, 0.78f, 0.73f);

                playerPosRadius = new Vector4(cameraWorldPos.X, cameraWorldPos.Y + 0.05f * wallH, cameraWorldPos.Z, 2.5f * block);
                playerColorIntensity = new Vector4(1.0f, 0.96f, 0.90f, 0.04f);

                shadowTint = new Vector3(0.95f, 0.97f, 1.04f);
                highlightTint = new Vector3(1.03f, 1.01f, 0.97f);
                aoStrength = 0.62f;
                splitStrength = 0.38f;
                fogHeightFalloff = 0.35f;
                emissiveFogResist = 0.15f;
                fogExpScale = 0.0f;
                break;
            }

            case Environment3DKind.OutdoorDawnDusk:
            {
                bool morning = env.TimeOfDayHours < 12.0f;
                sunDir = Vector3.Normalize(new Vector3(morning ? 0.72f : -0.72f, 0.42f, 0.45f));
                sunColor = Vector3.Lerp(new Vector3(0.20f, 0.22f, 0.34f), new Vector3(0.36f, 0.22f, 0.14f), 0.5f + 0.5f * env.DaylightFactor);
                skyAmbient = new Vector3(0.68f, 0.72f, 0.88f);
                groundAmbient = new Vector3(0.66f, 0.62f, 0.65f);

                float playerR = (2.4f + 1.4f * env.LightSpellLevel) * block;
                float playerI = (1.0f - env.DaylightFactor) * (0.34f + 0.08f * Math.Min(env.LightSpellLevel, 3u));
                playerPosRadius = new Vector4(cameraWorldPos.X, cameraWorldPos.Y + 0.05f * wallH, cameraWorldPos.Z, playerR);
                playerColorIntensity = new Vector4(1.12f, 0.86f, 0.56f, playerI);

                shadowTint = new Vector3(0.90f, 0.93f, 1.07f);
                highlightTint = new Vector3(1.07f, 0.98f, 0.90f);
                aoStrength = 0.65f;
                splitStrength = 0.52f;
                fogHeightFalloff = 0.45f;
                emissiveFogResist = 0.30f;
                fogExpScale = 0.0f;
                break;
            }

            case Environment3DKind.OutdoorNight:
            {
                // Night palette replacement already shifts textures deep nocturnal blue, so gently cool
                // distant surfaces to silver-indigo while letting the party lantern & torches carve warm pools.
                sunDir = Vector3.Normalize(new Vector3(-0.35f, 0.78f, 0.52f));
                sunColor = new Vector3(0.11f, 0.15f, 0.25f);
                skyAmbient = new Vector3(0.62f, 0.70f, 0.90f);
                groundAmbient = new Vector3(0.54f, 0.60f, 0.78f);

                float playerR = (2.4f + 1.35f * env.LightSpellLevel) * block;
                float playerI = 0.54f + 0.10f * Math.Min(env.LightSpellLevel, 3u);
                playerPosRadius = new Vector4(cameraWorldPos.X, cameraWorldPos.Y + 0.05f * wallH, cameraWorldPos.Z, playerR);
                playerColorIntensity = new Vector4(1.32f, 0.92f, 0.52f, playerI);

                shadowTint = new Vector3(0.88f, 0.93f, 1.09f);
                highlightTint = new Vector3(1.09f, 0.98f, 0.88f);
                aoStrength = 0.68f;
                splitStrength = 0.58f;
                fogHeightFalloff = 0.45f;
                emissiveFogResist = 0.42f;
                fogExpScale = 0.0f;
                break;
            }

            case Environment3DKind.IndoorLit:
            {
                sunDir = Vector3.Normalize(new Vector3(0.45f, 0.76f, 0.46f));
                sunColor = env.IsPalace ? new Vector3(0.24f, 0.21f, 0.16f) : new Vector3(0.20f, 0.19f, 0.20f);
                skyAmbient = env.IsPalace ? new Vector3(0.82f, 0.82f, 0.87f) : new Vector3(0.76f, 0.79f, 0.86f);
                groundAmbient = env.IsPalace ? new Vector3(0.74f, 0.71f, 0.69f) : new Vector3(0.70f, 0.70f, 0.74f);

                playerPosRadius = new Vector4(cameraWorldPos.X, cameraWorldPos.Y + 0.04f * wallH, cameraWorldPos.Z, 4.0f * block);
                playerColorIntensity = new Vector4(1.06f, 0.96f, 0.86f, env.IsPalace ? 0.18f : 0.24f);

                shadowTint = new Vector3(0.92f, 0.95f, 1.05f);
                highlightTint = env.IsPalace ? new Vector3(1.06f, 1.02f, 0.94f) : new Vector3(1.04f, 1.00f, 0.96f);
                aoStrength = 0.78f;
                splitStrength = 0.52f;
                fogHeightFalloff = 0.30f;
                emissiveFogResist = 0.35f;
                fogExpScale = 0.0f;
                break;
            }

            case Environment3DKind.Dungeon:
            default:
            {
                float lumScale = Math.Clamp(lightScalar, 0.0f, 1.0f);
                sunDir = Vector3.Normalize(new Vector3(0.32f, 0.82f, 0.47f));
                sunColor = new Vector3(0.10f, 0.11f, 0.16f) * lumScale;
                skyAmbient = new Vector3(0.38f, 0.44f, 0.58f) * (0.30f + 0.70f * lumScale);
                groundAmbient = new Vector3(0.30f, 0.34f, 0.46f) * (0.30f + 0.70f * lumScale);

                float breath = 1.0f + 0.02f * MathF.Sin(elapsedSeconds * 3.4f);
                float lightBoost = Math.Max(1.0f, (float)env.LightSpellLevel);
                float playerR = (2.35f + 1.55f * env.LightSpellLevel) * block;
                float playerI = (0.92f + 0.12f * Math.Min(lightBoost, 3.0f)) * lumScale * breath;
                playerPosRadius = new Vector4(cameraWorldPos.X, cameraWorldPos.Y + 0.05f * wallH, cameraWorldPos.Z, playerR);
                playerColorIntensity = new Vector4(1.26f, 0.96f, 0.62f, playerI);

                shadowTint = new Vector3(0.85f, 0.91f, 1.11f);
                highlightTint = new Vector3(1.11f, 1.00f, 0.87f);
                aoStrength = 0.84f;
                splitStrength = 0.68f;
                fogHeightFalloff = 0.25f;
                emissiveFogResist = 0.45f;
                fogExpScale = 1.0f;
                break;
            }
        }

        return new FrameLightingUniforms
        {
            SunDir = sunDir,
            SunColor = sunColor,
            SkyAmbient = skyAmbient,
            GroundAmbient = groundAmbient,
            PlayerLightPosRadius = playerPosRadius,
            PlayerLightColorIntensity = playerColorIntensity,
            ShadowTint = shadowTint,
            HighlightTint = highlightTint,
            EnvParams = new Vector4(aoStrength, splitStrength, fogHeightFalloff, emissiveFogResist),
            FogExpDensityScale = fogExpScale,
            ActivePointLightCount = ActivePointLightCount,
            PointLightPosRadius = pointPosRadius,
            PointLightColorIntensity = pointColorIntensity,
        };
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (aoShadowTex != 0)
        {
            state.Gl.DeleteTexture(aoShadowTex);
            aoShadowTex = 0;
        }
    }
}
#endif

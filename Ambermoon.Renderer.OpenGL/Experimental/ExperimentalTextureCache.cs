#if !GLES
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ambermoon.Renderer.OpenGL.Experimental;

using Data;
using Render;
using Texture = Ambermoon.Renderer.OpenGL.Texture;

internal readonly record struct TextureBucketKey(int LayerWidth, int LayerHeight, bool WrapRepeat);

internal readonly record struct TextureLayerKey(
    uint AtlasGlId,
    int SrcX,
    int SrcY,
    int SrcW,
    int SrcH,
    byte PaletteIndex,
    byte AlphaMode,
    byte SkyKeyMode,
    byte SkyColorIndex,
    uint SkyReplaceRgba,
    ulong PalReplaceHash,
    bool WrapRepeat,
    byte UpscaleFactor);

internal readonly record struct TextureEmissiveInfo(
    int WarmFireCount,
    int WarmHotCount,
    int CoolBrightCount,
    int OpaqueCount,
    float WarmR,
    float WarmG,
    float WarmB,
    float WarmCentroidX,
    float WarmCentroidY,
    float CoolR,
    float CoolG,
    float CoolB,
    float CoolCentroidX,
    float CoolCentroidY);

internal readonly record struct ResolvedTextureLayer(TextureArrayBucket Bucket, int LayerIndex, TextureEmissiveInfo Emissive);

/// <summary>
/// One <c>GL_TEXTURE_2D_ARRAY</c> pool sharing uniform mip0 dimensions and wrap mode,
/// configured with full trilinear mipmaps and up to 16x hardware anisotropic filtering.
/// </summary>
internal sealed class TextureArrayBucket : IDisposable
{
    readonly State state;
    readonly int mipCount;
    readonly int maxCapacity;
    readonly float anisotropy;
    readonly List<TextureLayerKey> slotKeys = new();
    readonly List<ulong> slotLastUsedFrame = new();
    readonly Stack<int> freeSlots = new();
    TextureMagFilter currentMagFilter = TextureMagFilter.Linear;
    bool disposed;

    public TextureBucketKey Key { get; }
    public uint TextureId { get; private set; }
    public int Width => Key.LayerWidth;
    public int Height => Key.LayerHeight;
    public int Capacity { get; private set; }
    public int UsedCount => slotKeys.Count - freeSlots.Count;

    public TextureArrayBucket(State state, TextureBucketKey key, float anisotropy)
    {
        this.state = state;
        Key = key;
        this.anisotropy = anisotropy;
        mipCount = EdgeDirectedUpscaler.CalculateMipCount(key.LayerWidth, key.LayerHeight);

        int initialCapacity = key.WrapRepeat ? 8 : (key.LayerWidth * key.LayerHeight >= 128 * 80 ? 32 : 16);
        maxCapacity = key.LayerWidth * key.LayerHeight >= 128 * 1024 ? 128 : 256;
        Capacity = initialCapacity;

        TextureId = AllocateArrayTexture(Capacity);
    }

    uint AllocateArrayTexture(int layers)
    {
        var gl = state.Gl;
        uint id = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, id);

        int w = Key.LayerWidth;
        int h = Key.LayerHeight;
        unsafe
        {
            for (int level = 0; level < mipCount; level++)
            {
                gl.TexImage3D(TextureTarget.Texture2DArray, level, InternalFormat.Rgba8,
                    (uint)w, (uint)h, (uint)layers, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)0);
                w = Math.Max(1, w >> 1);
                h = Math.Max(1, h >> 1);
            }
        }

        int wrap = Key.WrapRepeat ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge;
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, wrap);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, wrap);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)currentMagFilter);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBaseLevel, 0);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMaxLevel, mipCount - 1);
        if (anisotropy > 1.0f)
            gl.TexParameter(TextureTarget.Texture2DArray, (TextureParameterName)0x84FE, anisotropy);

        gl.BindTexture(TextureTarget.Texture2DArray, 0);
        return id;
    }

    public int AllocateLayer(
        TextureLayerKey key,
        ulong frameIndex,
        uint copyReadFbo,
        out bool evicted,
        out TextureLayerKey evictedKey)
    {
        evicted = false;
        evictedKey = default;

        if (freeSlots.Count > 0)
        {
            int slot = freeSlots.Pop();
            slotKeys[slot] = key;
            slotLastUsedFrame[slot] = frameIndex;
            return slot;
        }

        if (slotKeys.Count < Capacity)
        {
            int slot = slotKeys.Count;
            slotKeys.Add(key);
            slotLastUsedFrame.Add(frameIndex);
            return slot;
        }

        if (Capacity < maxCapacity)
        {
            Grow(Math.Min(maxCapacity, Capacity * 2), copyReadFbo);
            int slot = slotKeys.Count;
            slotKeys.Add(key);
            slotLastUsedFrame.Add(frameIndex);
            return slot;
        }

        // Evict the least-recently-used layer not active in the current frame.
        int bestSlot = -1;
        ulong oldestFrame = ulong.MaxValue;
        for (int i = 0; i < slotLastUsedFrame.Count; i++)
        {
            ulong used = slotLastUsedFrame[i];
            if (used < frameIndex && used < oldestFrame)
            {
                oldestFrame = used;
                bestSlot = i;
            }
        }

        if (bestSlot < 0)
        {
            // Every slot is active on this single frame; grow even past soft cap so nothing breaks.
            Grow(Capacity + 32, copyReadFbo);
            int slot = slotKeys.Count;
            slotKeys.Add(key);
            slotLastUsedFrame.Add(frameIndex);
            return slot;
        }

        evicted = true;
        evictedKey = slotKeys[bestSlot];
        slotKeys[bestSlot] = key;
        slotLastUsedFrame[bestSlot] = frameIndex;
        return bestSlot;
    }

    void Grow(int newCapacity, uint copyReadFbo)
    {
        var gl = state.Gl;
        uint oldTex = TextureId;
        int oldUsed = slotKeys.Count;
        uint newTex = AllocateArrayTexture(newCapacity);

        gl.GetInteger(GLEnum.ReadFramebufferBinding, out int prevReadFbo);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, copyReadFbo);
        gl.BindTexture(TextureTarget.Texture2DArray, newTex);

        int w = Key.LayerWidth;
        int h = Key.LayerHeight;
        for (int level = 0; level < mipCount; level++)
        {
            for (int layer = 0; layer < oldUsed; layer++)
            {
                gl.FramebufferTextureLayer(FramebufferTarget.ReadFramebuffer,
                    FramebufferAttachment.ColorAttachment0, oldTex, level, layer);
                gl.CopyTexSubImage3D(TextureTarget.Texture2DArray, level,
                    0, 0, layer, 0, 0, (uint)w, (uint)h);
            }
            w = Math.Max(1, w >> 1);
            h = Math.Max(1, h >> 1);
        }

        gl.FramebufferTextureLayer(FramebufferTarget.ReadFramebuffer,
            FramebufferAttachment.ColorAttachment0, 0, 0, 0);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)prevReadFbo);
        gl.BindTexture(TextureTarget.Texture2DArray, 0);
        gl.DeleteTexture(oldTex);

        TextureId = newTex;
        Capacity = newCapacity;
    }

    public void UploadMipChain(int layerIndex, List<(int Width, int Height, byte[] Rgba)> mipChain)
    {
        var gl = state.Gl;
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.BindTexture(TextureTarget.Texture2DArray, TextureId);
        unsafe
        {
            for (int level = 0; level < mipChain.Count; level++)
            {
                var (w, h, rgba) = mipChain[level];
                fixed (byte* ptr = rgba)
                {
                    gl.TexSubImage3D(TextureTarget.Texture2DArray, level,
                        0, 0, layerIndex, (uint)w, (uint)h, 1,
                        PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
                }
            }
        }
        gl.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    public void Touch(int layerIndex, ulong frameIndex)
    {
        if ((uint)layerIndex < (uint)slotLastUsedFrame.Count)
            slotLastUsedFrame[layerIndex] = frameIndex;
    }

    public void Bind(Texture3DFilterMode filterMode)
    {
        var gl = state.Gl;
        gl.ActiveTexture(GLEnum.Texture0);
        gl.BindTexture(TextureTarget.Texture2DArray, TextureId);

        // Keep GL_LINEAR magnification active in all modes so hardware 16x anisotropic
        // filtering never disengages when only one axis is magnified (e.g. receding floors).
        // SharpBilinear and Crisp compute exact per-axis magnification in the fragment shader.
        if (currentMagFilter != TextureMagFilter.Linear)
        {
            currentMagFilter = TextureMagFilter.Linear;
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (TextureId != 0)
        {
            state.Gl.DeleteTexture(TextureId);
            TextureId = 0;
        }
        slotKeys.Clear();
        slotLastUsedFrame.Clear();
        freeSlots.Clear();
    }
}

/// <summary>
/// Lazy RGBA texture-array cache for the Experimental 3D renderer.
/// Converts indexed 3D atlases + palettes (including <see cref="PaletteReplacement"/> and
/// sky-color keying/replacement) into premultiplied RGBA8 <c>GL_TEXTURE_2D_ARRAY</c> buckets
/// with coverage-preserving NPOT mipmaps, up to 16x anisotropy, and optional 4x edge-directed
/// upscaling.
/// </summary>
internal sealed class ExperimentalTextureCache : IDisposable
{
    sealed class AtlasSnapshot
    {
        public Texture TextureRef;
        public uint GlId;
        public int Width;
        public int Height;
        public byte[] Indices;
    }

    sealed class PaletteSnapshot
    {
        public Texture TextureRef;
        public uint GlId;
        public int Width;
        public int Height;
        public byte[] Rgba;
    }

    readonly State state;
    readonly Dictionary<uint, AtlasSnapshot> atlasSnapshots = new();
    PaletteSnapshot paletteSnapshot;
    readonly Dictionary<TextureBucketKey, TextureArrayBucket> buckets = new();
    readonly Dictionary<TextureLayerKey, ResolvedTextureLayer> cache = new();
    readonly List<TextureLayerKey> pendingKeys = new(64);
    readonly HashSet<TextureLayerKey> pendingSet = new();
    uint copyReadFbo;
    ulong frameCounter;
    bool disposed;

    public float AppliedAnisotropy { get; }
    public int CachedLayerCount => cache.Count;
    public int BucketCount => buckets.Count;
    public int TotalTexturesBaked { get; private set; }
    public ulong CacheEpoch { get; private set; } = 1;

    public ExperimentalTextureCache(State state)
    {
        this.state = state;
        copyReadFbo = state.Gl.GenFramebuffer();
        AppliedAnisotropy = QueryMaxAnisotropy(state.Gl);
    }

    static float QueryMaxAnisotropy(GL gl)
    {
        bool supported = false;
        gl.GetInteger(GetPName.MajorVersion, out int major);
        gl.GetInteger(GetPName.MinorVersion, out int minor);
        if (major > 4 || (major == 4 && minor >= 6))
        {
            supported = true;
        }
        else
        {
            gl.GetInteger(GetPName.NumExtensions, out int extCount);
            for (int i = 0; i < extCount; i++)
            {
                string ext = gl.GetStringS(StringName.Extensions, (uint)i);
                if (ext == "GL_EXT_texture_filter_anisotropic" || ext == "GL_ARB_texture_filter_anisotropic")
                {
                    supported = true;
                    break;
                }
            }
        }

        if (!supported)
            return 1.0f;

        gl.GetFloat((GLEnum)0x84FF, out float maxAniso);
        return Math.Clamp(maxAniso, 1.0f, 16.0f);
    }

    public void BeginFrame() => frameCounter++;

    public static ulong ComputePaletteReplacementHash(PaletteReplacement replacement)
    {
        if (replacement?.ColorData == null)
            return 0ul;

        byte[] data = replacement.ColorData;
        ulong hash = 14695981039346656037ul;
        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= 1099511628211ul;
        }
        return hash == 0ul ? 1ul : hash;
    }

    public static uint PackRgba(Render.Color color)
    {
        if (color == null)
            return 0u;
        return (uint)color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);
    }

    public static byte GetUpscaleFactor(Texture3DFilterMode filterMode, int srcW, int srcH)
    {
        if (filterMode != Texture3DFilterMode.EdgeDirected)
            return 1;
        return srcW <= 256 && srcH <= 256 ? (byte)4 : (byte)1;
    }

    public TextureLayerKey BuildKey(
        Texture atlas,
        int srcX, int srcY, int srcW, int srcH,
        byte paletteIndex,
        byte alphaMode,
        bool wrapRepeat,
        Experimental3DState uniforms,
        ulong palReplaceHash,
        Texture3DFilterMode filterMode)
    {
        byte skyKeyMode = 0;
        byte skyColorIndex = 0;
        uint skyReplaceRgba = 0;

        if (uniforms.SkyColorIndex.HasValue && uniforms.SkyColorIndex.Value <= 31u)
        {
            skyColorIndex = (byte)uniforms.SkyColorIndex.Value;
            if (alphaMode == 2)
            {
                // Opaque walls discard skyColorIndex regardless of skyColorReplace.
                skyKeyMode = 1;
            }
            else
            {
                skyKeyMode = 2;
                skyReplaceRgba = PackRgba(uniforms.SkyReplaceColor);
            }
        }

        byte upscale = GetUpscaleFactor(filterMode, srcW, srcH);

        return new TextureLayerKey(
            atlas.Index,
            srcX, srcY, srcW, srcH,
            paletteIndex,
            alphaMode,
            skyKeyMode,
            skyColorIndex,
            skyReplaceRgba,
            palReplaceHash,
            wrapRepeat,
            upscale);
    }

    public void EnsureLayers(
        List<TextureLayerKey> keys,
        Texture atlas,
        Texture palette,
        PaletteReplacement paletteReplacement)
    {
        if (disposed || keys.Count == 0)
            return;

        pendingKeys.Clear();
        pendingSet.Clear();

        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (cache.TryGetValue(key, out var existing))
            {
                existing.Bucket.Touch(existing.LayerIndex, frameCounter);
            }
            else if (pendingSet.Add(key))
            {
                pendingKeys.Add(key);
            }
        }

        if (pendingKeys.Count == 0)
            return;

        var atlasSnap = GetOrReadAtlas(atlas);
        var palSnap = GetOrReadPalette(palette);
        byte[] palReplaceBytes = paletteReplacement?.ColorData;

        int bakeCount = pendingKeys.Count;
        var bakedResults = new (List<(int Width, int Height, byte[] Rgba)> Mips, TextureEmissiveInfo Emissive)[bakeCount];

        if (bakeCount >= 4)
        {
            Parallel.For(0, bakeCount, i =>
            {
                bakedResults[i] = BakeLayer(pendingKeys[i], atlasSnap, palSnap, palReplaceBytes);
            });
        }
        else
        {
            for (int i = 0; i < bakeCount; i++)
                bakedResults[i] = BakeLayer(pendingKeys[i], atlasSnap, palSnap, palReplaceBytes);
        }

        for (int i = 0; i < bakeCount; i++)
        {
            var key = pendingKeys[i];
            var (mipChain, emissive) = bakedResults[i];
            var (outW, outH, _) = mipChain[0];
            var bucketKey = new TextureBucketKey(outW, outH, key.WrapRepeat);
            if (!buckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new TextureArrayBucket(state, bucketKey, AppliedAnisotropy);
                buckets.Add(bucketKey, bucket);
            }

            int layerIndex = bucket.AllocateLayer(key, frameCounter, copyReadFbo, out bool evicted, out var evictedKey);
            if (evicted)
            {
                cache.Remove(evictedKey);
                CacheEpoch++;
            }

            bucket.UploadMipChain(layerIndex, mipChain);
            cache[key] = new ResolvedTextureLayer(bucket, layerIndex, emissive);
            TotalTexturesBaked++;
        }
    }

    public bool TryGetResolved(in TextureLayerKey key, out ResolvedTextureLayer resolved)
    {
        if (cache.TryGetValue(key, out resolved))
        {
            resolved.Bucket.Touch(resolved.LayerIndex, frameCounter);
            return true;
        }
        return false;
    }

    public bool TryGetEmissive(in TextureLayerKey key, out TextureEmissiveInfo emissive)
    {
        if (cache.TryGetValue(key, out var resolved))
        {
            emissive = resolved.Emissive;
            return true;
        }
        emissive = default;
        return false;
    }

    static (List<(int Width, int Height, byte[] Rgba)> Mips, TextureEmissiveInfo Emissive) BakeLayer(
        TextureLayerKey key,
        AtlasSnapshot atlas,
        PaletteSnapshot palette,
        byte[] palReplaceBytes)
    {
        int srcW = Math.Max(1, key.SrcW);
        int srcH = Math.Max(1, key.SrcH);
        var srcPixels = new uint[srcW * srcH];
        bool hasTransparency = false;

        int palRow = Math.Clamp((int)key.PaletteIndex, 0, Math.Max(0, palette.Height - 1));
        int palRowOffset = palRow * palette.Width * 4;
        bool useReplace = key.PalReplaceHash != 0ul && palReplaceBytes != null && palReplaceBytes.Length >= 64;
        byte alphaMode = key.AlphaMode;
        byte skyMode = key.SkyKeyMode;
        byte skyIdx = key.SkyColorIndex;
        uint skyReplace = key.SkyReplaceRgba;

        for (int y = 0; y < srcH; y++)
        {
            int ay = Math.Clamp(key.SrcY + y, 0, atlas.Height - 1);
            int atlasRow = ay * atlas.Width;
            int dstRow = y * srcW;

            for (int x = 0; x < srcW; x++)
            {
                int ax = Math.Clamp(key.SrcX + x, 0, atlas.Width - 1);
                byte colorIndex = atlas.Indices[atlasRow + ax];

                byte r, g, b, a;
                if (useReplace && colorIndex < 16)
                {
                    int off = colorIndex * 4;
                    r = palReplaceBytes[off + 0];
                    g = palReplaceBytes[off + 1];
                    b = palReplaceBytes[off + 2];
                    a = palReplaceBytes[off + 3];
                }
                else
                {
                    int col = Math.Clamp((int)colorIndex, 0, palette.Width - 1);
                    int off = palRowOffset + col * 4;
                    r = palette.Rgba[off + 0];
                    g = palette.Rgba[off + 1];
                    b = palette.Rgba[off + 2];
                    a = palette.Rgba[off + 3];
                }

                // Exact parity with Classic alpha & sky-key semantics:
                // alphaMode 0: floor/ceiling (opaque; skyMode 2 replaces skyColorIndex)
                // alphaMode 1: transparent wall (index 0 or a<128 discarded; skyMode 2 replaces)
                // alphaMode 2: opaque wall (skyMode 1 discards skyColorIndex)
                // alphaMode 3: billboard (index 0 or a<128 discarded; skyMode 2 replaces)
                if ((alphaMode == 1 || alphaMode == 3) && (colorIndex == 0 || a < 128))
                {
                    srcPixels[dstRow + x] = 0u;
                    hasTransparency = true;
                }
                else if (skyMode != 0 && colorIndex == skyIdx)
                {
                    if (skyMode == 1)
                    {
                        srcPixels[dstRow + x] = 0u;
                        hasTransparency = true;
                    }
                    else
                    {
                        uint sr = skyReplace & 0xFF;
                        uint sg = (skyReplace >> 8) & 0xFF;
                        uint sb = (skyReplace >> 16) & 0xFF;
                        uint sa = (skyReplace >> 24) & 0xFF;
                        if (sa < 128)
                        {
                            srcPixels[dstRow + x] = 0u;
                            hasTransparency = true;
                        }
                        else
                        {
                            srcPixels[dstRow + x] = sr | (sg << 8) | (sb << 16) | 0xFF000000u;
                        }
                    }
                }
                else if (a < 128)
                {
                    srcPixels[dstRow + x] = 0u;
                    hasTransparency = true;
                }
                else
                {
                    srcPixels[dstRow + x] = (uint)r | ((uint)g << 8) | ((uint)b << 16) | 0xFF000000u;
                }
            }
        }

        var emissive = AnalyzeEmissive(srcPixels, srcW, srcH);

        uint[] mip0;
        int outW, outH;
        if (key.UpscaleFactor == 4)
        {
            mip0 = EdgeDirectedUpscaler.Upscale4x(srcPixels, srcW, srcH, key.WrapRepeat);
            outW = srcW * 4;
            outH = srcH * 4;
        }
        else
        {
            mip0 = srcPixels;
            outW = srcW;
            outH = srcH;
        }

        return (EdgeDirectedUpscaler.GeneratePremultipliedMipChain(mip0, outW, outH, hasTransparency), emissive);
    }

    static TextureEmissiveInfo AnalyzeEmissive(uint[] srcPixels, int w, int h)
    {
        int opaqueCount = 0;
        int warmFireCount = 0;
        int warmHotCount = 0;
        int warmWeighted = 0;
        int coolCount = 0;
        float warmR = 0, warmG = 0, warmB = 0, warmCx = 0, warmCy = 0;
        float coolR = 0, coolG = 0, coolB = 0, coolCx = 0, coolCy = 0;

        float invW = 1.0f / Math.Max(1, w);
        float invH = 1.0f / Math.Max(1, h);

        for (int y = 0; y < h; y++)
        {
            float ny = (y + 0.5f) * invH;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                uint p = srcPixels[row + x];
                if ((p >> 24) < 128u)
                    continue;

                opaqueCount++;
                int r = (int)(p & 0xFFu);
                int g = (int)((p >> 8) & 0xFFu);
                int b = (int)((p >> 16) & 0xFFu);
                float luma = 0.299f * r + 0.587f * g + 0.114f * b;

                // Separate genuine orange fire body (g>=85) and Amiga golden-orange flame tips (0xEB4/0xEC5 etc.)
                // from pure crimson tunics (low g) or earthy cave tan/wood (high b / low R-B).
                bool isFireRedOrange = r >= 225 && g >= 85 && b <= 65 && (r - b) >= 165;
                bool isFireHotCore = r >= 225 && g >= 145 && b <= 115 && (r - b) >= 120 && luma >= 168.0f;
                if (isFireRedOrange || isFireHotCore)
                {
                    if (isFireRedOrange)
                        warmFireCount++;
                    if (isFireHotCore)
                        warmHotCount++;
                    warmWeighted++;
                    warmR += r;
                    warmG += g;
                    warmB += b;
                    warmCx += (x + 0.5f) * invW;
                    warmCy += ny;
                }

                bool isMagicCyanBlue = b >= 228 && (b - r) >= 95 && g >= 130;
                bool isMagicViolet = r >= 220 && b >= 220 && (r - g) >= 85 && (b - g) >= 85;
                if ((isMagicCyanBlue || isMagicViolet) && luma >= 115.0f)
                {
                    coolCount++;
                    coolR += r;
                    coolG += g;
                    coolB += b;
                    coolCx += (x + 0.5f) * invW;
                    coolCy += ny;
                }
            }
        }

        float wAvgR = 0, wAvgG = 0, wAvgB = 0, wAvgCx = 0.5f, wAvgCy = 0.5f;
        if (warmWeighted > 0)
        {
            float inv = 1.0f / (warmWeighted * 255.0f);
            wAvgR = warmR * inv;
            wAvgG = warmG * inv;
            wAvgB = warmB * inv;
            wAvgCx = warmCx / warmWeighted;
            wAvgCy = warmCy / warmWeighted;
        }

        float cAvgR = 0, cAvgG = 0, cAvgB = 0, cAvgCx = 0.5f, cAvgCy = 0.5f;
        if (coolCount > 0)
        {
            float inv = 1.0f / (coolCount * 255.0f);
            cAvgR = coolR * inv;
            cAvgG = coolG * inv;
            cAvgB = coolB * inv;
            cAvgCx = coolCx / coolCount;
            cAvgCy = coolCy / coolCount;
        }

        return new TextureEmissiveInfo(
            warmFireCount, warmHotCount, coolCount, opaqueCount,
            wAvgR, wAvgG, wAvgB, wAvgCx, wAvgCy,
            cAvgR, cAvgG, cAvgB, cAvgCx, cAvgCy);
    }

    AtlasSnapshot GetOrReadAtlas(Texture atlas)
    {
        if (atlasSnapshots.TryGetValue(atlas.Index, out var snap)
            && ReferenceEquals(snap.TextureRef, atlas)
            && snap.Width == atlas.Width
            && snap.Height == atlas.Height)
        {
            return snap;
        }

        var gl = state.Gl;
        var indices = new byte[atlas.Width * atlas.Height];
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        gl.ActiveTexture(GLEnum.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, atlas.Index);
        unsafe
        {
            fixed (byte* ptr = indices)
                gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Red, PixelType.UnsignedByte, ptr);
        }
        gl.BindTexture(TextureTarget.Texture2D, 0);
        Texture.InvalidateActiveTexture();

        snap = new AtlasSnapshot
        {
            TextureRef = atlas,
            GlId = atlas.Index,
            Width = atlas.Width,
            Height = atlas.Height,
            Indices = indices,
        };
        atlasSnapshots[atlas.Index] = snap;
        return snap;
    }

    PaletteSnapshot GetOrReadPalette(Texture palette)
    {
        if (paletteSnapshot != null
            && ReferenceEquals(paletteSnapshot.TextureRef, palette)
            && paletteSnapshot.GlId == palette.Index
            && paletteSnapshot.Width == palette.Width
            && paletteSnapshot.Height == palette.Height)
        {
            return paletteSnapshot;
        }

        var gl = state.Gl;
        var rgba = new byte[palette.Width * palette.Height * 4];
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        gl.ActiveTexture(GLEnum.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, palette.Index);
        unsafe
        {
            fixed (byte* ptr = rgba)
                gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
        gl.BindTexture(TextureTarget.Texture2D, 0);
        Texture.InvalidateActiveTexture();

        paletteSnapshot = new PaletteSnapshot
        {
            TextureRef = palette,
            GlId = palette.Index,
            Width = palette.Width,
            Height = palette.Height,
            Rgba = rgba,
        };
        return paletteSnapshot;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (var bucket in buckets.Values)
            bucket.Dispose();
        buckets.Clear();
        cache.Clear();
        atlasSnapshots.Clear();
        paletteSnapshot = null;
        if (copyReadFbo != 0)
        {
            state.Gl.DeleteFramebuffer(copyReadFbo);
            copyReadFbo = 0;
        }
    }
}
#endif

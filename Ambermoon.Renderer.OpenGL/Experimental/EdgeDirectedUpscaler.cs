#if !GLES
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ambermoon.Renderer.OpenGL.Experimental;

/// <summary>
/// CPU pixel-art texture processor for the Experimental 3D renderer:
/// - Premultiplied-alpha-aware 4x edge-directed upscaling (xBRZ-style YCbCr perceptual metric)
///   with seamless wrapping for repeating floor/ceiling tiles and edge clamping for walls/billboards.
/// - Premultiplied NPOT mipmap chain generation with alpha-cutout coverage preservation so keyed
///   hedges, arches, grates, and billboards never bleed sky/black halos or vanish at a distance.
/// </summary>
internal static class EdgeDirectedUpscaler
{
    const float EqualColorTolerance = 30.0f;
    const float DominantDirectionThreshold = 3.6f;
    const float SteepDirectionThreshold = 2.2f;
    const float TransparentSeparationDistance = 1000.0f;

    public static int CalculateMipCount(int width, int height)
    {
        int levels = 1;
        int dim = Math.Max(width, height);
        while (dim > 1)
        {
            dim >>= 1;
            levels++;
        }
        return levels;
    }

    /// <summary>
    /// Upscales a premultiplied RGBA8 image (<c>R | G&lt;&lt;8 | B&lt;&lt;16 | A&lt;&lt;24</c>) by 4x using
    /// perceptual edge-directed corner and 2:1 slope reconstruction.
    /// </summary>
    public static uint[] Upscale4x(uint[] src, int srcW, int srcH, bool wrapRepeat)
    {
        int dstW = srcW * 4;
        int dstH = srcH * 4;
        var dst = new uint[dstW * dstH];

        for (int y = 0; y < srcH; y++)
        {
            int yM2 = wrapRepeat ? Wrap(y - 2, srcH) : Clamp(y - 2, srcH);
            int yM1 = wrapRepeat ? Wrap(y - 1, srcH) : Clamp(y - 1, srcH);
            int yP1 = wrapRepeat ? Wrap(y + 1, srcH) : Clamp(y + 1, srcH);
            int yP2 = wrapRepeat ? Wrap(y + 2, srcH) : Clamp(y + 2, srcH);

            int rowM2 = yM2 * srcW;
            int rowM1 = yM1 * srcW;
            int row0 = y * srcW;
            int rowP1 = yP1 * srcW;
            int rowP2 = yP2 * srcW;

            int outY = y * 4;

            for (int x = 0; x < srcW; x++)
            {
                uint e = src[row0 + x];
                int outX = x * 4;

                // Seed the 4x4 block with E.
                int baseIdx = outY * dstW + outX;
                dst[baseIdx + 0] = e; dst[baseIdx + 1] = e; dst[baseIdx + 2] = e; dst[baseIdx + 3] = e;
                int r1 = baseIdx + dstW;
                dst[r1 + 0] = e; dst[r1 + 1] = e; dst[r1 + 2] = e; dst[r1 + 3] = e;
                int r2 = r1 + dstW;
                dst[r2 + 0] = e; dst[r2 + 1] = e; dst[r2 + 2] = e; dst[r2 + 3] = e;
                int r3 = r2 + dstW;
                dst[r3 + 0] = e; dst[r3 + 1] = e; dst[r3 + 2] = e; dst[r3 + 3] = e;

                int xM1 = wrapRepeat ? Wrap(x - 1, srcW) : Clamp(x - 1, srcW);
                int xP1 = wrapRepeat ? Wrap(x + 1, srcW) : Clamp(x + 1, srcW);

                uint b = src[rowM1 + x];
                uint d = src[row0 + xM1];
                uint f = src[row0 + xP1];
                uint h = src[rowP1 + x];

                // Fast skip for uniform flat interior pixels.
                if (e == b && e == d && e == f && e == h)
                    continue;

                int xM2 = wrapRepeat ? Wrap(x - 2, srcW) : Clamp(x - 2, srcW);
                int xP2 = wrapRepeat ? Wrap(x + 2, srcW) : Clamp(x + 2, srcW);

                uint a1 = src[rowM2 + xM1], b1 = src[rowM2 + x], c1 = src[rowM2 + xP1];
                uint a0 = src[rowM1 + xM2], a = src[rowM1 + xM1], c = src[rowM1 + xP1], c4 = src[rowM1 + xP2];
                uint d0 = src[row0 + xM2], f4 = src[row0 + xP2];
                uint g0 = src[rowP1 + xM2], g = src[rowP1 + xM1], i = src[rowP1 + xP1], i4 = src[rowP1 + xP2];
                uint g5 = src[rowP2 + xM1], h5 = src[rowP2 + x], i5 = src[rowP2 + xP1];

                // Bottom-Right (0 deg)
                if (e != f && e != h)
                    BlendCorner(dst, dstW, outX, outY, 0, b, c, d, e, f, g, h, i, f4, i4, h5, i5);

                // Bottom-Left (90 deg CW)
                if (e != h && e != d)
                    BlendCorner(dst, dstW, outX, outY, 1, f, i, b, e, h, a, d, g, h5, g5, d0, g0);

                // Top-Left (180 deg)
                if (e != d && e != b)
                    BlendCorner(dst, dstW, outX, outY, 2, h, g, f, e, d, c, b, a, d0, a0, b1, a1);

                // Top-Right (270 deg CW)
                if (e != b && e != f)
                    BlendCorner(dst, dstW, outX, outY, 3, d, a, h, e, b, i, f, c, b1, c1, f4, c4);
            }
        }

        return dst;
    }

    /// <summary>
    /// Generates all mipmap levels (0 .. mipCount-1) as tightly packed RGBA8 byte buffers
    /// suitable for <c>glTexSubImage3D</c>. Preserves premultiplied alpha so transparent texels
    /// contribute zero RGB tint, and preserves alpha-cutout coverage on transparent textures.
    /// </summary>
    public static List<(int Width, int Height, byte[] Rgba)> GeneratePremultipliedMipChain(
        uint[] mip0, int width, int height, bool hasTransparency)
    {
        int mipCount = CalculateMipCount(width, height);
        var chain = new List<(int Width, int Height, byte[] Rgba)>(mipCount);

        chain.Add((width, height, PackToBytes(mip0)));

        if (mipCount <= 1)
            return chain;

        float refCoverage = 1.0f;
        if (hasTransparency)
        {
            int opaque = 0;
            for (int i = 0; i < mip0.Length; i++)
            {
                if ((mip0[i] >> 24) >= 128u)
                    opaque++;
            }
            refCoverage = (float)opaque / mip0.Length;
        }

        uint[] prev = mip0;
        int prevW = width;
        int prevH = height;

        for (int level = 1; level < mipCount; level++)
        {
            int dstW = Math.Max(1, prevW >> 1);
            int dstH = Math.Max(1, prevH >> 1);
            uint[] next = DownsamplePremultiplied(prev, prevW, prevH, dstW, dstH, hasTransparency, refCoverage);
            chain.Add((dstW, dstH, PackToBytes(next)));
            prev = next;
            prevW = dstW;
            prevH = dstH;
        }

        return chain;
    }

    static uint[] DownsamplePremultiplied(
        uint[] src, int srcW, int srcH, int dstW, int dstH, bool hasTransparency, float refCoverage)
    {
        bool oddW = (srcW > 1) && ((srcW & 1) != 0);
        bool oddH = (srcH > 1) && ((srcH & 1) != 0);

        if (!hasTransparency)
        {
            var dst = new uint[dstW * dstH];
            for (int dy = 0; dy < dstH; dy++)
            {
                int sy = dy * 2;
                float wy0, wy1, wy2;
                int yCount;
                if (srcH == 1)
                {
                    wy0 = 1.0f; wy1 = 0.0f; wy2 = 0.0f; yCount = 1;
                }
                else if (oddH)
                {
                    float inv = 1.0f / srcH;
                    wy0 = (dstH - dy) * inv;
                    wy1 = dstH * inv;
                    wy2 = (dy + 1) * inv;
                    yCount = 3;
                }
                else
                {
                    wy0 = 0.5f; wy1 = 0.5f; wy2 = 0.0f; yCount = 2;
                }

                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx = dx * 2;
                    float wx0, wx1, wx2;
                    int xCount;
                    if (srcW == 1)
                    {
                        wx0 = 1.0f; wx1 = 0.0f; wx2 = 0.0f; xCount = 1;
                    }
                    else if (oddW)
                    {
                        float inv = 1.0f / srcW;
                        wx0 = (dstW - dx) * inv;
                        wx1 = dstW * inv;
                        wx2 = (dx + 1) * inv;
                        xCount = 3;
                    }
                    else
                    {
                        wx0 = 0.5f; wx1 = 0.5f; wx2 = 0.0f; xCount = 2;
                    }

                    float sr = 0, sg = 0, sb = 0;
                    for (int oy = 0; oy < yCount; oy++)
                    {
                        float wy = oy == 0 ? wy0 : oy == 1 ? wy1 : wy2;
                        int row = Math.Min(sy + oy, srcH - 1) * srcW;
                        for (int ox = 0; ox < xCount; ox++)
                        {
                            float w = wy * (ox == 0 ? wx0 : ox == 1 ? wx1 : wx2);
                            uint p = src[row + Math.Min(sx + ox, srcW - 1)];
                            sr += (p & 0xFF) * w;
                            sg += ((p >> 8) & 0xFF) * w;
                            sb += ((p >> 16) & 0xFF) * w;
                        }
                    }

                    uint r = (uint)Math.Clamp((int)(sr + 0.5f), 0, 255);
                    uint g = (uint)Math.Clamp((int)(sg + 0.5f), 0, 255);
                    uint b = (uint)Math.Clamp((int)(sb + 0.5f), 0, 255);
                    dst[dy * dstW + dx] = r | (g << 8) | (b << 16) | 0xFF000000u;
                }
            }
            return dst;
        }

        // Alpha-aware premultiplied path with coverage preservation.
        int count = dstW * dstH;
        var straightR = new float[count];
        var straightG = new float[count];
        var straightB = new float[count];
        var avgA = new float[count];

        for (int dy = 0; dy < dstH; dy++)
        {
            int sy = dy * 2;
            float wy0, wy1, wy2;
            int yCount;
            if (srcH == 1)
            {
                wy0 = 1.0f; wy1 = 0.0f; wy2 = 0.0f; yCount = 1;
            }
            else if (oddH)
            {
                float inv = 1.0f / srcH;
                wy0 = (dstH - dy) * inv;
                wy1 = dstH * inv;
                wy2 = (dy + 1) * inv;
                yCount = 3;
            }
            else
            {
                wy0 = 0.5f; wy1 = 0.5f; wy2 = 0.0f; yCount = 2;
            }

            for (int dx = 0; dx < dstW; dx++)
            {
                int sx = dx * 2;
                float wx0, wx1, wx2;
                int xCount;
                if (srcW == 1)
                {
                    wx0 = 1.0f; wx1 = 0.0f; wx2 = 0.0f; xCount = 1;
                }
                else if (oddW)
                {
                    float inv = 1.0f / srcW;
                    wx0 = (dstW - dx) * inv;
                    wx1 = dstW * inv;
                    wx2 = (dx + 1) * inv;
                    xCount = 3;
                }
                else
                {
                    wx0 = 0.5f; wx1 = 0.5f; wx2 = 0.0f; xCount = 2;
                }

                float sr = 0, sg = 0, sb = 0, sa = 0;
                for (int oy = 0; oy < yCount; oy++)
                {
                    float wy = oy == 0 ? wy0 : oy == 1 ? wy1 : wy2;
                    int row = Math.Min(sy + oy, srcH - 1) * srcW;
                    for (int ox = 0; ox < xCount; ox++)
                    {
                        float w = wy * (ox == 0 ? wx0 : ox == 1 ? wx1 : wx2);
                        uint p = src[row + Math.Min(sx + ox, srcW - 1)];
                        uint aByte = p >> 24;
                        if (aByte == 0)
                            continue;
                        // Premultiplied RGB is already weighted by aByte / 255.
                        sr += (p & 0xFF) * w;
                        sg += ((p >> 8) & 0xFF) * w;
                        sb += ((p >> 16) & 0xFF) * w;
                        sa += aByte * w;
                    }
                }

                int idx = dy * dstW + dx;
                if (sa > 0.001f)
                {
                    float invA = 255.0f / sa;
                    straightR[idx] = Math.Clamp(sr * invA, 0.0f, 255.0f);
                    straightG[idx] = Math.Clamp(sg * invA, 0.0f, 255.0f);
                    straightB[idx] = Math.Clamp(sb * invA, 0.0f, 255.0f);
                    avgA[idx] = sa / 255.0f;
                }
            }
        }

        // Preserve cutout silhouette density across mip levels (bounded to avoid bloating).
        float alphaScale = 1.0f;
        if (refCoverage > 0.01f && refCoverage < 0.99f && count >= 4)
        {
            float lo = 0.75f;
            float hi = 2.0f;
            for (int iter = 0; iter < 6; iter++)
            {
                float mid = 0.5f * (lo + hi);
                int passCount = 0;
                for (int i = 0; i < count; i++)
                {
                    if (avgA[i] * mid >= 0.5f)
                        passCount++;
                }
                float cov = (float)passCount / count;
                if (cov < refCoverage)
                    lo = mid;
                else
                    hi = mid;
            }
            alphaScale = 0.5f * (lo + hi);
        }

        var result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            float a = Math.Clamp(avgA[i] * alphaScale, 0.0f, 1.0f);
            if (a <= 0.001f)
            {
                result[i] = 0u;
                continue;
            }
            uint aOut = (uint)Math.Clamp((int)(a * 255.0f + 0.5f), 1, 255);
            float aNorm = aOut / 255.0f;
            uint r = (uint)Math.Clamp((int)(straightR[i] * aNorm + 0.5f), 0, (int)aOut);
            uint g = (uint)Math.Clamp((int)(straightG[i] * aNorm + 0.5f), 0, (int)aOut);
            uint b = (uint)Math.Clamp((int)(straightB[i] * aNorm + 0.5f), 0, (int)aOut);
            result[i] = r | (g << 8) | (b << 16) | (aOut << 24);
        }

        return result;
    }

    static void BlendCorner(
        uint[] dst, int dstStride, int outX, int outY, int rot,
        uint b, uint c, uint d, uint e, uint f, uint g, uint h, uint i,
        uint f4, uint i4, uint h5, uint i5)
    {
        float wFH = ColorDist(e, c) + ColorDist(e, g) + ColorDist(i, h5) + ColorDist(i, f4) + 4.0f * ColorDist(h, f);
        float wEI = ColorDist(h, d) + ColorDist(h, i5) + ColorDist(f, i4) + ColorDist(f, b) + 4.0f * ColorDist(e, i);

        if (wFH >= wEI)
            return;

        uint px = ColorDist(e, f) <= ColorDist(e, h) ? f : h;

        bool doLineBlend = wEI > DominantDirectionThreshold * wFH
            || (ColorEqual(f, h) && !ColorEqual(e, f) && !ColorEqual(e, h));

        if (!doLineBlend)
        {
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 3, px, 1, 2);
            return;
        }

        float distFG = ColorDist(f, g);
        float distHC = ColorDist(h, c);
        bool haveShallow = SteepDirectionThreshold * distFG <= distHC && !ColorEqual(e, g) && !ColorEqual(d, g);
        bool haveSteep = SteepDirectionThreshold * distHC <= distFG && !ColorEqual(e, c) && !ColorEqual(b, c);

        if (haveShallow && haveSteep)
        {
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 1, px, 1, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 2, 2, px, 1, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 2, px, 3, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 1, 3, px, 1, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 2, 3, px, 3, 4);
            SetRotated(dst, dstStride, outX, outY, rot, 3, 3, px);
        }
        else if (haveShallow)
        {
            BlendRotated(dst, dstStride, outX, outY, rot, 2, 2, px, 1, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 2, px, 3, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 1, 3, px, 1, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 2, 3, px, 3, 4);
            SetRotated(dst, dstStride, outX, outY, rot, 3, 3, px);
        }
        else if (haveSteep)
        {
            BlendRotated(dst, dstStride, outX, outY, rot, 2, 2, px, 1, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 2, 3, px, 3, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 1, px, 1, 4);
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 2, px, 3, 4);
            SetRotated(dst, dstStride, outX, outY, rot, 3, 3, px);
        }
        else
        {
            BlendRotated(dst, dstStride, outX, outY, rot, 2, 3, px, 1, 2);
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 2, px, 1, 2);
            BlendRotated(dst, dstStride, outX, outY, rot, 3, 3, px, 7, 8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int RotatedOffset(int dstStride, int outX, int outY, int rot, int lx, int ly) => rot switch
    {
        0 => (outY + ly) * dstStride + (outX + lx),
        1 => (outY + lx) * dstStride + (outX + (3 - ly)),
        2 => (outY + (3 - ly)) * dstStride + (outX + (3 - lx)),
        _ => (outY + (3 - lx)) * dstStride + (outX + ly),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SetRotated(uint[] dst, int dstStride, int outX, int outY, int rot, int lx, int ly, uint src)
    {
        dst[RotatedOffset(dstStride, outX, outY, rot, lx, ly)] = src;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void BlendRotated(uint[] dst, int dstStride, int outX, int outY, int rot, int lx, int ly, uint src, uint num, uint den)
    {
        int idx = RotatedOffset(dstStride, outX, outY, rot, lx, ly);
        uint d = dst[idx];
        if (d == src)
            return;

        uint inv = den - num;
        uint half = den >> 1;
        uint r = ((d & 0xFF) * inv + (src & 0xFF) * num + half) / den;
        uint g = (((d >> 8) & 0xFF) * inv + ((src >> 8) & 0xFF) * num + half) / den;
        uint b = (((d >> 16) & 0xFF) * inv + ((src >> 16) & 0xFF) * num + half) / den;
        uint a = (((d >> 24) & 0xFF) * inv + ((src >> 24) & 0xFF) * num + half) / den;
        dst[idx] = r | (g << 8) | (b << 16) | (a << 24);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool ColorEqual(uint c1, uint c2) => c1 == c2 || ColorDist(c1, c2) < EqualColorTolerance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float ColorDist(uint c1, uint c2)
    {
        if (c1 == c2)
            return 0.0f;

        uint a1 = c1 >> 24;
        uint a2 = c2 >> 24;
        if (a1 == 0 && a2 == 0)
            return 0.0f;
        if (a1 == 0 || a2 == 0)
            return TransparentSeparationDistance;

        int dr = (int)(c1 & 0xFF) - (int)(c2 & 0xFF);
        int dg = (int)((c1 >> 8) & 0xFF) - (int)((c2 >> 8) & 0xFF);
        int db = (int)((c1 >> 16) & 0xFF) - (int)((c2 >> 16) & 0xFF);

        float y = 0.299f * dr + 0.587f * dg + 0.114f * db;
        float cb = -0.168736f * dr - 0.331264f * dg + 0.5f * db;
        float cr = 0.5f * dr - 0.418688f * dg - 0.081312f * db;
        return MathF.Sqrt(y * y + cb * cb + cr * cr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Wrap(int v, int size)
    {
        int m = v % size;
        return m < 0 ? m + size : m;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Clamp(int v, int size)
    {
        if (v < 0) return 0;
        if (v >= size) return size - 1;
        return v;
    }

    static byte[] PackToBytes(uint[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
#endif

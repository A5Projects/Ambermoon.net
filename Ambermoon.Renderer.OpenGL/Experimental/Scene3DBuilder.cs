#if !GLES
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ambermoon.Renderer.OpenGL.Experimental;

using Render;
using Texture = Ambermoon.Renderer.OpenGL.Texture;

internal readonly record struct SceneBounds3D(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
{
    public static SceneBounds3D Empty => new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity,
        float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX;

    public SceneBounds3D Union(in SceneBounds3D other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return new(
            Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Min(MinZ, other.MinZ),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY), Math.Max(MaxZ, other.MaxZ));
    }
}

internal readonly record struct TextureCacheContext(
    uint AtlasGlId,
    uint PaletteGlId,
    ulong PalReplaceHash,
    byte SkyColorKey,
    uint SkyReplaceRgba,
    Texture3DFilterMode FilterMode);

internal readonly record struct BatchDrawRange(TextureArrayBucket Bucket, int FirstIndex, int IndexCount);

[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceVertex
{
    public float PosX, PosY, PosZ;
    public float NormalX, NormalY, NormalZ;
    public float UvX, UvY;
    public float TexLayer;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BillboardVertex
{
    public float PosX, PosY, PosZ;
    public float CenterX, CenterY, CenterZ;
    public uint Orientation;
    public float UvX, UvY;
    public float TexLayer;
    public float Extrude;
}

/// <summary>
/// Compact GPU mesh batch for static/semi-static 3D geometry (floor, ceiling, walls)
/// using a single interleaved VBO + bucket-grouped EBO rebuilt lazily only when surfaces
/// or texture-cache inputs change.
/// </summary>
internal sealed class SurfaceMeshBatch : IDisposable
{
    readonly State state;
    readonly List<ISurface3D> slots = new(256);
    readonly Dictionary<ISurface3D, int> surfaceToSlot = new(256);
    readonly SortedSet<int> freeSlots = new();
    readonly List<TextureLayerKey> scratchKeys = new(256);
    readonly List<ISurface3D> scratchSurfaces = new(256);
    readonly Dictionary<TextureArrayBucket, List<uint>> bucketIndices = new();
    readonly List<TextureArrayBucket> bucketOrder = new();
    readonly List<BatchDrawRange> drawRanges = new();
    readonly List<ResolvedTextureLayer> activeLayers = new();
    readonly HashSet<ResolvedTextureLayer> activeLayerSet = new();
    SurfaceVertex[] vertices = [];
    uint[] indices = [];
    uint vao;
    uint vbo;
    uint ebo;
    int activeQuadCount;
    bool dirty = true;
    bool hasBuiltContext;
    TextureCacheContext builtContext;
    ulong builtCacheEpoch;
    bool disposed;

    public uint TextureFactor { get; set; } = 1;
    public int QuadCount => activeQuadCount;
    public int RebuildCount { get; private set; }
    public int DrawCallCount => drawRanges.Count;
    public SceneBounds3D Bounds { get; private set; } = SceneBounds3D.Empty;

    public SurfaceMeshBatch(State state)
    {
        this.state = state;
        var gl = state.Gl;

        vao = gl.GenVertexArray();
        vbo = gl.GenBuffer();
        ebo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);

        uint stride = (uint)Marshal.SizeOf<SurfaceVertex>();
        unsafe
        {
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.PosX)));

            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.NormalX)));

            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.UvX)));

            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.TexLayer)));

            gl.EnableVertexAttribArray(4);
            gl.VertexAttribIPointer(4, 1, VertexAttribIType.UnsignedInt, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.Flags)));
        }

        gl.BindVertexArray(0);
    }

    public void Add(ISurface3D surface)
    {
        if (surfaceToSlot.ContainsKey(surface))
        {
            dirty = true;
            return;
        }

        int slot;
        if (freeSlots.Count > 0)
        {
            slot = freeSlots.Min;
            freeSlots.Remove(slot);
            slots[slot] = surface;
        }
        else
        {
            slot = slots.Count;
            slots.Add(surface);
        }

        surfaceToSlot[surface] = slot;
        dirty = true;
    }

    public void Remove(ISurface3D surface)
    {
        if (surfaceToSlot.Remove(surface, out int slot))
        {
            slots[slot] = null;
            freeSlots.Add(slot);
            dirty = true;
        }
    }

    public void MarkDirty() => dirty = true;

    public void EnsureUploaded(
        ExperimentalTextureCache textureCache,
        Texture atlas,
        Texture palette,
        Experimental3DState uniforms,
        in TextureCacheContext context)
    {
        if (disposed)
            return;

        if (!dirty && hasBuiltContext && builtContext == context && builtCacheEpoch == textureCache.CacheEpoch)
            return;

        dirty = false;
        hasBuiltContext = true;
        builtContext = context;
        RebuildCount++;

        drawRanges.Clear();
        activeLayers.Clear();
        activeLayerSet.Clear();

        int quadCount = surfaceToSlot.Count;
        activeQuadCount = quadCount;

        if (quadCount == 0)
        {
            Bounds = SceneBounds3D.Empty;
            builtCacheEpoch = textureCache.CacheEpoch;
            return;
        }

        scratchSurfaces.Clear();
        scratchKeys.Clear();
        int factor = (int)Math.Max(1u, TextureFactor);

        for (int s = 0; s < slots.Count; s++)
        {
            var surface = slots[s];
            if (surface == null)
                continue;

            int texW = Math.Max(1, (int)(surface.TextureWidth * factor));
            int texH = Math.Max(1, (int)(surface.TextureHeight * factor));
            byte alphaMode = (byte)(surface.Alpha ? 1 : surface.Type == SurfaceType.Wall ? 2 : 0);
            bool wrapRepeat = surface.Type == SurfaceType.Floor || surface.Type == SurfaceType.Ceiling;

            var key = textureCache.BuildKey(
                atlas,
                surface.TextureAtlasOffset.X,
                surface.TextureAtlasOffset.Y,
                texW,
                texH,
                surface.PaletteIndex,
                alphaMode,
                wrapRepeat,
                uniforms,
                context.PalReplaceHash,
                context.FilterMode);

            scratchSurfaces.Add(surface);
            scratchKeys.Add(key);
        }

        textureCache.EnsureLayers(scratchKeys, atlas, palette, uniforms.PaletteReplacement);
        builtCacheEpoch = textureCache.CacheEpoch;

        int actualQuads = scratchSurfaces.Count;
        int vertexCount = actualQuads * 4;
        int indexCount = actualQuads * 6;
        if (vertices.Length < vertexCount)
            vertices = new SurfaceVertex[Math.Max(vertexCount, Math.Max(64, vertices.Length * 2))];
        if (indices.Length < indexCount)
            indices = new uint[Math.Max(indexCount, Math.Max(96, indices.Length * 2))];

        foreach (var list in bucketIndices.Values)
            list.Clear();
        bucketOrder.Clear();

        int vOut = 0;
        var totalBounds = SceneBounds3D.Empty;

        for (int i = 0; i < actualQuads; i++)
        {
            var surface = scratchSurfaces[i];
            if (!textureCache.TryGetResolved(scratchKeys[i], out var resolved))
                continue;

            if (activeLayerSet.Add(resolved))
                activeLayers.Add(resolved);

            int baseVertex = vOut;
            WriteSurfaceQuad(surface, resolved.LayerIndex, vertices, baseVertex, out var bounds);
            totalBounds = totalBounds.Union(bounds);

            if (!bucketIndices.TryGetValue(resolved.Bucket, out var idxList))
            {
                idxList = new List<uint>(64);
                bucketIndices.Add(resolved.Bucket, idxList);
            }
            if (idxList.Count == 0)
                bucketOrder.Add(resolved.Bucket);

            // Match Classic IndexBuffer winding: (0, 1, 2) and (3, 0, 2).
            uint bv = (uint)baseVertex;
            idxList.Add(bv + 0);
            idxList.Add(bv + 1);
            idxList.Add(bv + 2);
            idxList.Add(bv + 3);
            idxList.Add(bv + 0);
            idxList.Add(bv + 2);

            vOut += 4;
        }

        int iOut = 0;
        for (int b = 0; b < bucketOrder.Count; b++)
        {
            var bucket = bucketOrder[b];
            var idxList = bucketIndices[bucket];
            if (idxList.Count == 0)
                continue;
            int first = iOut;
            idxList.CopyTo(indices, iOut);
            iOut += idxList.Count;
            drawRanges.Add(new BatchDrawRange(bucket, first, idxList.Count));
        }

        activeQuadCount = vOut / 4;
        Bounds = totalBounds;

        var gl = state.Gl;
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        unsafe
        {
            fixed (SurfaceVertex* vPtr = vertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vOut * Marshal.SizeOf<SurfaceVertex>()), vPtr, BufferUsageARB.DynamicDraw);
            fixed (uint* iPtr = indices)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(iOut * sizeof(uint)), iPtr, BufferUsageARB.DynamicDraw);
        }
        gl.BindVertexArray(0);
    }

    static void WriteSurfaceQuad(ISurface3D surface, int layerIndex, SurfaceVertex[] dst, int offset, out SceneBounds3D bounds)
    {
        float x = surface.X;
        float y = surface.Y;
        float z = surface.Z;
        float w = surface.Width;
        float h = surface.Height;

        float x0 = x, y0 = y, z0 = z;
        float x1 = x, y1 = y, z1 = z;
        float x2 = x, y2 = y, z2 = z;
        float x3 = x, y3 = y, z3 = z;
        float nx = 0, ny = 1, nz = 0;

        float repU = surface.TextureWidth > 0 ? (float)surface.MappedTextureWidth / surface.TextureWidth : 1.0f;
        float repV = surface.TextureHeight > 0 ? (float)surface.MappedTextureHeight / surface.TextureHeight : 1.0f;

        float u0 = 0, v0 = 0;
        float u1 = 0, v1 = 0;
        float u2 = 0, v2 = 0;
        float u3 = 0, v3 = 0;

        switch (surface.Type)
        {
            case SurfaceType.Floor:
                x1 = x;     y1 = y; z1 = z + h;
                x2 = x + w; y2 = y; z2 = z + h;
                x3 = x + w; y3 = y; z3 = z;
                nx = 0; ny = 1; nz = 0;
                u0 = 0.0f; v0 = repV;
                u1 = 0.0f; v1 = 0.0f;
                u2 = repU; v2 = 0.0f;
                u3 = repU; v3 = repV;
                break;

            case SurfaceType.Ceiling:
                x1 = x;     y1 = y; z1 = z - h;
                x2 = x + w; y2 = y; z2 = z - h;
                x3 = x + w; y3 = y; z3 = z;
                nx = 0; ny = -1; nz = 0;
                u0 = 0.0f; v0 = 0.0f;
                u1 = 0.0f; v1 = repV;
                u2 = repU; v2 = repV;
                u3 = repU; v3 = 0.0f;
                break;

            case SurfaceType.Wall:
                switch (surface.WallOrientation)
                {
                    case WallOrientation.Normal:
                        x1 = x + w; y1 = y;     z1 = z;
                        x2 = x + w; y2 = y - h; z2 = z;
                        x3 = x;     y3 = y - h; z3 = z;
                        nx = 0; ny = 0; nz = -1;
                        break;
                    case WallOrientation.Rotated90:
                        x1 = x; y1 = y;     z1 = z + w;
                        x2 = x; y2 = y - h; z2 = z + w;
                        x3 = x; y3 = y - h; z3 = z;
                        nx = 1; ny = 0; nz = 0;
                        break;
                    case WallOrientation.Rotated180:
                        x1 = x - w; y1 = y;     z1 = z;
                        x2 = x - w; y2 = y - h; z2 = z;
                        x3 = x;     y3 = y - h; z3 = z;
                        nx = 0; ny = 0; nz = 1;
                        break;
                    case WallOrientation.Rotated270:
                        x1 = x; y1 = y;     z1 = z - w;
                        x2 = x; y2 = y - h; z2 = z - w;
                        x3 = x; y3 = y - h; z3 = z;
                        nx = -1; ny = 0; nz = 0;
                        break;
                }
                u0 = repU; v0 = 0.0f;
                u1 = 0.0f; v1 = 0.0f;
                u2 = 0.0f; v2 = repV;
                u3 = repU; v3 = repV;
                break;
        }

        // bit 0: alpha-cutout enabled (all walls can carry keyed/transparent holes; floor/ceiling are opaque)
        // bit 1: floor surface marker
        uint flags = (surface.Alpha || surface.Type == SurfaceType.Wall) ? 1u : 0u;
        if (surface.Type == SurfaceType.Floor)
            flags |= 2u;
        float texLayer = layerIndex;

        dst[offset + 0] = new SurfaceVertex
        {
            PosX = x0, PosY = y0, PosZ = z0,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            UvX = u0, UvY = v0, TexLayer = texLayer, Flags = flags
        };
        dst[offset + 1] = new SurfaceVertex
        {
            PosX = x1, PosY = y1, PosZ = z1,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            UvX = u1, UvY = v1, TexLayer = texLayer, Flags = flags
        };
        dst[offset + 2] = new SurfaceVertex
        {
            PosX = x2, PosY = y2, PosZ = z2,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            UvX = u2, UvY = v2, TexLayer = texLayer, Flags = flags
        };
        dst[offset + 3] = new SurfaceVertex
        {
            PosX = x3, PosY = y3, PosZ = z3,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            UvX = u3, UvY = v3, TexLayer = texLayer, Flags = flags
        };

        float minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        float minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        float minZ = Math.Min(Math.Min(z0, z1), Math.Min(z2, z3));
        float maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        float maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        float maxZ = Math.Max(Math.Max(z0, z1), Math.Max(z2, z3));
        bounds = new SceneBounds3D(minX, minY, minZ, maxX, maxY, maxZ);
    }

    public void TouchActiveLayers(ulong frameIndex)
    {
        for (int i = 0; i < activeLayers.Count; i++)
            activeLayers[i].Bucket.Touch(activeLayers[i].LayerIndex, frameIndex);
    }

    public void Draw(
        ExperimentalTextureCache textureCache,
        Texture atlas,
        Texture palette,
        Experimental3DState uniforms,
        in TextureCacheContext context,
        ulong frameIndex,
        Action<TextureArrayBucket> applyBucketUniforms)
    {
        if (disposed)
            return;

        EnsureUploaded(textureCache, atlas, palette, uniforms, context);
        if (activeQuadCount == 0 || drawRanges.Count == 0)
            return;

        TouchActiveLayers(frameIndex);

        var gl = state.Gl;
        gl.BindVertexArray(vao);
        for (int r = 0; r < drawRanges.Count; r++)
        {
            var range = drawRanges[r];
            range.Bucket.Bind(context.FilterMode);
            applyBucketUniforms(range.Bucket);
            unsafe
            {
                gl.DrawElements(PrimitiveType.Triangles, (uint)range.IndexCount,
                    DrawElementsType.UnsignedInt, (void*)(range.FirstIndex * sizeof(uint)));
            }
        }
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        var gl = state.Gl;
        if (ebo != 0) gl.DeleteBuffer(ebo);
        if (vbo != 0) gl.DeleteBuffer(vbo);
        if (vao != 0) gl.DeleteVertexArray(vao);
        ebo = vbo = vao = 0;
        slots.Clear();
        surfaceToSlot.Clear();
        freeSlots.Clear();
        drawRanges.Clear();
        activeLayers.Clear();
        activeLayerSet.Clear();
        bucketIndices.Clear();
        bucketOrder.Clear();
    }
}

/// <summary>
/// Compact GPU mesh batch for 3D billboards and billboard-floor surfaces (objects, NPCs, monsters),
/// grouped by exact texture-array bucket for native clamp-to-edge, mipmaps, and 16x anisotropy.
/// </summary>
internal sealed class BillboardMeshBatch : IDisposable
{
    readonly State state;
    readonly List<ISurface3D> slots = new(128);
    readonly Dictionary<ISurface3D, int> surfaceToSlot = new(128);
    readonly SortedSet<int> freeSlots = new();
    readonly List<TextureLayerKey> scratchKeys = new(128);
    readonly List<ISurface3D> scratchSurfaces = new(128);
    readonly Dictionary<TextureArrayBucket, List<uint>> bucketIndices = new();
    readonly List<TextureArrayBucket> bucketOrder = new();
    readonly List<BatchDrawRange> drawRanges = new();
    readonly List<ResolvedTextureLayer> activeLayers = new();
    readonly HashSet<ResolvedTextureLayer> activeLayerSet = new();
    BillboardVertex[] vertices = [];
    uint[] indices = [];
    uint vao;
    uint vbo;
    uint ebo;
    int activeQuadCount;
    bool dirty = true;
    bool hasBuiltContext;
    TextureCacheContext builtContext;
    ulong builtCacheEpoch;
    bool disposed;

    public uint TextureFactor { get; set; } = 1;
    public int QuadCount => activeQuadCount;
    public int RebuildCount { get; private set; }
    public int DrawCallCount => drawRanges.Count;

    public BillboardMeshBatch(State state)
    {
        this.state = state;
        var gl = state.Gl;

        vao = gl.GenVertexArray();
        vbo = gl.GenBuffer();
        ebo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);

        uint stride = (uint)Marshal.SizeOf<BillboardVertex>();
        unsafe
        {
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.PosX)));

            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.CenterX)));

            gl.EnableVertexAttribArray(2);
            gl.VertexAttribIPointer(2, 1, VertexAttribIType.UnsignedInt, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.Orientation)));

            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.UvX)));

            gl.EnableVertexAttribArray(4);
            gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.TexLayer)));

            gl.EnableVertexAttribArray(5);
            gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.Extrude)));
        }

        gl.BindVertexArray(0);
    }

    public void Add(ISurface3D surface)
    {
        if (surfaceToSlot.ContainsKey(surface))
        {
            dirty = true;
            return;
        }

        int slot;
        if (freeSlots.Count > 0)
        {
            slot = freeSlots.Min;
            freeSlots.Remove(slot);
            slots[slot] = surface;
        }
        else
        {
            slot = slots.Count;
            slots.Add(surface);
        }

        surfaceToSlot[surface] = slot;
        dirty = true;
    }

    public void Remove(ISurface3D surface)
    {
        if (surfaceToSlot.Remove(surface, out int slot))
        {
            slots[slot] = null;
            freeSlots.Add(slot);
            dirty = true;
        }
    }

    public void MarkDirty() => dirty = true;

    public void EnsureUploaded(
        ExperimentalTextureCache textureCache,
        Texture atlas,
        Texture palette,
        Experimental3DState uniforms,
        in TextureCacheContext context)
    {
        if (disposed)
            return;

        if (!dirty && hasBuiltContext && builtContext == context && builtCacheEpoch == textureCache.CacheEpoch)
            return;

        dirty = false;
        hasBuiltContext = true;
        builtContext = context;
        RebuildCount++;

        drawRanges.Clear();
        activeLayers.Clear();
        activeLayerSet.Clear();

        int quadCount = surfaceToSlot.Count;
        activeQuadCount = quadCount;
        if (quadCount == 0)
        {
            builtCacheEpoch = textureCache.CacheEpoch;
            return;
        }

        scratchSurfaces.Clear();
        scratchKeys.Clear();
        int factor = (int)Math.Max(1u, TextureFactor);

        for (int s = 0; s < slots.Count; s++)
        {
            var surface = slots[s];
            if (surface == null)
                continue;

            int texW = Math.Max(1, (int)(surface.TextureWidth * factor));
            int texH = Math.Max(1, (int)(surface.TextureHeight * factor));

            var key = textureCache.BuildKey(
                atlas,
                surface.TextureAtlasOffset.X,
                surface.TextureAtlasOffset.Y,
                texW,
                texH,
                surface.PaletteIndex,
                alphaMode: 3,
                wrapRepeat: false,
                uniforms,
                context.PalReplaceHash,
                context.FilterMode);

            scratchSurfaces.Add(surface);
            scratchKeys.Add(key);
        }

        textureCache.EnsureLayers(scratchKeys, atlas, palette, uniforms.PaletteReplacement);
        builtCacheEpoch = textureCache.CacheEpoch;

        int actualQuads = scratchSurfaces.Count;
        int vertexCount = actualQuads * 4;
        int indexCount = actualQuads * 6;
        if (vertices.Length < vertexCount)
            vertices = new BillboardVertex[Math.Max(vertexCount, Math.Max(64, vertices.Length * 2))];
        if (indices.Length < indexCount)
            indices = new uint[Math.Max(indexCount, Math.Max(96, indices.Length * 2))];

        foreach (var list in bucketIndices.Values)
            list.Clear();
        bucketOrder.Clear();

        int vOut = 0;
        for (int i = 0; i < actualQuads; i++)
        {
            var surface = scratchSurfaces[i];
            if (!textureCache.TryGetResolved(scratchKeys[i], out var resolved))
                continue;

            if (activeLayerSet.Add(resolved))
                activeLayers.Add(resolved);

            int baseVertex = vOut;
            WriteBillboardQuad(surface, resolved.LayerIndex, vertices, baseVertex);

            if (!bucketIndices.TryGetValue(resolved.Bucket, out var idxList))
            {
                idxList = new List<uint>(32);
                bucketIndices.Add(resolved.Bucket, idxList);
            }
            if (idxList.Count == 0)
                bucketOrder.Add(resolved.Bucket);

            uint bv = (uint)baseVertex;
            idxList.Add(bv + 0);
            idxList.Add(bv + 1);
            idxList.Add(bv + 2);
            idxList.Add(bv + 3);
            idxList.Add(bv + 0);
            idxList.Add(bv + 2);

            vOut += 4;
        }

        int iOut = 0;
        for (int b = 0; b < bucketOrder.Count; b++)
        {
            var bucket = bucketOrder[b];
            var idxList = bucketIndices[bucket];
            if (idxList.Count == 0)
                continue;
            int first = iOut;
            idxList.CopyTo(indices, iOut);
            iOut += idxList.Count;
            drawRanges.Add(new BatchDrawRange(bucket, first, idxList.Count));
        }

        activeQuadCount = vOut / 4;

        var gl = state.Gl;
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        unsafe
        {
            fixed (BillboardVertex* vPtr = vertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vOut * Marshal.SizeOf<BillboardVertex>()), vPtr, BufferUsageARB.DynamicDraw);
            fixed (uint* iPtr = indices)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(iOut * sizeof(uint)), iPtr, BufferUsageARB.DynamicDraw);
        }
        gl.BindVertexArray(0);
    }

    static void WriteBillboardQuad(ISurface3D surface, int layerIndex, BillboardVertex[] dst, int offset)
    {
        float cx = surface.X;
        float cy = surface.Y;
        float cz = surface.Z;
        float halfW = 0.5f * surface.Width;
        float h = surface.Height;
        bool isFloor = surface.Type == SurfaceType.BillboardFloor;
        uint orientation = isFloor ? 1u : 0u;
        float extrude = surface.Extrude;
        float texLayer = layerIndex;

        float repU = surface.TextureWidth > 0 ? (float)surface.MappedTextureWidth / surface.TextureWidth : 1.0f;
        float repV = surface.TextureHeight > 0 ? (float)surface.MappedTextureHeight / surface.TextureHeight : 1.0f;

        float topY = isFloor ? cy + 0.5f * h : cy;
        float bottomY = isFloor ? cy - 0.5f * h : cy - h;

        float u0 = 0.0f, v0 = isFloor ? repV : 0.0f;
        float u1 = repU, v1 = isFloor ? repV : 0.0f;
        float u2 = repU, v2 = isFloor ? 0.0f : repV;
        float u3 = 0.0f, v3 = isFloor ? 0.0f : repV;

        dst[offset + 0] = new BillboardVertex
        {
            PosX = cx - halfW, PosY = topY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation, UvX = u0, UvY = v0, TexLayer = texLayer, Extrude = extrude
        };
        dst[offset + 1] = new BillboardVertex
        {
            PosX = cx + halfW, PosY = topY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation, UvX = u1, UvY = v1, TexLayer = texLayer, Extrude = extrude
        };
        dst[offset + 2] = new BillboardVertex
        {
            PosX = cx + halfW, PosY = bottomY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation, UvX = u2, UvY = v2, TexLayer = texLayer, Extrude = extrude
        };
        dst[offset + 3] = new BillboardVertex
        {
            PosX = cx - halfW, PosY = bottomY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation, UvX = u3, UvY = v3, TexLayer = texLayer, Extrude = extrude
        };
    }

    public void TouchActiveLayers(ulong frameIndex)
    {
        for (int i = 0; i < activeLayers.Count; i++)
            activeLayers[i].Bucket.Touch(activeLayers[i].LayerIndex, frameIndex);
    }

    public void Draw(
        ExperimentalTextureCache textureCache,
        Texture atlas,
        Texture palette,
        Experimental3DState uniforms,
        in TextureCacheContext context,
        ulong frameIndex,
        Action<TextureArrayBucket> applyBucketUniforms)
    {
        if (disposed)
            return;

        EnsureUploaded(textureCache, atlas, palette, uniforms, context);
        if (activeQuadCount == 0 || drawRanges.Count == 0)
            return;

        TouchActiveLayers(frameIndex);

        var gl = state.Gl;
        gl.BindVertexArray(vao);
        for (int r = 0; r < drawRanges.Count; r++)
        {
            var range = drawRanges[r];
            range.Bucket.Bind(context.FilterMode);
            applyBucketUniforms(range.Bucket);
            unsafe
            {
                gl.DrawElements(PrimitiveType.Triangles, (uint)range.IndexCount,
                    DrawElementsType.UnsignedInt, (void*)(range.FirstIndex * sizeof(uint)));
            }
        }
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        var gl = state.Gl;
        if (ebo != 0) gl.DeleteBuffer(ebo);
        if (vbo != 0) gl.DeleteBuffer(vbo);
        if (vao != 0) gl.DeleteVertexArray(vao);
        ebo = vbo = vao = 0;
        slots.Clear();
        surfaceToSlot.Clear();
        freeSlots.Clear();
        drawRanges.Clear();
        activeLayers.Clear();
        activeLayerSet.Clear();
        bucketIndices.Clear();
        bucketOrder.Clear();
    }
}

/// <summary>
/// Builds and maintains compact, independently dirty-tracked 3D GPU meshes
/// (floor/ceiling, walls, billboards) from live <see cref="ISurface3D"/> instances.
/// </summary>
internal sealed class Scene3DBuilder : IScene3DObserver, IDisposable
{
    public SurfaceMeshBatch FloorCeilingBatch { get; }
    public SurfaceMeshBatch WallBatch { get; }
    public BillboardMeshBatch BillboardBatch { get; }

    public int FloorCeilingQuadCount => FloorCeilingBatch.QuadCount;
    public int WallQuadCount => WallBatch.QuadCount;
    public int BillboardQuadCount => BillboardBatch.QuadCount;
    public int FloorCeilingRebuilds => FloorCeilingBatch.RebuildCount;
    public int WallRebuilds => WallBatch.RebuildCount;
    public int BillboardRebuilds => BillboardBatch.RebuildCount;

    public Scene3DBuilder(State state)
    {
        FloorCeilingBatch = new SurfaceMeshBatch(state);
        WallBatch = new SurfaceMeshBatch(state);
        BillboardBatch = new BillboardMeshBatch(state);
    }

    public void OnSurfaceAdded(Layer layer, ISurface3D surface)
    {
        switch (layer)
        {
            case Layer.Map3DCeiling:
                FloorCeilingBatch.Add(surface);
                break;
            case Layer.Map3D:
                WallBatch.Add(surface);
                break;
            case Layer.Billboards3D:
                BillboardBatch.Add(surface);
                break;
        }
    }

    public void OnSurfaceRemoved(Layer layer, ISurface3D surface)
    {
        switch (layer)
        {
            case Layer.Map3DCeiling:
                FloorCeilingBatch.Remove(surface);
                break;
            case Layer.Map3D:
                WallBatch.Remove(surface);
                break;
            case Layer.Billboards3D:
                BillboardBatch.Remove(surface);
                break;
        }
    }

    public void OnSurfaceUpdated(Layer layer, ISurface3D surface)
    {
        switch (layer)
        {
            case Layer.Map3DCeiling:
                FloorCeilingBatch.MarkDirty();
                break;
            case Layer.Map3D:
                WallBatch.MarkDirty();
                break;
            case Layer.Billboards3D:
                BillboardBatch.MarkDirty();
                break;
        }
    }

    public void OnTextureFactorChanged(Layer layer, uint textureFactor)
    {
        switch (layer)
        {
            case Layer.Map3DCeiling:
                FloorCeilingBatch.TextureFactor = textureFactor;
                FloorCeilingBatch.MarkDirty();
                break;
            case Layer.Map3D:
                WallBatch.TextureFactor = textureFactor;
                WallBatch.MarkDirty();
                break;
            case Layer.Billboards3D:
                BillboardBatch.TextureFactor = textureFactor;
                BillboardBatch.MarkDirty();
                break;
        }
    }

    public void Dispose()
    {
        FloorCeilingBatch.Dispose();
        WallBatch.Dispose();
        BillboardBatch.Dispose();
    }
}
#endif

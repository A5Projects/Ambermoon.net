#if !GLES
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ambermoon.Renderer.OpenGL.Experimental;

using Render;

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

[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceVertex
{
    public float PosX, PosY, PosZ;
    public float NormalX, NormalY, NormalZ;
    public int TexX, TexY;
    public int TexEndX, TexEndY;
    public int TexSizeX, TexSizeY;
    public uint PaletteIndex;
    public uint AlphaFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BillboardVertex
{
    public float PosX, PosY, PosZ;
    public float CenterX, CenterY, CenterZ;
    public uint Orientation;
    public int TexX, TexY;
    public int TexEndX, TexEndY;
    public int TexSizeX, TexSizeY;
    public uint PaletteIndex;
    public float Extrude;
}

/// <summary>
/// Compact GPU mesh batch for static/semi-static 3D geometry (floor, ceiling, walls)
/// using a single interleaved VBO + EBO rebuilt lazily only when surfaces change.
/// </summary>
internal sealed class SurfaceMeshBatch : IDisposable
{
    readonly State state;
    readonly List<ISurface3D> slots = new(256);
    readonly Dictionary<ISurface3D, int> surfaceToSlot = new(256);
    readonly SortedSet<int> freeSlots = new();
    SurfaceVertex[] vertices = [];
    uint[] indices = [];
    uint vao;
    uint vbo;
    uint ebo;
    int activeQuadCount;
    bool dirty = true;
    bool disposed;

    public uint TextureFactor { get; set; } = 1;
    public int QuadCount => activeQuadCount;
    public int RebuildCount { get; private set; }
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
            gl.VertexAttribIPointer(2, 2, VertexAttribIType.Int, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.TexX)));

            gl.EnableVertexAttribArray(3);
            gl.VertexAttribIPointer(3, 2, VertexAttribIType.Int, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.TexEndX)));

            gl.EnableVertexAttribArray(4);
            gl.VertexAttribIPointer(4, 2, VertexAttribIType.Int, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.TexSizeX)));

            gl.EnableVertexAttribArray(5);
            gl.VertexAttribIPointer(5, 1, VertexAttribIType.UnsignedInt, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.PaletteIndex)));

            gl.EnableVertexAttribArray(6);
            gl.VertexAttribIPointer(6, 1, VertexAttribIType.UnsignedInt, stride, (void*)Marshal.OffsetOf<SurfaceVertex>(nameof(SurfaceVertex.AlphaFlags)));
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

    public void EnsureUploaded()
    {
        if (!dirty || disposed)
            return;

        dirty = false;
        RebuildCount++;

        int quadCount = surfaceToSlot.Count;
        activeQuadCount = quadCount;

        if (quadCount == 0)
        {
            Bounds = SceneBounds3D.Empty;
            return;
        }

        int vertexCount = quadCount * 4;
        int indexCount = quadCount * 6;
        if (vertices.Length < vertexCount)
            vertices = new SurfaceVertex[Math.Max(vertexCount, Math.Max(64, vertices.Length * 2))];
        if (indices.Length < indexCount)
            indices = new uint[Math.Max(indexCount, Math.Max(96, indices.Length * 2))];

        int vOut = 0;
        int iOut = 0;
        int factor = (int)Math.Max(1u, TextureFactor);
        var totalBounds = SceneBounds3D.Empty;

        for (int s = 0; s < slots.Count; s++)
        {
            var surface = slots[s];
            if (surface == null)
                continue;

            int baseVertex = vOut;
            WriteSurfaceQuad(surface, factor, vertices, baseVertex, out var bounds);
            totalBounds = totalBounds.Union(bounds);

            // Match Classic IndexBuffer winding: (0, 1, 2) and (3, 0, 2).
            uint bv = (uint)baseVertex;
            indices[iOut++] = bv + 0;
            indices[iOut++] = bv + 1;
            indices[iOut++] = bv + 2;
            indices[iOut++] = bv + 3;
            indices[iOut++] = bv + 0;
            indices[iOut++] = bv + 2;

            vOut += 4;
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

    static void WriteSurfaceQuad(ISurface3D surface, int textureFactor, SurfaceVertex[] dst, int offset, out SceneBounds3D bounds)
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

        int mappedW = (int)(surface.MappedTextureWidth * textureFactor);
        int mappedH = (int)(surface.MappedTextureHeight * textureFactor);
        int texW = (int)(surface.TextureWidth * textureFactor);
        int texH = (int)(surface.TextureHeight * textureFactor);
        int offX = surface.TextureAtlasOffset.X;
        int offY = surface.TextureAtlasOffset.Y;
        int endX = offX + surface.FrameCount * texW;
        int endY = offY + texH;

        int u0 = offX, v0 = offY;
        int u1 = offX, v1 = offY;
        int u2 = offX, v2 = offY;
        int u3 = offX, v3 = offY;

        switch (surface.Type)
        {
            case SurfaceType.Floor:
                x1 = x;     y1 = y; z1 = z + h;
                x2 = x + w; y2 = y; z2 = z + h;
                x3 = x + w; y3 = y; z3 = z;
                nx = 0; ny = 1; nz = 0;
                u0 = offX;           v0 = offY + mappedH;
                u1 = offX;           v1 = offY;
                u2 = offX + mappedW; v2 = offY;
                u3 = offX + mappedW; v3 = offY + mappedH;
                break;

            case SurfaceType.Ceiling:
                x1 = x;     y1 = y; z1 = z - h;
                x2 = x + w; y2 = y; z2 = z - h;
                x3 = x + w; y3 = y; z3 = z;
                nx = 0; ny = -1; nz = 0;
                u0 = offX;           v0 = offY;
                u1 = offX;           v1 = offY + mappedH;
                u2 = offX + mappedW; v2 = offY + mappedH;
                u3 = offX + mappedW; v3 = offY;
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
                u0 = offX + mappedW; v0 = offY;
                u1 = offX;           v1 = offY;
                u2 = offX;           v2 = offY + mappedH;
                u3 = offX + mappedW; v3 = offY + mappedH;
                break;
        }

        uint alphaFlags = (uint)(surface.Alpha ? 1 : surface.Type == SurfaceType.Wall ? 2 : 0);
        if (surface.Type == SurfaceType.Floor)
            alphaFlags |= Texture3DShader.FloorSurfaceFlag;
        uint pal = surface.PaletteIndex;

        dst[offset + 0] = new SurfaceVertex
        {
            PosX = x0, PosY = y0, PosZ = z0,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            TexX = u0, TexY = v0, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, AlphaFlags = alphaFlags
        };
        dst[offset + 1] = new SurfaceVertex
        {
            PosX = x1, PosY = y1, PosZ = z1,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            TexX = u1, TexY = v1, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, AlphaFlags = alphaFlags
        };
        dst[offset + 2] = new SurfaceVertex
        {
            PosX = x2, PosY = y2, PosZ = z2,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            TexX = u2, TexY = v2, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, AlphaFlags = alphaFlags
        };
        dst[offset + 3] = new SurfaceVertex
        {
            PosX = x3, PosY = y3, PosZ = z3,
            NormalX = nx, NormalY = ny, NormalZ = nz,
            TexX = u3, TexY = v3, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, AlphaFlags = alphaFlags
        };

        float minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        float minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        float minZ = Math.Min(Math.Min(z0, z1), Math.Min(z2, z3));
        float maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        float maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        float maxZ = Math.Max(Math.Max(z0, z1), Math.Max(z2, z3));
        bounds = new SceneBounds3D(minX, minY, minZ, maxX, maxY, maxZ);
    }

    public void Draw()
    {
        if (disposed)
            return;

        EnsureUploaded();
        if (activeQuadCount == 0)
            return;

        var gl = state.Gl;
        gl.BindVertexArray(vao);
        unsafe
        {
            gl.DrawElements(PrimitiveType.Triangles, (uint)(activeQuadCount * 6), DrawElementsType.UnsignedInt, (void*)0);
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
    }
}

/// <summary>
/// Compact GPU mesh batch for 3D billboards and billboard-floor surfaces (objects, NPCs, monsters).
/// </summary>
internal sealed class BillboardMeshBatch : IDisposable
{
    readonly State state;
    readonly List<ISurface3D> slots = new(128);
    readonly Dictionary<ISurface3D, int> surfaceToSlot = new(128);
    readonly SortedSet<int> freeSlots = new();
    BillboardVertex[] vertices = [];
    uint[] indices = [];
    uint vao;
    uint vbo;
    uint ebo;
    int activeQuadCount;
    bool dirty = true;
    bool disposed;

    public uint TextureFactor { get; set; } = 1;
    public int QuadCount => activeQuadCount;
    public int RebuildCount { get; private set; }

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
            gl.VertexAttribIPointer(3, 2, VertexAttribIType.Int, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.TexX)));

            gl.EnableVertexAttribArray(4);
            gl.VertexAttribIPointer(4, 2, VertexAttribIType.Int, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.TexEndX)));

            gl.EnableVertexAttribArray(5);
            gl.VertexAttribIPointer(5, 2, VertexAttribIType.Int, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.TexSizeX)));

            gl.EnableVertexAttribArray(6);
            gl.VertexAttribIPointer(6, 1, VertexAttribIType.UnsignedInt, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.PaletteIndex)));

            gl.EnableVertexAttribArray(7);
            gl.VertexAttribPointer(7, 1, VertexAttribPointerType.Float, false, stride, (void*)Marshal.OffsetOf<BillboardVertex>(nameof(BillboardVertex.Extrude)));
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

    public void EnsureUploaded()
    {
        if (!dirty || disposed)
            return;

        dirty = false;
        RebuildCount++;

        int quadCount = surfaceToSlot.Count;
        activeQuadCount = quadCount;
        if (quadCount == 0)
            return;

        int vertexCount = quadCount * 4;
        int indexCount = quadCount * 6;
        if (vertices.Length < vertexCount)
            vertices = new BillboardVertex[Math.Max(vertexCount, Math.Max(64, vertices.Length * 2))];
        if (indices.Length < indexCount)
            indices = new uint[Math.Max(indexCount, Math.Max(96, indices.Length * 2))];

        int vOut = 0;
        int iOut = 0;
        int factor = (int)Math.Max(1u, TextureFactor);

        for (int s = 0; s < slots.Count; s++)
        {
            var surface = slots[s];
            if (surface == null)
                continue;

            int baseVertex = vOut;
            WriteBillboardQuad(surface, factor, vertices, baseVertex);

            uint bv = (uint)baseVertex;
            indices[iOut++] = bv + 0;
            indices[iOut++] = bv + 1;
            indices[iOut++] = bv + 2;
            indices[iOut++] = bv + 3;
            indices[iOut++] = bv + 0;
            indices[iOut++] = bv + 2;

            vOut += 4;
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

    static void WriteBillboardQuad(ISurface3D surface, int textureFactor, BillboardVertex[] dst, int offset)
    {
        float cx = surface.X;
        float cy = surface.Y;
        float cz = surface.Z;
        float halfW = 0.5f * surface.Width;
        float h = surface.Height;
        bool isFloor = surface.Type == SurfaceType.BillboardFloor;
        uint orientation = isFloor ? 1u : 0u;

        int mappedW = (int)(surface.MappedTextureWidth * textureFactor);
        int mappedH = (int)(surface.MappedTextureHeight * textureFactor);
        int texW = (int)(surface.TextureWidth * textureFactor);
        int texH = (int)(surface.TextureHeight * textureFactor);
        int offX = surface.TextureAtlasOffset.X;
        int offY = surface.TextureAtlasOffset.Y;
        int endX = offX + surface.FrameCount * texW;
        int endY = offY + texH;
        uint pal = surface.PaletteIndex;
        float extrude = surface.Extrude;

        float topY = isFloor ? cy + 0.5f * h : cy;
        float bottomY = isFloor ? cy - 0.5f * h : cy - h;

        int u0 = offX, v0 = isFloor ? offY + mappedH : offY;
        int u1 = offX + mappedW, v1 = isFloor ? offY + mappedH : offY;
        int u2 = offX + mappedW, v2 = isFloor ? offY : offY + mappedH;
        int u3 = offX, v3 = isFloor ? offY : offY + mappedH;

        dst[offset + 0] = new BillboardVertex
        {
            PosX = cx - halfW, PosY = topY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation,
            TexX = u0, TexY = v0, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, Extrude = extrude
        };
        dst[offset + 1] = new BillboardVertex
        {
            PosX = cx + halfW, PosY = topY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation,
            TexX = u1, TexY = v1, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, Extrude = extrude
        };
        dst[offset + 2] = new BillboardVertex
        {
            PosX = cx + halfW, PosY = bottomY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation,
            TexX = u2, TexY = v2, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, Extrude = extrude
        };
        dst[offset + 3] = new BillboardVertex
        {
            PosX = cx - halfW, PosY = bottomY, PosZ = cz,
            CenterX = cx, CenterY = cy, CenterZ = cz,
            Orientation = orientation,
            TexX = u3, TexY = v3, TexEndX = endX, TexEndY = endY, TexSizeX = texW, TexSizeY = texH,
            PaletteIndex = pal, Extrude = extrude
        };
    }

    public void Draw()
    {
        if (disposed)
            return;

        EnsureUploaded();
        if (activeQuadCount == 0)
            return;

        var gl = state.Gl;
        gl.BindVertexArray(vao);
        unsafe
        {
            gl.DrawElements(PrimitiveType.Triangles, (uint)(activeQuadCount * 6), DrawElementsType.UnsignedInt, (void*)0);
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

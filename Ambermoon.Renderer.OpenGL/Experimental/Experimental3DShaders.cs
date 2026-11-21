#if !GLES
using Silk.NET.OpenGL;
using System;

namespace Ambermoon.Renderer.OpenGL.Experimental;

using Data;
using Render;

internal sealed class Experimental3DState
{
    public float Light { get; set; } = 1.0f;
    public float Fade { get; set; } = 1.0f;
    public bool SmoothDistantFloor { get; set; }
    public bool SmoothWalls { get; set; }
    public Texture3DFilterMode FilterMode { get; set; } = Texture3DFilterMode.EdgeDirected;
    public PaletteReplacement PaletteReplacement { get; set; }
    public uint? SkyColorIndex { get; set; }
    public Render.Color SkyReplaceColor { get; set; } = Render.Color.Transparent;
    public Render.Color FogColor { get; set; } = Render.Color.Transparent;
    public float FogDistance { get; set; } = 100.0f;
    public (float Start, float End, float Blend)? OutdoorFade { get; set; }
}

internal static class ExperimentalShaderBuilder
{
    public static string FragmentHeader(State state)
        => $"#version {state.GLSLVersionMajor}{state.GLSLVersionMinor}\n\nout vec4 outColor;\n";

    public static string VertexHeader(State state)
        => $"#version {state.GLSLVersionMajor}{state.GLSLVersionMinor}\n\n";

    public static ShaderProgram Build(State state, string fragmentSource, string vertexSource, (uint Location, string Name)[] attribBindings)
    {
        using var fragmentShader = new Shader(state, Shader.Type.Fragment, fragmentSource);
        using var vertexShader = new Shader(state, Shader.Type.Vertex, vertexSource);

        var program = new ShaderProgram(state);
        program.AttachShader(fragmentShader);
        program.AttachShader(vertexShader);

        foreach (var (location, name) in attribBindings)
            state.Gl.BindAttribLocation(program.ProgramIndex, location, name);

        program.Link(detachShaders: true);
        return program;
    }
}

/// <summary>
/// Experimental 3D floor/ceiling/wall shader sampling premultiplied RGBA8 texture-array layers
/// with hardware trilinear mipmaps, up to 16x anisotropic filtering, and selectable magnification
/// (EdgeDirected 4x, SharpBilinear via textureGrad, or Crisp nearest-mag).
/// Retires Classic's manual 8-tap floor/wall shaders while carrying surface normals for P3 lighting.
/// </summary>
internal sealed class ExperimentalSurfaceShader : IDisposable
{
    readonly State state;
    readonly ShaderProgram program;
    bool disposed;

    public ExperimentalSurfaceShader(State state)
    {
        this.state = state;

        string frag = ExperimentalShaderBuilder.FragmentHeader(state) + """
        uniform sampler2DArray texArray;
        uniform vec2 layerTexSize;
        uniform int filterMode;
        uniform float light;
        uniform vec4 fogColor;
        uniform float fogDist;
        uniform vec3 fogFadeRange;
        uniform float fogCap;
        uniform float fade;

        in vec2 varUv;
        in vec3 varNormal;
        in float dist;
        flat in float varTexLayer;
        flat in float isCutout;

        vec4 samplePremultiplied(vec2 uv, float layer, vec2 uvDx, vec2 uvDy)
        {
            if (filterMode == 1 || filterMode == 2)
            {
                // Per-axis magnification (SharpBilinear = subpixel AA; Crisp = nearest texel)
                // handing off seamlessly per axis to hardware trilinear mipmaps + 16x anisotropy
                // via original geometric derivatives so receding floors never lose aniso.
                vec2 p = uv * layerTexSize;
                vec2 s = p - 0.5;
                vec2 i = floor(s);
                vec2 f = s - i;
                vec2 deriv = abs(uvDx * layerTexSize) + abs(uvDy * layerTexSize);
                vec2 w = filterMode == 1
                    ? clamp(deriv, vec2(0.0001), vec2(1.0))
                    : mix(vec2(0.0001), vec2(1.0), smoothstep(0.85, 1.15, deriv));
                vec2 fAdj = clamp((f - 0.5) / w + 0.5, 0.0, 1.0);
                vec2 adjUv = (i + fAdj + 0.5) / layerTexSize;
                return textureGrad(texArray, vec3(adjUv, layer), uvDx, uvDy);
            }

            return textureGrad(texArray, vec3(uv, layer), uvDx, uvDy);
        }

        void main()
        {
            vec2 uvDx = dFdx(varUv);
            vec2 uvDy = dFdy(varUv);

            if (light < 0.01)
                discard;

            vec4 texel = samplePremultiplied(varUv, varTexLayer, uvDx, uvDy);

            if (isCutout > 0.5)
            {
                if (texel.a < 0.5)
                    discard;
                texel.rgb = clamp(texel.rgb / texel.a, 0.0, 1.0);
                texel.a = 1.0;
            }

            outColor = vec4(max(vec3(0.0), texel.rgb + vec3(light) - vec3(1.0)), texel.a);

            if (fogColor.a > 0.001)
            {
                float fogFactor = fogColor.a * min(fogCap, dist / fogDist);
                float originalAlpha = outColor.a;
                if (fogFadeRange.z > 0.0)
                {
                    float distantFog = fogColor.a * smoothstep(fogFadeRange.x, fogFadeRange.y, max(0.0, dist));
                    fogFactor = mix(fogFactor, distantFog, fogFadeRange.z);
                }
                outColor = outColor * (1.0 - fogFactor) + fogFactor * fogColor;
                if (fogFadeRange.z > 0.0)
                    outColor.a = mix(outColor.a, originalAlpha, fogFadeRange.z);
            }

            if (fade < 0.9999)
                outColor = outColor * fade;
        }
        """;

        string vert = ExperimentalShaderBuilder.VertexHeader(state) + """
        in vec3 position;
        in vec3 normal;
        in vec2 uv;
        in float texLayer;
        in uint flags;

        uniform mat4 projMat;
        uniform mat4 mvMat;
        uniform vec2 fogDepth;

        out vec2 varUv;
        out vec3 varNormal;
        out float dist;
        flat out float varTexLayer;
        flat out float isCutout;

        void main()
        {
            varUv = uv;
            varNormal = normal;
            varTexLayer = texLayer;
            isCutout = (flags & 1u) != 0u ? 1.0 : 0.0;
            vec4 viewPosition = mvMat * vec4(position, 1.0);
            gl_Position = projMat * viewPosition;
            dist = dot(fogDepth, viewPosition.zw);
        }
        """;

        program = ExperimentalShaderBuilder.Build(state, frag, vert,
        [
            (0, "position"),
            (1, "normal"),
            (2, "uv"),
            (3, "texLayer"),
            (4, "flags"),
        ]);
    }

    public void BindAndApply(Experimental3DState data)
    {
        program.Use();
        program.SetInputMatrix("mvMat", state.CurrentModelViewMatrix.ToArray(), true);
        program.SetInputMatrix("projMat", state.CurrentProjectionMatrix.ToArray(), true);
        program.SetInputVector2("fogDepth", state.FogDepth3D.Scale, state.FogDepth3D.Offset);
        program.SetInput("texArray", 0);
        program.SetInput("filterMode", (int)data.FilterMode);
        program.SetInput("light", data.Light);
        program.SetInput("fade", data.Fade);

        bool hasSkyKey = data.SkyColorIndex.HasValue && data.SkyColorIndex.Value <= 31u;
        program.SetInput("fogCap", hasSkyKey ? 0.8f : 1.0f);
        program.SetInputVector4("fogColor",
            data.FogColor.R / 255.0f, data.FogColor.G / 255.0f,
            data.FogColor.B / 255.0f, data.FogColor.A / 255.0f);
        program.SetInput("fogDist", data.FogDistance);
        var fadeRange = data.OutdoorFade ?? (Start: 0.0f, End: 1.0f, Blend: 0.0f);
        program.SetInputVector3("fogFadeRange", fadeRange.Start, fadeRange.End, fadeRange.Blend);
    }

    public void SetLayerTextureSize(int width, int height)
    {
        program.SetInputVector2("layerTexSize", (float)width, (float)height);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            program.Dispose();
            disposed = true;
        }
    }
}

/// <summary>
/// Experimental 3D billboard shader sampling premultiplied RGBA8 texture-array layers
/// with zero silhouette tint bleed, coverage-preserving mipmaps, and 16x anisotropy.
/// </summary>
internal sealed class ExperimentalBillboardShader : IDisposable
{
    readonly State state;
    readonly ShaderProgram program;
    bool disposed;

    public ExperimentalBillboardShader(State state)
    {
        this.state = state;

        string frag = ExperimentalShaderBuilder.FragmentHeader(state) + """
        uniform sampler2DArray texArray;
        uniform vec2 layerTexSize;
        uniform int filterMode;
        uniform float light;
        uniform vec4 fogColor;
        uniform float fogDist;
        uniform vec3 fogFadeRange;
        uniform float fogCap;
        uniform float outdoorTaper;
        uniform float fade;

        in vec2 varUv;
        in float dist;
        in float drawY;
        flat in float varTexLayer;

        vec4 samplePremultiplied(vec2 uv, float layer, vec2 uvDx, vec2 uvDy)
        {
            if (filterMode == 1 || filterMode == 2)
            {
                vec2 p = uv * layerTexSize;
                vec2 s = p - 0.5;
                vec2 i = floor(s);
                vec2 f = s - i;
                vec2 deriv = abs(uvDx * layerTexSize) + abs(uvDy * layerTexSize);
                vec2 w = filterMode == 1
                    ? clamp(deriv, vec2(0.0001), vec2(1.0))
                    : mix(vec2(0.0001), vec2(1.0), smoothstep(0.85, 1.15, deriv));
                vec2 fAdj = clamp((f - 0.5) / w + 0.5, 0.0, 1.0);
                vec2 adjUv = (i + fAdj + 0.5) / layerTexSize;
                return textureGrad(texArray, vec3(adjUv, layer), uvDx, uvDy);
            }

            return textureGrad(texArray, vec3(uv, layer), uvDx, uvDy);
        }

        void main()
        {
            vec2 uvDx = dFdx(varUv);
            vec2 uvDy = dFdy(varUv);

            if (light < 0.01)
                discard;

            vec4 texel = samplePremultiplied(varUv, varTexLayer, uvDx, uvDy);
            if (texel.a < 0.5)
                discard;

            vec3 straightRgb = clamp(texel.rgb / texel.a, 0.0, 1.0);
            outColor = vec4(max(vec3(0.0), straightRgb + vec3(light) - vec3(1.0)), 1.0);

            if (fogColor.a > 0.001)
            {
                float fogFactor = fogColor.a * (outdoorTaper > 0.5 && drawY > 0.0
                    ? min(fogCap, dist / (fogDist * (1.0 + 2.5 * drawY)))
                    : min(fogCap, dist / fogDist));
                float originalAlpha = outColor.a;
                if (fogFadeRange.z > 0.0)
                {
                    float fadeDistance = max(0.0, dist - clamp(drawY, 0.0, 1.0));
                    float distantFog = fogColor.a * smoothstep(fogFadeRange.x, fogFadeRange.y, fadeDistance);
                    fogFactor = mix(fogFactor, distantFog, fogFadeRange.z);
                }
                outColor = outColor * (1.0 - fogFactor) + fogFactor * fogColor;
                if (fogFadeRange.z > 0.0)
                    outColor.a = mix(outColor.a, originalAlpha, fogFadeRange.z);
            }

            if (fade < 0.9999)
                outColor = outColor * fade;
        }
        """;

        string vert = ExperimentalShaderBuilder.VertexHeader(state) + """
        in vec3 position;
        in vec3 center;
        in uint orientation;
        in vec2 uv;
        in float texLayer;
        in float extrude;

        uniform mat4 projMat;
        uniform mat4 mvMat;
        uniform vec2 fogDepth;

        out vec2 varUv;
        out float dist;
        out float drawY;
        flat out float varTexLayer;

        void main()
        {
            vec3 offset = (position - center);
            vec4 localPos = mvMat * vec4(center, 1.0);
            if (orientation == 0u)
            {
                localPos += vec4(offset.xy, extrude, 0.0);
            }
            else
            {
                vec4 rotatedOffset = -1.0 * mvMat * vec4(offset.x, 0.0, offset.y, 0.0);
                localPos += vec4(rotatedOffset.x, 0.0, rotatedOffset.z, 0.0);
            }
            varUv = uv;
            varTexLayer = texLayer;
            gl_Position = projMat * localPos;
            dist = dot(fogDepth, localPos.zw);
            drawY = localPos.y;
        }
        """;

        program = ExperimentalShaderBuilder.Build(state, frag, vert,
        [
            (0, "position"),
            (1, "center"),
            (2, "orientation"),
            (3, "uv"),
            (4, "texLayer"),
            (5, "extrude"),
        ]);
    }

    public void BindAndApply(Experimental3DState data)
    {
        program.Use();
        program.SetInputMatrix("mvMat", state.CurrentModelViewMatrix.ToArray(), true);
        program.SetInputMatrix("projMat", state.CurrentProjectionMatrix.ToArray(), true);
        program.SetInputVector2("fogDepth", state.FogDepth3D.Scale, state.FogDepth3D.Offset);
        program.SetInput("texArray", 0);
        program.SetInput("filterMode", (int)data.FilterMode);
        program.SetInput("light", data.Light);
        program.SetInput("fade", data.Fade);

        bool hasSkyKey = data.SkyColorIndex.HasValue && data.SkyColorIndex.Value <= 31u;
        program.SetInput("fogCap", hasSkyKey ? 0.8f : 1.0f);
        program.SetInput("outdoorTaper", hasSkyKey ? 1.0f : 0.0f);
        program.SetInputVector4("fogColor",
            data.FogColor.R / 255.0f, data.FogColor.G / 255.0f,
            data.FogColor.B / 255.0f, data.FogColor.A / 255.0f);
        program.SetInput("fogDist", data.FogDistance);
        var fadeRange = data.OutdoorFade ?? (Start: 0.0f, End: 1.0f, Blend: 0.0f);
        program.SetInputVector3("fogFadeRange", fadeRange.Start, fadeRange.End, fadeRange.Blend);
    }

    public void SetLayerTextureSize(int width, int height)
    {
        program.SetInputVector2("layerTexSize", (float)width, (float)height);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            program.Dispose();
            disposed = true;
        }
    }
}

/// <summary>
/// Fullscreen-triangle blit shader for compositing the offscreen 3D target back to the
/// active destination framebuffer (default FBO or effect FBO).
/// </summary>
internal sealed class ExperimentalCompositeShader : IDisposable
{
    readonly ShaderProgram program;
    bool disposed;

    public ExperimentalCompositeShader(State state)
    {
        string frag = ExperimentalShaderBuilder.FragmentHeader(state) + """
        uniform sampler2D sceneColor;
        in vec2 varTexCoord;

        void main()
        {
            outColor = textureLod(sceneColor, varTexCoord, 0.0);
        }
        """;

        string vert = ExperimentalShaderBuilder.VertexHeader(state) + """
        out vec2 varTexCoord;

        void main()
        {
            vec2 pos = vec2((gl_VertexID == 1) ? 3.0 : -1.0, (gl_VertexID == 2) ? 3.0 : -1.0);
            gl_Position = vec4(pos, 0.0, 1.0);
            varTexCoord = 0.5 * pos + 0.5;
        }
        """;

        program = ExperimentalShaderBuilder.Build(state, frag, vert, []);
    }

    public void Bind()
    {
        program.Use();
        program.SetInput("sceneColor", 0);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            program.Dispose();
            disposed = true;
        }
    }
}
#endif

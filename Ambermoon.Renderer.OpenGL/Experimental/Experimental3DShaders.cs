#if !GLES
using Silk.NET.OpenGL;
using System;

namespace Ambermoon.Renderer.OpenGL.Experimental;

using Data;

internal sealed class Experimental3DState
{
    public float Light { get; set; } = 1.0f;
    public float Fade { get; set; } = 1.0f;
    public bool SmoothDistantFloor { get; set; }
    public bool SmoothWalls { get; set; }
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
/// Experimental 3D floor/ceiling/wall shader. In P1 this delivers pixel-exact parity with
/// Classic Texture3DShader while carrying surface normals for P3 atmospheric lighting.
/// </summary>
internal sealed class ExperimentalSurfaceShader : IDisposable
{
    readonly State state;
    readonly ShaderProgram program;
    bool disposed;

    public ExperimentalSurfaceShader(State state)
    {
        this.state = state;

        string frag = ExperimentalShaderBuilder.FragmentHeader(state) + $$"""
        uniform sampler2D sampler;
        uniform sampler2D palette;
        uniform uvec2 atlasSize;
        uniform float light;
        uniform float palCount;
        uniform vec4 palReplace[16];
        uniform float useReplace;
        uniform float skyColorIndex;
        uniform vec4 skyColorReplace;
        uniform vec4 fogColor;
        uniform float fogDist;
        uniform vec3 fogFadeRange;
        uniform float fade;
        uniform float smoothDistantFloor;
        uniform float smoothWalls;

        in vec2 varTexCoord;
        in vec3 varNormal;
        in float dist;
        flat in float palIndex;
        flat in vec2 varTextureEndCoord;
        flat in vec2 varTextureSize;
        flat in float alphaEnabled;
        flat in float floorSurface;

        vec4 paletteColor(float index)
        {
            return useReplace > 0.5 && index < 15.5 ? palReplace[int(index + 0.5)]
                : textureLod(palette, vec2((index + 0.5) / 32.0, (palIndex + 0.5) / palCount), 0.0);
        }

        vec4 wallSample(ivec2 texel, ivec2 first, ivec2 last)
        {
            float index = texelFetch(sampler, clamp(texel, first, last), 0).r * 255.0;
            if (abs(index - skyColorIndex) < 0.5)
                return vec4(0.0);
            vec4 color = paletteColor(index);
            if (color.a < 0.5)
                return vec4(0.0);
            return vec4(max(vec3(0.0), color.rgb + vec3(light) - vec3(1.0)), 1.0);
        }

        vec3 smoothWallColor(vec3 centerColor, vec2 dx, vec2 dy)
        {
            vec2 atlas = vec2(atlasSize);
            ivec2 last = ivec2(round(varTextureEndCoord * atlas)) - ivec2(1);
            ivec2 first = last + ivec2(1) - ivec2(round(varTextureSize * atlas));
            vec2 position = varTexCoord * atlas;
            float distant = smoothstep(1.0, 2.0, max(length(dx), length(dy)));
            vec3 closeColor = centerColor;
            if (distant < 1.0)
            {
                vec2 fromCenter = fract(position) - 0.5;
                vec2 span = max(abs(dx) + abs(dy), vec2(0.000001));
                vec2 w = clamp(0.5 + (abs(fromCenter) - 0.5) / span, 0.0, 0.5);
                if (w.x > 0.0 || w.y > 0.0)
                {
                    ivec2 here = ivec2(floor(position));
                    ivec2 neighbor = ivec2(2.0 * step(vec2(0.0), fromCenter) - 1.0);
                    vec4 sum = (1.0 - w.x) * (1.0 - w.y) * vec4(centerColor, 1.0);
                    if (w.x > 0.0)
                        sum += w.x * (1.0 - w.y) * wallSample(here + ivec2(neighbor.x, 0), first, last);
                    if (w.y > 0.0)
                        sum += (1.0 - w.x) * w.y * wallSample(here + ivec2(0, neighbor.y), first, last);
                    if (w.x > 0.0 && w.y > 0.0)
                        sum += w.x * w.y * wallSample(here + neighbor, first, last);
                    closeColor = sum.rgb / sum.a;
                }
            }
            if (distant <= 0.0)
                return closeColor;

            const vec2 taps[8] = vec2[8](vec2(-7,-3), vec2(-5,7), vec2(-3,-1), vec2(-1,-5),
                                         vec2(1,5), vec2(3,1), vec2(5,-7), vec2(7,3));
            vec4 sum = vec4(0.0);
            for (int i = 0; i < 8; ++i)
            {
                vec2 samplePosition = position + (taps[i].x * dx + taps[i].y * dy) / 16.0;
                sum += wallSample(ivec2(floor(samplePosition)), first, last);
            }
            return mix(closeColor, sum.a > 0.0 ? sum.rgb / sum.a : centerColor, distant);
        }

        vec3 sampleFloorColor(vec2 coord)
        {
            vec2 first = varTextureEndCoord - varTextureSize;
            vec2 halfTexel = vec2(0.5) / vec2(atlasSize);
            vec2 wrapped = first + mod(coord - first, varTextureSize);
            wrapped = clamp(wrapped, first + halfTexel, varTextureEndCoord - halfTexel);
            float index = textureLod(sampler, wrapped, 0.0).r * 255.0;
            vec4 color = paletteColor(index);
            if (abs(index - skyColorIndex) < 0.5)
                return skyColorReplace.rgb;
            return max(vec3(0.0), color.rgb + vec3(light) - vec3(1.0));
        }

        void main()
        {
            vec2 texCoordDx = dFdx(varTexCoord);
            vec2 texCoordDy = dFdy(varTexCoord);
            outColor = vec4(0.0);
            vec2 realTexCoord = varTexCoord;
            if (realTexCoord.x >= varTextureEndCoord.x)
                realTexCoord.x -= floor((varTextureSize.x + realTexCoord.x - varTextureEndCoord.x) / varTextureSize.x) * varTextureSize.x;
            if (realTexCoord.y >= varTextureEndCoord.y)
                realTexCoord.y -= floor((varTextureSize.y + realTexCoord.y - varTextureEndCoord.y) / varTextureSize.y) * varTextureSize.y;
            float colorIndex = textureLod(sampler, realTexCoord, 0.0).r * 255.0;
            vec4 pixelColor = paletteColor(colorIndex);

            if (alphaEnabled > 0.5 && alphaEnabled < 1.5 && (colorIndex < 0.5 || pixelColor.a < 0.5) || light < 0.01)
                discard;
            else if (abs(colorIndex - skyColorIndex) < 0.5)
            {
                if (alphaEnabled > 1.5)
                    discard;
                else
                    outColor = skyColorReplace;
            }
            else
                outColor = vec4(max(vec3(0.0), pixelColor.rgb + vec3(light) - vec3(1.0)), pixelColor.a);

            if (smoothDistantFloor > 0.5 && floorSurface > 0.5 && alphaEnabled < 0.5)
            {
                float footprint = max(length(texCoordDx * vec2(atlasSize)), length(texCoordDy * vec2(atlasSize)));
                float filterAmount = smoothstep(1.0, 2.0, footprint);
                if (filterAmount > 0.0)
                {
                    vec2 dx = 1.5 * texCoordDx;
                    vec2 dy = 1.5 * texCoordDy;
                    vec3 filtered = sampleFloorColor(varTexCoord - 0.4375 * dx - 0.1875 * dy)
                        + sampleFloorColor(varTexCoord - 0.3125 * dx + 0.4375 * dy)
                        + sampleFloorColor(varTexCoord - 0.1875 * dx - 0.0625 * dy)
                        + sampleFloorColor(varTexCoord - 0.0625 * dx - 0.3125 * dy)
                        + sampleFloorColor(varTexCoord + 0.0625 * dx + 0.3125 * dy)
                        + sampleFloorColor(varTexCoord + 0.1875 * dx + 0.0625 * dy)
                        + sampleFloorColor(varTexCoord + 0.3125 * dx - 0.4375 * dy)
                        + sampleFloorColor(varTexCoord + 0.4375 * dx + 0.1875 * dy);
                    outColor.rgb = mix(outColor.rgb, filtered * 0.125, filterAmount);
                }
            }

            if (smoothWalls > 0.5 && alphaEnabled > 1.5 && pixelColor.a >= 0.5)
                outColor.rgb = smoothWallColor(outColor.rgb, texCoordDx * vec2(atlasSize), texCoordDy * vec2(atlasSize));

            if (fogColor.a > 0.001)
            {
                float fogFactor = fogColor.a * min(skyColorIndex < 31.5 ? 0.8 : 1.0, dist / fogDist);
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

        string vert = ExperimentalShaderBuilder.VertexHeader(state) + $$"""
        in vec3 position;
        in vec3 normal;
        in ivec2 texCoord;
        in ivec2 texEndCoord;
        in ivec2 texSize;
        in uint paletteIndex;
        in uint alpha;

        uniform uvec2 atlasSize;
        uniform mat4 projMat;
        uniform mat4 mvMat;
        uniform vec2 fogDepth;

        out vec2 varTexCoord;
        out vec3 varNormal;
        out float dist;
        flat out float palIndex;
        flat out vec2 varTextureEndCoord;
        flat out vec2 varTextureSize;
        flat out float alphaEnabled;
        flat out float floorSurface;

        void main()
        {
            vec2 atlasFactor = vec2(1.0 / float(atlasSize.x), 1.0 / float(atlasSize.y));
            varTexCoord = atlasFactor * vec2(texCoord.x, texCoord.y);
            varNormal = normal;
            palIndex = float(paletteIndex);
            varTextureEndCoord = atlasFactor * vec2(texEndCoord.x, texEndCoord.y);
            varTextureSize = atlasFactor * vec2(texSize.x, texSize.y);
            alphaEnabled = float(alpha & 3u);
            floorSurface = (alpha & {{Texture3DShader.FloorSurfaceFlag}}u) != 0u ? 1.0 : 0.0;
            vec4 viewPosition = mvMat * vec4(position, 1.0);
            gl_Position = projMat * viewPosition;
            dist = dot(fogDepth, viewPosition.zw);
        }
        """;

        program = ExperimentalShaderBuilder.Build(state, frag, vert,
        [
            (0, "position"),
            (1, "normal"),
            (2, "texCoord"),
            (3, "texEndCoord"),
            (4, "texSize"),
            (5, "paletteIndex"),
            (6, "alpha"),
        ]);
    }

    public void BindAndApply(Experimental3DState data, uint atlasWidth, uint atlasHeight, int paletteCount)
    {
        program.Use();
        program.SetInputMatrix("mvMat", state.CurrentModelViewMatrix.ToArray(), true);
        program.SetInputMatrix("projMat", state.CurrentProjectionMatrix.ToArray(), true);
        program.SetInputVector2("fogDepth", state.FogDepth3D.Scale, state.FogDepth3D.Offset);
        program.SetInput("sampler", 0);
        program.SetInput("palette", 1);
        program.SetInputVector2("atlasSize", atlasWidth, atlasHeight);
        program.SetInput("palCount", (float)paletteCount);
        program.SetInput("light", data.Light);
        program.SetInput("fade", data.Fade);
        program.SetInput("smoothDistantFloor", data.SmoothDistantFloor ? 1.0f : 0.0f);
        program.SetInput("smoothWalls", data.SmoothWalls ? 1.0f : 0.0f);

        if (data.PaletteReplacement == null)
        {
            program.SetInput("useReplace", 0.0f);
        }
        else
        {
            program.SetInputColorArray("palReplace", data.PaletteReplacement.ColorData);
            program.SetInput("useReplace", 1.0f);
        }

        program.SetInput("skyColorIndex", data.SkyColorIndex == null ? 32.0f : (float)data.SkyColorIndex.Value);
        if (data.SkyReplaceColor != null)
        {
            program.SetInputVector4("skyColorReplace",
                data.SkyReplaceColor.R / 255.0f, data.SkyReplaceColor.G / 255.0f,
                data.SkyReplaceColor.B / 255.0f, data.SkyReplaceColor.A / 255.0f);
        }

        program.SetInputVector4("fogColor",
            data.FogColor.R / 255.0f, data.FogColor.G / 255.0f,
            data.FogColor.B / 255.0f, data.FogColor.A / 255.0f);
        program.SetInput("fogDist", data.FogDistance);
        var fadeRange = data.OutdoorFade ?? (Start: 0.0f, End: 1.0f, Blend: 0.0f);
        program.SetInputVector3("fogFadeRange", fadeRange.Start, fadeRange.End, fadeRange.Blend);
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
/// Experimental 3D billboard shader. In P1 this delivers pixel-exact parity with
/// Classic Billboard3DShader.
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
        uniform sampler2D sampler;
        uniform sampler2D palette;
        uniform float light;
        uniform float palCount;
        uniform vec4 palReplace[16];
        uniform float useReplace;
        uniform float skyColorIndex;
        uniform vec4 skyColorReplace;
        uniform vec4 fogColor;
        uniform float fogDist;
        uniform vec3 fogFadeRange;
        uniform float fade;

        in vec2 varTexCoord;
        in float dist;
        in float drawY;
        flat in float palIndex;
        flat in vec2 varTextureEndCoord;
        flat in vec2 varTextureSize;

        void main()
        {
            outColor = vec4(0.0);
            vec2 realTexCoord = varTexCoord;
            if (realTexCoord.x >= varTextureEndCoord.x)
                realTexCoord.x -= floor((varTextureSize.x + realTexCoord.x - varTextureEndCoord.x) / varTextureSize.x) * varTextureSize.x;
            if (realTexCoord.y >= varTextureEndCoord.y)
                realTexCoord.y -= floor((varTextureSize.y + realTexCoord.y - varTextureEndCoord.y) / varTextureSize.y) * varTextureSize.y;
            float colorIndex = textureLod(sampler, realTexCoord, 0.0).r * 255.0;
            vec4 pixelColor = useReplace > 0.5 && colorIndex < 15.5 ? palReplace[int(colorIndex + 0.5)]
                : textureLod(palette, vec2((colorIndex + 0.5) / 32.0, (palIndex + 0.5) / palCount), 0.0);

            if (colorIndex < 0.5 || pixelColor.a < 0.5 || light < 0.01)
                discard;
            else if (abs(colorIndex - skyColorIndex) < 0.001)
                outColor = skyColorReplace;
            else
                outColor = vec4(pixelColor.rgb + vec3(light) - vec3(1.0), pixelColor.a);

            if (fogColor.a > 0.001)
            {
                float fogFactor = fogColor.a * (skyColorIndex < 31.5 && drawY > 0.0
                    ? min(skyColorIndex < 31.5 ? 0.8 : 1.0, dist / (fogDist * (1.0 + 2.5 * drawY)))
                    : min(skyColorIndex < 31.5 ? 0.8 : 1.0, dist / fogDist));
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
        in ivec2 texCoord;
        in ivec2 texEndCoord;
        in ivec2 texSize;
        in uint paletteIndex;
        in float extrude;

        uniform uvec2 atlasSize;
        uniform mat4 projMat;
        uniform mat4 mvMat;
        uniform vec2 fogDepth;

        out vec2 varTexCoord;
        out float dist;
        out float drawY;
        flat out float palIndex;
        flat out vec2 varTextureEndCoord;
        flat out vec2 varTextureSize;

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
            vec2 atlasFactor = vec2(1.0 / float(atlasSize.x), 1.0 / float(atlasSize.y));
            varTexCoord = atlasFactor * vec2(texCoord.x, texCoord.y);
            palIndex = float(paletteIndex);
            varTextureEndCoord = atlasFactor * vec2(texEndCoord.x, texEndCoord.y);
            varTextureSize = atlasFactor * vec2(texSize.x, texSize.y);
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
            (3, "texCoord"),
            (4, "texEndCoord"),
            (5, "texSize"),
            (6, "paletteIndex"),
            (7, "extrude"),
        ]);
    }

    public void BindAndApply(Experimental3DState data, uint atlasWidth, uint atlasHeight, int paletteCount)
    {
        program.Use();
        program.SetInputMatrix("mvMat", state.CurrentModelViewMatrix.ToArray(), true);
        program.SetInputMatrix("projMat", state.CurrentProjectionMatrix.ToArray(), true);
        program.SetInputVector2("fogDepth", state.FogDepth3D.Scale, state.FogDepth3D.Offset);
        program.SetInput("sampler", 0);
        program.SetInput("palette", 1);
        program.SetInputVector2("atlasSize", atlasWidth, atlasHeight);
        program.SetInput("palCount", (float)paletteCount);
        program.SetInput("light", data.Light);
        program.SetInput("fade", data.Fade);

        if (data.PaletteReplacement == null)
        {
            program.SetInput("useReplace", 0.0f);
        }
        else
        {
            program.SetInputColorArray("palReplace", data.PaletteReplacement.ColorData);
            program.SetInput("useReplace", 1.0f);
        }

        program.SetInput("skyColorIndex", data.SkyColorIndex == null ? 32.0f : (float)data.SkyColorIndex.Value);
        if (data.SkyReplaceColor != null)
        {
            program.SetInputVector4("skyColorReplace",
                data.SkyReplaceColor.R / 255.0f, data.SkyReplaceColor.G / 255.0f,
                data.SkyReplaceColor.B / 255.0f, data.SkyReplaceColor.A / 255.0f);
        }

        program.SetInputVector4("fogColor",
            data.FogColor.R / 255.0f, data.FogColor.G / 255.0f,
            data.FogColor.B / 255.0f, data.FogColor.A / 255.0f);
        program.SetInput("fogDist", data.FogDistance);
        var fadeRange = data.OutdoorFade ?? (Start: 0.0f, End: 1.0f, Blend: 0.0f);
        program.SetInputVector3("fogFadeRange", fadeRange.Start, fadeRange.End, fadeRange.Blend);
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
/// active destination framebuffer (default FBO or effect FBO). In P1 this is an exact
/// 1:1 copy; in P3/P5 this is where 3D-only colour grading, dithering, and optional warp hook in.
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

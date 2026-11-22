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
    public Lighting3DMode LightingMode { get; set; } = Lighting3DMode.Atmospheric;
    public Scene3DEnvironment Environment { get; set; } = Scene3DEnvironment.Default;
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
/// with hardware trilinear mipmaps, 16x anisotropy, selectable P2 texture filter modes, and
/// P3 atmospheric lighting (hemisphere/directional light, player radial falloff, up to 16 coloured
/// point lights, baked corner/contact AO, split-toning, and height-modulated exponential fog).
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
        uniform sampler2D aoShadowMap;
        uniform vec2 layerTexSize;
        uniform int filterMode;
        uniform int lightingMode;
        uniform float light;
        uniform vec4 fogColor;
        uniform float fogDist;
        uniform vec3 fogFadeRange;
        uniform float fogCap;
        uniform float fade;

        uniform vec4 aoMapBounds;
        uniform vec3 sunDir;
        uniform vec3 sunColor;
        uniform vec3 skyAmbient;
        uniform vec3 groundAmbient;
        uniform vec4 playerLightPosRadius;
        uniform vec4 playerLightColorIntensity;
        uniform vec4 pointLightPosRadius[16];
        uniform vec4 pointLightColorIntensity[16];
        uniform int pointLightCount;
        uniform vec3 shadowTint;
        uniform vec3 highlightTint;
        uniform vec4 envParams;
        uniform float fogExpScale;

        in vec2 varUv;
        in vec3 varNormal;
        in vec3 varWorldPos;
        in float dist;
        in float varAo;
        flat in float varTexLayer;
        flat in float isCutout;
        flat in float surfaceKind;

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

            if (isCutout > 0.5)
            {
                if (texel.a < 0.5)
                    discard;
                texel.rgb = clamp(texel.rgb / texel.a, 0.0, 1.0);
                texel.a = 1.0;
            }

            float localGlow = 0.0;

            if (lightingMode == 1)
            {
                outColor = vec4(max(vec3(0.0), texel.rgb + vec3(light) - vec3(1.0)), texel.a);
            }
            else
            {
                vec2 aoUv = clamp((varWorldPos.xz - aoMapBounds.xy) * aoMapBounds.zw, vec2(0.001), vec2(0.999));
                vec2 rgAo = textureLod(aoShadowMap, aoUv, 0.0).rg;
                float aoRaw = surfaceKind > 1.5
                    ? mix(1.0, rgAo.r, 0.85)
                    : surfaceKind > 0.5
                        ? (rgAo.r * rgAo.g)
                        : varAo;
                float ao = mix(1.0, aoRaw, envParams.x);

                vec3 N = normalize(varNormal);
                vec3 hemi = mix(groundAmbient, skyAmbient, 0.5 + 0.5 * N.y);
                float ndotl = clamp(dot(N, sunDir) * 0.55 + 0.45, 0.0, 1.0);
                vec3 lit = (hemi + sunColor * ndotl) * ao;

                if (playerLightColorIntensity.a > 0.001)
                {
                    vec3 toPlayer = playerLightPosRadius.xyz - varWorldPos;
                    float dP = length(toPlayer);
                    float rP = max(playerLightPosRadius.w, 0.001);
                    if (dP < rP)
                    {
                        vec3 Lp = toPlayer / max(dP, 0.0001);
                        float nP = surfaceKind > 0.5 ? max(0.42, dot(N, Lp) * 0.58 + 0.42) : max(0.32, dot(N, Lp) * 0.68 + 0.32);
                        float uP = dP / rP;
                        float qP = max(0.0, 1.0 - uP * uP);
                        float falloff = (qP * qP) / (1.0 + 0.11 * dP * dP);
                        float contrib = playerLightColorIntensity.a * falloff * nP;
                        lit += playerLightColorIntensity.rgb * contrib;
                    }
                }

                vec3 pointAccum = vec3(0.0);
                for (int i = 0; i < pointLightCount; ++i)
                {
                    vec4 pr = pointLightPosRadius[i];
                    vec4 ci = pointLightColorIntensity[i];
                    if (ci.a <= 0.001)
                        continue;
                    vec3 toL = pr.xyz - varWorldPos;
                    float d = length(toL);
                    float r = max(pr.w, 0.001);
                    if (d < r)
                    {
                        vec3 L = toL / max(d, 0.0001);
                        float nL = max(0.28, dot(N, L) * 0.68 + 0.32);
                        float u = d / r;
                        float q = max(0.0, 1.0 - u * u);
                        float falloff = (q * q) / (1.0 + 0.22 * d * d);
                        float contrib = ci.a * falloff * nL;
                        pointAccum += ci.rgb * contrib * (0.50 + 0.50 * ao);
                        localGlow += contrib;
                    }
                }
                lit += pointAccum / (vec3(1.0) + pointAccum * 0.65);

                vec3 baseColor = texel.rgb * lit;

                // Preserve strictly vivid luminous fire sconces / night windows without triggering on cave tan or wood.
                if (surfaceKind < 0.5 && groundAmbient.g < 0.72 &&
                    ((texel.r > 0.92 && texel.b < 0.18 && texel.g > 0.32 && texel.g <= 0.76)
                    || (texel.r > 0.95 && texel.g > 0.80 && texel.b < 0.32)))
                {
                    baseColor = max(baseColor, texel.rgb * 0.98);
                    localGlow = max(localGlow, 0.45);
                }

                float luma = dot(baseColor, vec3(0.299, 0.587, 0.114));
                vec3 grade = mix(shadowTint, highlightTint, smoothstep(0.12, 0.82, clamp(luma, 0.0, 1.0)));
                vec3 graded = mix(baseColor, baseColor * grade, envParams.y);
                outColor = vec4(max(vec3(0.0), graded), texel.a);
            }

            if (fogColor.a > 0.001)
            {
                float fogFactor;
                float originalAlpha = outColor.a;

                if (lightingMode == 1)
                {
                    fogFactor = fogColor.a * min(fogCap, dist / fogDist);
                    if (fogFadeRange.z > 0.0)
                    {
                        float distantFog = fogColor.a * smoothstep(fogFadeRange.x, fogFadeRange.y, max(0.0, dist));
                        fogFactor = mix(fogFactor, distantFog, fogFadeRange.z);
                    }
                    outColor = outColor * (1.0 - fogFactor) + fogFactor * fogColor;
                    if (fogFadeRange.z > 0.0)
                        outColor.a = mix(outColor.a, originalAlpha, fogFadeRange.z);
                }
                else
                {
                    float normD = max(0.0, dist) / max(0.001, fogDist);
                    float rawExp = fogExpScale > 0.5
                        ? clamp(1.0 - exp(-1.95 * pow(normD, 1.75)) + 0.18 * smoothstep(0.88, 1.12, normD), 0.0, 1.0)
                        : (1.0 - exp(-1.55 * pow(normD, 1.25)));
                    fogFactor = fogColor.a * min(fogCap, rawExp);
                    if (fogFadeRange.z > 0.0)
                    {
                        float linT = clamp((max(0.0, dist) - fogFadeRange.x) / max(0.001, fogFadeRange.y - fogFadeRange.x), 0.0, 1.0);
                        float expT = 1.0 - exp(-2.15 * linT * linT);
                        float distantFog = fogColor.a * mix(linT, expT, 0.55);
                        float heightAtten = 1.0 / (1.0 + max(0.0, varWorldPos.y) * envParams.z);
                        distantFog *= mix(1.0, heightAtten, 0.45);
                        fogFactor = mix(fogFactor, distantFog, fogFadeRange.z);
                    }
                    fogFactor *= 1.0 - min(0.42, localGlow * envParams.w);
                    outColor.rgb = mix(outColor.rgb, fogColor.rgb, fogFactor);
                    outColor.a = originalAlpha;
                }
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
        in float ao;

        uniform mat4 projMat;
        uniform mat4 mvMat;
        uniform vec2 fogDepth;

        out vec2 varUv;
        out vec3 varNormal;
        out vec3 varWorldPos;
        out float dist;
        out float varAo;
        flat out float varTexLayer;
        flat out float isCutout;
        flat out float surfaceKind;

        void main()
        {
            varUv = uv;
            varNormal = normal;
            varWorldPos = position;
            varAo = ao;
            varTexLayer = texLayer;
            isCutout = (flags & 1u) != 0u ? 1.0 : 0.0;
            surfaceKind = (flags & 2u) != 0u ? 1.0 : ((flags & 4u) != 0u ? 2.0 : 0.0);
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
            (5, "ao"),
        ]);
    }

    public void BindAndApply(Experimental3DState data, in FrameLightingUniforms lighting, SceneLightingEngine engine)
    {
        program.Use();
        program.SetInputMatrix("mvMat", state.CurrentModelViewMatrix.ToArray(), true);
        program.SetInputMatrix("projMat", state.CurrentProjectionMatrix.ToArray(), true);
        program.SetInputVector2("fogDepth", state.FogDepth3D.Scale, state.FogDepth3D.Offset);
        program.SetInput("texArray", 0);
        program.SetInput("aoShadowMap", 1);
        program.SetInput("filterMode", (int)data.FilterMode);
        program.SetInput("lightingMode", (int)data.LightingMode);
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

        if (data.LightingMode == Lighting3DMode.Atmospheric)
        {
            var bounds = engine.AoMapBounds;
            program.SetInputVector4("aoMapBounds", bounds.X, bounds.Y, bounds.Z, bounds.W);
            program.SetInputVector3("sunDir", lighting.SunDir.X, lighting.SunDir.Y, lighting.SunDir.Z);
            program.SetInputVector3("sunColor", lighting.SunColor.X, lighting.SunColor.Y, lighting.SunColor.Z);
            program.SetInputVector3("skyAmbient", lighting.SkyAmbient.X, lighting.SkyAmbient.Y, lighting.SkyAmbient.Z);
            program.SetInputVector3("groundAmbient", lighting.GroundAmbient.X, lighting.GroundAmbient.Y, lighting.GroundAmbient.Z);
            program.SetInputVector4("playerLightPosRadius", lighting.PlayerLightPosRadius.X, lighting.PlayerLightPosRadius.Y, lighting.PlayerLightPosRadius.Z, lighting.PlayerLightPosRadius.W);
            program.SetInputVector4("playerLightColorIntensity", lighting.PlayerLightColorIntensity.X, lighting.PlayerLightColorIntensity.Y, lighting.PlayerLightColorIntensity.Z, lighting.PlayerLightColorIntensity.W);
            program.SetInputVector3("shadowTint", lighting.ShadowTint.X, lighting.ShadowTint.Y, lighting.ShadowTint.Z);
            program.SetInputVector3("highlightTint", lighting.HighlightTint.X, lighting.HighlightTint.Y, lighting.HighlightTint.Z);
            program.SetInputVector4("envParams", lighting.EnvParams.X, lighting.EnvParams.Y, lighting.EnvParams.Z, lighting.EnvParams.W);
            program.SetInput("fogExpScale", lighting.FogExpDensityScale);
            program.SetInput("pointLightCount", lighting.ActivePointLightCount);
            if (lighting.ActivePointLightCount > 0)
            {
                uint c = (uint)lighting.ActivePointLightCount;
                program.SetInputVector4Array("pointLightPosRadius", lighting.PointLightPosRadius, c);
                program.SetInputVector4Array("pointLightColorIntensity", lighting.PointLightColorIntensity, c);
            }
        }
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
/// Experimental 3D billboard shader supporting hardware mipmaps/anisotropy and P3 atmospheric
/// lighting, self-luminous flame/magic pixel preservation, and grounded contact softening.
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
        uniform int lightingMode;
        uniform float light;
        uniform vec4 fogColor;
        uniform float fogDist;
        uniform vec3 fogFadeRange;
        uniform float fogCap;
        uniform float outdoorTaper;
        uniform float fade;

        uniform vec3 cameraWorldPos;
        uniform vec3 sunDir;
        uniform vec3 sunColor;
        uniform vec3 skyAmbient;
        uniform vec3 groundAmbient;
        uniform vec4 playerLightPosRadius;
        uniform vec4 playerLightColorIntensity;
        uniform vec4 pointLightPosRadius[16];
        uniform vec4 pointLightColorIntensity[16];
        uniform int pointLightCount;
        uniform vec3 shadowTint;
        uniform vec3 highlightTint;
        uniform vec4 envParams;
        uniform float fogExpScale;
        uniform float wallHeight;

        in vec2 varUv;
        in vec3 varWorldPos;
        in float dist;
        in float drawY;
        flat in float varTexLayer;
        flat in float isFloorBillboard;

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
            float localGlow = 0.0;

            if (lightingMode == 1)
            {
                outColor = vec4(max(vec3(0.0), straightRgb + vec3(light) - vec3(1.0)), 1.0);
            }
            else
            {
                bool isWarmGlow = (straightRgb.r > 0.91 && straightRgb.b < 0.20 && straightRgb.g <= 0.75)
                    || (straightRgb.r > 0.95 && straightRgb.g > 0.80 && straightRgb.b < 0.34);
                bool isMagicGlow = (straightRgb.b > 0.88 && (straightRgb.b - straightRgb.r) > 0.35 && straightRgb.g > 0.50)
                    || (straightRgb.r > 0.86 && straightRgb.b > 0.86 && (straightRgb.r - straightRgb.g) > 0.34);
                bool selfLit = isWarmGlow || isMagicGlow;

                vec3 N = isFloorBillboard > 0.5
                    ? vec3(0.0, 1.0, 0.0)
                    : normalize(vec3(cameraWorldPos.x - varWorldPos.x, 0.38, cameraWorldPos.z - varWorldPos.z));

                float baseContact = (isFloorBillboard < 0.5 && !selfLit)
                    ? mix(1.0 - 0.16 * envParams.x, 1.0, clamp(varWorldPos.y / max(0.32 * wallHeight, 0.08), 0.0, 1.0))
                    : 1.0;

                vec3 hemi = mix(groundAmbient, skyAmbient, 0.5 + 0.5 * N.y);
                float ndotl = clamp(dot(N, sunDir) * 0.45 + 0.55, 0.0, 1.0);
                vec3 lit = (hemi + sunColor * ndotl) * baseContact;

                if (playerLightColorIntensity.a > 0.001)
                {
                    vec3 toPlayer = playerLightPosRadius.xyz - varWorldPos;
                    float dP = length(toPlayer);
                    float rP = max(playerLightPosRadius.w, 0.001);
                    if (dP < rP)
                    {
                        float uP = dP / rP;
                        float qP = max(0.0, 1.0 - uP * uP);
                        float falloff = (qP * qP) / (1.0 + 0.11 * dP * dP);
                        float contrib = playerLightColorIntensity.a * falloff * 0.90;
                        lit += playerLightColorIntensity.rgb * contrib;
                    }
                }

                vec3 pointAccum = vec3(0.0);
                for (int i = 0; i < pointLightCount; ++i)
                {
                    vec4 pr = pointLightPosRadius[i];
                    vec4 ci = pointLightColorIntensity[i];
                    if (ci.a <= 0.001)
                        continue;
                    vec3 toL = pr.xyz - varWorldPos;
                    float d = length(toL);
                    float r = max(pr.w, 0.001);
                    if (d < r)
                    {
                        vec3 L = toL / max(d, 0.0001);
                        float nL = max(0.38, dot(N, L) * 0.62 + 0.38);
                        float u = d / r;
                        float q = max(0.0, 1.0 - u * u);
                        float falloff = (q * q) / (1.0 + 0.22 * d * d);
                        float contrib = ci.a * falloff * nL;
                        pointAccum += ci.rgb * contrib;
                        localGlow += contrib;
                    }
                }
                lit += pointAccum / (vec3(1.0) + pointAccum * 0.65);

                vec3 baseColor = straightRgb * lit;
                if (selfLit)
                {
                    baseColor = max(baseColor, straightRgb * 1.06);
                    localGlow = max(localGlow, 0.85);
                }

                float luma = dot(baseColor, vec3(0.299, 0.587, 0.114));
                vec3 grade = mix(shadowTint, highlightTint, smoothstep(0.12, 0.82, clamp(luma, 0.0, 1.0)));
                vec3 graded = mix(baseColor, baseColor * grade, envParams.y);
                outColor = vec4(max(vec3(0.0), graded), 1.0);
            }

            if (fogColor.a > 0.001)
            {
                float fogFactor;
                float originalAlpha = outColor.a;

                if (lightingMode == 1)
                {
                    fogFactor = fogColor.a * (outdoorTaper > 0.5 && drawY > 0.0
                        ? min(fogCap, dist / (fogDist * (1.0 + 2.5 * drawY)))
                        : min(fogCap, dist / fogDist));
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
                else
                {
                    float effDist = outdoorTaper > 0.5 && drawY > 0.0
                        ? dist / (1.0 + 2.5 * drawY)
                        : max(0.0, dist);
                    float normD = max(0.0, effDist) / max(0.001, fogDist);
                    float rawExp = fogExpScale > 0.5
                        ? clamp(1.0 - exp(-1.95 * pow(normD, 1.75)) + 0.18 * smoothstep(0.88, 1.12, normD), 0.0, 1.0)
                        : (1.0 - exp(-1.55 * pow(normD, 1.25)));
                    fogFactor = fogColor.a * min(fogCap, rawExp);
                    if (fogFadeRange.z > 0.0)
                    {
                        float fadeDistance = max(0.0, dist - clamp(drawY, 0.0, 1.0));
                        float linT = clamp((fadeDistance - fogFadeRange.x) / max(0.001, fogFadeRange.y - fogFadeRange.x), 0.0, 1.0);
                        float expT = 1.0 - exp(-2.15 * linT * linT);
                        float distantFog = fogColor.a * mix(linT, expT, 0.55);
                        fogFactor = mix(fogFactor, distantFog, fogFadeRange.z);
                    }
                    fogFactor *= 1.0 - min(0.45, localGlow * envParams.w);
                    outColor.rgb = mix(outColor.rgb, fogColor.rgb, fogFactor);
                    outColor.a = originalAlpha;
                }
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
        uniform vec3 cameraWorldPos;

        out vec2 varUv;
        out vec3 varWorldPos;
        out float dist;
        out float drawY;
        flat out float varTexLayer;
        flat out float isFloorBillboard;

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
            isFloorBillboard = orientation != 0u ? 1.0 : 0.0;
            // localPos = R * (worldPos - cameraWorldPos) with orthogonal R => worldPos = camera + localPos * R.
            varWorldPos = cameraWorldPos + (localPos.xyz * mat3(mvMat));
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

    public void BindAndApply(
        Experimental3DState data,
        in FrameLightingUniforms lighting,
        float camX, float camY, float camZ)
    {
        program.Use();
        program.SetInputMatrix("mvMat", state.CurrentModelViewMatrix.ToArray(), true);
        program.SetInputMatrix("projMat", state.CurrentProjectionMatrix.ToArray(), true);
        program.SetInputVector2("fogDepth", state.FogDepth3D.Scale, state.FogDepth3D.Offset);
        program.SetInput("texArray", 0);
        program.SetInput("filterMode", (int)data.FilterMode);
        program.SetInput("lightingMode", (int)data.LightingMode);
        program.SetInput("light", data.Light);
        program.SetInput("fade", data.Fade);
        program.SetInputVector3("cameraWorldPos", camX, camY, camZ);

        bool hasSkyKey = data.SkyColorIndex.HasValue && data.SkyColorIndex.Value <= 31u;
        program.SetInput("fogCap", hasSkyKey ? 0.8f : 1.0f);
        program.SetInput("outdoorTaper", hasSkyKey ? 1.0f : 0.0f);
        program.SetInputVector4("fogColor",
            data.FogColor.R / 255.0f, data.FogColor.G / 255.0f,
            data.FogColor.B / 255.0f, data.FogColor.A / 255.0f);
        program.SetInput("fogDist", data.FogDistance);
        var fadeRange = data.OutdoorFade ?? (Start: 0.0f, End: 1.0f, Blend: 0.0f);
        program.SetInputVector3("fogFadeRange", fadeRange.Start, fadeRange.End, fadeRange.Blend);

        if (data.LightingMode == Lighting3DMode.Atmospheric)
        {
            program.SetInput("wallHeight", Math.Max(0.25f, data.Environment.WallHeight));
            program.SetInputVector3("sunDir", lighting.SunDir.X, lighting.SunDir.Y, lighting.SunDir.Z);
            program.SetInputVector3("sunColor", lighting.SunColor.X, lighting.SunColor.Y, lighting.SunColor.Z);
            program.SetInputVector3("skyAmbient", lighting.SkyAmbient.X, lighting.SkyAmbient.Y, lighting.SkyAmbient.Z);
            program.SetInputVector3("groundAmbient", lighting.GroundAmbient.X, lighting.GroundAmbient.Y, lighting.GroundAmbient.Z);
            program.SetInputVector4("playerLightPosRadius", lighting.PlayerLightPosRadius.X, lighting.PlayerLightPosRadius.Y, lighting.PlayerLightPosRadius.Z, lighting.PlayerLightPosRadius.W);
            program.SetInputVector4("playerLightColorIntensity", lighting.PlayerLightColorIntensity.X, lighting.PlayerLightColorIntensity.Y, lighting.PlayerLightColorIntensity.Z, lighting.PlayerLightColorIntensity.W);
            program.SetInputVector3("shadowTint", lighting.ShadowTint.X, lighting.ShadowTint.Y, lighting.ShadowTint.Z);
            program.SetInputVector3("highlightTint", lighting.HighlightTint.X, lighting.HighlightTint.Y, lighting.HighlightTint.Z);
            program.SetInputVector4("envParams", lighting.EnvParams.X, lighting.EnvParams.Y, lighting.EnvParams.Z, lighting.EnvParams.W);
            program.SetInput("fogExpScale", lighting.FogExpDensityScale);
            program.SetInput("pointLightCount", lighting.ActivePointLightCount);
            if (lighting.ActivePointLightCount > 0)
            {
                uint c = (uint)lighting.ActivePointLightCount;
                program.SetInputVector4Array("pointLightPosRadius", lighting.PointLightPosRadius, c);
                program.SetInputVector4Array("pointLightColorIntensity", lighting.PointLightColorIntensity, c);
            }
        }
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
/// Fullscreen-triangle blit shader for compositing the HDR offscreen 3D target back to
/// the active destination framebuffer, applying soft-shoulder highlight roll-off and subtle
/// 4x4 ordered Bayer dithering in Atmospheric mode while keeping <= 0.98 sky/palette values exact.
/// </summary>
internal sealed class ExperimentalCompositeShader : IDisposable
{
    readonly ShaderProgram program;
    bool disposed;

    public ExperimentalCompositeShader(State state)
    {
        string frag = ExperimentalShaderBuilder.FragmentHeader(state) + """
        uniform sampler2D sceneColor;
        uniform int lightingMode;
        in vec2 varTexCoord;

        float bayer4x4(ivec2 p)
        {
            const float bayer[16] = float[16](
                0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
               12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
               15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            );
            int idx = (p.y & 3) * 4 + (p.x & 3);
            return bayer[idx] - 0.5;
        }

        void main()
        {
            vec4 sampleVal = textureLod(sceneColor, varTexCoord, 0.0);
            if (lightingMode == 1)
            {
                outColor = sampleVal;
                return;
            }

            vec3 c = max(vec3(0.0), sampleVal.rgb);
            float peak = max(c.r, max(c.g, c.b));
            if (peak > 1.0)
            {
                // Preserve <= 1.0 sky/mountains untouched while smoothly compressing HDR point-light peaks.
                float excess = peak - 1.0;
                c /= peak;
                c = mix(c, vec3(1.0), 0.35 * (1.0 - exp(-excess * 0.85)));
            }
            c = clamp(c, 0.0, 1.0);

            if (peak > 0.003)
            {
                float dither = bayer4x4(ivec2(gl_FragCoord.xy) & 3) * (0.85 / 255.0);
                c = clamp(c + vec3(dither), 0.0, 1.0);
            }

            outColor = vec4(c, sampleVal.a);
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

    public void Bind(Lighting3DMode lightingMode)
    {
        program.Use();
        program.SetInput("sceneColor", 0);
        program.SetInput("lightingMode", (int)lightingMode);
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

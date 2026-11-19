/*
 * Texture3DShader.cs - Shader for textured 3D objects
 *
 * Copyright (C) 2020-2023  Robert Schneckenhaus <robert.schneckenhaus@web.de>
 *
 * This file is part of Ambermoon.net.
 *
 * Ambermoon.net is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Ambermoon.net is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Ambermoon.net. If not, see <http://www.gnu.org/licenses/>.
 */

namespace Ambermoon.Renderer.OpenGL;

using Data;

// NOTE: This won't support non-palette graphics as it uses the color indices
// for color replacements.
internal class Texture3DShader : ColorShader
{
    // The low two bits retain the existing alpha modes; only real floor surfaces
    // carry this marker. Ceilings share the draw layer but must stay unfiltered.
    internal const byte FloorSurfaceFlag = 4;
    internal static readonly string DefaultTexCoordName = TextureShader.DefaultTexCoordName;
    internal static readonly string DefaultSamplerName = TextureShader.DefaultSamplerName;
    internal static readonly string DefaultAtlasSizeName = TextureShader.DefaultAtlasSizeName;
    internal static readonly string DefaultTexEndCoordName = "texEndCoord";
    internal static readonly string DefaultTexSizeName = "texSize";
    internal static readonly string DefaultPaletteName = TextureShader.DefaultPaletteName;
    internal static readonly string DefaultPaletteIndexName = TextureShader.DefaultPaletteIndexName;
    internal static readonly string DefaultAlphaName = "alpha";
    internal static readonly string DefaultLightName = "light";
    internal static readonly string DefaultColorReplaceName = "palReplace";
    internal static readonly string DefaultUseColorReplaceName = "useReplace";
    internal static readonly string DefaultSkyColorIndexName = "skyColorIndex";
    internal static readonly string DefaultSkyReplaceColorName = "skyColorReplace";
    internal static readonly string DefaultPaletteCountName = TextureShader.DefaultPaletteCountName;
    internal static readonly string DefaultFogColorName = "fogColor";
    internal static readonly string DefaultFogDistanceName = "fogDist";
    internal static readonly string DefaultFogFadeRangeName = "fogFadeRange";
    internal static readonly string DefaultFogDepthName = "fogDepth";
    internal static readonly string DefaultFadeName = "fade";
    internal static readonly string DefaultSmoothDistantFloorName = "smoothDistantFloor";
    internal static readonly string DefaultSmoothWallsName = "smoothWalls";

    // The palette has a size of 32xNumPalettes pixels.
    // Each row represents one palette of 32 colors.
    // So the palette index determines the pixel row.
    // The column is the palette color index from 0 to 31.
    static string[] Texture3DFragmentShader(State state) =>
    [
        GetFragmentShaderHeader(state),
        $"uniform sampler2D {DefaultSamplerName};",
        $"uniform sampler2D {DefaultPaletteName};",
        $"uniform uvec2 {DefaultAtlasSizeName};",
        $"uniform float {DefaultLightName};",
        $"uniform float {DefaultPaletteCountName};",
        $"uniform vec4 {DefaultColorReplaceName}[16];",
        $"uniform float {DefaultUseColorReplaceName};",
        $"uniform float {DefaultSkyColorIndexName};",
        $"uniform vec4 {DefaultSkyReplaceColorName};",
        $"uniform vec4 {DefaultFogColorName};",
        $"uniform float {DefaultFogDistanceName};",
        $"uniform vec3 {DefaultFogFadeRangeName};",
        $"uniform float {DefaultFadeName};",
        $"uniform float {DefaultSmoothDistantFloorName};",
        $"uniform float {DefaultSmoothWallsName};",
        $"in vec2 varTexCoord;",
        $"in float dist;",
        $"flat in float palIndex;",
        $"flat in vec2 varTextureEndCoord;",
        $"flat in vec2 varTextureSize;",
        $"flat in float alphaEnabled;",
        $"flat in float floorSurface;",
        $"",
        $$"""
        vec4 paletteColor(float index)
        {
            return {{DefaultUseColorReplaceName}} > 0.5 && index < 15.5 ? {{DefaultColorReplaceName}}[int(index + 0.5)]
                : textureLod({{DefaultPaletteName}}, vec2((index + 0.5) / 32.0, (palIndex + 0.5) / {{DefaultPaletteCountName}}), 0.0);
        }

        // RGB is accumulated with the weight in .a. Keyed or transparent texels
        // contribute no weight, so a hedge opening cannot tint adjacent leaves.
        vec4 wallSample(ivec2 texel, ivec2 first, ivec2 last)
        {
            float index = texelFetch({{DefaultSamplerName}}, clamp(texel, first, last), 0).r * 255.0;
            if (abs(index - {{DefaultSkyColorIndexName}}) < 0.5)
                return vec4(0.0);
            vec4 color = paletteColor(index);
            if (color.a < 0.5)
                return vec4(0.0);
            return vec4(max(vec3(0.0), color.rgb + vec3({{DefaultLightName}}) - vec3(1.0)), 1.0);
        }

        vec3 smoothWallColor(vec3 centerColor, vec2 dx, vec2 dy)
        {
            vec2 atlas = vec2({{DefaultAtlasSizeName}});
            ivec2 last = ivec2(round(varTextureEndCoord * atlas)) - ivec2(1);
            ivec2 first = last + ivec2(1) - ivec2(round(varTextureSize * atlas));
            vec2 position = varTexCoord * atlas;
            float distant = smoothstep(1.0, 2.0, max(length(dx), length(dy)));
            vec3 closeColor = centerColor;
            if (distant < 1.0)
            {
                // Only mix a neighbour if this screen pixel straddles a texel edge.
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

            // Same symmetric positions as the floor filter, across one pixel footprint.
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
        """,
        $"",
        // Sample palette colours, not palette numbers. Every displaced tap wraps
        // within this tile, including negative offsets at the first repetition.
        $"vec3 sampleFloorColor(vec2 coord)",
        $"{{",
        $"    vec2 first = varTextureEndCoord - varTextureSize;",
        $"    vec2 halfTexel = vec2(0.5f) / vec2({DefaultAtlasSizeName});",
        $"    vec2 wrapped = first + mod(coord - first, varTextureSize);",
        $"    wrapped = clamp(wrapped, first + halfTexel, varTextureEndCoord - halfTexel);",
        $"    float index = textureLod({DefaultSamplerName}, wrapped, 0.0f).r * 255.0f;",
        $"    vec4 color = paletteColor(index);",
        $"    if (abs(index - {DefaultSkyColorIndexName}) < 0.5f)",
        $"        return {DefaultSkyReplaceColorName}.rgb;",
        $"    return max(vec3(0), color.rgb + vec3({DefaultLightName}) - vec3(1));",
        $"}}",
        $"",
        $"void main()",
        $"{{",
        // Derivatives must precede discard and varying control flow. Wrapped
        // coordinates have discontinuities which would falsely enlarge the filter.
        $"    vec2 texCoordDx = dFdx(varTexCoord);",
        $"    vec2 texCoordDy = dFdy(varTexCoord);",
        $"    {DefaultFragmentOutColorName} = vec4(0);",
        $"    vec2 realTexCoord = varTexCoord;",
        $"    if (realTexCoord.x >= varTextureEndCoord.x)",
        $"        realTexCoord.x -= floor((varTextureSize.x + realTexCoord.x - varTextureEndCoord.x) / varTextureSize.x) * varTextureSize.x;",
        $"    if (realTexCoord.y >= varTextureEndCoord.y)",
        $"        realTexCoord.y -= floor((varTextureSize.y + realTexCoord.y - varTextureEndCoord.y) / varTextureSize.y) * varTextureSize.y;",
        $"    float colorIndex = textureLod({DefaultSamplerName}, realTexCoord, 0.0f).r * 255.0f;",
        $"    vec4 pixelColor = paletteColor(colorIndex);",
        $"    ",
        $"    if (alphaEnabled > 0.5f && alphaEnabled < 1.5f && (colorIndex < 0.5f || pixelColor.a < 0.5f) || {DefaultLightName} < 0.01f)",
        $"        discard;",
        $"    else if (abs(colorIndex - {DefaultSkyColorIndexName}) < 0.5f)",
        $"    {{",
        $"        if (alphaEnabled > 1.5f)",
        $"            discard;",
        $"        else",
        $"            {DefaultFragmentOutColorName} = {DefaultSkyReplaceColorName};",
        $"    }}",
        $"    else",
        $"        {DefaultFragmentOutColorName} = vec4(max(vec3(0), pixelColor.rgb + vec3({DefaultLightName}) - vec3(1)), pixelColor.a);",
        $"    ",
        $"    if ({DefaultSmoothDistantFloorName} > 0.5f && floorSurface > 0.5f && alphaEnabled < 0.5f)",
        $"    {{",
        $"        float footprint = max(length(texCoordDx * vec2({DefaultAtlasSizeName})), length(texCoordDy * vec2({DefaultAtlasSizeName})));",
        $"        float filterAmount = smoothstep(1.0f, 2.0f, footprint);",
        $"        if (filterAmount > 0.0f)",
        $"        {{",
        // Eight symmetric stratified taps avoid time-varying noise and the strong
        // directional pattern of a small regular grid. The slightly wider footprint
        // trades distant detail for stability; magnified nearby texels are unchanged.
        $"            vec2 dx = 1.5f * texCoordDx;",
        $"            vec2 dy = 1.5f * texCoordDy;",
        $"            vec3 filtered = sampleFloorColor(varTexCoord - 0.4375f * dx - 0.1875f * dy)",
        $"                + sampleFloorColor(varTexCoord - 0.3125f * dx + 0.4375f * dy)",
        $"                + sampleFloorColor(varTexCoord - 0.1875f * dx - 0.0625f * dy)",
        $"                + sampleFloorColor(varTexCoord - 0.0625f * dx - 0.3125f * dy)",
        $"                + sampleFloorColor(varTexCoord + 0.0625f * dx + 0.3125f * dy)",
        $"                + sampleFloorColor(varTexCoord + 0.1875f * dx + 0.0625f * dy)",
        $"                + sampleFloorColor(varTexCoord + 0.3125f * dx - 0.4375f * dy)",
        $"                + sampleFloorColor(varTexCoord + 0.4375f * dx + 0.1875f * dy);",
        $"            {DefaultFragmentOutColorName}.rgb = mix({DefaultFragmentOutColorName}.rgb, filtered * 0.125f, filterAmount);",
        $"        }}",
        $"    }}",
        $"    ",
        // RenderBuffer assigns alpha mode 2 exclusively to opaque walls; explicit
        // transparent walls keep their existing centre sampling and discard rules.
        $"    if ({DefaultSmoothWallsName} > 0.5f && alphaEnabled > 1.5f && pixelColor.a >= 0.5f)",
        $"        {DefaultFragmentOutColorName}.rgb = smoothWallColor({DefaultFragmentOutColorName}.rgb, texCoordDx * vec2({DefaultAtlasSizeName}), texCoordDy * vec2({DefaultAtlasSizeName}));",
        $"    if ({DefaultFogColorName}.a > 0.001f)",
        $"    {{",
        $"        float fogFactor = {DefaultFogColorName}.a * min({DefaultSkyColorIndexName} < 31.5f ? 0.8f : 1.0f, dist / {DefaultFogDistanceName});",
        $"        float originalAlpha = {DefaultFragmentOutColorName}.a;",
        $"        if ({DefaultFogFadeRangeName}.z > 0.0f)",
        $"        {{",
        $"            float distantFog = {DefaultFogColorName}.a * smoothstep({DefaultFogFadeRangeName}.x, {DefaultFogFadeRangeName}.y, max(0.0f, dist));",
        $"            fogFactor = mix(fogFactor, distantFog, {DefaultFogFadeRangeName}.z);",
        $"        }}",
        $"        {DefaultFragmentOutColorName} = {DefaultFragmentOutColorName} * (1.0f - fogFactor) + fogFactor * {DefaultFogColorName};",
        // The new distance haze changes colour, not the opacity of the surface.
        $"        if ({DefaultFogFadeRangeName}.z > 0.0f)",
        $"            {DefaultFragmentOutColorName}.a = mix({DefaultFragmentOutColorName}.a, originalAlpha, {DefaultFogFadeRangeName}.z);",
        $"    }}",
        $"    if ({DefaultFadeName} < 0.9999f)",
        $"    {{",
        $"        {DefaultFragmentOutColorName} = {DefaultFragmentOutColorName} * {DefaultFadeName};",
        $"    }}",
        $"}}"
    ];

    static string[] Texture3DVertexShader(State state) =>
    [
        GetVertexShaderHeader(state),
        $"in vec3 {DefaultPositionName};",
        $"in ivec2 {DefaultTexCoordName};",
        $"in ivec2 {DefaultTexEndCoordName};",
        $"in ivec2 {DefaultTexSizeName};",
        $"in uint {DefaultPaletteIndexName};",
        $"in uint {DefaultAlphaName};",
        $"uniform uvec2 {DefaultAtlasSizeName};",
        $"uniform mat4 {DefaultProjectionMatrixName};",
        $"uniform mat4 {DefaultModelViewMatrixName};",
        $"uniform vec2 {DefaultFogDepthName};",
        $"out vec2 varTexCoord;",
        $"out float dist;",
        $"flat out float palIndex;",
        $"flat out vec2 varTextureEndCoord;",
        $"flat out vec2 varTextureSize;",
        $"flat out float alphaEnabled;",
        $"flat out float floorSurface;",
        $"",
        $"void main()",
        $"{{",
        $"    vec2 atlasFactor = vec2(1.0f / float({DefaultAtlasSizeName}.x), 1.0f / float({DefaultAtlasSizeName}.y));",
        $"    varTexCoord = atlasFactor * vec2({DefaultTexCoordName}.x, {DefaultTexCoordName}.y);",
        $"    palIndex = float({DefaultPaletteIndexName});",
        $"    varTextureEndCoord = atlasFactor * vec2({DefaultTexEndCoordName}.x, {DefaultTexEndCoordName}.y);",
        $"    varTextureSize = atlasFactor * vec2({DefaultTexSizeName}.x, {DefaultTexSizeName}.y);",
        $"    alphaEnabled = float({DefaultAlphaName} & 3u);",
        $"    floorSurface = ({DefaultAlphaName} & {FloorSurfaceFlag}u) != 0u ? 1.0f : 0.0f;",
        $"    vec4 viewPosition = {DefaultModelViewMatrixName} * vec4({DefaultPositionName}, 1.0f);",
        $"    gl_Position = {DefaultProjectionMatrixName} * viewPosition;",
        $"    dist = dot({DefaultFogDepthName}, viewPosition.zw);",
        $"}}"
    ];

    Texture3DShader(State state)
        : this(state, Texture3DFragmentShader(state), Texture3DVertexShader(state))
    {

    }

    protected Texture3DShader(State state, string[] fragmentShaderLines, string[] vertexShaderLines)
        : base(state, fragmentShaderLines, vertexShaderLines)
    {

    }

    public override void UpdateMatrices(State state)
    {
        base.UpdateMatrices(state);
        shaderProgram.SetInputVector2(DefaultFogDepthName, state.FogDepth3D.Scale, state.FogDepth3D.Offset);
    }

    public void UsePalette(bool use)
    {
        shaderProgram.SetInput(DefaultPaletteName, use ? 1.0f : 0.0f);
    }

    public void SetSampler(int textureUnit = 0)
    {
        shaderProgram.SetInput(DefaultSamplerName, textureUnit);
    }

    public void SetPalette(int textureUnit = 1)
    {
        shaderProgram.SetInput(DefaultPaletteName, textureUnit);
    }

    public void SetAtlasSize(uint width, uint height)
    {
        shaderProgram.SetInputVector2(DefaultAtlasSizeName, width, height);
    }

    public void SetLight(float light)
    {
        shaderProgram.SetInput(DefaultLightName, light);
    }

    public void SetSmoothDistantFloor(bool enabled)
    {
        shaderProgram.SetInput(DefaultSmoothDistantFloorName, enabled ? 1.0f : 0.0f);
    }

    public void SetSmoothWalls(bool enabled)
    {
        shaderProgram.SetInput(DefaultSmoothWallsName, enabled ? 1.0f : 0.0f);
    }

    public void SetPaletteReplacement(PaletteReplacement paletteReplacement)
    {
        if (paletteReplacement == null)
        {
            shaderProgram.SetInput(DefaultUseColorReplaceName, 0.0f);
        }
        else
        {
            shaderProgram.SetInputColorArray(DefaultColorReplaceName, paletteReplacement.ColorData);
            shaderProgram.SetInput(DefaultUseColorReplaceName, 1.0f);
        }
    }

    public void SetSkyColorReplacement(uint? skyColor, Render.Color replaceColor)
    {
        shaderProgram.SetInput(DefaultSkyColorIndexName, skyColor == null ? 32.0f : skyColor.Value);
        if (replaceColor != null)
        {
            shaderProgram.SetInputVector4(DefaultSkyReplaceColorName, replaceColor.R / 255.0f,
                replaceColor.G / 255.0f, replaceColor.B / 255.0f, replaceColor.A / 255.0f);
        }
    }

    public void SetPaletteCount(int count)
    {
        shaderProgram.SetInput(DefaultPaletteCountName, (float)count);
    }

    public void SetFog(Render.Color fogColor, float distance, (float Start, float End, float Blend)? outdoorFade = null)
    {
        shaderProgram.SetInputVector4(DefaultFogColorName, fogColor.R / 255.0f,
            fogColor.G / 255.0f, fogColor.B / 255.0f, fogColor.A / 255.0f);
        shaderProgram.SetInput(DefaultFogDistanceName, distance);
        // A zero blend keeps the legacy equation, including its billboard taper.
        var fade = outdoorFade ?? (Start: 0.0f, End: 1.0f, Blend: 0.0f);
        shaderProgram.SetInputVector3(DefaultFogFadeRangeName, fade.Start, fade.End, fade.Blend);
    }

    public void SetFade(float fade)
    {
        shaderProgram.SetInput(DefaultFadeName, fade);
    }

    public new static Texture3DShader Create(State state) => new Texture3DShader(state);
}

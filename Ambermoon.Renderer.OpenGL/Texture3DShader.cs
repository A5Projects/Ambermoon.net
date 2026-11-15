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
        $"in vec2 varTexCoord;",
        $"in float dist;",
        $"flat in float palIndex;",
        $"flat in vec2 varTextureEndCoord;",
        $"flat in vec2 varTextureSize;",
        $"flat in float alphaEnabled;",
        $"flat in float floorSurface;",
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
        $"    vec4 color = {DefaultUseColorReplaceName} > 0.5f && index < 15.5f ? {DefaultColorReplaceName}[int(index + 0.5f)]",
        $"        : textureLod({DefaultPaletteName}, vec2((index + 0.5f) / 32.0f, (palIndex + 0.5f) / {DefaultPaletteCountName}), 0.0f);",
        $"    if (abs(index - {DefaultSkyColorIndexName}) < 0.5f)",
        $"        return {DefaultSkyReplaceColorName}.rgb;",
        $"    return max(vec3(0), color.rgb + vec3({DefaultLightName}) - vec3(1));",
        $"}}",
        $"",
        $"void main()",
        $"{{",
        // Derivatives must precede discard and varying control flow. Wrapped
        // coordinates have discontinuities which would falsely enlarge the filter.
        $"    vec2 floorDx = dFdx(varTexCoord);",
        $"    vec2 floorDy = dFdy(varTexCoord);",
        $"    {DefaultFragmentOutColorName} = vec4(0);",
        $"    vec2 realTexCoord = varTexCoord;",
        $"    if (realTexCoord.x >= varTextureEndCoord.x)",
        $"        realTexCoord.x -= floor((varTextureSize.x + realTexCoord.x - varTextureEndCoord.x) / varTextureSize.x) * varTextureSize.x;",
        $"    if (realTexCoord.y >= varTextureEndCoord.y)",
        $"        realTexCoord.y -= floor((varTextureSize.y + realTexCoord.y - varTextureEndCoord.y) / varTextureSize.y) * varTextureSize.y;",
        $"    float colorIndex = textureLod({DefaultSamplerName}, realTexCoord, 0.0f).r * 255.0f;",
        $"    vec4 pixelColor = {DefaultUseColorReplaceName} > 0.5f && colorIndex < 15.5f ? {DefaultColorReplaceName}[int(colorIndex + 0.5f)]",
        $"        : textureLod({DefaultPaletteName}, vec2((colorIndex + 0.5f) / 32.0f, (palIndex + 0.5f) / {DefaultPaletteCountName}), 0.0f);",
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
        $"        float footprint = max(length(floorDx * vec2({DefaultAtlasSizeName})), length(floorDy * vec2({DefaultAtlasSizeName})));",
        $"        float filterAmount = smoothstep(1.0f, 2.0f, footprint);",
        $"        if (filterAmount > 0.0f)",
        $"        {{",
        // Eight symmetric stratified taps avoid time-varying noise and the strong
        // directional pattern of a small regular grid. The slightly wider footprint
        // trades distant detail for stability; magnified nearby texels are unchanged.
        $"            vec2 dx = 1.5f * floorDx;",
        $"            vec2 dy = 1.5f * floorDy;",
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

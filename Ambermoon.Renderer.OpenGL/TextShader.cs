/*
 * TextShader.cs - Shader for text rendering
 *
 * Copyright (C) 2020-2021  Robert Schneckenhaus <robert.schneckenhaus@web.de>
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

internal class TextShader : TextureShader
{
    internal static readonly string DefaultTextColorIndexName = "textColorIndex";
    internal static readonly string DefaultGlyphOriginName = "glyphOrigin";
    internal static readonly string DefaultGlyphEndName = "glyphEnd";

    // The palette has a size of 32xNumPalettes pixels.
    // Each row represents one palette of 32 colors.
    // So the palette index determines the pixel row.
    // The column is the palette color index from 0 to 31.
    static string[] TextFragmentShader(State state) =>
    [
        GetFragmentShaderHeader(state),
        $"uniform float {DefaultUsePaletteName};",
        $"uniform float {DefaultPaletteCountName};",
        $"uniform sampler2D {DefaultSamplerName};",
        $"uniform sampler2D {DefaultPaletteName};",
        $"uniform uvec2 {DefaultAtlasSizeName};",
        $"in vec2 varTexCoord;",
        $"flat in float palIndex;",
        $"flat in float textColIndex;",
        $"flat in float opacity;",
        $"flat in vec2 glyphStart;",
        $"flat in vec2 glyphStop;",
        $"",
        $"vec4 glyphPixel(vec2 pixel)",
        $"{{",
        $"    if (any(lessThan(pixel, glyphStart)) || any(greaterThanEqual(pixel, glyphStop)))",
        $"        return vec4(0.0f);",
        $"    vec4 color = textureLod({DefaultSamplerName}, (pixel + vec2(0.5f)) / vec2({DefaultAtlasSizeName}), 0.0f);",
        $"    if ({DefaultUsePaletteName} > 0.5f)",
        $"        return vec4(step(0.5f, color.r * 255.0f));",
        $"    return vec4(color.rgb * color.a, color.a);",
        $"}}",
        $"",
        $"void main()",
        $"{{",
        // Keep original bitmap interiors sharp. Only the immediate edge gets a
        // roughly 1.25 framebuffer-pixel transition, independent of UI scaling.
        // Explicit mask sampling leaves the palette-index atlas nearest-filtered.
        $"    vec2 pixel = varTexCoord * vec2({DefaultAtlasSizeName}) - vec2(0.5f);",
        $"    vec2 base = floor(pixel);",
        $"    vec2 edgeWidth = clamp(fwidth(pixel) * 1.25f, vec2(0.01f), vec2(1.0f));",
        $"    vec2 weight = smoothstep(vec2(0.5f) - edgeWidth * 0.5f, vec2(0.5f) + edgeWidth * 0.5f, fract(pixel));",
        $"    vec4 upper = mix(glyphPixel(base), glyphPixel(base + vec2(1.0f, 0.0f)), weight.x);",
        $"    vec4 lower = mix(glyphPixel(base + vec2(0.0f, 1.0f)), glyphPixel(base + vec2(1.0f)), weight.x);",
        $"    vec4 glyph = mix(upper, lower, weight.y);",
        $"    float coverage = glyph.a * opacity;",
        $"    if (coverage < 0.0039f)",
        $"        discard;",
        $"    vec4 textColor = textureLod({DefaultPaletteName}, vec2((textColIndex + 0.5f) / 32.0f, (palIndex + 0.5f) / {DefaultPaletteCountName}), 0.0f);",
        $"    {DefaultFragmentOutColorName} = vec4(glyph.rgb / glyph.a * textColor.rgb, coverage * textColor.a);",
        $"}}"
    ];

    static string[] TextVertexShader(State state) =>
    [
        GetVertexShaderHeader(state),
        $"in vec2 {DefaultPositionName};",
        $"in ivec2 {DefaultTexCoordName};",
        $"in uint {DefaultLayerName};",
        $"in uint {DefaultPaletteIndexName};",
        $"in uint {DefaultTextColorIndexName};",
        $"in uint {AlphaTextureShader.DefaultAlphaName};",
        $"in ivec2 {DefaultGlyphOriginName};",
        $"in ivec2 {DefaultGlyphEndName};",
        $"uniform uvec2 {DefaultAtlasSizeName};",
        $"uniform float {DefaultZName};",
        $"uniform mat4 {DefaultProjectionMatrixName};",
        $"uniform mat4 {DefaultModelViewMatrixName};",
        $"out vec2 varTexCoord;",
        $"flat out float palIndex;",
        $"flat out float textColIndex;",
        $"flat out float opacity;",
        $"flat out vec2 glyphStart;",
        $"flat out vec2 glyphStop;",
        $"",
        $"void main()",
        $"{{",
        $"    vec2 atlasFactor = vec2(1.0f / float({DefaultAtlasSizeName}.x), 1.0f / float({DefaultAtlasSizeName}.y));",
        $"    vec2 pos = vec2({DefaultPositionName}.x + 0.49f, {DefaultPositionName}.y + 0.49f);",
        $"    varTexCoord = atlasFactor * vec2({DefaultTexCoordName}.x, {DefaultTexCoordName}.y);",
        $"    palIndex = float({DefaultPaletteIndexName});",
        $"    textColIndex = float({DefaultTextColorIndexName});",
        $"    opacity = float({AlphaTextureShader.DefaultAlphaName}) / 255.0f;",
        $"    glyphStart = vec2({DefaultGlyphOriginName});",
        $"    glyphStop = vec2({DefaultGlyphEndName});",
        $"    gl_Position = {DefaultProjectionMatrixName} * {DefaultModelViewMatrixName} * vec4(pos, 1.0f - {DefaultZName} - float({DefaultLayerName}) * 0.00001f, 1.0f);",
        $"}}"
    ];

    TextShader(State state)
        : this(state, TextFragmentShader(state), TextVertexShader(state))
    {

    }

    protected TextShader(State state, string[] vertexShaderLines)
        : base(state, TextFragmentShader(state), vertexShaderLines)
    {

    }

    protected TextShader(State state, string[] fragmentShaderLines, string[] vertexShaderLines)
        : base(state, fragmentShaderLines, vertexShaderLines)
    {

    }

    public new static TextShader Create(State state) => new(state);
}

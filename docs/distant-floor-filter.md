# Distant floor filtering

This local Immersive Edition change addresses shimmer in the repeated 3D floor texture while moving. At increasing distance, several source texels occupy one rendered pixel; the original nearest sampling picks different colours from frame to frame. The point where the pattern becomes unstable therefore follows the camera.

## Comparing the two modes

Open Options, move to page 4 and toggle **Smooth distant floor** (German: **3D Boden glätten**). Close Options using EXIT, Escape or right-click to apply it. It is enabled by default in existing configurations that do not yet contain the setting. The corresponding `ambermoon.cfg` entry is `SmoothDistantFloor`; do not edit the file while the game is running.

The setting applies to both Original and Advanced. It is independent of distance fog, floor visibility and the global Smooth/Blur/CRT options. Disabling it restores the original floor sampling. Expanded is unchanged.

## Implementation

- Only `SurfaceType.Floor` receives a marker in an unused bit of the existing surface attribute. The original alpha modes occupy the low two bits. Walls, ceilings and billboard objects are not marked.
- The shader measures the texture footprint using derivatives of unwrapped UV coordinates. This avoids treating repetition boundaries as sudden changes in scale.
- Nearby floor pixels retain the original single sample. Filtering blends in smoothly from one to two source texels per rendered pixel.
- The filtered result averages eight fixed, symmetric, stratified samples across a footprint 1.5 times the size of the screen pixel. This favours stability over distant detail without introducing frame-dependent noise.
- Each displaced sample wraps within its own atlas tile, with half-texel bounds that exclude neighbouring assets. Palette lookup, palette replacement, sky-colour replacement and lighting happen before RGB averaging. Palette index numbers are never averaged.
- Original surface alpha, fog and scene fading are retained. There is no new render pass, mipmap texture, authored artwork, movement timing, camera update, game-data change or save-format change.

The setting is passed to the shared shader on game creation and whenever Options changes are applied, including when changed on a 2D map before returning to 3D. Desktop and Android configuration types expose the field; current runtime validation targets Windows OpenGL, not Android.

## Validation

Windowed Windows OpenGL checks passed for both Original and Advanced (195 checks each). These covered the option and saved preference, all three Options closing methods, outdoor and indoor scenes, fog, Smooth/Blur/CRT, and transitions to 2D. Hidden-floor comparisons confirmed that walls, ceilings, objects and the UI are unchanged. A temporary GPU-only atlas fixture checked that samples do not bleed from neighbouring assets; no game textures were changed on disk.

With the option off, complete captured images from Illien, Spannenberg, a dungeon and the Baron's palace were byte-identical to the previous build. With it on, 32 paired camera positions showed less frame-to-frame change in the distant floor; nearby floor pixels stayed identical. This image-difference measure also reflects reduced detail and is not a percentage measurement of shimmer removal. Final motion quality still needs playtesting.

Synchronized frame timings were similar in this windowed test but appear refresh-limited; they do not establish the GPU cost at higher resolutions. Existing OpenGL error flags were also reproduced in the previous build, with no additional errors from switching this filter on.

## Limits

Eight samples can reduce shimmer but cannot reconstruct arbitrarily compressed distant detail. Some residual aliasing is expected at the horizon or with high-contrast repeated patterns. It costs additional texture and palette reads for affected floor pixels; performance depends on GPU and resolution. It does not change wall/object sampling or movement cadence.

GPU-generated mipmaps or ordinary linear filtering cannot simply be enabled on this palette-index atlas: averaging indices produces unrelated colours and can mix neighbouring assets. A separate colour floor texture with mipmaps would be a more substantial alternative if this small filter is insufficient.

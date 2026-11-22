# Experimental 3D Renderer & Pacing Changes

Ambermoon Experimental v1.2.0-preview.1 includes renderer milestones **P0**, **P1**, and **P2**, developed from Immersive Edition v1.2.0 at `5c3cbd00`. [Experimental branch](https://github.com/A5Projects/Ambermoon.net/tree/Experimental) · [Windows preview](https://github.com/A5Projects/Ambermoon.net/releases/tag/experimental-v1.2.0-preview.1).

- **Target platform:** Windows desktop (`win-x64`, self-contained .NET 9 / Silk.NET OpenGL).
- **Compatibility:** Savegame format, 2D maps, UI/HUD, dialogs, inventory, combat, graphic filters (`Smooth` / `Blur` / `CRT`), screen effects (`Sepia` / `Grayscale`), and Original/Advanced balance remain fully compatible with Immersive Edition v1.2.0.
- **Safety net:** The original Immersive Edition 3D renderer (**Classic**) remains built in and can be toggled live in-game at any time for instant A/B comparison.

---

## Part 1 — User Guide (What Changed & How to Use It)

### 1. Smoother 3D movement, sane frame rates, and fullscreen refresh fix (P0)

- **No more runaway 1,500–3,000+ FPS:** Previously, running with VSync off (or when a GPU driver overrode VSync) could let the game spin at thousands of frames per second unnecessarily. A desktop frame pacer now keeps frame delivery steady and caps runaway frame rates automatically.
- **Live `Frame limit` (`FPS-Limit`) option (Options Page 4, slot 7):** You can now change the frame rate cap directly in-game without restarting or editing `ambermoon.cfg`. Presets:
  - **`VSync`** — locks to your monitor's refresh rate (default).
  - **`60`**, **`120`**, **`144`**, **`240`** — explicit smooth frame caps (disables VSync while active).
  - **`2x`** — caps at twice your monitor's active refresh rate (for example, `2x (168)` on an 84 Hz display).
- **Consistent 3D walking & turning speed across monitors:** Modern 3D movement now runs on a fixed-rate simulation timeline (default `60` logical steps/s) with smooth camera interpolation, so walking speed no longer feels too fast on a 240 Hz laptop or changes when switching FPS limits. Mouse-look stays immediate with zero added lag.
- **F11 exclusive fullscreen keeps custom/overclocked refresh rates:** Switching window modes with `F11` (`Normal` → `Fullscreen` → `Borderless`) previously caused exclusive fullscreen to fall back to a lower base refresh rate (e.g. `75 Hz` instead of an overclocked `84 Hz` desktop mode). Exclusive fullscreen now preserves the monitor's active refresh rate.
- **Clean mountain skyline in outdoor fog:** Outdoor haze still softens the 3D streets, buildings, and distant objects using sky-derived colours, while leaving the distant mountain backdrop untouched (fixing the solid orange/coloured band that previously appeared over the skyline).

### 2. New Experimental 3D renderer & texture filtering (P1 & P2)

Instead of rendering 3D worlds directly from 8-bit palette-index atlases with manual multi-tap shader workarounds, the **Experimental** 3D renderer converts active 3D textures into true colour (RGBA) GPU texture arrays with hardware mipmaps and **16× anisotropic filtering** across floors, ceilings, walls, and 3D objects/NPCs.

Two new live settings appear on **Options Page 5** (both take effect immediately without restarting):

1. **`3D renderer` / `3D-Renderer`** (Page 5, slot 1):
   - **`Experimental` / `Experimentell`** *(default)* — uses the new 3D renderer pass.
   - **`Classic` / `Klassisch`** — switches immediately back to the original Immersive Edition v1.2.0 renderer for instant A/B comparison.
2. **`3D textures` / `3D-Texturen`** (Page 5, slot 2 — applies when `Experimental` is active):
   - **`Edge-directed` / `Kantengeführt`** *(default)* — pre-upscales 3D pixel art 4× along diagonal contours and slopes before mipmapping. Keeps pixel-art stonework, timber beams, arches, and foliage cohesive while softening harsh stairstep jumps on nearby side walls during movement (especially noticeable on large 60–84 Hz screens).
   - **`Sharp bilinear` / `Scharf bilinear`** — keeps native 1× pixel blocks with crisp interiors and subtle sub-pixel edge anti-aliasing up close, plus full hardware mipmaps and 16× anisotropic filtering in the distance.
   - **`Crisp` / `Pixelgenau`** — pure razor-sharp original Amiga pixels up close, while still using full hardware mipmaps and 16× anisotropic filtering on receding floors and distant walls so distant shimmer stays gone.

**What improved visually in P2:**
- **Zero distant floor & wall shimmer** across all three texture modes, replacing the old 8-tap manual floor/wall filters in the Experimental pass.
- **Cleaner nearby walls in motion** with `Edge-directed` as the default.
- **Zero dark fringes or sky-colour bleed** around keyed openings (Illien hedges, barred windows, arches, trees, and NPC/monster billboards), and distant hedges/foliage keep their visual density instead of thinning out.
- **All classic visual effects still work:** day/dusk/night palette transitions, Light spell darkness falloff, scene fades, outdoor/dungeon fog, `Smooth`/`Blur`/`CRT` filters, and `Sepia`/`Grayscale` effects.

### 3. Command-line overrides (per-run, never written to `ambermoon.cfg`)

| Flag | Values | Effect |
| --- | --- | --- |
| `--renderer` | `classic`, `experimental` | Override the 3D renderer for this session |
| `--texture-filter` | `edge`, `sharp`, `crisp` | Override the Experimental texture filter for this session |
| `--vsync` | `on`, `off` | Override VSync for this session |
| `--fps` | `<n>`, `2x`, `auto`, `0` (uncapped) | Override frame cap for this session |
| `--timeline-3d` | `on`, `off` | Override fixed-rate 3D movement timeline |
| `--move-rate` | `20`–`240` | Override logical 3D movement rate (steps/s) |
| `--frame-stats` | *(flag)* | Write `frame-stats.log` with pacing and mode telemetry on exit |

---

## Part 2 — Developer & Architecture Reference

The following milestone commits record the implementation and the measurements taken during development.

### Milestone P0 (`experimental-p0`, commit `ed81352f`) — Pacing, Timing, Display & Fog

1. **Frame pacing & VSync safeguard (`Ambermoon.net/FramePacer.cs`, `FrameStats.cs`, `GameWindow.cs`):**
   - Replaces Silk.NET's busy-spin cap with a Windows `timeBeginPeriod(1)` hybrid sleep + short spin-wait deadline pacer (`SleepThresholdSeconds = 2.5 ms`, `SpinMarginSeconds = 1.5 ms`).
   - Queries `wglGetSwapIntervalEXT` and monitors consecutive sub-millisecond swaps (`BypassTriggerStreak = 8`) to detect GPU drivers forcing VSync off; when bypass is detected under `VSync = true`, falls back to pacing at the active monitor refresh rate instead of spinning at 1,500+ fps.
   - `MaxFps` semantics in `Configuration.cs` / `ICoreConfiguration.cs`: `0` = auto (`VSync` when blocking, or `max(refresh, 240)` ceiling when VSync is off), `-2` = `2x` active display refresh (clamped `60–360`), `> 0` = explicit FPS cap, `-1` = uncapped.
   - Wired live Page 4 slot 7 (`Frame limit` / `FPS-Limit`) in `Ambermoon.Core/UI/Layout.cs`, clearing non-persistent CLI `--fps`/`--vsync` overrides via `GameCore.FrameLimitChanged`.
2. **Exclusive fullscreen refresh preservation (`Ambermoon.net/GameWindow.cs`):**
   - Reads the active desktop display mode (`EnumDisplaySettingsW` / `VideoMode.RefreshRate`) before entering `WindowMode.Fullscreen` and explicitly passes the desktop refresh rate (e.g. `84 Hz`) when switching GLFW fullscreen video mode instead of letting GLFW pick a default lower refresh mode (`75 Hz`).
3. **Fixed-rate 3D movement timeline (`Ambermoon.Core/Game/Movement.cs`, `Input.cs`, `Render/MovementClock.cs`, `Render/Player3D.cs`, `Ambermoon.Renderer.OpenGL/Camera3D.cs`):**
   - Adds `Timeline3DMovement` (default `true`) and `Movement3DRate` (default `60` steps/s, range `20–240`).
   - Accumulates real elapsed time in `MovementClock` and executes fixed-rate logical movement/turn ticks independent of render cadence, while `Camera3D` linearly interpolates translation and keyboard yaw between previous and current logical snapshots using the residual phase `alpha ∈ [0, 1]`. Mouse-look deltas bypass interpolation for immediate response.
   - Legacy mode (`Timeline3DMovement = false`) remains intact for comparison.
4. **Outdoor mountain fog fix (`Ambermoon.Core/Render/RenderMap3D.cs`, `docs/outdoor-fog.md`):**
   - Removes fog tinting from the distant `Map3DBackgroundFog` mountain horizon band while preserving full distance haze across the 3D scene (`Map3DCeiling`, `Map3D`, `Billboards3D`).

### Milestone P1 (`experimental-p1`, commit `9a3ab008`) — Experimental 3D Renderer Scaffold

Goal: introduce a cleanly isolated, desktop-only (`#if !GLES`) Experimental 3D pipeline inside the existing OpenGL context with **pixel-exact parity** to Classic before changing visuals.

- **Core contract & live toggle:**
  - Added `Renderer3DMode { Classic, Experimental }` and `IGameRenderView.SupportsExperimental3D` / `Renderer3DMode` in `Ambermoon.Core/Render/IRenderView.cs`.
  - Added `Renderer3D` (default `Experimental` on desktop, `Classic` on Android) to `ICoreConfiguration` / `Configuration` / `ModConfiguration`, CLI `--renderer`, and Options Page 5 slot 1 (`Layout.cs`).
- **Observer-driven scene graph (`Ambermoon.Renderer.OpenGL/Experimental/IScene3DObserver.cs`, `Scene3DBuilder.cs`, `Surface3D.cs`):**
  - `Surface3DFactory` passes an optional `IScene3DObserver` (`Scene3DBuilder`) to each `Surface3D`. Add/remove/position/offset/palette/extrude mutations keep Classic and Experimental synchronized continuously so live toggling has zero map-reload cost.
  - `Scene3DBuilder` packs surfaces into three independently dirty-tracked interleaved VBO/EBO batches (`FloorCeilingBatch`, `WallBatch`, `BillboardBatch`) with outward world-space surface normals (for P3 lighting) and AABB `SceneBounds3D`, matching Classic's `(0, 1, 2, 3, 0, 2)` quad winding and performing **0 GPU rebuilds** on static frames.
- **Offscreen 3D render target & thin hooks (`Experimental3DRenderer.cs`, `RenderView.cs`, `RenderLayer.cs`, `Texture.cs`):**
  - Virtual hook points on `RenderView` (`IsExperimental3DActive`, `BeginExperimental3D`, `RenderExperimental3DAndComposite`, `NotifyTextureFactorChanged`, `DisposeResources`) are overridden in `GameRenderView` (`#if !GLES`).
  - `Experimental3DRenderer` binds its dedicated `FrameBuffer sceneTarget` (`viewport.Width × viewport.Height`) before `Layer.Map3DBackground`, lets sky/horizon draw into it, draws Experimental floor/ceiling, walls, and billboards into `Map3DViewArea`, and blits 1:1 via a fullscreen-triangle shader (`ExperimentalCompositeShader`) back to `effectFrameBuffer` or default FBO `0` before 2D/UI, `GraphicFilter`, and `Effects` run.
  - Added `Texture.InvalidateActiveTexture()` to keep `Texture.ActiveTexture` caching synchronized whenever raw FBO or texture-array bindings run.
- **P1 verification:** `28/28` checks passed; **0-pixel bit-exact parity** (`maxDiff = 0`, `0 / 655,360` differing pixels) in FBO-to-FBO mode across Newlake, Illien, dungeon, and Baron's palace; `0` Experimental GL errors; `84.00 fps` across F11 modes.

### Milestone P2 (`experimental-p2`, commit `8147694c`) — RGBA Texture Arrays, Premultiplied Mipmaps, 16× Anisotropy & Edge-Directed Upscaling

Goal: break out of the 8-bit palette-index atlas constraint that forced Classic's manual 8-tap floor/wall shaders and prevented real hardware filtering.

- **Lazy RGBA texture-array cache (`Ambermoon.Renderer.OpenGL/Experimental/ExperimentalTextureCache.cs`):**
  - Reads back each Labdata 8-bit `R8` atlas and `RGBA8` palette once lazily via `glGetTexImage` (`PackAlignment = 1`, validated by `Texture` reference identity).
  - Resolves every active 3D surface rect into a `TextureLayerKey(AtlasGlId, SrcX, SrcY, SrcW, SrcH, PaletteIndex, AlphaMode, SkyKeyMode, SkyColorIndex, SkyReplaceRgba, PalReplaceHash, WrapRepeat, UpscaleFactor)`.
  - Preserves exact Classic alpha and sky-key semantics:
    - Floor/ceiling (`alphaMode = 0`): opaque, `GL_REPEAT` wrap.
    - Transparent walls (`alphaMode = 1`) and billboards (`alphaMode = 3`): index `0` or `alpha < 128` becomes transparent `(0, 0, 0, 0)`, `GL_CLAMP_TO_EDGE`.
    - Opaque walls (`alphaMode = 2`): `skyColorIndex` becomes transparent `(0, 0, 0, 0)` (so outdoor hedges/arches/crenellations punch through to sky cleanly and do not depend on `SkyReplaceRgba`).
  - Packs layers into `GL_TEXTURE_2D_ARRAY` `TextureArrayBucket`s grouped by `(LayerWidth, LayerHeight, WrapRepeat)`:
    - Floors/ceilings (`64×64` at 1× or `256×256` at 4×) share one repeating array → **1 draw call**.
    - Walls (`128×80` at 1× or `512×320` at 4×) share one clamped array → **1 draw call**.
    - Billboards group by exact `(w, h)` array buckets so hardware `GL_CLAMP_TO_EDGE` works natively on all 4 edges with zero padding erosion and zero adjacent-frame animation bleed.
  - Missing layers are baked in parallel (`Parallel.For`) on CPU and uploaded via `glTexSubImage3D` (`UnpackAlignment = 1`).
  - Buckets grow GPU-to-GPU via a scratch read FBO (`glFramebufferTextureLayer` + `glCopyTexSubImage3D`, core OpenGL 3.0) up to a bounded capacity (`128` for large 4× wall arrays, `256` otherwise) with per-frame active-layer LRU touch protection and cache-epoch invalidation so dawn/dusk palette changes never leak VRAM.
  - Queries `GL_MAX_TEXTURE_MAX_ANISOTROPY_EXT` (guarded by GL 4.6 / extension check) and enables up to **16× anisotropic filtering** + `GL_LINEAR_MIPMAP_LINEAR` on every bucket.
- **4× Edge-directed upscaling & premultiplied NPOT mipmaps (`Ambermoon.Renderer.OpenGL/Experimental/EdgeDirectedUpscaler.cs`):**
  - `Upscale4x`: xBRZ-style 4-corner perceptual edge-directed upscaler using BT.601 YCbCr colour distance (`EqualColorTolerance = 30.0`, `DominantDirectionThreshold = 3.6`, `SteepDirectionThreshold = 2.2`), with `TransparentSeparationDistance = 1000.0` so opaque silhouettes carve smooth 4× diagonal cutouts against transparency without blending into background colour, a `ColorEqual(f, h)` guard to preserve speckled stone/plaster detail, and seamless modulo wrapping for repeating floor/ceiling tiles.
  - `GeneratePremultipliedMipChain`: builds full NPOT mipmaps down to `1×1` (3-tap polyphase weighting on odd dimensions such as `128×80 → … → 8×5 → 4×2`) in **premultiplied RGBA** space, plus a bounded 6-step binary search (`scale ∈ [0.75, 2.0]`) preserving alpha-cutout coverage across mip levels so distant hedges, grates, and trees do not vanish.
- **Unified `sampler2DArray` shaders (`Experimental3DShaders.cs`, `Scene3DBuilder.cs`):**
  - Retires Classic's manual 8-tap `smoothDistantFloor` and `smoothWallColor` passes inside the Experimental renderer.
  - Shrinks vertex structs (`SurfaceVertex` 40 bytes, `BillboardVertex` 44 bytes) using normalized UVs + `texLayer` float and bucket-grouped `BatchDrawRange` draws.
  - Keeps `GL_TEXTURE_MAG_FILTER = GL_LINEAR` on all buckets and samples via `textureGrad(texArray, vec3(adjUv, layer), uvDx, uvDy)` using the original geometric derivatives:
    - `EdgeDirected` (`filterMode = 0`): hardware bilinear + trilinear + 16× aniso on 4× edge-directed layers.
    - `SharpBilinear` (`filterMode = 1`): per-axis `deriv`-driven sharp-bilinear UV adjustment on 1× layers.
    - `Crisp` (`filterMode = 2`): per-axis nearest snap on magnified axes (`deriv < 0.85`) handing off per axis (`smoothstep(0.85, 1.15, deriv)`) to full hardware trilinear + 16× anisotropy on minified axes—preventing the OpenGL `MAG_FILTER = NEAREST` anisotropic bypass on receding floors.
  - Un-premultiplies cutout fragments (`clamp(texel.rgb / texel.a, 0.0, 1.0)`) after `if (texel.a < 0.5) discard;`, guaranteeing zero black or sky-colour fringe through bilinear, mip, and 16× anisotropic taps.
- **Live Page 5 slot 2 & CLI (`IRenderView.cs`, `ICoreConfiguration.cs`, `Settings.cs`, `Layout.cs`, `Configuration.cs`, `CommandLineOptions.cs`, `GameWindow.cs`):**
  - Added `Texture3DFilterMode { EdgeDirected, SharpBilinear, Crisp }` (default `EdgeDirected`), localized Page 5 slot 2 across all 5 languages (`OptionCount = 30`), and `--texture-filter <edge|sharp|crisp>`.

### Key File Map (Delta vs `main` `5c3cbd00`)

| Area | New / Modified Files |
| --- | --- |
| **Frame pacing & display** | `Ambermoon.net/FramePacer.cs` *(new)*, `Ambermoon.net/FrameStats.cs` *(new)*, `Ambermoon.net/GameWindow.cs`, `Ambermoon.net/CommandLineOptions.cs` |
| **3D movement timeline** | `Ambermoon.Core/Game/Movement.cs`, `Input.cs`, `Ambermoon.Core/Render/MovementClock.cs`, `ICamera3D.cs`, `Player3D.cs`, `Ambermoon.Renderer.OpenGL/Camera3D.cs` |
| **Outdoor fog fix** | `Ambermoon.Core/Render/RenderMap3D.cs`, `docs/outdoor-fog.md` |
| **Experimental 3D renderer** | `Ambermoon.Renderer.OpenGL/Experimental/IScene3DObserver.cs` *(new)*, `Scene3DBuilder.cs` *(new)*, `Experimental3DRenderer.cs` *(new)*, `Experimental3DShaders.cs` *(new)*, `ExperimentalTextureCache.cs` *(new)*, `EdgeDirectedUpscaler.cs` *(new)* |
| **Renderer hooks** | `Ambermoon.Renderer.OpenGL/RenderView.cs`, `Surface3D.cs`, `RenderLayer.cs`, `Texture.cs` |
| **Config & Options UI** | `Ambermoon.Core/Render/IRenderView.cs`, `ICoreConfiguration.cs`, `Game/Settings.cs`, `GameCore.cs`, `UI/Layout.cs`, `Ambermoon.net/Configuration.cs`, `ModConfiguration.cs`, `AmbermoonAndroid/Configuration.cs`, `Configuration.md` |

### Verified Metrics Summary (P0–P2)

- **P0:** `44/44` checks passed; `84.0 fps` exclusive fullscreen (`wglSwapInterval = 1`), `168 fps` (`2x`) & `240 fps` caps verified, `updates/render = 1.00`, timeline walking distance consistent across refresh rates.
- **P1:** `28/28` checks passed; `0 / 655,360` differing pixels in FBO-to-FBO parity across Newlake, Illien, dungeon, and Baron's palace; `0` Experimental GL errors; `+0` static mesh rebuilds.
- **P2:** `23/23` checks passed; `16.0×` anisotropy active; `+0` static bakes/rebuilds and `+0` bakes switching `SharpBilinear → Crisp`; `0` black-fringe and `0` sky-bleed pixels on Illien keyed hedges (`52.46%–52.48%` coverage vs `52.48%` Classic); Newlake walking far-floor temporal shimmer reduced from `22.29` (Classic raw) and `15.15` (Classic smooth 8-tap) to `13.32` (`EdgeDirected`), `13.33` (`SharpBilinear`), and `14.08` (`Crisp`); near-wall gradient roughness lowered from `7.64` (Classic smooth) / `7.75` (Crisp) to `7.33` (`EdgeDirected`); `84.00 fps` across F11 fullscreen/borderless/normal modes with `0` Experimental GL errors.

### Planned Next Milestones

- **P3 (`experimental-p3`):** Atmospheric lighting and colour pipeline — smooth distance falloff around the player, coloured dynamic point lights (torches, braziers, fire/lava, spell light), baked corner ambient occlusion, soft billboard contact shadows, per-environment colour grading with optional pixel-art-safe ordered dithering, and smooth exponential/height fog (keeping the mountain backdrop untouched).
- **P4 (`experimental-p4`):** Scale and performance for large view distances at `3840×1600` (spatial chunk batching, frustum culling, instanced/sorted billboards).
- **P5 (optional):** Subtle peripheral projection warp and/or derived micro-bevel normal detail if desired after evaluating P3/P4.

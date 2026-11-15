# Outdoor distance fog

The old outdoor morning fog mixed every surface toward pure white from the
camera onward. At 08:50 the maximum mix was about 40%, leaving distant texture
contrast visible while washing out nearer scenery. Between 12:00 and 16:59,
the enabled option intentionally produced no outdoor fog.

The Immersive adjustment uses the existing sky colours and existing fog shaders:

- A subdued colour from the bottom sky band replaces pure white during daylight.
- A smooth distance curve leaves nearby geometry clear, then fades distant
  texture contrast. The range moves from approximately 4–14 blocks in the
  morning to 7–22 at noon, with maximum coverage about 92% at the far end.
- The new curve blends into the old one at 06:00–08:00 and 17:00–19:00. Outside
  that period the previous night calculation is retained. Existing Light-spell
  distance extensions are respected.
- Walls, floor, billboard objects, and the mountain backdrop use coordinated
  colours. Billboard tops retain a small height allowance, capped at one block,
  so distant trees do not stand out through otherwise faded surroundings.
- The mountain backdrop is treated as farther away than the rendered terrain:
  full daytime haze obscures it completely, blending back during dusk/dawn.
  Disabling fog restores the backdrop.
- Dungeon and ordinary indoor fog retain their existing calculations. The sole
  indoor exception is map 427, the permanently lit Baron's palace in both
  Original and Advanced. Other lit interiors, including crypts and caves, keep
  their fog as requested.

`SetFog` carries an optional explicit outdoor fade range and blend. A zero blend
uses the legacy calculation, and every update resets these parameters when
switching maps or disabling fog. There are no new render passes, textures,
geometry, movement changes, or save fields. Fog reduces visible texture shimmer;
it does not change the underlying nearest-sampled palette-index textures.

## Options-close correction

In the 1.15.1 base, changing an option and closing its popup with Escape or
right-click could leave the renderer using its previous configuration. Clicking
EXIT applied the change. A native test reproduced this across ten toggles,
including byte-identical on/off screenshots when the renderer was not refreshed.

The relevant correction from upstream
[d749ba2](https://github.com/Pyrdacor/Ambermoon.net/commit/d749ba226deafe2597f562d66586e33bdde542de)
moves settings application and fullscreen-event cleanup into the popup's close
event. This fork preserves its existing navigation: EXIT also closes the outer
menu; Escape/right-click close the popup first. The fog option still requires
both floor and ceiling rendering to be enabled.

## Verification

Isolated windowed fixtures compare Original and Advanced against the previous
build: actual Options close paths, repeated toggles, floor/ceiling dependency,
morning/noon/night, dawn/dusk parameters, the palace, another lit interior, and
dungeon Light-spell levels. Shaders are checked for consistent and reset fog
parameters on map transitions. Personal saves and installed configuration are
not test fixtures. Local evidence is retained in `.tools/outdoor-fog-verification`
and the candidate's `Verification/OutdoorFog` folder in the development workspace.

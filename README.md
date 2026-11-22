# Ambermoon Experimental

**v1.2.0-preview.1** is a Windows x64 prerelease for trying a new 3D renderer and movement timing in Immersive Edition v1.2.0. Playtesting is still limited. Install it into its own folder alongside your regular game. To try it with an existing party, copy your `Saves` folder and select the same game-data edition and language. [Download the preview and checksums](https://github.com/A5Projects/Ambermoon.net/releases/tag/experimental-v1.2.0-preview.1).

In a 3D area, open **Options → page 5** to switch between **Experimental** and **Classic** while looking at the same scene. Experimental offers three texture modes. **Edge-directed** is the default, **Sharp bilinear** softens pixel edges, and **Crisp** keeps nearby texels sharp. All three filter distant surfaces. **Options → page 4 → Frame limit** lets you compare VSync and frame caps. [Renderer guide and command-line options](docs/experimental-renderer.md).

[A5Projects/Ambermoon.net](https://github.com/A5Projects/Ambermoon.net) is a fork of [Pyrdacor/Ambermoon.net](https://github.com/Pyrdacor/Ambermoon.net), based on version 1.15.1 with selected fixes backported from 1.15.2 and later upstream commits. Ambermoon.net and its original game rewrite are the work of Pyrdacor and the upstream contributors.

**Ambermoon Immersive Edition v1.2.0** is the stable Windows release. [Stable downloads and release notes](https://github.com/A5Projects/Ambermoon.net/releases/tag/immersive-v1.2.0) · [Upstream credits from v1.1.0](docs/immersive-edition-1.1.0.md).

Branch | Version in this fork
--- | ---
[`Experimental`](https://github.com/A5Projects/Ambermoon.net/tree/Experimental) — this  | **Experimental** previews the desktop renderer and frame pacing changes.
[`main`](https://github.com/A5Projects/Ambermoon.net/tree/main) — default | **Immersive Edition:** larger exploration views and compact overlays (Albion inspired). <b>Smooth 2D movement! No flickering and shimmering in 3D anymore. Optional 3D Mouse View (= modern First person wasd+mouse controls) in all versions.</b>       3 New Cheats. Original = original balance; Advanced = relaxed balance esp early game.
[`expanded`](https://github.com/A5Projects/Ambermoon.net/tree/expanded) | **Expanded:** slightly enlarged 3D view, maximized within the original interface, standard Ambermoon.net balance. Optional Mouse View controls ((= modern wasd+mouse controls) in all versions. New Cheats. It does NOT contain the movement smoothing and other changes. Main is the main!
### Immersive Edition (main):
<p float="left">
  <a href="https://github.com/user-attachments/assets/d8536746-6d6b-443d-9040-31750ad6ef33"><img src="https://github.com/user-attachments/assets/d8536746-6d6b-443d-9040-31750ad6ef33" width="300" alt="3D town with the action menu" /></a>
  <a href="https://github.com/user-attachments/assets/4c824184-cb94-404c-a8d8-3b0c3df0151f"><img src="https://github.com/user-attachments/assets/4c824184-cb94-404c-a8d8-3b0c3df0151f" width="300" alt="2D interior with the menu hidden" /></a>
</p>

### Expanded Base(simpler): <br>
<img width="240" alt="Screenshot_04-11-2026 00-43-23" src="https://github.com/user-attachments/assets/18e196d5-c4e5-46f4-b897-c807ccbe02d5" />
<br>

### note: i have no intention to make a mobile version. 
The focus is the opposite: modern-ish mouse/Keyboard controls in 3D view, with which i'm happy now. The mouse/key/Arrow stuff remains fully available  as ingame options.
### Ongoing and expanding renderer changes, while still staying fairly true to the original. All new options are optional, toggle via menu. Additional things via commandline (below).  Parts of this like the fps fix will eventually make it into main. Other, visual changes perhaps. That's the reason for this experimantel version. 

### Savegames wil remain interchangeable between versions, mine and upstream. That is a fixed guideline, so there is no risk in running it. 

Following a overview over renderer changes for the user. More indepth under docs. 
### 1. Smoother 3D movement, sane frame rates, and fullscreen refresh fix (P0)
No more runaway 1,500–3,000+ FPS: Previously, running with VSync off (or when a GPU driver overrode VSync) could let the game spin at thousands of frames per second unnecessarily. A desktop frame pacer now keeps frame delivery steady and caps runaway frame rates automatically.
Live Frame limit (FPS-Limit) option (Options Page 4, slot 7): You can now change the frame rate cap directly in-game without restarting or editing ambermoon.cfg. Presets:
VSync — locks to your monitor's refresh rate (default).
60, 120, 144, 240 — explicit smooth frame caps (disables VSync while active).
2x — caps at twice your monitor's active refresh rate (for example, 2x (168) on an 84 Hz display).
Consistent 3D walking & turning speed across monitors: Modern 3D movement now runs on a fixed-rate simulation timeline (default 60 logical steps/s) with smooth camera interpolation, so walking speed no longer feels too fast on a 240 Hz laptop or changes when switching FPS limits. Mouse-look stays immediate with zero added lag.
F11 exclusive fullscreen keeps custom/overclocked refresh rates: Switching window modes with F11 (Normal → Fullscreen → Borderless) previously caused exclusive fullscreen to fall back to a lower base refresh rate (e.g. 75 Hz instead of an overclocked 84 Hz desktop mode). Exclusive fullscreen now preserves the monitor's active refresh rate.
Clean mountain skyline in outdoor fog: Outdoor haze still softens the 3D streets, buildings, and distant objects using sky-derived colours, while leaving the distant mountain backdrop untouched (fixing the solid orange/coloured band that previously appeared over the skyline).
## 2. New Experimental 3D renderer & texture filtering (P1 & P2)
Instead of rendering 3D worlds directly from 8-bit palette-index atlases with manual multi-tap shader workarounds, the Experimental 3D renderer converts active 3D textures into true colour (RGBA) GPU texture arrays with hardware mipmaps and 16× anisotropic filtering across floors, ceilings, walls, and 3D objects/NPCs.

## Two new live settings appear on Options Page 5 (both take effect immediately without restarting):

3D renderer / 3D-Renderer (Page 5, slot 1):
Experimental / Experimentell (default) — uses the new 3D renderer pass.
Classic / Klassisch — switches immediately back to the original Immersive Edition v1.2.0 renderer for instant A/B comparison.
3D textures / 3D-Texturen (Page 5, slot 2 — applies when Experimental is active):
Edge-directed / Kantengeführt (default) — pre-upscales 3D pixel art 4× along diagonal contours and slopes before mipmapping. Keeps pixel-art stonework, timber beams, arches, and foliage cohesive while softening harsh stairstep jumps on nearby side walls during movement (especially noticeable on large 60–84 Hz screens).
Sharp bilinear / Scharf bilinear — keeps native 1× pixel blocks with crisp interiors and subtle sub-pixel edge anti-aliasing up close, plus full hardware mipmaps and 16× anisotropic filtering in the distance.
Crisp / Pixelgenau — pure razor-sharp original Amiga pixels up close, while still using full hardware mipmaps and 16× anisotropic filtering on receding floors and distant walls so distant shimmer stays gone.
What improved visually in P2:

### Zero distant floor & wall shimmer across all three texture modes, replacing the old 8-tap manual floor/wall filters in the Experimental pass.
Cleaner nearby walls in motion with Edge-directed as the default.
Zero dark fringes or sky-colour bleed around keyed openings (Illien hedges, barred windows, arches, trees, and NPC/monster billboards), and distant hedges/foliage keep their visual density instead of thinning out.
All classic visual effects still work: day/dusk/night palette transitions, Light spell darkness falloff, scene fades, outdoor/dungeon fog, Smooth/Blur/CRT filters, and Sepia/Grayscale effects.
## 3. Command-line overrides (per-run, never written to ambermoon.cfg)
Flag	Values	Effect
--renderer	classic, experimental	Override the 3D renderer for this session
--texture-filter	edge, sharp, crisp	Override the Experimental texture filter for this session
--vsync	on, off	Override VSync for this session
--fps	<n>, 2x, auto, 0 (uncapped)	Override frame cap for this session
--timeline-3d	on, off	Override fixed-rate 3D movement timeline
--move-rate	20–240	Override logical 3D movement rate (steps/s)
--frame-stats	(flag)	Write frame-stats.log with pacing and mode telemetry on exit
## back to offical readme:
All branches retain the startup selector for **Original 1.22** and **Ambermoon Advanced 1.33**. Gameplay, apart from the contorl and visual changes is identical.  
“Expanded” names this fork's initial interface variant and control changes, "expanded" slightly from the Ambermoon.net main UI. (removed gaps around the world view, mouse view) It has the mouse contorls and the main fixes from upstream. Beyond that it will stay as is. and not be updated. It is fairly close to the og ambermoon.net (pre their fullscreen changes).; 
<br> All branches contain the new mouse view. In combination with QEWASD active, controls resemble modern First Person WASD+mouse controls (except up/down look).<br>
Transfer saves between builds using the same game-data edition and language. See [save transfer and version details](CARRYWEIGHT.txt).

## Published downloads

Edition | Windows x64 downloads | Release notes
--- | --- | ---
**Experimental v1.2.0-preview.1** | [Installer](https://github.com/A5Projects/Ambermoon.net/releases/download/experimental-v1.2.0-preview.1/Ambermoon-Experimental-1.2.0-preview.1-Windows-x64-Setup.exe) · [Portable ZIP](https://github.com/A5Projects/Ambermoon.net/releases/download/experimental-v1.2.0-preview.1/Ambermoon-Experimental-1.2.0-preview.1-Windows-x64.zip) | [Preview notes and checksums](https://github.com/A5Projects/Ambermoon.net/releases/tag/experimental-v1.2.0-preview.1)
**Immersive Edition v1.2.0** — full exploration view; Original balance or relaxed Advanced | [Installer](https://github.com/A5Projects/Ambermoon.net/releases/download/immersive-v1.2.0/Ambermoon-ImmersiveEdition-1.2.0-Windows-x64-Setup.exe) · [Portable ZIP](https://github.com/A5Projects/Ambermoon.net/releases/download/immersive-v1.2.0/Ambermoon-ImmersiveEdition-1.2.0-Windows-x64.zip) | [Details and checksums](https://github.com/A5Projects/Ambermoon.net/releases/tag/immersive-v1.2.0)
**Expanded v1.0.1** — Only slightly enlarged 3D view and standard balance | [ZIP](https://github.com/A5Projects/Ambermoon.net/releases/download/expanded-v1.0.1/Ambermoon.net-Expanded-1.0.1-Windows-x64.zip) | [Details and checksums](https://github.com/A5Projects/Ambermoon.net/releases/tag/expanded-v1.0.1)

The Immersive installer lets you choose the folder and whether to create a Start menu shortcut. To update a portable copy in place, select its current folder. Later installer runs offer that folder again. Updating in place and uninstalling both preserve your saves, settings, and screenshots.
The installer is seperate between main and experimental version. It protects (it should, but make sure against installing experimental over main. Savegames are fully compatible between all versions, including upstream. 
Experimental sits on top of v1.2.0, so below patch notes for main  remain true .

For a portable installation, extract the whole ZIP into a fresh writable folder and run `Ambermoon.net.exe`. Both editions include the .NET runtime and Original/Advanced game data. Fork updates are manual. When switching folders, copy `Saves` and `ambermoon.cfg` to carry over your progress and settings. Use the same game-data edition and language. Characters may be overloaded when moving from Immersive Edition to Expanded. See [save transfer](CARRYWEIGHT.txt).

These fork releases are for Windows x64. The downloads and build badges in the upstream documentation below refer to **upstream builds**, which do not include this fork's changes. [Build and packaging instructions](RELEASING.md) are available for contributors.

**New in v1.2.0** — Optional 3D wall smoothing reduces shimmer as you pass walls. Windows sprint follows Caps Lock or held Shift at 85%. Ctrl+S quicksaves and F7 opens the quest log. Save exchange with upstream 1.16.0 preserves quest flags and companion inventory weights. [Save-transfer details](CARRYWEIGHT.txt).

**Previously in v1.1.0** — Gentler font edges and clearer reading panels, revised outdoor fog, optional distant-floor smoothing, and selected upstream fixes and additions. Advanced now has **twice** normal carrying capacity (previously four times); Original keeps normal capacity. Restored monster defence remains separate from Advanced's improved party hit chance. Friendly combat spells and items can target portraits. [Full changes and credits](docs/immersive-edition-1.1.0.md).

**Retained from Immersive UI v1.0.2:** Inventory selection and transfer cancellation lock fixes, the targeted upstream inventory crash correction, and corrected carpet ordering during smooth 2D movement. The dedicated exploration quicksave loads through the normal menu. **Ctrl+S** reuses the **QuickSave F7** slot from earlier releases. **Advanced's normal clock runs about 30% slower** while moving or standing still. Original's clock and explicit waits remain unchanged. [Inventory investigation](docs/inventory-input-lock.md).

Since v1.0.1, selecting Original keeps original balance. Advanced retains the relaxed balance. Earlier Immersive UI v1.0.0 downloads applied the carrying, gold, accuracy, and starter-weapon changes to both selections. Smooth 2D scrolling, 3D movement smoothing, hold-to-sprint, and the persistent action menu are also retained. 3D smoothing keeps mouse-look and keyboard turning immediate and retains the earlier movement cadence. See [configuration](Configuration.md) for the `Smooth3DMovement` switch. Expanded v1.0.1 adds the inventory-lock fixes while retaining its existing movement, appearance and standard balance.

## Stable Immersive Edition

- Both 2D and 3D exploration fill the area below the party portraits. Dialogs, inventory, shops, and battles keep their original interfaces.
- 2D movement scrolls between tile positions using the original pixel art, including continuous crossings between neighboring world-map sections. Gameplay remains on the original tile grid; teleports, entering locations and opening interfaces settle immediately.
- A compact action overlay opens at the bottom right without moving or resizing the world view. It stays open after actions, returns after temporary screens close, and allows held keyboard or mouse-arrow movement.
- One translucent top bar holds the left-aligned location, centered scrolling compass, clock, and other acquired indicators. The compass appears only after activation in the game. A thin black strip closes the gap beneath the portrait names.
- Optional horizontal mouse-look works with the existing keyboard controls. **C**, a mouse-wheel click, or the clickable **3D mouse** setting on settings page 4 switches modes.
- **Smooth distant floor** on settings page 4 reduces distant 3D floor shimmer while retaining sharp nearby pixels and the original artwork. It starts enabled and can be switched off for comparison. [Details](docs/distant-floor-filter.md).
- Selecting **Original** keeps original balance, including carry limits, gold weight, misses, and weapon breakage. Selecting **Advanced** retains our relaxed balance: twice the normal carrying capacity, lighter gold, improved party hit chances, unbreakable ordinary starter weapons, and original party growth/spell costs where applicable. Advanced XP thresholds, existing character progress, quests, locations, and added world interactions remain. Both selections keep the immersive visuals and controls. See [the exact balance changes](IMMERSIVE-BALANCE.txt).

**Esc**, **Enter**, **Tab**, or **MENU** toggles the exploration actions; Esc closes an existing window first. On Windows, **Caps Lock off** walks at **60%** by default. **Caps Lock on** or **holding Shift** sprints at **85%**. **F7** opens or closes the quest log. **F11** cycles window modes. Select **QWEASD** for W/S movement, A/D strafing, and Q/E turning; **Space** interacts ahead. See [Controls](Controls.md) and [Immersive Edition details](ImmersiveEdition.md).

2D movement uses elapsed time, with bounded catch-up after stalls. 3D retains the earlier movement cadence after a timing experiment caused uneven camera motion. Its speed can depend on update frequency; see the [3D timing investigation](docs/3d-movement-timing.md) for the findings, rollback, and possible future solutions.

Both branches include the optional session cheats `noencounters`, `notired`, and `carryweight`. Enable the cheat console in settings, then use `help <command>` for details. [Carry capacity and save transfer](CARRYWEIGHT.txt) explains the shared save format and weight correction.

### video: https://youtu.be/JBkPtCe59hE
### Immersive Edition (main branch). Screenshots:

Click any image to view it at full size.

<p float="left">
  <a href="https://github.com/user-attachments/assets/2c922c5e-0cca-42d1-887a-b7ed15efc794"><img src="https://github.com/user-attachments/assets/2c922c5e-0cca-42d1-887a-b7ed15efc794" width="400" alt="2D outdoor map with the menu hidden" /></a>
  <a href="https://github.com/user-attachments/assets/a147a13d-067b-4c5e-8426-912848f5c02f"><img src="https://github.com/user-attachments/assets/a147a13d-067b-4c5e-8426-912848f5c02f" width="400" alt="2D interior with the action menu" /></a>
</p>
<p float="left">
  <a href="https://github.com/user-attachments/assets/ff3c435e-0366-4b98-953c-01f64114b9a5"><img src="https://github.com/user-attachments/assets/ff3c435e-0366-4b98-953c-01f64114b9a5" width="400" alt="2D outdoor map with the action menu" /></a>
  <a href="https://github.com/user-attachments/assets/97893d68-2122-4aeb-91d5-7bb5dde1ed93"><img src="https://github.com/user-attachments/assets/97893d68-2122-4aeb-91d5-7bb5dde1ed93" width="400" alt="3D interior with the menu hidden" /></a>
</p>
<p float="left">
  <a href="https://github.com/user-attachments/assets/b40196b5-0cdc-42b1-8189-c6b51c6583e4"><img src="https://github.com/user-attachments/assets/b40196b5-0cdc-42b1-8189-c6b51c6583e4" width="400" alt="3D interior with the action menu" /></a>
  <a href="https://github.com/user-attachments/assets/d95697b2-a5e8-4748-889d-3debff867091"><img src="https://github.com/user-attachments/assets/d95697b2-a5e8-4748-889d-3debff867091" width="400" alt="Immersive Edition controls and options" /></a>
</p>
<p float="left">
  <a href="https://github.com/user-attachments/assets/9d735232-acd5-467c-9f61-d43073b04c79"><img src="https://github.com/user-attachments/assets/9d735232-acd5-467c-9f61-d43073b04c79" width="400" alt="Additional cheat commands" /></a>
  <a href="https://github.com/user-attachments/assets/75e7bb00-d528-4a06-a849-a685783841af"><img src="https://github.com/user-attachments/assets/75e7bb00-d528-4a06-a849-a685783841af" width="400" alt="Original dialog over the 3D world" /></a>
</p>

## Ambermoon.net Expanded edition (Second branch, less changed version) Screenshots:
Closer to the original visually and unchanged balance. Slightly  expanded world view, mouse view controls, added cheats. Original 
### Screenshots:
<img width="640" height="360" alt="Amermoon.net Expanded" src="https://github.com/user-attachments/assets/d4e517ff-7779-49f3-bbb5-32cdec9d6ef3" />
<img width="640" height="360" alt="Screenshot_07-11-2026 01-04-40" src="https://github.com/user-attachments/assets/fb6dacfc-d2d3-4605-8733-bb239e01ff72" />



## Upstream documentation and credits

The original README content below is retained from **Pyrdacor/Ambermoon.net**. Its first-person text, contact details, donations, screenshots, and release links belong to the upstream project. For this branch's controls and balance, use the fork documentation linked above.

## Introduction

Ambermoon.net is a full C# rewrite of Ambermoon and will run at least on Windows, Linux and Mac.

### Download

This retained upstream section describes base version 1.15.1, released November 3rd, 2026. It is historical base documentation, not the current fork release number. It includes [original version 1.22](https://github.com/Pyrdacor/Ambermoon) and also [Ambermoon Advanced 1.33](https://github.com/Pyrdacor/Ambermoon-Advanced).

You can also download the game on my website at www.pyrdacor.net!

Platform | Download 1.15.1
--- | :-:
**Windows 64bit** | [Link](https://ambermoon-net.pyrdacor.net/download/recent/Ambermoon.net-Windows.zip)
**Linux 64bit** | [Link](https://ambermoon-net.pyrdacor.net/download/recent/Ambermoon.net-Linux.tar.gz)
**Mac 64bit (Intel processor)** | [Link](https://ambermoon-net.pyrdacor.net/download/recent/Ambermoon.net-Mac.zip)
**Mac 64bit (ARM processor, M1, M2, etc)** | [Link](https://ambermoon-net.pyrdacor.net/download/recent/Ambermoon.net-Mac-ARM.zip)
**Windows 32bit** | [Link](https://ambermoon-net.pyrdacor.net/download/recent/Ambermoon.net-Windows32Bit.zip)


Problems? The [FAQ](FAQ.md) might help.

Other platforms like Android, iOS or Raspberry Pi4 might follow later.

[![Build status](https://ci.appveyor.com/api/projects/status/cr6temgl1vknho6t?svg=true)](https://ci.appveyor.com/project/Pyrdacor/ambermoon-net)

### Amiga versions

If you are interested in the original Amiga game files you can find them here: https://github.com/Pyrdacor/Ambermoon. I released some
new versions and patches of the original game files there as well.

A completely new game extension was also released called [Ambermoon Advanced](https://GitHub.com/Pyrdacor/Ambermoon-Advanced). Check it out!

### Additional Resources

- [Map](https://ambermoon-net.pyrdacor.net/download/AmbermoonMap.pdf)
- [Rune Table](https://ambermoon-net.pyrdacor.net/download/AmbermoonRuneTable.png)
- [German Manual / Handbuch Deutsch](https://ambermoon-net.pyrdacor.net/download/Ambermoon_Handbuch_Deutsch.pdf)
- [English Manual](https://ambermoon-net.pyrdacor.net/download/AmbermoonManualEnglish.pdf)
- [English Amiga Walkthrough](https://ambermoon-net.pyrdacor.net/download/Walkthrough.txt) by Michael Böhnisch (1999)
- [Tools](https://github.com/Pyrdacor/Ambermoon/blob/master/AmbermoonTools/README.md)


### Need help?

- (*English*) New to Ambermoon? Please have a look [here](FirstPlay.md). \
If you have questions feel free to ask via [email](mailto:trobt@web.de), on [Twitter](https://twitter.com/Pyrdacor) or here on GitHub on the [issues page](https://github.com/Pyrdacor/Ambermoon.net/issues). Some more info follows below as well.
- (*German*) Du kennst Ambermoon noch nicht? Schau bitte [hier](FirstPlay.md). \
Wenn du Fragen hast, meld dich einfach per [Email](mailto:trobt@web.de), auf [Twitter](https://twitter.com/Pyrdacor) oder hier auf GitHub unter [Issues](https://github.com/Pyrdacor/Ambermoon.net/issues). Weiter unten gibt es auch noch einige Infos.

### Support the project

I appreciate all kind of support for this project. If you want to donate to help increase my motivation and allow me to spend more time developing this Ambermoon project and similar projects like a 3rd part of the trilogy or map and character editors, you can do so here. \
- <a href="https://www.patreon.com/bePatron?u=44764566"><img src="https://github.com/Pyrdacor/github-images/blob/main/patreon.svg" width="140" height="28" alt="Become a patron" /></a>
- <a href="https://github.com/sponsors/Pyrdacor"><img src="https://github.com/Pyrdacor/github-images/blob/main/sponsor.svg" width="70" height="24" alt="Sponsor" /></a>
- <a href="https://ko-fi.com/pyrdacor"><img src="https://storage.ko-fi.com/cdn/cup-border.png" width="39" alt="Sponsor" /> Buy me a coffee</a>
- [![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=76DV5MK5GNEMS&source=url)
- [![Flattr](https://ambermoon-net.pyrdacor.net/assets/flattr-badge-large.png)](https://flattr.com/submit/auto?user_id=Pyrdacor&url=https://github.com/Pyrdacor/Ambermoon.net&title=Ambermoon.net&language=C#&tags=github&category=software) \
Thank you very much.

I will also post updates about releases and interesting stuff on twitter: https://twitter.com/Pyrdacor

## Screenshots

<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MapRendering1.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MapRendering1.png" width="400" alt="Indoor map" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MapRendering2.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MapRendering2.png" width="400" alt="World map" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Skies.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Skies.png" width="400" alt="Skies" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Map3D.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Map3D.png" width="400" alt="3D map" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Battle.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Battle.png" width="400" alt="Battles" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Spells.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Spells.png" width="400" alt="Spells" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/BattleLoot.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/BattleLoot.png" width="400" alt="Battle loot" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/BattlePositions.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/BattlePositions.png" width="400" alt="Battle positions" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Chests.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Chests.png" width="400" alt="Chests" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Inventory.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Inventory.png" width="400" alt="Inventory" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Merchant.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Merchant.png" width="400" alt="Merchant" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Healer.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Healer.png" width="400" alt="Healer" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Conversation.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Conversation.png" width="400" alt="Conversations" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/EventBox.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/EventBox.png" width="400" alt="Events" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Monsters3D.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Monsters3D.png" width="400" alt="Monsters" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MapEffects.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MapEffects.png" width="400" alt="Map effects" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Rest.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Rest.png" width="400" alt="Rest" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Items.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Items.png" width="400" alt="Items" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Doors.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Doors.png" width="400" alt="Locked doors" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/LevelUp.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/LevelUp.png" width="400" alt="Level Up" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/GameOver.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/GameOver.png" width="400" alt="Game Over" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Riddlemouth.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Riddlemouth.png" width="400" alt="Riddlemouth" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/FogOfWar.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/FogOfWar.png" width="400" alt="Fog of war" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Drugs.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Drugs.png" width="400" alt="Drug effect" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Automap.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Automap.png" width="400" alt="Automap" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Minimap.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Minimap.png" width="400" alt="Minimap" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MainMenu.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/MainMenu.png" width="400" alt="Main menu" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/CharacterCreator.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/CharacterCreator.png" width="400" alt="Character creation" /></a>
</p>
<p float="left">
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/VersionSelector.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/VersionSelector.png" width="400" alt="Version selector" /></a>
  <a href="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Options.png"><img src="https://github.com/Pyrdacor/Ambermoon.net/raw/master/Screenshots/Options.png" width="400" alt="Options" /></a>
</p>


## Important controls

**Please also have a look [here](Controls.md).**

Key | Description
--- | ---
Up or W | Move up (2D) or forward (3D)
Down or S| Move down (2D) or backward (3D)
Left or A | Move left (2D) or turn left (3D)
Right or D | Move right (2D) or turn right (3D)
F11 | Toggle fullscreen mode
ESC | Close ingame windows, stop item dragging/specific cursors/etc
Num1-Num9 | Action buttons 1-9 (button panel in the lower right corner)
1-6 | Set party member 1-6 as active
F1-F6 | Open inventory of party member 1-6
F7 | Open or close the quest log (Immersive Edition)
Ctrl + S | Quicksave during exploration (Immersive Edition)
Ctrl + 0-9 | Quicksave into save slot 1-10 (0 saves into slot 10)
PrintScreen or Ctrl + P | Saves a screenshot to the folder "Screenshots" next to the program (or in config folder if you have no permissions to write there)

### Left clicks

- On portrait will select the party member as the active one.
- Drag&drop of items.
- On map center (Zzz) it will wait for 5 minutes.
- All kinds of interactions and selections.

### Right clicks

- On portrait will open the inventory.
- On buttons in map view will toggle between move arrows and actions.
- In 2D map view will show a crosshair cursor which can be used for interactions. Or it aborts an already active action cursor.
- In 3D map view:
  - On the corners (turn arrows) it will perform 90° or 180° rotations.
  - On the center (Zzz) it will trigger interactions with a nearby map object.
- On item it will pickup all the items without asking for the amount.
- When dragging an item it will reset the item back to its source slot.

### Mouse wheel

- Scrolling the wheel on 2D maps changes the cursor.
- Clicking the wheel or middle mouse button on 2D maps will show the crosshair or mouth cursor.
- If a scrollbar is present, it can be controlled by the mouse wheel as well.


## Configuration

Ambermoon.net can be configured ingame but also via a configuration file.
See [Configuration](Configuration.md) for more details.


## Changelog

You can find the full changelog [here](changelog.md). You can also look at the [releases](https://github.com/Pyrdacor/Ambermoon.net/releases). They have more details in general.


## Custom music

You can use your own music with Ambermoon (e.g. the Atari music or the remixes from Matthias Steinwachs). For more details read [here](CustomMusic.md). \
I also provide the Atari tracks [here](https://ambermoon-net.pyrdacor.net/download/music_atari.zip). Just extract the mp3 to a folder called "music" next to the main executable to enjoy the music. Note that not all tracks were converted to Atari so only 13 out of 32 tracks are available.


## Mods

Ambermoon.net can be extended with mods: new graphics, music, maps, monsters and items, or C# code which hooks into the game. Ambermoon DC, a dungeon crawler with endless generated floors, is included as an example. See [Modding](Modding.md) for details.


## Special thanks

Special thanks to my top patreons and github sponsors:

- Philip Breitsprecher
- Sebberick
- Thomas Ritschel
- Tschorle
- Mike Valtix
- timbo t
- Anton Huber
- Lars Haugseth
- giom
- MD
- Daniel Egger
- Kaspar
- Wolfgang Dobmeier
- NLS
- [Stay Forever](https://www.stayforever.de/)
- Other Retro Matt
- meok meok
- LoneRaider
- Lorenz P.
- Unreality
- Milan
- NeXuS-Arts
- Peter Holtgrewe
- frostworx
- Martin Tramm
- André Wösten
- Alexander Holland
- Stephan Mankie
- Benno
- JR_Riketz
- Sprudel
- Carl-Eric Menzel
- Benjamin Ziebert
- David Geiger
- crediar
- Robin Mattheussen
- Levidega
- orgi
- skobry
- AMike
- soulsuckingjerk
- Mahen            
- Teladi

Special thanks to some special contributors and testers as well:
- kermitfrog
- a1exh (Alex Holland)
- Metibor
- Thallyrion
- Uukrull
- Nephilim
- skdubg
- crediar
- nicode
- Jurie Horneman
- dlfrsilver and CFOU! (French translation)
- galon3 (Polish translation)
- Czudak (App icon, suggestion for a patreon page)
- Hessi9b (adding auto battle option)
- Raphaël Huchet (adding more quests to the quest log)


## Other projects

Checkout my already working 'SerfCity (Die Siedler) rewrite' at [freeserf.net](https://github.com/Pyrdacor/freeserf.net).

I also created another github project called [Ambermoon](https://github.com/Pyrdacor/Ambermoon) for providing resources and research the game data. Feel free to have a look or contribute.

And I work on a huge extension of Ambermoon called [Ambermoon Advanced](https://github.com/Pyrdacor/Ambermoon-Advanced).

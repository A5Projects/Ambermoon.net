# Ambermoon.net — Immersive UI fork

[A5Projects/Ambermoon.net](https://github.com/A5Projects/Ambermoon.net) is a fork of [Pyrdacor/Ambermoon.net](https://github.com/Pyrdacor/Ambermoon.net), based on version 1.15.1. Ambermoon.net and its original game rewrite are the work of Pyrdacor and the upstream contributors.

Branch | Version in this fork
--- | ---
[`main`](https://github.com/A5Projects/Ambermoon.net/tree/main) — default | **Immersive UI:** larger exploration views and compact overlays. Original uses original balance; Advanced includes relaxed balance and a higher carrying limit.
[`expanded`](https://github.com/A5Projects/Ambermoon.net/tree/expanded) | **Expanded:** enlarged 3D view, maximized within the original interface, and standard balance. 

### Personal words, Background, motivation, Decisions: The "expqanded" Version was the first Version i did. Then i played it for a bit, discovered that with some changes it holds up remarkably well, and decided to go all in, with a more radical visual and control overhaul (The immersive UI edition).
Heavily inspired (and justified) by Albion, the official successor to Ambermoon, which has the semi full screen 2D and 3D worlds, so this is what Thalion would have wanted. (but the Amiga 500 struggled with even that small 3d window with the new smooth movement and turning mode(Amiga first). 
<br> I also decided to reverse all balance changes in the Ambermoon.net Advanced edition, where they did not affect deeper (new) game mechanics. Those and related stats were left alone to not break anything/reduce the scope of the changes. The exception is the maximum carry weight, which was greatly increased.   
### What was NOT changed was the content. The art, textures, world details...  this is a direct copy of the base game and the changes the amazing Ambermoon.net project did. Only the view size and how it renders, and controls were changed. 

## Finally, ai disclaimer and background: 
when i say "i" in 2026 i obviously mean i and ai. namely codex and GPT Astra, mostly on Ultra.  This project was done only with my input and my decisions. All changes and choices done (or not) are mine!
<br> Apropos mine: I played Ambermoon when it came out, first on my (the Family) Amiga 500, then A2000 (with harddisk). Later i played it again with WinUAE. But it felt increasingly aged in all but content even though the 3D performance was much better..
<br> Now, building on the modern Ambermoon.net version, i can fix the things that annoyed me back then (RIP tedious start with more misses than hits and breaking weapons, annoying cary weight limits) and now (ancient controls).

## back to offical readme:
Both branches retain the startup selector for **Original 1.22** and **Ambermoon Advanced 1.33**. “Expanded” names this fork's interface variant and control changes, "expanded" slightly from the Ambermoon.net main UI. (removed gaps around the world view, mouse view); 
<br> Both branches contain the new mouse view. In combination with QEWASD active, controls resemble modern First Person WASD+mouse controls (except up/down look).<br>
Savegames are fully compatible between all versions.

## Download this fork

Edition | Windows 64-bit portable download | Release notes
--- | --- | ---
**Immersive UI v1.0.1** — full exploration view; Original balance or relaxed Advanced | [ZIP](https://github.com/A5Projects/Ambermoon.net/releases/download/immersive-v1.0.1/Ambermoon.net-ImmersiveUI-1.0.1-Windows-x64.zip) | [Details and checksums](https://github.com/A5Projects/Ambermoon.net/releases/tag/immersive-v1.0.1)
**Expanded v1.0.0** — enlarged 3D view and standard balance | [ZIP](https://github.com/A5Projects/Ambermoon.net/releases/download/expanded-v1.0.0/Ambermoon.net-Expanded-1.0.0-Windows-x64.zip) | [Details and checksums](https://github.com/A5Projects/Ambermoon.net/releases/tag/expanded-v1.0.0)

Extract the whole ZIP into its own folder and run `Ambermoon.net.exe`. Both packages include the .NET runtime and Original/Advanced game data; no separate runtime or emulator installation is needed. The upstream automatic updater is omitted; install future fork releases into a fresh folder. Transfer saves using the same game-data edition and language; characters may be overloaded when moving from Immersive UI to Expanded. See [save transfer](CARRYWEIGHT.txt).

These first fork releases are for Windows x64. The downloads and build badges in the upstream documentation below refer to **upstream builds**, which do not include this fork's changes. [Build and packaging instructions](RELEASING.md) are available for contributors.

**New in Immersive UI v1.0.1:** Selecting Original now keeps original balance. Advanced retains the relaxed balance. Earlier Immersive UI v1.0.0 downloads applied the carrying, gold, accuracy, and starter-weapon changes to both selections.

This release also adds smooth 2D scrolling, 3D movement smoothing, hold-to-sprint, and the persistent action-menu fixes. 3D smoothing keeps mouse-look and keyboard turning immediate and retains the earlier movement cadence. See [configuration](Configuration.md) for the `Smooth3DMovement` switch. Expanded remains at v1.0.0.

## Immersive UI

- Both 2D and 3D exploration fill the area below the party portraits. Dialogs, inventory, shops, and battles keep their original interfaces.
- 2D movement scrolls between tile positions using the original pixel art, including continuous crossings between neighboring world-map sections. Gameplay remains on the original tile grid; teleports, entering locations and opening interfaces settle immediately.
- A compact action overlay opens at the bottom right without moving or resizing the world view. It stays open after actions, returns after temporary screens close, and allows held keyboard or mouse-arrow movement.
- One translucent top bar holds the left-aligned location, centered scrolling compass, clock, and other acquired indicators. The compass appears only after activation in the game. A thin black strip closes the gap beneath the portrait names.
- Optional horizontal mouse-look works with the existing keyboard controls. **C**, a mouse-wheel click, or the clickable **3D mouse** setting on settings page 4 switches modes.
- Selecting **Original** keeps original balance, including carry limits, gold weight, misses, and weapon breakage. Selecting **Advanced** retains our relaxed balance: four times carrying capacity, lighter gold, improved party hit chances, unbreakable ordinary starter weapons, and original party growth/spell costs where applicable. Advanced XP thresholds, existing character progress, quests, locations, and added world interactions remain. Both selections keep the immersive visuals and controls. See [the exact balance changes](IMMERSIVE-BALANCE.txt).

**Esc**, **Enter**, **Tab**, or **MENU** toggles the exploration actions; Esc closes an existing window first. Keyboard movement defaults to **60% walk speed**; **Caps Lock** toggles **100% sprint**, and **holding Shift** temporarily sprints. The saved Caps Lock mode is independent of the keyboard's lock light. **F11** cycles window modes. Select **QWEASD** for W/S movement, A/D strafing, and Q/E turning; **Space** interacts ahead. See [Controls](Controls.md) and [Immersive UI details](ImmersiveUI.md).

2D movement uses elapsed time, with bounded catch-up after stalls. 3D retains the earlier movement cadence after a timing experiment caused uneven camera motion. Its speed can depend on update frequency; see the [3D timing investigation](docs/3d-movement-timing.md) for the findings, rollback, and possible future solutions.

Both branches include the optional session cheats `noencounters`, `notired`, and `carryweight`. Enable the cheat console in settings, then use `help <command>` for details. [Carry capacity and save transfer](CARRYWEIGHT.txt) explains the shared save format and weight correction.


### Immersive UI (Main branch, most changed version). Screenshots:

Click any image to view it at full size.

<p float="left">
  <a href="https://github.com/user-attachments/assets/d8536746-6d6b-443d-9040-31750ad6ef33"><img src="https://github.com/user-attachments/assets/d8536746-6d6b-443d-9040-31750ad6ef33" width="400" alt="3D town with the action menu" /></a>
  <a href="https://github.com/user-attachments/assets/ff3c435e-0366-4b98-953c-01f64114b9a5"><img src="https://github.com/user-attachments/assets/ff3c435e-0366-4b98-953c-01f64114b9a5" width="400" alt="2D outdoor map with the action menu" /></a>
</p>
<p float="left">
  <a href="https://github.com/user-attachments/assets/2c922c5e-0cca-42d1-887a-b7ed15efc794"><img src="https://github.com/user-attachments/assets/2c922c5e-0cca-42d1-887a-b7ed15efc794" width="400" alt="2D outdoor map with the menu hidden" /></a>
  <a href="https://github.com/user-attachments/assets/a147a13d-067b-4c5e-8426-912848f5c02f"><img src="https://github.com/user-attachments/assets/a147a13d-067b-4c5e-8426-912848f5c02f" width="400" alt="2D interior with the action menu" /></a>
</p>
<p float="left">
  <a href="https://github.com/user-attachments/assets/4c824184-cb94-404c-a8d8-3b0c3df0151f"><img src="https://github.com/user-attachments/assets/4c824184-cb94-404c-a8d8-3b0c3df0151f" width="400" alt="2D interior with the menu hidden" /></a>
  <a href="https://github.com/user-attachments/assets/97893d68-2122-4aeb-91d5-7bb5dde1ed93"><img src="https://github.com/user-attachments/assets/97893d68-2122-4aeb-91d5-7bb5dde1ed93" width="400" alt="3D interior with the menu hidden" /></a>
</p>
<p float="left">
  <a href="https://github.com/user-attachments/assets/b40196b5-0cdc-42b1-8189-c6b51c6583e4"><img src="https://github.com/user-attachments/assets/b40196b5-0cdc-42b1-8189-c6b51c6583e4" width="400" alt="3D interior with the action menu" /></a>
  <a href="https://github.com/user-attachments/assets/d95697b2-a5e8-4748-889d-3debff867091"><img src="https://github.com/user-attachments/assets/d95697b2-a5e8-4748-889d-3debff867091" width="400" alt="Immersive UI controls and options" /></a>
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

Version 1.15.1 is the most recent version from November 3rd, 2026. It includes [original version 1.22](https://github.com/Pyrdacor/Ambermoon) and also [Ambermoon Advanced 1.33](https://github.com/Pyrdacor/Ambermoon-Advanced).

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

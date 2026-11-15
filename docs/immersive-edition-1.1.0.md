# Ambermoon Immersive Edition v1.1.0

Local development build; not yet published. Previously named **Immersive UI**.

Based on **Ambermoon.net 1.15.1**, with selected fixes backported from **1.15.2** and later upstream commits. This is the fork's version number, independent of both the upstream engine and the Original 1.22 / Advanced 1.33 game data. It is not a full upstream update.

## Changes since Immersive UI v1.0.2

- The window title, startup version label, options information and executable product metadata identify Ambermoon Immersive Edition v1.1.0. Options show the upstream base separately. The executable remains `Ambermoon.net.exe`, and existing save/configuration paths remain valid.
- Upstream automatic patching is disabled for this edition, including local builds that still contain the updater executable. Its upstream version comparison and downloads do not apply to the fork's separate version number. Install fork updates manually; existing configuration files are preserved.
- Advanced carrying capacity is **2 kg per effective strength point**, reduced from 4 kg. Original remains 1 kg. The original 999 g allowance remains; gold stays at 1 g in Advanced and 5 g in Original. Strength, stat caps, inventory slots and existing items are unchanged. Heavily loaded parties may need to store items or use the temporary `carryweight` cheat.
- Monsters taking no other action can parry, using their existing skill and condition restrictions. The Advanced party attack-roll floor remains **85% before condition penalties**; a successful attack can still be blocked. Monsters unable to attack or move can cast when otherwise eligible. Passive monsters do not attack, move or flee; they may cast. Group advance respects passive and disabling conditions. These upstream combat behaviours apply to both startup selections, without new enemy stat tables.
- Friendly battle spell/item targets can be selected through party portraits; target highlighting follows the selected character. A local integration fix releases pending battle targeting before opening inventory and restricts the shortcut to the battle screen, preventing a stale mouse trap. Advanced monster walking animations are supported when present, with fallback for existing data. The bundled Original/Advanced data currently has no separate walking animations, so its existing animations remain in use.
- Nested item-information popups no longer leave the underlying eye button pressed. Duplicating an item updates carried weight immediately. Scaled text keeps correct source-glyph dimensions. Boss appearance precedes summoned creatures, and quest-log visibility is corrected during the outro and startup load errors.
- The original font now has gentle edge smoothing, retaining its dimensions and wrapping. Large reading panels are darker with a lighter shadow; world labels keep their strong protective shadow.
- Fresh settings enable the traditional Thalion/game intro and disable the other intro/logo sequences. Existing preferences are preserved.
- Outdoor fog has a subdued sky-derived colour and a farther fade, hiding the mountain backdrop in full daylight haze. Only the Baron's palace has its indoor fog removed. Other interiors retain their previous fog. Closing Options with EXIT, Escape or right-click applies changed settings reliably.
- **Smooth distant floor**, enabled by default on settings page 4, gradually averages eight colour samples as the floor texture becomes compressed. This trades distant detail for less shimmer while retaining nearby pixels and the original artwork. Switch it off to compare with the previous rendering. See [floor filtering](distant-floor-filter.md).

No save format, saved character stats, XP thresholds, bundled game data or 3D movement timing is changed. The Expanded branch is outside this batch. The earlier inventory-lock fixes remain in both local branches.

## Upstream credits

Thanks to **Pyrdacor (Robert Schneckenhaus)** and the Ambermoon.net contributors. The focused changes below are adapted from their work; unrelated changes within those commits are excluded.

| Included change | Upstream source |
| --- | --- |
| Inventory callback ownership (already included in v1.0.2) | [45dde2f](https://github.com/Pyrdacor/Ambermoon.net/commit/45dde2f18b321227c07b4eb6b9cc3132c677c654) |
| Apply options on every close; quest-log error-screen cleanup | [d749ba2](https://github.com/Pyrdacor/Ambermoon.net/commit/d749ba226deafe2597f562d66586e33bdde542de) |
| Nested item-info button input guards | [f1597cd](https://github.com/Pyrdacor/Ambermoon.net/commit/f1597cdd81eaac94c3beb65cdb4e4883c6ed2fab) |
| Scaled text source dimensions; hide quest log during outro | [32450d4](https://github.com/Pyrdacor/Ambermoon.net/commit/32450d4693894bcc4e469c8fdb35cf76ceeef443) |
| Duplicated-item weight accounting | [8c32c57](https://github.com/Pyrdacor/Ambermoon.net/commit/8c32c57e53bec8c322c97abd461e74b4236e7029) |
| Boss and summons appearance ordering | [2ebc02f](https://github.com/Pyrdacor/Ambermoon.net/commit/2ebc02f55ec03a31fcf499088c60c11279abd623) |
| Friendly targeting through portraits | [e189913](https://github.com/Pyrdacor/Ambermoon.net/commit/e189913fe54a5799a474da9710780fe6228dd2d8) |
| Optional monster walking animation support | [9d4a1ee](https://github.com/Pyrdacor/Ambermoon.net/commit/9d4a1ee98ffaf8057677770b3d6561fcba37b74f) |
| Spellcasting by monsters unable to attack or move | [7432e3e](https://github.com/Pyrdacor/Ambermoon.net/commit/7432e3e6f7bdebe58af9baa6cf7c194fb525a2df) |
| Monster parrying, passive flag and group-advance rules (post-1.15.2) | [96bc6e4](https://github.com/Pyrdacor/Ambermoon.net/commit/96bc6e48ebc84a5c46614ebfd719dd8f8eb3bd62) |

Save-option bit migration and remembered map/rune acquisition are deferred for separate cross-version save testing. Android, authoring scripts and upstream CI/packaging changes are not included. See [the original review](upstream-review-2026-11-15.md) and [fog notes](outdoor-fog.md).

## Local verification

Windowed native checks passed with both German Original and Advanced data: 326 combat/balance checks, 114 focused bug-fix checks, 86 portrait-targeting/animation checks, 22 branding/defaults/layout checks and 132 inventory-lock regression checks. These include the independent attack/parry rolls, 2x/1x capacity and edition switching, real duplication and weight serialization, actual scaled credits glyphs, failed transfers and Escape cancellation, and entering inventory during battle targeting. The bundled data scan found no separate walking frames in its 61 Original and 96 Advanced monsters; the existing animation fallback was verified along with synthetic separate frames. Packaging version parsing and source whitespace checks also passed.

These automated checks supplement playtesting; they do not establish that every encounter or rare input sequence has been exercised. Personal saves and configuration are excluded from test fixtures and installation.

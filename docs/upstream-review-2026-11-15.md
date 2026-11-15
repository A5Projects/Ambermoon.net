# Upstream review — 2026-11-15

**Follow-up:** the user approved the reviewed small fixes, restored combat behaviour and both optional additions for local **Ambermoon Immersive Edition v1.1.0**. The lists below preserve the original review; see [the current implementation and credits](immersive-edition-1.1.0.md). Save-option migration remains deferred. Nothing has been published from this work.

Latest published release checked online: [v1.15.2](https://github.com/Pyrdacor/Ambermoon.net/releases/tag/v1.15.2), published November 8, 2026.
Upstream master was `96bc6e48ebc84a5c46614ebfd719dd8f8eb3bd62`, one gameplay commit beyond the local `462fe22a` checkout.
This review does not merge the upstream release or its game data.

## Already backported or included in the fog work

- [45dde2f](https://github.com/Pyrdacor/Ambermoon.net/commit/45dde2f18b321227c07b4eb6b9cc3132c677c654): inventory callbacks retain their owning character and slot. Included in both forks, alongside our selection/transfer cancellation fixes.
- [d749ba2](https://github.com/Pyrdacor/Ambermoon.net/commit/d749ba226deafe2597f562d66586e33bdde542de): apply changed options when their popup closes, including Escape and right-click. The focused portion is included with Immersive fog changes; existing EXIT navigation is preserved. The unrelated quest-log change is not included.

## Small fixes worth considering separately

| Fix | Source | Scope and checks |
| --- | --- | --- |
| Item-info eye button remains pressed | [f1597cd](https://github.com/Pyrdacor/Ambermoon.net/commit/f1597cdd81eaac94c3beb65cdb4e4883c6ed2fab) | Two child-popup input guards in `Popup.cs`. Check nested item information and cancellation. Exclude unrelated authoring/script additions. |
| Garbled credits or other scaled text | [32450d4](https://github.com/Pyrdacor/Ambermoon.net/commit/32450d4693894bcc4e469c8fdb35cf76ceeef443) | Separate source glyph dimensions from scaled display dimensions in `RenderText.cs`. Adapt to our glyph bounds and check actual scaled/subpixel text and credits. Exclude custom-outro scripting. |
| Duplicated items do not update inventory weight | [8c32c57](https://github.com/Pyrdacor/Ambermoon.net/commit/8c32c57e53bec8c322c97abd461e74b4236e7029) | Three-line `Spells.cs` correctness fix using existing weight accounting. No weight values or capacity rules change. Check duplication and save/reload totals. |
| Boss appearance plays after summons | [2ebc02f](https://github.com/Pyrdacor/Ambermoon.net/commit/2ebc02f55ec03a31fcf499088c60c11279abd623) | Sort initial battle animations. Check a boss encounter with summoned monsters. No combat calculation changes. |
| Quest-log icon on outro/load-error screens | Parts of [32450d4](https://github.com/Pyrdacor/Ambermoon.net/commit/32450d4693894bcc4e469c8fdb35cf76ceeef443) and [d749ba2](https://github.com/Pyrdacor/Ambermoon.net/commit/d749ba226deafe2597f562d66586e33bdde542de) | Small cosmetic cleanup; lower priority. |

## Optional additions

- [e189913](https://github.com/Pyrdacor/Ambermoon.net/commit/e189913fe54a5799a474da9710780fe6228dd2d8): choose friendly spell/item targets through portraits and highlight only the active character's target. Useful, but intersects battle input and our inventory cancellation fixes; test both together.
- [9d4a1ee](https://github.com/Pyrdacor/Ambermoon.net/commit/9d4a1ee98ffaf8057677770b3d6561fcba37b74f): separate Advanced monster walking animations with fallback. Check whether the bundled data provides those animations before adding support.

## Keep separate or defer

- [1f4717c](https://github.com/Pyrdacor/Ambermoon.net/commit/1f4717c119d57f217c5e8d2fe28d9c0d1b5c8dbe): correct save-option bits and remembered map/rune acquisition. Although the save layout stays the same, interpretation and migration change. Cross-version save transfers need explicit checks in both forks.
- [7432e3e](https://github.com/Pyrdacor/Ambermoon.net/commit/7432e3e6f7bdebe58af9baa6cf7c194fb525a2df): immobilized monsters can cast spells. This changes combat difficulty.
- [96bc6e4](https://github.com/Pyrdacor/Ambermoon.net/commit/96bc6e48ebc84a5c46614ebfd719dd8f8eb3bd62): unreleased passive-monster, group-advance, and monster-parrying changes. These overlap our deliberate balance choices.
- Android scrolling, Czech tooltip wording, authoring scripts, and upstream packaging/CI changes are outside the current Windows fog work.

At the time of the fog-only change, only the options-close correction had been implemented. The follow-up linked above records what was subsequently included.

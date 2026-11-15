# Expanded inventory fixes

This branch includes the two focused inventory corrections already tested in
Immersive UI. They were ported on 2026-11-15 without importing its other changes.

## Button-lock cleanup

Inventory and chest item selections own their cancellation callbacks. Escape
cancels the selection, and rebuilding a window ends the selection before its
item grids are destroyed. Cleanup uses the grids that started the selection.
Battlefield Escape and right-click handling only run on the battle screen,
with popups retaining input precedence.

Gold and food transfer callbacks now complete exactly once on success or
cancellation. Cancelling transfers zero currency and restores the controls.
This prevents an action-grid lock from surviving an aborted selection or
transfer, without enabling buttons that are disabled by an actual modal state.

## Upstream inventory crash correction

The targeted upstream correction is
[45dde2f18b321227c07b4eb6b9cc3132c677c654](https://github.com/Pyrdacor/Ambermoon.net/commit/45dde2f18b321227c07b4eb6b9cc3132c677c654).
Inventory callbacks retain their owning party member and slot instead of
looking up the mutable current inventory after a character switch or window
closure. This addresses an exception and possible wrong-owner updates.

The source port changes only `Game/Input.cs`, `Game/Windowing.cs`, and
`UI/Layout.cs`. Expanded retains its existing balance, movement, renderer,
fonts, intro defaults, controls, game data, and save format. The rest of the
upstream update is not included.

The native verification fixture uses isolated windowed builds and in-memory
parties. It checks combat and non-combat cancellation, full-inventory failed
portrait transfers, character switching, captured callback ownership, popup
precedence, and item/gold/food conservation in both Original and Advanced.
Personal saves and installed settings are not test fixtures.

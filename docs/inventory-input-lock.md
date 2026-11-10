# Inventory input lock investigation

Investigated on 2026-11-10 against Immersive UI v1.0.1, based on Ambermoon.net
1.15.1. The reported symptom was an inventory whose action buttons and Escape
stopped responding while tooltips and item dragging still worked. The latest
incident happened during combat after a portrait transfer; a failed transfer
to a full inventory and Escape cancellation may have been involved. A short
screen stutter was also recalled. The exact original sequence is uncertain.

## Reproduced button lock

A native windowed test reproduced this sequence on the published build:

1. Open a party member's inventory during combat and transfer an item to a
   second member's portrait.
2. Click Use to enter item selection, then press Escape.
3. Switch to the other character's inventory.
4. Items can be dragged, but clicking Exit and pressing Escape cannot close
   the inventory.

The selection temporarily disables the action grid and item dragging and traps
the mouse. The combat Escape handler previously treated any mouse trap as a
battlefield action, even while an inventory was open. It released the trap but
left the inventory selection active. Rebuilding the inventory destroyed its
selection handlers and created draggable item grids while the shared action
grid stayed disabled. Escape also uses that grid's Exit action, so it could
not recover. Outside combat, switching inventories directly during item
selection could discard the same cleanup.

The portrait transfer itself does not disable the action grid. A full recipient
with a failed item transfer is a separate test case; it must preserve the item
and allow cancellation without losing or duplicating items. Reopening the
source inventory while cancelling a drag can rebuild the screen, but this does
not establish the cause of the reported brief stutter.

These ownership omissions were also present in the older Expanded source.
They were not introduced by the persistent exploration action-menu change.

## Local corrections

- Inventory and chest item selections own an explicit cancellation callback.
  Escape cancels selection; rebuilding the layout cancels it before destroying
  its grids. Cleanup uses the grids that started the selection and releases
  only its own temporary button lock.
- A popup retains Escape precedence. Battlefield Escape/right-click handling
  runs only on the battle screen, not inside inventory or other windows.
- Gold/food transfer completion is consumed exactly once, on success or on
  cancellation. Previously cancelling gold with Escape discarded the callback
  that restored the buttons. Cancellation transfers zero currency.
- Genuine modal states are not bypassed by a blanket button-enable operation.

## Upstream inventory crash correction

[Upstream v1.15.2](https://github.com/Pyrdacor/Ambermoon.net/releases/tag/v1.15.2)
also lists an inventory crash fix. Its specific change is
[commit 45dde2f18b321227c07b4eb6b9cc3132c677c654](https://github.com/Pyrdacor/Ambermoon.net/commit/45dde2f18b321227c07b4eb6b9cc3132c677c654),
associated with [issue 426](https://github.com/Pyrdacor/Ambermoon.net/issues/426).
Inventory item callbacks now retain their owning party member and slot instead
of looking up the mutable current inventory, which may have changed or been
cleared when a window closes. That focused correction is backported here.

It addresses an exception and possible wrong-owner updates, separately from
the reproducible action-grid lock. The rest of v1.15.2 is not merged. Game data,
save format, balance, and movement behavior are unchanged by these fixes.

## Verification

Native tests use isolated windowed builds and in-memory parties; personal saves
and installed settings are not test fixtures. Baseline evidence is retained in
`.tools/inventory-lock-verification` in the development workspace. It records
the actual disabled flags, drag behavior, input state, and failure of both Exit
and Escape after a character switch. Candidate checks exercise cancellation,
successful selection, character switching, failed transfers, currency totals,
popup precedence, and battlefield targeting. The final local results report
records the tested cases and limits.

Fixing the reproduced paths does not prove that every rare instance of the
reported symptom has the same cause. Any recurrence should record the current
window, whether an item or currency is being dragged, the last selection action,
and whether a character switch or popup occurred.

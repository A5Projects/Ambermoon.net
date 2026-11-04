# Immersive UI experiment

This branch builds on the custom v1.15.1 version. Exploration fills the 320x163 logical pixel area below the original party portrait bar in both 2D and 3D. Original dialogs, inventory, shops, and battle layouts remain available. See [Controls](Controls.md) for the overlay controls.

The 2D renderer allocates 20x11 tile slots, clips the partial bottom row, and updates tile and character positions from the same render origin. Small indoor maps are centered where they do not fill the viewport; scrolling is clamped without reading beyond their edges. World maps continue to use the existing neighboring-map mechanism. Picking translates through the same origin and rejects empty padding.

The exploration action overlay reuses the existing action grid at the bottom-right edge and opens without changing the viewport or camera. Escape closes an existing window first, then toggles the exploration overlay. Movement buttons are hidden during exploration; keyboard and mouse movement still work. Special-item indicators use the existing assets and unlock rules, with active spells above the compact status strip. The original monster sight behavior is kept separate from the larger display area.

Optional horizontal mouse-look uses relative desktop mouse deltas with an unbounded cursor. Middle-click and settings page 4 switch modes; capture is suspended for UI screens, 2D, and focus loss. Backends without a disabled cursor use recentering. The existing WASD and WASDQE keyboard modes are preserved, including Space interaction. Vertical camera tilt is intentionally excluded.

The experimental viewport is always enabled in this branch. `Expanded3DView` remains in configuration for compatibility with the stable custom branch. This is a separate build with copied saves, so the stable build can be used independently.

This first version keeps the existing action icons and right-click interaction behavior. A new context menu, redesigned dialogs, adaptive portrait sizes, and widescreen logical resolution are not part of this experiment. The game still scales its 320x200 logical canvas to the window.

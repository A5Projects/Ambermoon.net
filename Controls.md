# Controls

## General (work everywhere)

Input | Action
--- | ---
1 to 6 | Select party member 1 to 6
F1 to F6 | Open inventory of party member 1 to 6
F7 | Change battle speed (use Ctrl, Shift or Alt to change backwards)
F8 | Change graphic filter (use Ctrl, Shift or Alt to change backwards)
F9 | Change graphic overlay (use Ctrl, Shift or Alt to change backwards)
F10 | Change color effect (use Ctrl, Shift or Alt to change backwards)
PrintScreen | Take screenshot (stored in folder Screenshots)
Ctrl + P | Take screenshot (stored in folder Screenshots)
Ctrl + M | Toggle music
Ctrl + , | Lower volume
Ctrl + . | Increase volume
Escape | Generally aborts or closes things
Return | Generally submits or closes things, toggle the action buttons in map view
Space | Generally closes things, in 3D you can active events or NPCs with it
Ctrl + 0 to 9 | Quick save in slots 1 to 10 (0 is slot 10)
Ctrl + Shift + 0 to 9 | Quick load from slots 1 to 10 (0 is slot 10)
Ctrl + Shift + C | Center the window on the main monitor
NumPad 1 to 9 | Action buttons in lower right corner (see below)

![NumPad Example 1](docs/action_buttons_0.png "Move buttons")

![NumPad Example 2](docs/action_buttons_1.png "Action buttons")

<div style="page-break-after: always"></div>

## Map

You can toggle the action buttons on the lower right with either a right click on them or the return key. Num pad enter key will work as well.

On desktop, **Caps Lock** toggles keyboard movement between the configured slower speed (60% by default) and full speed. This applies to arrow keys, WASD/QE and numpad movement, including turning in 3D. The location title briefly shows the selected percentage. The setting is remembered between sessions; mouse movement keeps its normal behavior. In 2D, slowing movement increases the time between tile steps.

#### 2D Maps

You can right click on the map to transform the cursor into a crosshair cursor. Using it will perform any suitable interaction of the following type: Eye, Hand or Mouth.

Hovering over the player the cursor becomes Zzz. Left clicking will wait for 5 minutes.

#### 3D Maps

**C** or **middle-click (mouse wheel button)** switches between the original mouse arrows and horizontal mouse-look while exploring. The same saved option is available as **3D mouse** on settings page 4 (**Maussteuerung 3D** in German). Mouse-look hides and captures the pointer; moving the mouse left/right turns the view. Vertical movement does not tilt the camera.

The viewport and full interface remain visible in both modes. Press C or middle-click again to release the pointer and click the existing controls. Opening an inventory, a dialog, or the options menu also releases the pointer; returning to eligible 3D exploration resumes mouse-look. Switching to 2D or away from the game window releases it as well.

For first-person controls, select **QWEASD** in the existing 3D movement setting: W/S move, A/D strafe, and Q/E still turn. **Space** continues to interact with what is directly in front of you. Left/right mouse buttons do not start arrow movement or switch to the interaction cursor while mouse-look is active. The default mouse mode remains arrows; changing it is remembered. `MouseLookSensitivity` in the configuration adjusts turning sensitivity independently of Caps Lock's keyboard-speed setting.

In the original **arrows** mouse mode:

Hovering over the map view will change the cursor to movement arrows or Zzz in the center.

You will move faster if the mouse is at the edge of the view and slower if it is more in the center.

Left clicking in the center (Zzz) will wait for 5 minutes.

Right clicking in the center will interact with things in front of you.

<div style="page-break-after: always"></div>

## Amount popups

![Amount popup](docs/amount_popup.png "Amount popup")

Input | Action
--- | ---
Page Up | Max amount
Page down | Min amount (0)
Home (Pos1) | Min amount (0)
End | Max amount
Up | +1
Down | -1
Return | Confirm
Escape | Abort

Right clicking on the arrow buttons will select the minimum or maximum as well but right clicking somewhere else will close the popup and abort.

<div style="page-break-after: always"></div>

## Inventory

Input | Action
--- | ---
Ctrl + Left Click | Use item
Shift + Left Click | Equip or unequip item

Right clicking a stack of items will take all of them immediately. Left clicks would open the amount popup to select how many you want to take.

Dragging items can be aborted with right click or escape key.


## Battle

The default battle speed requires you to click after actions. All other speeds (+x%) will execute battle actions automatically at a given pace.

You can click or hit the space key to acknowledge battle actions and texts.

Changing the battle speed is forbidden during an active battle round. This means if the battle actions are currently performed. You still can change the battle speed between rounds when you setup your actions.




*to be continued ...*

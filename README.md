# ModafenClientXZERO

A Windows x64 CS2 external client with a green, eight-page control panel and 85 configurable settings. The source builds into one executable using Microsoft .NET Framework assemblies, with no third-party packages.

## Download and start

Open **Actions**, choose a successful **Build Windows app** run, and download **ModafenClientXZERO-Windows-x64** under Artifacts. Extract it and run **ModafenClientXZERO.exe**. Close the previous CS2Overlay.exe before starting this version. CS2 should use **Fullscreen Windowed** so the external overlay can appear above it.

## Default keybinds

| Key | Action |
| --- | --- |
| F8 | Show/hide the green panel |
| F6 | Wallhack on/off |
| F5 | Aimbot on/off; hold Right Mouse to activate |
| F4 | Aim assist on/off; hold Left Mouse to activate |
| F7 | Bunnyhop on/off; hold physical Space |
| F9 | Spinbot on/off |
| F12 | Turn all features off |
| F10 | Quit the client |

Change keys on **Keybinds**, then select **Apply keybinds**. Each toggle needs a unique key. Aimbot, assist, bunnyhop, spin and flash limiting start disabled. Input and flash limiting only run for a live local player while CS2 is in the foreground.

## Pages and behavior

- **Dashboard:** connection and offset status, refresh target from 30 to 240 Hz (120 by default), idle rate, line antialiasing, HUD, diagnostics and pause-all button. The update counter measures this client, not game FPS.
- **Wallhack:** independent full/corner boxes, skeleton lines and a red bone silhouette; health, distance, names, snaplines, teammate filters, colors, line thickness and range. Hostage carriers can be marked using the carried-hostage handle. The optional radar is an external radar overlay linked to wallhack, not a modification of CS2's native radar.
- **Aimbot + assist:** hold-key targeting, pixel FOV, head/chest/pelvis points, time-based smoothing, deadzone, mouse-step limit, target priority/lock, distance, spotted filtering, sensitivity and target indicators. Aim assist has its own smaller FOV, smoothing, key and strength. The page shows the requested **RISK** notice.
- **Movement:** hold-Space jump repetition with configurable interval and optional ground-only gate. CS2 Jump must be bound to Space.
- **Spinbot:** SSG 08 (Scout) only; spin speed and look-down pitch, optional automatic jumping, spotted on-screen targeting and fire interval. Targeting interrupts spinning while an eligible enemy is on screen. Auto-fire only clicks when CS2's crosshair entity identifies a spotted enemy; merely projecting an enemy onto the screen does not trigger a shot. The page shows **HIGHER RISK**. Spin activates only while a live local player has the SSG 08 equipped. Optional automatic practice camera setup requests thirdperson_mayamode, then monitors the rendered camera versus player angles. Spin stops if the camera follows the view or camera data is unavailable; firing waits until the camera is observed staying steady. The command requires a practice server allowing cheats. A public server may reject it, and no public-server camera bypass is provided. Use Practice camera to restore first person afterward.
- **Practice camera:** third person, camera distance and FOV requests up to 200. These use console commands and require a server with **sv_cheats 1**. Public servers can reject them, and the engine may clamp FOV. The client does not enable server cheats. Enable CS2's developer console, select its key, and use the 3-second Apply button with an empty prompt, or copy/export the commands. Applying pauses automation.
- **Keybinds:** eight configurable global actions. **NO KEYBINDS** immediately unregisters and ignores all client action shortcuts except the menu key. Feature activation controls such as holding Space or a mouse button still work after a feature is enabled in the panel.
- **Profiles:** three manually saved local profiles and reset. Loading a profile leaves all input automation disabled until toggled on. No profile autoloads at startup.

The silhouette is an approximation drawn from bones, not the rendered player model or a game material replacement. Bone indices and layout are version-dependent; invalid coordinates are omitted. Where bones are unavailable, aim points use approximate player height. Spotted filtering is CS2's reported local spotted mask, not a geometric visibility trace. Mouse aiming assumes the standard 0.022-degree mouse scale; nonstandard scaling and zoom can need calibration. No feature can recover positions the server has not sent to the client.

## Aimbot recoil control

Aimbot now has **Aimbot recoil control** and **Recoil strength** settings. It reads current AimPunchServices predictable recoil, with legacy-schema fallback, and applies correction during target tracking. While the aimbot activation key and fire are held without a selected target, it compensates changes in recoil using bounded mouse steps. Invalid data, release, death and loss of game focus stop correction. The standard recoil scale is assumed; strength can be tuned from 0 to 2. Live game tuning is not verified by the offline checks.

## Flashbang white limit

On **Wallhack**, enable **Limit flashbang white** and set **Maximum flash opacity** from 0 to 255 (default 180). Lower values reduce the white layer; 255 removes the cap. It changes only the current local pawn's `m_flFlashMaxAlpha` field. Other flash effects, such as sound, are not adjusted.

This optional feature opens a separate memory-write handle while active. Disabling it, pausing, losing game focus, dying or closing the client attempts to restore the prior value and closes that handle. Restoration only occurs if the pawn is still current and the field still contains this client's last value. A replaced pawn or an externally changed value is left alone. Missing offsets or denied access disable the option; the client does not bypass access controls. Profile loads leave it off.

## Bomb display and independent FOV circle

On **Wallhack**, enable **Show planted bomb + defuse timers**. This works even when player wallhack is off. It marks the planted C4 in the world, shows site A/B, seconds until explosion, the latest time to start a 5-second kit or 10-second normal defuse, and any active defuse countdown with its predicted result. If the radar is enabled, planted C4 appears there as an orange square. Defused and exploded bombs stop showing a running countdown.

The timers use the game clock rather than elapsed desktop time, so pauses do not create a false countdown. Unavailable or invalid clock data is labelled unavailable. Positions and timer data still depend on the current CS2 layout and what the client receives.

On **Aimbot + assist**, **Show FOV circle (also when aim is off)** controls the circle independently. Its radius and color remain adjustable. Assist-only mode uses the assist radius; otherwise the aimbot radius is shown. Showing the circle does not enable aim input. F12 also turns off the circle and bomb display.

## Window styling

The green client uses a rounded custom window, rounded cards and buttons, subtle outlines, dark selectors, hover highlights and native resize/drag support. Minimize, maximize and close controls are in the upper right.

## Spectator chase camera

On **Practice camera**, the **Spectator chase** button requests `spec_mode 5`, the third-person player-follow view used by spectator controls. **First-person spectating** requests `spec_mode 4`. Both use the same console-key countdown and pause automation. CS2 must already have you dead or spectating, and the server must permit the view. This does not give a living player spectator privileges. The separate live Scout spin camera still uses the steady Maya practice camera.

## Smoother updates

The previous 33 ms Windows Forms timer is replaced with a paced callback that queues at most one sample. Entity-page lookups are batched and reused within each sample, pawn fields are read together, and name reads are cached. Idle work is reduced when CS2 loses focus. The refresh setting is a target; actual cadence depends on Windows scheduling and read/render cost.

## Build and verification

On Windows 10/11 x64 with .NET Framework 4.8:

    powershell.exe -NoProfile -File .\\scripts\\build.ps1

The script compiles with warnings as errors and runs offline Windows checks for native x64 input layout, memory reads and batching, projection, target selection, input timing/gates, bone/radar validation, planted-C4 reads and game-clock countdowns, flash-write restoration using the test process only, profiles, camera commands, GUI bindings and timer shutdown. It also renders eight real Windows GUI and overlay previews into the build artifact. The runner does not send jump, aim or fire input and does not run CS2.

The output includes the executable, source revision, SHA-256, check results and QUICKSTART.txt. Current in-game behavior, input acceptance and server camera permissions require live testing. The build is unsigned; see [SECURITY.md](SECURITY.md).

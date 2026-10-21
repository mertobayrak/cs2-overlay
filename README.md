# ModafenClientXZERO

A Windows x64 CS2 external client with a green, eight-page control panel and 78 configurable settings. The source builds into one executable using Microsoft .NET Framework assemblies, with no third-party packages.

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

Change keys on **Keybinds**, then select **Apply keybinds**. Each toggle needs a unique key. Aimbot, assist, bunnyhop and spin start disabled. Input only runs for a live local player while CS2 is in the foreground.

## Pages and behavior

- **Dashboard:** connection and offset status, refresh target from 30 to 240 Hz (120 by default), idle rate, line antialiasing, HUD, diagnostics and pause-all button. The update counter measures this client, not game FPS.
- **Wallhack:** independent full/corner boxes, skeleton lines and a red bone silhouette; health, distance, names, snaplines, teammate filters, colors, line thickness and range. Hostage carriers can be marked using the carried-hostage handle. The optional radar is an external radar overlay linked to wallhack, not a modification of CS2's native radar.
- **Aimbot + assist:** hold-key targeting, pixel FOV, head/chest/pelvis points, time-based smoothing, deadzone, mouse-step limit, target priority/lock, distance, spotted filtering, sensitivity and target indicators. Aim assist has its own smaller FOV, smoothing, key and strength. The page shows the requested **RISK** notice.
- **Movement:** hold-Space jump repetition with configurable interval and optional ground-only gate. CS2 Jump must be bound to Space.
- **Spinbot:** spin speed and look-down pitch, optional automatic jumping, spotted on-screen targeting and fire interval. Targeting interrupts spinning while an eligible enemy is on screen. Auto-fire only clicks when CS2's crosshair entity identifies a spotted enemy; merely projecting an enemy onto the screen does not trigger a shot. The page shows **HIGHER RISK**.
- **Practice camera:** third person, camera distance and FOV requests up to 200. These use console commands and require a server with **sv_cheats 1**. Public servers can reject them, and the engine may clamp FOV. The client does not enable server cheats. Enable CS2's developer console, select its key, and use the 3-second Apply button with an empty prompt, or copy/export the commands. Applying pauses automation.
- **Keybinds:** eight configurable global actions.
- **Profiles:** three manually saved local profiles and reset. Loading a profile leaves all input automation disabled until toggled on. No profile autoloads at startup.

The silhouette is an approximation drawn from bones, not the rendered player model or a game material replacement. Bone indices and layout are version-dependent; invalid coordinates are omitted. Where bones are unavailable, aim points use approximate player height. Spotted filtering is CS2's reported local spotted mask, not a geometric visibility trace. Mouse aiming assumes the standard 0.022-degree mouse scale; nonstandard scaling and zoom can need calibration. No feature can recover positions the server has not sent to the client.

## Smoother updates

The previous 33 ms Windows Forms timer is replaced with a paced callback that queues at most one sample. Entity-page lookups are batched and reused within each sample, pawn fields are read together, and name reads are cached. Idle work is reduced when CS2 loses focus. The refresh setting is a target; actual cadence depends on Windows scheduling and read/render cost.

## Build and verification

On Windows 10/11 x64 with .NET Framework 4.8:

    powershell.exe -NoProfile -File .\\scripts\\build.ps1

The script compiles with warnings as errors and runs offline Windows checks for native x64 input layout, memory reads and batching, projection, target selection, input timing/gates, bone/radar validation, profiles, camera commands, GUI bindings and timer shutdown. It also renders six real Windows GUI previews into the build artifact. The runner does not send jump, aim or fire input and does not run CS2.

The output includes the executable, source revision, SHA-256, check results and QUICKSTART.txt. Current in-game behavior, input acceptance and server camera permissions require live testing. The build is unsigned; see [SECURITY.md](SECURITY.md).

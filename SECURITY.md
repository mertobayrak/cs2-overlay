# Behavior and verification scope

ModafenClientXZERO normally requests PROCESS_VM_READ and PROCESS_QUERY_INFORMATION for CS2. Drawing takes place in a separate click-through window.

The optional Flashbang white limit opens a separate handle with PROCESS_VM_READ, PROCESS_QUERY_INFORMATION, PROCESS_VM_WRITE and PROCESS_VM_OPERATION. It writes only the current local pawn's four-byte m_flFlashMaxAlpha field after verifying the current-pawn pointer and a finite value in 0-255. It does not allocate remote memory or create remote threads. Disabling the setting, pausing, loss of game focus, death or closing attempts to restore the original value only if the same pawn and this client's last written value remain. It then closes the write handle. A changed pawn or externally changed value prevents restoration. Abrupt termination cannot guarantee restoration. Missing data or denied write access turns the feature off without attempting a bypass. This option starts disabled and is also disabled when loading profiles.

Enabled aim/spin controls use standard Windows SendInput mouse motion. Jumping sends paired Space press/release events; spin auto-fire sends paired left-button events only for an eligible game-crosshair enemy. A live local player and foreground CS2 are required. Focus is checked again immediately before every submitted input batch. Opening the panel moves focus away from the game. All input automation starts disabled and also stays disabled when a saved profile loads.

When hold-Space bunnyhop is enabled, a low-level Windows keyboard hook observes only Space down/up state and ignores injected events. It forwards every event, does not record typed text and is removed when bunnyhop is disabled or the app closes. The hook runs in this app, not inside CS2. Global toggle keys use RegisterHotKey.

The app downloads only JSON offset data from api.github.com and raw.githubusercontent.com over HTTPS. Both data files are pinned to one recorded commit. GitHub receives normal request metadata such as IP address and User-Agent. The app does not download or execute scripts or binaries, collect credentials, upload gameplay data, register startup tasks, install drivers, inject DLLs or bypass anti-cheat/access restrictions.

User-initiated profile saves write JSON under the current user's AppData/ModafenClientXZERO directory. User-initiated camera export writes the selected .cfg file. Copy camera commands uses the clipboard. Automatic Scout spin camera setup requests thirdperson_mayamode using the selected game console key and pauses automation during setup. The active weapon is checked before spin; unknown weapon data prevents it. Camera direction is monitored and spin stops when the rendered camera follows the turning view.

Apply camera commands types the selected commands into CS2's developer console after a countdown; it pauses automation first. It does not enable sv_cheats or bypass server camera permissions.

The spectator chase and first-person buttons request spec_mode 5 and spec_mode 4 through that same console path. They do not write observer state or change a living player's permissions. They require the game to permit spectator controls.

The Windows build checks use only the runner's test process and offline fixtures, including flash writes and restoration within that test process. UI previews use an offline mode without offset downloads, keyboard hooks or global hotkeys. The checks construct input packets but do not submit aim/jump/fire input. A successful build and source review are not malware certification. Live CS2 operation, game updates and anti-cheat compatibility are not verified by CI. The executable is unsigned.

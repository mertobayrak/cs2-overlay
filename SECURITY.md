# Behavior and verification scope

ModafenClientXZERO requests PROCESS_VM_READ and PROCESS_QUERY_INFORMATION for CS2. It does not request memory-write, allocation or thread-creation rights. Drawing takes place in a separate click-through window.

Enabled aim/spin controls use standard Windows SendInput mouse motion. Jumping sends paired Space press/release events; spin auto-fire sends paired left-button events only for an eligible game-crosshair enemy. A live local player and foreground CS2 are required. Focus is checked again immediately before every submitted input batch. Opening the panel moves focus away from the game. All input automation starts disabled and also stays disabled when a saved profile loads.

When hold-Space bunnyhop is enabled, a low-level Windows keyboard hook observes only Space down/up state and ignores injected events. It forwards every event, does not record typed text and is removed when bunnyhop is disabled or the app closes. The hook runs in this app, not inside CS2. Global toggle keys use RegisterHotKey.

The app downloads only JSON offset data from api.github.com and raw.githubusercontent.com over HTTPS. Both data files are pinned to one recorded commit. GitHub receives normal request metadata such as IP address and User-Agent. The app does not download or execute scripts or binaries, collect credentials, upload gameplay data, register startup tasks, install drivers, inject DLLs or bypass anti-cheat/access restrictions.

User-initiated profile saves write JSON under the current user's AppData/ModafenClientXZERO directory. User-initiated camera export writes the selected .cfg file. Copy camera commands uses the clipboard. Apply camera commands types the selected commands into CS2's developer console after a countdown; it pauses automation first. It does not enable sv_cheats or bypass server camera permissions.

The Windows build checks use only the runner's test process and offline fixtures. UI previews use an offline mode without offset downloads, keyboard hooks or global hotkeys. The checks construct input packets but do not submit aim/jump/fire input. A successful build and source review are not malware certification. Live CS2 operation, game updates and anti-cheat compatibility are not verified by CI. The executable is unsigned.

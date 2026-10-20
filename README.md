# CS2 Overlay

A Windows x64 application with a visible controller, automatic CS2 detection,
and a read-only external entity overlay.

The controller reports waiting, connection, offset-download and game-data
errors. F6 toggles the overlay; F8 quits. CS2 must use Fullscreen Windowed for
the external window to be visible above the game.

## Download

Open the repository's **Actions** tab, select a successful **Build Windows app**
run, and download **CS2Overlay-Windows-x64** under Artifacts. Extract the archive
and run `CS2Overlay.exe`. See [QUICKSTART.txt](QUICKSTART.txt).

## What the program does

- Downloads JSON offset data from the public a2x/cs2-dumper repository over HTTPS.
  Both data files come from the same recorded commit.
- Opens a read-only process handle for CS2.
- Reads view-projection and available player entity data.
- Draws approximate opponent boxes and health in a separate click-through window.
- Displays attachment/read failures in the controller instead of failing silently.

It cannot recover positions the server has not sent to the client. Dormant
entities are omitted. The standing-height box is an approximation, including
when players crouch. Current offsets and in-game operation are not verified
by the automated build.

## Build on Windows

Run the following from the repository:

    powershell.exe -NoProfile -File .\scripts\build.ps1

The script uses the installed .NET Framework C# compiler, runs Windows checks,
and writes the executable, checksums and test report to `artifacts`.

There are no third-party NuGet packages. The application uses Microsoft .NET
Framework assemblies and Windows APIs. Windows 10/11 x64 with .NET Framework
4.8 or newer is required.

## Verification

The Windows job treats compiler warnings as errors. It checks viewport
projection, behind-camera/non-finite rejection, actual read-only native
memory reads of the test process, graceful failure at an invalid address,
and controller initialization without CS2.

The job does **not** run CS2 or establish gameplay/anti-cheat compatibility.
An executable SHA-256 and the source commit are included in the build.
The application is unsigned. See [SECURITY.md](SECURITY.md) for its scope.

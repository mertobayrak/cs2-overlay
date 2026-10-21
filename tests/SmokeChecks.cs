using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Cs2Overlay
{
    internal static class SmokeChecks
    {
        private static readonly List<string> passed = new List<string>();
        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message); passed.Add(message);
        }
        private static MemoryReader OwnMemory()
        {
            IntPtr handle = Native.OpenProcess(Native.ReadAccess, false, Process.GetCurrentProcess().Id);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Cannot open test process.");
            return new MemoryReader(handle);
        }
        private static void ProjectionChecks()
        {
            float[] identity = new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 }; PointF point;
            Require(Projection.WorldToScreen(identity, 0, 0, 0, new Size(1920,1080), out point) && point.X == 960 && point.Y == 540, "World origin projects to viewport center");
            Require(Projection.WorldToScreen(identity, 0, 1, 0, new Size(1920,1080), out point) && point.X == 960 && point.Y == 0, "Positive world Y projects upward");
            Require(Projection.WorldToScreen(identity, 1, 0, 0, new Size(1280,720), out point) && point.X == 1280 && point.Y == 360, "Projection respects viewport dimensions");
            identity[15] = -1; Require(!Projection.WorldToScreen(identity, 0, 0, 0, new Size(1920,1080), out point), "Behind-camera points are rejected");
            identity[15] = 1; Require(!Projection.WorldToScreen(identity, Single.NaN, 0, 0, new Size(1920,1080), out point), "Non-finite coordinates are rejected");
        }
        private static void MemoryChecks()
        {
            byte[] fixture = BitConverter.GetBytes(0x1234567812345678L); GCHandle pin = GCHandle.Alloc(fixture, GCHandleType.Pinned);
            try
            {
                using (MemoryReader memory = OwnMemory())
                {
                    int value; long address = pin.AddrOfPinnedObject().ToInt64();
                    Require(memory.TryInt32(address, out value) && value == 0x12345678, "Native read preserves a known 32-bit value");
                    Require(memory.Pointer(address) == 0x1234567812345678L, "Native read preserves a known 64-bit value");
                    byte[] invalid; Require(!memory.TryBytes(1, 4, out invalid), "Unreadable native addresses fail gracefully");
                }
            }
            finally { pin.Free(); }
            byte[] entityFixture = new byte[64 + 512 * 0x78]; pin = GCHandle.Alloc(entityFixture, GCHandleType.Pinned);
            try
            {
                long root = pin.AddrOfPinnedObject().ToInt64(), page = root + 64;
                Buffer.BlockCopy(BitConverter.GetBytes(page), 0, entityFixture, 0x10, 8);
                for (int i = 1; i <= 64; i++) Buffer.BlockCopy(BitConverter.GetBytes(0x123400000000L + i), 0, entityFixture, 64 + i * 0x78, 8);
                using (MemoryReader memory = OwnMemory())
                {
                    EntityPages pages = new EntityPages(memory); pages.BeginFrame(root); bool correct = true;
                    for (int i = 1; i <= 64; i++) if (pages.Get(i, 0x78) != 0x123400000000L + i) correct = false;
                    Require(correct && memory.ReadCalls == 2, "64 entity lookups use two native reads per page");
                    Buffer.BlockCopy(BitConverter.GetBytes(0x567800000001L), 0, entityFixture, 64 + 0x78, 8);
                    Require(pages.Get(1, 0x78) == 0x123400000001L, "An entity page is stable within one sample");
                    pages.BeginFrame(root);
                    Require(pages.Get(1, 0x78) == 0x567800000001L && memory.ReadCalls == 4, "The next sample refreshes entity data");
                }
            }
            finally { pin.Free(); }
        }
        private static void InputChecks()
        {
            Require(Marshal.SizeOf(typeof(Native.Input)) == 40 && Marshal.SizeOf(typeof(Native.InputUnion)) == 32, "SendInput uses the Windows x64 INPUT layout");
            Require(Marshal.SizeOf(typeof(Native.KeyboardInput)) == 24 && Marshal.OffsetOf(typeof(Native.Input), "Data").ToInt32() == 8, "Keyboard input and union alignment match the Windows ABI");
            Native.Input down = InputSender.Key(0, 0x39, 8), up = InputSender.Key(0, 0x39, 10);
            Require(down.Type == 1 && down.Data.Keyboard.Scan == 0x39 && up.Data.Keyboard.Flags == 10, "Jump packets form a Space press and release pair without sending input");
            SpaceState key = new SpaceState(); key.Observe(true, false); key.Observe(false, true);
            Require(key.Held, "Injected releases do not erase a held physical Space key");
            key.Observe(false, false); key.Observe(true, true); Require(!key.Held, "Injected presses cannot enable held-Space jumping");
            RepeatGate repeat = new RepeatGate();
            Require(!repeat.Pulse(false, true, true, true, 100, 20) && !repeat.Pulse(true, false, true, true, 100, 20) && !repeat.Pulse(true, true, false, true, 100, 20) && !repeat.Pulse(true, true, true, false, 100, 20), "Repeating input requires enable, focus, life and activation");
            Require(repeat.Pulse(true, true, true, true, 100, 20) && !repeat.Pulse(true, true, true, true, 119, 20) && repeat.Pulse(true, true, true, true, 120, 20), "Repeating input respects its configured interval");
            repeat.Pulse(true, false, true, true, 121, 20);
            Require(repeat.Pulse(true, true, true, true, 122, 20), "Focus loss resets the repeat schedule without a delayed burst");
        }
        private static void AimChecks()
        {
            AppSettings settings = new AppSettings(); Size size = new Size(1000, 800);
            Frame frame = new Frame { Alive = true, AnglesValid = true, Eye = new Vec3(0,0,64),
                Matrix = new float[] {0,-1,0,0, 0,0,1,-64, 0,0,1,0, 1,0,0,0} };
            Player enemy = new Player { Id = 10, Position = new Vec3(1000,-100,0), Health = 100, Distance = 25, SpottedByLocal = true };
            frame.Players.Add(enemy);
            AimSolution solution = new AimEngine().Step(frame, settings, size, 1.0 / 120, 0);
            Require(solution != null && solution.Target == enemy && solution.Move.X > 0 && solution.Move.X <= settings.AimMaxStep, "Aim selects an in-FOV enemy and moves in the correct bounded direction");
            enemy.Teammate = true; Require(AimEngine.Select(frame, settings, size, 0, 0) == null, "Aim excludes teammates"); enemy.Teammate = false;
            enemy.SpottedByLocal = false; Require(AimEngine.Select(frame, settings, size, 0, 0) == null, "The local spotted filter rejects unspotted targets"); enemy.SpottedByLocal = true;
            settings.AimRadius = 10; Require(AimEngine.Select(frame, settings, size, 0, 0) == null, "Aim does not select beyond its FOV radius"); settings.AimRadius = 140;
            settings.AimDistance = 10; Require(AimEngine.Select(frame, settings, size, 0, 0) == null, "Aim respects its target distance limit"); settings.AimDistance = 200;
            Frame dead = new Frame { Matrix = frame.Matrix, Eye = frame.Eye, AnglesValid = true }; dead.Players.Add(enemy);
            solution = new AimEngine().Step(dead, settings, size, 0.01, 0); Require(solution != null && solution.Move == Point.Empty, "A dead local player produces no aim movement");
            Require(AimEngine.Wrap(359) == -1 && AimEngine.Wrap(-359) == 1, "Aim uses the shortest yaw difference across angle wrap");
            Point spin = AimEngine.Spin(frame, settings, 1.0/120); Require(spin.X > 0 && spin.Y > 0, "Spin produces rotation and downward pitch correction");
            frame.Alive = false; Require(AimEngine.Spin(frame, settings, 1.0/120) == Point.Empty, "Spin stops without a live local player"); frame.Alive = true;
            frame.CrosshairPawn = enemy.Id; Require(InputPolicy.CanFire(frame), "Auto-fire accepts a spotted enemy identified by the game crosshair");
            enemy.Teammate = true; Require(!InputPolicy.CanFire(frame), "Auto-fire excludes a teammate at the crosshair"); enemy.Teammate = false;
            enemy.SpottedByLocal = false; Require(!InputPolicy.CanFire(frame), "Auto-fire excludes an unspotted crosshair entity");
            enemy.SpottedByLocal = true; frame.CrosshairPawn = 0; Require(!InputPolicy.CanFire(frame), "Auto-fire does not click merely because an enemy exists on screen");
        }
        private static void DataAndProfileChecks()
        {
            byte[] bones = new byte[28 * 32];
            for (int i = 0; i < 28; i++) Buffer.BlockCopy(BitConverter.GetBytes(i == 6 ? 64f : 32f), 0, bones, i * 32 + 8, 4);
            Require(BoneData.Decode(bones, new Vec3()) != null, "Plausible world-space bone positions decode");
            Buffer.BlockCopy(BitConverter.GetBytes(Single.NaN), 0, bones, 6 * 32, 4);
            Require(BoneData.Decode(bones, new Vec3()) == null, "Invalid bone layouts are rejected instead of drawn");
            PointF p = RadarProjection.Point(new Vec3(100,0,0), new Vec3(), 0, true, 60, new RectangleF(0,0,200,200));
            Require(Math.Abs(p.X - 100) < 0.01 && p.Y < 100, "Rotating radar places forward targets ahead of the player");
            p = RadarProjection.Point(new Vec3(100000,0,0), new Vec3(), 0, true, 60, new RectangleF(0,0,200,200));
            Require(p.Y >= 10 && p.Y <= 190, "Radar clamps distant targets to its edge");
            AppSettings settings = new AppSettings { CameraFov = 999, ThirdPerson = true, CustomFov = true, AimEnabled = true, AssistEnabled = true, BhopEnabled = true, SpinEnabled = true };
            AppSettings restored = Profiles.Decode(Profiles.Encode(settings));
            Require(restored.CameraFov == 200 && restored.ThirdPerson, "Profiles retain camera settings and clamp the FOV request to 200");
            Require(!restored.AimEnabled && !restored.AssistEnabled && !restored.BhopEnabled && !restored.SpinEnabled, "Loading a profile keeps all input automation disabled");
            string command = PracticeCamera.Command(restored);
            Require(command.Contains("thirdperson") && command.Contains("fov_cs_debug 200") && !command.Contains("sv_cheats"), "Camera commands request the configured view without enabling server cheats");
            restored.ThirdPerson = restored.CustomFov = false;
            Require(PracticeCamera.Command(restored) == "firstperson; c_minpitch 0; c_maxpitch 90; c_maxdistance 200; fov_cs_debug 0", "Camera restore requests first person, default camera bounds and default FOV");
            bool rejected = false; restored.EspBind = restored.MenuBind;
            try { restored.Validate(); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Duplicate global keybinds are rejected");
        }
        private static void Put(byte[] data, int offset, byte[] value) { Buffer.BlockCopy(value, 0, data, offset, value.Length); }
        private static void FlashChecks()
        {
            byte[] data = new byte[512]; GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                long address = pin.AddrOfPinnedObject().ToInt64(), pawn = address + 128;
                Put(data, 0, BitConverter.GetBytes(pawn)); Put(data, 144, BitConverter.GetBytes(255f));
                int process = Process.GetCurrentProcess().Id; string error;
                using (FlashLimiter flash = new FlashLimiter())
                {
                    Require(flash.Apply(process, pawn, address, 16, 100, out error) && BitConverter.ToSingle(data, 144) == 100,
                        "Flash limit writes the selected opacity in the test process only");
                    Require(flash.Apply(process, pawn, address, 16, 180, out error) && BitConverter.ToSingle(data, 144) == 180,
                        "Raising the flash limit retains the original opacity for restoration");
                    flash.Disable();
                    Require(BitConverter.ToSingle(data, 144) == 255, "Disabling flash limit restores its own previous value");
                    Put(data, 144, BitConverter.GetBytes(70f));
                    Require(flash.Apply(process, pawn, address, 16, 100, out error) && BitConverter.ToSingle(data, 144) == 70,
                        "Flash limit never increases an externally lower opacity");
                    flash.Disable(); Put(data, 144, BitConverter.GetBytes(255f));
                    flash.Apply(process, pawn, address, 16, 100, out error); Put(data, 144, BitConverter.GetBytes(80f)); flash.Disable();
                    Require(BitConverter.ToSingle(data, 144) == 80, "Flash restoration preserves a value changed by the engine or another owner");
                    Put(data, 144, BitConverter.GetBytes(255f)); flash.Apply(process, pawn, address, 16, 0, out error);
                    Require(BitConverter.ToSingle(data, 144) == 0, "Zero is accepted as the minimum flash-white limit");
                    Require(flash.Apply(process, pawn, address, 16, 255, out error) && BitConverter.ToSingle(data, 144) == 255,
                        "The normal-strength setting restores and releases the flash writer");
                    flash.Apply(process, pawn, address, 16, 100, out error); Put(data, 0, BitConverter.GetBytes(pawn + 32)); flash.Disable();
                    Require(BitConverter.ToSingle(data, 144) == 100, "A replaced local pawn prevents stale flash restoration writes");
                    Put(data, 0, BitConverter.GetBytes(pawn)); Put(data, 144, BitConverter.GetBytes(Single.NaN));
                    Require(!flash.Apply(process, pawn, address, 16, 100, out error), "Invalid flash data disables the writer without a write");
                    Require(!flash.Apply(process, pawn, address, 0, 100, out error), "Missing flash offsets fail before opening write access");
                }
                AppSettings saved = Profiles.Decode(Profiles.Encode(new AppSettings { FlashLimitEnabled = true, FlashWhiteLimit = -10 }));
                Require(!saved.FlashLimitEnabled && saved.FlashWhiteLimit == 0, "Profiles clamp flash opacity and reload with the memory-writing option off");
            }
            finally { pin.Free(); }
        }
        private static void NewFeatureChecks()
        {
            AppSettings settings = new AppSettings { EspEnabled = false, Hud = false, Crosshair = false, DrawFov = true };
            Require(!settings.AimEnabled && !settings.AssistEnabled && OverlayOptions.ShouldDraw(settings), "The FOV circle keeps the overlay visible while all aim input is off");
            settings.DrawFov = false; settings.BombInfo = true;
            Require(OverlayOptions.ShouldDraw(settings), "Bomb information can display while player wallhack is off");
            settings.BombInfo = false; Require(!OverlayOptions.ShouldDraw(settings), "Disabling the new displays permits the overlay to hide");
            settings.NoKeybinds = true; bool allBlocked = true;
            foreach (int id in new int[] { Native.ToggleHotkey, Native.AimHotkey, Native.AssistHotkey, Native.JumpHotkey, Native.SpinHotkey, Native.QuitHotkey, Native.PanicHotkey })
                if (KeyOptions.ActionEnabled(settings, id)) allBlocked = false;
            Require(allBlocked && KeyOptions.ActionEnabled(settings, Native.MenuHotkey), "NO KEYBINDS permits only the menu action, including queued shortcuts");
            settings.SpinEnabled = true;
            Frame frame = new Frame { Alive = true, WeaponKnown = true, WeaponDefinition = WeaponReader.Scout };
            Require(InputPolicy.SpinAllowed(settings, frame), "Scout spin is eligible with a live SSG 08 equipped");
            frame.WeaponDefinition = 9; Require(!InputPolicy.SpinAllowed(settings, frame), "Scout spin is inactive with an AWP or other weapon");
            frame.WeaponDefinition = WeaponReader.Scout; frame.WeaponKnown = false;
            Require(!InputPolicy.SpinAllowed(settings, frame), "Unknown weapon data cannot activate spin");
            string command = PracticeCamera.SpinCommand(settings, -20);
            Require(command.StartsWith("thirdperson;") && command.Contains("cam_idealdist 150") && command.Contains("cam_collision 1") && command.Contains("cam_idealpitch 32") && command.Contains("c_minpitch -180") && !command.Contains("sv_cheats"),
                "Spin enters third person with chase distance, collision and pitch independent of the player's look angle");
            Require(PracticeCamera.SteadyChaseCommand == "thirdperson_mayamode" && !command.Contains("thirdperson_mayamode"),
                "Steady orbit is a separate stage after the chase camera settles");
            settings.SpinCameraDistance = 999; settings.SpinCameraPitch = -999; settings.Validate();
            Require(settings.SpinCameraDistance == 400 && settings.SpinCameraPitch == -60,
                "Spin chase distance and pitch are bounded independently of the spin look-down pitch");
            bool cancelled = false;
            try { InputSender.CameraCommand(IntPtr.Zero, 0, "thirdperson", delegate { return false; }).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled, "Cancelled camera preparation stops before submitting any native input");
            SpinCameraMonitor camera = new SpinCameraMonitor();
            Frame cameraFrame = new Frame { AnglesValid = true, Matrix = new float[16] }; cameraFrame.Matrix[12] = 1;
            Require(camera.Observe(cameraFrame) == SpinCameraState.Pending, "Camera verification starts without assuming the console command succeeded");
            cameraFrame.Yaw = 40;
            Require(camera.Observe(cameraFrame) == SpinCameraState.Steady && camera.Verified, "A stationary rendered camera is distinguished from a turning player");
            camera.Reset(); cameraFrame.Yaw = 0; cameraFrame.Matrix[12] = 1; cameraFrame.Matrix[13] = 0; camera.Observe(cameraFrame);
            cameraFrame.Yaw = 40; cameraFrame.Matrix[12] = (float)Math.Cos(40 * Math.PI / 180); cameraFrame.Matrix[13] = (float)Math.Sin(40 * Math.PI / 180);
            Require(camera.Observe(cameraFrame) == SpinCameraState.FollowingView, "A camera that spins with the view is rejected instead of silently accepted");

            byte[] data = new byte[4096]; GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                long address = pin.AddrOfPinnedObject().ToInt64();
                Offsets o = new Offsets { PlantedC4 = 0x100, GlobalVars = 0x108, SceneNode = 0x20, Origin = 0x10,
                    BombTicking = 0x80, BombSite = 0x84, BombBlowTime = 0x88, BombTimerLength = 0x8C,
                    BombDefusing = 0x90, BombDefuseLength = 0x94, BombDefuseEnd = 0x98, BombDefused = 0x9C, BombExploded = 0x9D, BombCannotDefuse = 0x9E };
                Put(data, 0x100, BitConverter.GetBytes(address + 0x180)); Put(data, 0x180, BitConverter.GetBytes(address + 0x200));
                Put(data, 0x108, BitConverter.GetBytes(address + 0x600)); Put(data, 0x630, BitConverter.GetBytes(100f));
                Put(data, 0x220, BitConverter.GetBytes(address + 0x500)); Put(data, 0x510, BitConverter.GetBytes(100f)); Put(data, 0x514, BitConverter.GetBytes(200f));
                data[0x280] = 1; Put(data, 0x284, BitConverter.GetBytes(1)); Put(data, 0x288, BitConverter.GetBytes(130f)); Put(data, 0x28C, BitConverter.GetBytes(40f));
                using (MemoryReader memory = OwnMemory())
                {
                    BombSnapshot bomb = BombReader.Read(memory, address, o);
                    Require(bomb != null && bomb.Site == "B" && bomb.PositionValid && bomb.Position.X == 100 && bomb.TimerKnown && bomb.SecondsLeft == 30, "Native planted-C4 reads resolve the site, location and game-clock countdown");
                    Require(bomb.LatestStart(true) == 25 && bomb.LatestStart(false) == 20, "Kit and normal defuse budgets use five and ten seconds");
                    Put(data, 0x630, BitConverter.GetBytes(100f));
                    Require(BombReader.Read(memory, address, o).SecondsLeft == 30, "A paused game clock leaves the bomb countdown unchanged");
                    Put(data, 0x630, BitConverter.GetBytes(102f)); bomb = BombReader.Read(memory, address, o);
                    Require(bomb.SecondsLeft == 28, "Bomb countdown advances using game time rather than desktop time");
                    data[0x290] = 1; Put(data, 0x294, BitConverter.GetBytes(5f)); Put(data, 0x298, BitConverter.GetBytes(106f));
                    bomb = BombReader.Read(memory, address, o);
                    Require(bomb.DefuseTimerKnown && bomb.DefuseSeconds == 4 && bomb.DefuseInTime, "An active defuse has a separate four-second countdown and completion result");
                    Put(data, 0x288, BitConverter.GetBytes(104f)); bomb = BombReader.Read(memory, address, o);
                    Require(!bomb.DefuseInTime && bomb.StartLabel(true) == "too late", "An impossible defuse is marked too late");
                    Put(data, 0x630, BitConverter.GetBytes(Single.NaN)); bomb = BombReader.Read(memory, address, o);
                    Require(bomb.PositionValid && !bomb.TimerKnown && !bomb.DefuseTimerKnown, "An invalid game clock cannot produce a fabricated countdown");
                    data[0x280] = 0; data[0x29C] = 1; bomb = BombReader.Read(memory, address, o);
                    Require(bomb.Phase == BombPhase.Defused, "Defused bombs are identified as finished");
                    data[0x29C] = 0; data[0x29D] = 1; Require(BombReader.Read(memory, address, o).Phase == BombPhase.Exploded, "Exploded bombs are identified as finished");
                    data[0x29D] = 0; Require(BombReader.Read(memory, address, o) == null, "An inactive C4 does not produce a planted-bomb marker");
                    data[0x280] = 1; Put(data, 0x100, BitConverter.GetBytes(address + 0x200));
                    Require(BombReader.Read(memory, address, o) != null, "Validated direct planted-C4 pointers are supported");
                }
            }
            finally { pin.Free(); }
            data = new byte[65536]; pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                long address = pin.AddrOfPinnedObject().ToInt64();
                Offsets o = new Offsets { WeaponServices = 0x10, ActiveWeapon = 0x20, AttributeManager = 0x10, Item = 0x10, ItemDefinition = 2,
                    ShotsFired = 0x30, AimPunchServices = 0x38, PredictablePunch = 0x20 };
                Put(data, 0x10, BitConverter.GetBytes(address + 0x100)); Put(data, 0x100 + 2 * 0x78, BitConverter.GetBytes(address + 0xF600));
                Put(data, 0xF210, BitConverter.GetBytes(address + 0xF400)); Put(data, 0xF420, BitConverter.GetBytes(2));
                Put(data, 0xF622, BitConverter.GetBytes((ushort)WeaponReader.Scout));
                Put(data, 0xF230, BitConverter.GetBytes(5)); Put(data, 0xF238, BitConverter.GetBytes(address + 0xF800));
                Put(data, 0xF820, BitConverter.GetBytes(-2f)); Put(data, 0xF824, BitConverter.GetBytes(0.5f));
                using (MemoryReader memory = OwnMemory())
                {
                    EntityPages pages = new EntityPages(memory); pages.BeginFrame(address); ushort definition;
                    Require(WeaponReader.Read(memory, pages, address + 0xF200, o, 0x78, out definition) && definition == WeaponReader.Scout, "The active-weapon handle resolves a native SSG 08 item definition");
                    frame = new Frame { Alive = true }; RecoilReader.Read(memory, address + 0xF200, o, frame);
                    Require(frame.PunchKnown && frame.ShotsFired == 5 && frame.Punch.X == -2 && frame.Punch.Y == 0.5f, "Recoil reads use the current AimPunchServices component");
                    Put(data, 0xF820, BitConverter.GetBytes(Single.NaN)); Frame invalid = new Frame(); RecoilReader.Read(memory, address + 0xF200, o, invalid);
                    Require(!invalid.PunchKnown, "Non-finite recoil data is rejected");
                }
            }
            finally { pin.Free(); }
            settings = new AppSettings { AimMaxStep = 500 };
            RecoilCompensation correction = new RecoilCompensation();
            Point move = correction.Step(frame, settings, true, true);
            Require(move.Y > 0 && move.X > 0, "Recoil compensation pulls downward and counters horizontal kick");
            Require(correction.Step(frame, settings, true, true) == Point.Empty, "Stable recoil does not cause repeated extra correction");
            Require(correction.Step(frame, settings, false, true) == Point.Empty, "Recoil correction stops when firing stops");
            settings.RecoilControl = false; Require(correction.Step(frame, settings, true, true) == Point.Empty, "The recoil toggle disables mouse compensation");
            AppSettings saved = Profiles.Decode(Profiles.Encode(new AppSettings { BombInfo = true, NoKeybinds = true, RecoilStrength = 0.5m }));
            Require(saved.BombInfo && saved.NoKeybinds && saved.RecoilStrength == 0.5m && !saved.SpinEnabled, "Profiles retain new visual, keybind and recoil settings with automation off");
        }
        private static void OverlayChecks(string directory)
        {
            AppSettings settings = new AppSettings { EspEnabled = false, Hud = false, Crosshair = false, DrawFov = true, Antialias = false };
            Frame frame = new Frame { Matrix = new float[] {1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1} };
            using (OverlayForm overlay = new OverlayForm())
            {
                overlay.Present(frame, new Rectangle(0,0,640,480), settings, null, "preview"); Application.DoEvents();
                using (Bitmap screenshot = new Bitmap(640,480))
                {
                    overlay.DrawToBitmap(screenshot, new Rectangle(0,0,640,480)); bool ring = false;
                    for (int x = 457; x <= 463; x++) { Color pixel = screenshot.GetPixel(x, 240); if (pixel.G > pixel.R && pixel.G > pixel.B) ring = true; }
                    Require(ring, "The renderer draws the FOV circle with aim and assist disabled");
                    screenshot.Save(Path.Combine(directory, "FOV-circle.png"), ImageFormat.Png);
                }
                settings.DrawFov = false; settings.BombInfo = true;
                frame.Bomb = new BombSnapshot { Phase = BombPhase.Planted, Site = "B", PositionValid = true, Position = new Vec3(0.5f,0,0),
                    TimerKnown = true, SecondsLeft = 23.4f, TimerLength = 40, BeingDefused = true, DefuseTimerKnown = true, DefuseSeconds = 3.2f, DefuseInTime = true };
                overlay.Present(frame, new Rectangle(0,0,640,480), settings, null, "preview"); Application.DoEvents();
                using (Bitmap screenshot = new Bitmap(640,480))
                {
                    overlay.DrawToBitmap(screenshot, new Rectangle(0,0,640,480));
                    Require(screenshot.GetPixel(320,100).ToArgb() == Color.Black.ToArgb(), "Bomb information renders without a live player or player wallhack");
                    screenshot.Save(Path.Combine(directory, "Bomb-timer.png"), ImageFormat.Png);
                }
            }
        }
        private static void UiChecks(string output)
        {
            using (ControllerForm form = new ControllerForm(true))
            {
                Require(form.Text == "ModafenClientXZERO" && form.PageCount == 8, "The branded green client initializes all eight pages offline");
                HashSet<string> bound = new HashSet<string>(form.BoundSettings); bool complete = true;
                foreach (PropertyInfo property in typeof(AppSettings).GetProperties()) if (!bound.Contains(property.Name)) complete = false;
                Require(complete && bound.Count >= 30, "Every exposed setting has a GUI control");
                form.Show(); Application.DoEvents();
                Require(form.IsHandleCreated && form.Handle != IntPtr.Zero, "The native controller window opens without CS2 or network access");
                Control[] boxes = form.Controls.Find("setting.Boxes", true);
                Require(boxes.Length == 1 && boxes[0] is CheckBox, "The independent box control is present");
                ((CheckBox)boxes[0]).Checked = false;
                Require(!form.Configuration.Boxes, "Changing a GUI control updates the runtime configuration");
                Require(form.Controls.Find("risk.aim", true).Length == 1 && form.Controls.Find("risk.spin", true)[0].Text.StartsWith("HIGHER RISK"), "Aim and spin pages display the requested risk labels");
                int ticks = 0;
                using (PacedPulse pulse = new PacedPulse(form, delegate { ticks++; }))
                {
                    pulse.Start(8); Stopwatch deadline = Stopwatch.StartNew();
                    while (deadline.ElapsedMilliseconds < 100) { Application.DoEvents(); Thread.Sleep(1); }
                }
                int before = ticks; Thread.Sleep(20); Application.DoEvents();
                Require(ticks > 0 && ticks == before, "Paced UI updates run and stop cleanly after disposal");
                Control[] noKeys = form.Controls.Find("setting.NoKeybinds", true);
                ((CheckBox)noKeys[0]).Checked = true;
                Require(form.Configuration.NoKeybinds && !form.Controls.Find("setting.EspBind", true)[0].Enabled && form.Controls.Find("setting.MenuBind", true)[0].Enabled,
                    "NO KEYBINDS disables action selectors and preserves the menu key");
                Require(form.FormBorderStyle == FormBorderStyle.None && form.Region != null,
                    "The custom client window has rounded geometry and custom window controls");
                Control closeButton = form.Controls.Find("window.Close", true)[0];
                Require(closeButton.Parent.ClientRectangle.Contains(closeButton.Bounds) && closeButton.Parent.Parent.ClientRectangle.Contains(closeButton.Parent.Bounds),
                    "The complete close button remains inside the visible header");
                string directory = Path.Combine(Path.GetDirectoryName(output), "previews"); Directory.CreateDirectory(directory);
                foreach (string page in new string[] { "Dashboard", "Wallhack", "Aimbot + assist", "Spinbot", "Practice camera", "Keybinds" })
                {
                    form.SelectPage(page); form.PerformLayout(); Application.DoEvents();
                    using (Bitmap screenshot = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(screenshot, new Rectangle(0,0,form.Width,form.Height));
                        screenshot.Save(Path.Combine(directory, page.Replace(" + ", "-").Replace(" ", "-") + ".png"), ImageFormat.Png);
                    }
                }
                OverlayChecks(directory);
                Require(Directory.GetFiles(directory, "*.png").Length == 8, "Six GUI pages and two overlay previews render successfully");
                form.Close();
            }
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string output = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "modafen-smoke.json");
                Require(IntPtr.Size == 8, "The build runs as a 64-bit process");
                ProjectionChecks(); MemoryChecks(); InputChecks(); AimChecks(); DataAndProfileChecks(); NewFeatureChecks(); FlashChecks();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); UiChecks(output);
                string json = new JavaScriptSerializer().Serialize(new { success = true, checks = passed.ToArray(), settings_controls = typeof(AppSettings).GetProperties().Length,
                    limitations = "CS2 is unavailable on this runner. Live offsets, gameplay, input acceptance and server camera permissions are not tested." });
                File.WriteAllText(output, json); Console.WriteLine(json); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        }
    }
}

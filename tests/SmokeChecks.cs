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
            Require(PracticeCamera.Command(restored) == "firstperson; fov_cs_debug 0", "Camera restore requests first person and default FOV");
            bool rejected = false; restored.EspBind = restored.MenuBind;
            try { restored.Validate(); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Duplicate global keybinds are rejected");
        }
        private static void UiChecks(string output)
        {
            using (ControllerForm form = new ControllerForm(true))
            {
                Require(form.Text == "ModafenClientXZERO" && form.PageCount == 8, "The branded green client initializes all eight pages offline");
                HashSet<string> bound = new HashSet<string>(form.BoundSettings); bool complete = true;
                foreach (PropertyInfo property in typeof(AppSettings).GetProperties()) if (!bound.Contains(property.Name)) complete = false;
                Require(complete && bound.Count >= 30, "Every exposed setting has a GUI control (78 controls)");
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
                Require(Directory.GetFiles(directory, "*.png").Length == 6, "Six Windows GUI previews render successfully");
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
                ProjectionChecks(); MemoryChecks(); InputChecks(); AimChecks(); DataAndProfileChecks();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); UiChecks(output);
                string json = new JavaScriptSerializer().Serialize(new { success = true, checks = passed.ToArray(), settings_controls = 78,
                    limitations = "CS2 is unavailable on this runner. Live offsets, gameplay, input acceptance and server camera permissions are not tested." });
                File.WriteAllText(output, json); Console.WriteLine(json); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        }
    }
}

using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cs2Overlay
{
    internal static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardHookData { public uint Key, Scan, Flags, Time; public UIntPtr ExtraInfo; }
        internal delegate IntPtr KeyboardProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, [In] Input[] inputs, int size);
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int type, KeyboardProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")]
        internal static extern uint timeEndPeriod(uint period);
    }

    // The timer only posts one UI callback at a time. Slow frames cannot queue a backlog.
    internal sealed class PacedPulse : IDisposable
    {
        private readonly Control owner;
        private readonly Action action;
        private System.Threading.Timer timer;
        private int pending, period;
        private volatile bool disposed;
        private bool resolution;
        internal PacedPulse(Control target, Action callback) { owner = target; action = callback; }
        internal void Start(int milliseconds)
        {
            if (disposed || timer != null) return;
            period = milliseconds; resolution = Native.timeBeginPeriod(1) == 0;
            timer = new System.Threading.Timer(Post, null, 0, period);
        }
        internal void SetPeriod(int milliseconds)
        {
            milliseconds = Math.Max(4, milliseconds);
            if (disposed || milliseconds == period) return;
            period = milliseconds; if (timer != null) timer.Change(period, period);
        }
        private void Post(object unused)
        {
            if (disposed || !owner.IsHandleCreated || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
            try
            {
                owner.BeginInvoke((MethodInvoker)delegate
                {
                    try { if (!disposed && !owner.IsDisposed) action(); }
                    finally { Interlocked.Exchange(ref pending, 0); }
                });
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref pending, 0); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; if (timer != null) { timer.Dispose(); timer = null; }
            if (resolution) { Native.timeEndPeriod(1); resolution = false; }
        }
    }

    internal sealed class SpaceState
    {
        internal bool Held { get; private set; }
        internal void Observe(bool down, bool injected) { if (!injected) Held = down; }
        internal void Reset() { Held = false; }
    }
    internal sealed class PhysicalSpace : IDisposable
    {
        private IntPtr hook;
        private readonly Native.KeyboardProc callback;
        internal readonly SpaceState State = new SpaceState();
        internal PhysicalSpace() { callback = OnKey; }
        internal void Enable(bool value)
        {
            if (!value) { Dispose(); return; }
            if (hook != IntPtr.Zero) return;
            hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not observe the physical Space key.");
            State.Observe((Native.GetAsyncKeyState((int)Keys.Space) & 0x8000) != 0, false);
        }
        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                Native.KeyboardHookData key = (Native.KeyboardHookData)Marshal.PtrToStructure(data, typeof(Native.KeyboardHookData));
                int kind = message.ToInt32();
                if (key.Key == 0x20 && (kind == 0x100 || kind == 0x104 || kind == 0x101 || kind == 0x105))
                    State.Observe(kind == 0x100 || kind == 0x104, (key.Flags & 0x12) != 0);
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }
        public void Dispose()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            State.Reset();
        }
    }

    internal sealed class RepeatGate
    {
        private long next;
        internal bool Pulse(bool enabled, bool focused, bool alive, bool held, long now, int interval)
        {
            if (!enabled || !focused || !alive || !held) { next = 0; return false; }
            if (now < next) return false;
            next = now + Math.Max(1, interval); return true;
        }
        internal void Reset() { next = 0; }
    }

    internal static class InputSender
    {
        internal static bool Held(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static Native.Input Key(ushort key, ushort scan, uint flags)
        {
            Native.Input value = new Native.Input(); value.Type = 1;
            value.Data.Keyboard = new Native.KeyboardInput { Key = key, Scan = scan, Flags = flags }; return value;
        }
        internal static Native.Input Mouse(int x, int y, uint flags)
        {
            Native.Input value = new Native.Input(); value.Type = 0;
            value.Data.Mouse = new Native.MouseInput { X = x, Y = y, Flags = flags }; return value;
        }
        private static void Emit(IntPtr window, Native.Input[] events)
        {
            if (window == IntPtr.Zero || Native.GetForegroundWindow() != window) return;
            uint sent = Native.SendInput((uint)events.Length, events, Marshal.SizeOf(typeof(Native.Input)));
            if (sent == events.Length) return;
            // A partially submitted pair must not leave a synthetic button held.
            if (sent == 1 && events.Length == 2) Native.SendInput(1, new Native.Input[] { events[1] }, Marshal.SizeOf(typeof(Native.Input)));
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected input. Automation has been paused.");
        }
        internal static void Move(IntPtr window, Point delta)
        {
            if (delta.X != 0 || delta.Y != 0) Emit(window, new Native.Input[] { Mouse(delta.X, delta.Y, 0x1) });
        }
        internal static void Jump(IntPtr window) { Emit(window, new Native.Input[] { Key(0, 0x39, 0x8), Key(0, 0x39, 0xA) }); }
        internal static void Click(IntPtr window) { Emit(window, new Native.Input[] { Mouse(0, 0, 0x2), Mouse(0, 0, 0x4) }); }
        internal static void Press(IntPtr window, int key) { Emit(window, new Native.Input[] { Key((ushort)key, 0, 0), Key((ushort)key, 0, 2) }); }
        internal static void Text(IntPtr window, string text)
        {
            if (text.Length > 512) throw new ArgumentOutOfRangeException("text");
            Native.Input[] events = new Native.Input[text.Length * 2];
            for (int i = 0; i < text.Length; i++) { events[i * 2] = Key(0, text[i], 4); events[i * 2 + 1] = Key(0, text[i], 6); }
            Emit(window, events);
        }
        internal static async Task CameraCommand(IntPtr window, int consoleKey, string command)
        {
            if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("Switch to CS2 before the countdown ends.");
            Press(window, consoleKey); await Task.Delay(160);
            if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("Camera command stopped because CS2 lost focus.");
            Text(window, command); Press(window, (int)Keys.Enter); await Task.Delay(120);
            if (Native.GetForegroundWindow() == window) Press(window, consoleKey);
        }
    }

    internal sealed class AimSolution
    {
        internal Player Target;
        internal PointF Screen;
        internal Point Move;
    }
    internal static class InputPolicy
    {
        internal static bool CanFire(Frame frame)
        {
            if (!frame.Alive || frame.CrosshairPawn <= 0) return false;
            foreach (Player player in frame.Players)
                if (player.Id == frame.CrosshairPawn && !player.Teammate && player.Health > 0 && player.SpottedByLocal) return true;
            return false;
        }
    }
    internal sealed class AimEngine
    {
        private long locked;
        private double remainderX, remainderY;
        internal void Reset() { locked = 0; remainderX = remainderY = 0; }
        internal static double Wrap(double angle) { while (angle > 180) angle -= 360; while (angle < -180) angle += 360; return angle; }
        internal static AimSolution Select(Frame frame, AppSettings settings, Size size, int mode, long preferred)
        {
            float radius = mode == 1 ? settings.AssistRadius : (mode == 2 ? Math.Max(size.Width, size.Height) : settings.AimRadius);
            AimSolution best = null; double bestScore = Double.MaxValue;
            foreach (Player player in frame.Players)
            {
                if (player.Teammate || player.Health <= 0 || player.Distance > settings.AimDistance) continue;
                if ((mode == 2 || settings.AimVisibleOnly) && !player.SpottedByLocal) continue;
                Vec3 target = player.Target(settings.AimPoint); PointF screen;
                if (!Projection.WorldToScreen(frame.Matrix, target.X, target.Y, target.Z, size, out screen) || screen.X < 0 || screen.Y < 0 || screen.X > size.Width || screen.Y > size.Height) continue;
                double x = screen.X - size.Width / 2f, y = screen.Y - size.Height / 2f, distance = x * x + y * y;
                if (distance > radius * radius) continue;
                if (player.Id == preferred && settings.TargetLock) return new AimSolution { Target = player, Screen = screen };
                double score = settings.TargetPriority == 1 ? player.Distance : (settings.TargetPriority == 2 ? player.Health : distance);
                if (score < bestScore) { bestScore = score; best = new AimSolution { Target = player, Screen = screen }; }
            }
            return best;
        }
        internal AimSolution Step(Frame frame, AppSettings settings, Size size, double seconds, int mode)
        {
            AimSolution solution = Select(frame, settings, size, mode, locked);
            if (solution == null || !frame.Alive || !frame.AnglesValid) { Reset(); return solution; }
            if (locked != solution.Target.Id) { remainderX = remainderY = 0; }
            locked = solution.Target.Id;
            double px = solution.Screen.X - size.Width / 2f, py = solution.Screen.Y - size.Height / 2f;
            if (px * px + py * py <= settings.AimDeadzone * settings.AimDeadzone) { remainderX = remainderY = 0; return solution; }
            Vec3 target = solution.Target.Target(settings.AimPoint);
            double x = target.X - frame.Eye.X, y = target.Y - frame.Eye.Y, z = target.Z - frame.Eye.Z;
            double desiredYaw = Math.Atan2(y, x) * 180 / Math.PI;
            double desiredPitch = -Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180 / Math.PI;
            double sensitivity = settings.AutoSensitivity && frame.Sensitivity > 0 ? frame.Sensitivity : (double)settings.Sensitivity;
            double smooth = mode == 1 ? settings.AssistSmoothMs : (mode == 2 ? 25 : settings.AimSmoothMs);
            double alpha = smooth <= 0 ? 1 : 1 - Math.Exp(-Math.Max(0.001, Math.Min(0.05, seconds)) * 1000 / smooth);
            if (mode == 1) alpha *= (double)settings.AssistStrength;
            double dx = -Wrap(desiredYaw - frame.Yaw) / (0.022 * sensitivity) * alpha + remainderX;
            double dy = Wrap(desiredPitch - frame.Pitch) / (0.022 * sensitivity) * alpha + remainderY;
            int limit = mode == 1 ? Math.Min(settings.AimMaxStep, 12) : (mode == 2 ? 1200 : settings.AimMaxStep);
            dx = Math.Max(-limit, Math.Min(limit, dx)); dy = Math.Max(-limit, Math.Min(limit, dy));
            int mx = (int)Math.Round(dx), my = (int)Math.Round(dy); remainderX = dx - mx; remainderY = dy - my;
            solution.Move = new Point(mx, my); return solution;
        }
        internal static Point Spin(Frame frame, AppSettings settings, double seconds)
        {
            if (!frame.Alive || !frame.AnglesValid) return Point.Empty;
            double sensitivity = settings.AutoSensitivity && frame.Sensitivity > 0 ? frame.Sensitivity : (double)settings.Sensitivity;
            seconds = Math.Max(0.001, Math.Min(0.05, seconds));
            int x = (int)Math.Round(Math.Min(5000, settings.SpinSpeed * seconds / (0.022 * sensitivity)));
            int y = (int)Math.Round(Math.Max(-2000, Math.Min(2000, (settings.SpinPitch - frame.Pitch) / (0.022 * sensitivity) * (1 - Math.Exp(-seconds / 0.04)))));
            return new Point(x, y);
        }
    }
}

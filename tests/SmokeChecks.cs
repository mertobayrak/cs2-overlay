using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Cs2Overlay
{
    internal static class SmokeChecks
    {
        private static readonly List<string> passed = new List<string>();
        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            passed.Add(message);
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Require(IntPtr.Size == 8, "Build runs as a 64-bit process");
                float[] identity = new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
                PointF point;
                Require(Projection.WorldToScreen(identity, 0, 0, 0, new Size(1920,1080), out point) &&
                    point.X == 960 && point.Y == 540, "World origin projects to viewport center");
                Require(Projection.WorldToScreen(identity, 0, 1, 0, new Size(1920,1080), out point) &&
                    point.X == 960 && point.Y == 0, "Positive world Y projects upward");
                Require(Projection.WorldToScreen(identity, 1, 0, 0, new Size(1280,720), out point) &&
                    point.X == 1280 && point.Y == 360, "Projection respects viewport dimensions");
                identity[15] = -1;
                Require(!Projection.WorldToScreen(identity, 0, 0, 0, new Size(1920,1080), out point),
                    "Points behind the camera are rejected");
                identity[15] = 1;
                Require(!Projection.WorldToScreen(identity, Single.NaN, 0, 0, new Size(1920,1080), out point),
                    "Non-finite coordinates are rejected");
                IntPtr handle = Native.OpenProcess(Native.ReadAccess, false, Process.GetCurrentProcess().Id);
                Require(handle != IntPtr.Zero, "Read-only process handle opens for the test process");
                byte[] fixture = BitConverter.GetBytes(0x1234567812345678L);
                GCHandle pin = GCHandle.Alloc(fixture, GCHandleType.Pinned);
                try
                {
                    using (MemoryReader memory = new MemoryReader(handle))
                    {
                        int value;
                        long address = pin.AddrOfPinnedObject().ToInt64();
                        Require(memory.TryInt32(address, out value) && value == 0x12345678,
                            "Native read preserves a known 32-bit value");
                        Require(memory.Pointer(address) == 0x1234567812345678L,
                            "Native read preserves a known 64-bit value");
                        byte[] invalid;
                        Require(!memory.TryBytes(1, 4, out invalid),
                            "An unreadable address returns failure without a crash");
                    }
                }
                finally { pin.Free(); }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (ControllerForm form = new ControllerForm())
                {
                    Require(form.Text == "CS2 Overlay" && form.Controls.Count >= 8,
                        "Controller window and controls initialize without CS2");
                    IntPtr window = form.Handle;
                    Require(window != IntPtr.Zero && form.IsHandleCreated,
                        "Windows form handle initializes");
                }
                string json = new JavaScriptSerializer().Serialize(new {
                    success = true, checks = passed.ToArray(),
                    limitations = "CS2 gameplay, current offsets and anti-cheat compatibility are not tested."
                });
                if (args.Length == 1) File.WriteAllText(args[0], json);
                Console.WriteLine(json);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.ToString());
                return 1;
            }
        }
    }
}

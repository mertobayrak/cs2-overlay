using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("CS2 Overlay")]
[assembly: AssemblyDescription("Windows controller and read-only CS2 entity overlay.")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace Cs2Overlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs args)
            {
                MessageBox.Show(args.Exception.Message, "CS2 Overlay error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new ControllerForm());
        }
    }

    internal static class Native
    {
        internal const uint ReadAccess = 0x0010 | 0x0400;
        internal const int ToggleHotkey = 601, QuitHotkey = 602, WmHotkey = 0x0312;
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { public int X, Y; }
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr window);
    }

    internal sealed class MemoryReader : IDisposable
    {
        private IntPtr handle;
        internal MemoryReader(IntPtr processHandle) { handle = processHandle; }
        internal bool TryBytes(long address, int size, out byte[] bytes)
        {
            bytes = null;
            if (handle == IntPtr.Zero || address <= 0 || size <= 0 || size > 4096) return false;
            byte[] buffer = new byte[size];
            UIntPtr read;
            if (!Native.ReadProcessMemory(handle, new IntPtr(address), buffer,
                new UIntPtr((uint)size), out read) || read.ToUInt64() != (ulong)size) return false;
            bytes = buffer;
            return true;
        }
        internal bool TryInt32(long address, out int value)
        {
            byte[] bytes;
            bool ok = TryBytes(address, 4, out bytes);
            value = ok ? BitConverter.ToInt32(bytes, 0) : 0;
            return ok;
        }
        internal bool TryByte(long address, out byte value)
        {
            byte[] bytes;
            bool ok = TryBytes(address, 1, out bytes);
            value = ok ? bytes[0] : (byte)0;
            return ok;
        }
        internal long Pointer(long address)
        {
            byte[] bytes;
            return TryBytes(address, 8, out bytes) ? BitConverter.ToInt64(bytes, 0) : 0;
        }
        internal float[] Floats(long address, int count)
        {
            byte[] bytes;
            if (!TryBytes(address, count * 4, out bytes)) return null;
            float[] values = new float[count];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { Native.CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }

    internal sealed class Offsets
    {
        internal long EntityList, LocalPawn, LocalController, ViewMatrix;
        internal long Health, Team, LifeState, SceneNode, Origin, PlayerPawn, Dormant;
        internal string Revision;
        private static Dictionary<string, object> Map(object value)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null) throw new InvalidDataException("Unexpected offset JSON structure.");
            return result;
        }
        private static object Get(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value)) throw new InvalidDataException("Offset data is missing " + key + ".");
            return value;
        }
        private static long Number(Dictionary<string, object> map, string key, long maximum)
        {
            long value = Convert.ToInt64(Get(map, key), CultureInfo.InvariantCulture);
            if (value <= 0 || value >= maximum) throw new InvalidDataException("Offset " + key + " is outside the expected range.");
            return value;
        }
        private static long Field(Dictionary<string, object> classes, string type, string field)
        {
            string current = type;
            for (int depth = 0; depth < 32 && current != null; depth++)
            {
                object classValue;
                if (!classes.TryGetValue(current, out classValue)) break;
                Dictionary<string, object> data = Map(classValue);
                Dictionary<string, object> fields = Map(Get(data, "fields"));
                object value;
                if (fields.TryGetValue(field, out value)) return Number(fields, field, 0x100000);
                object parent;
                current = data.TryGetValue("parent", out parent) ? parent as string : null;
            }
            throw new InvalidDataException("Offset data is missing " + type + "." + field + ".");
        }
        internal static Offsets Parse(string offsetsJson, string schemaJson, string revision)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 8 * 1024 * 1024;
            Dictionary<string, object> globals = Map(Get(Map(serializer.DeserializeObject(offsetsJson)), "client.dll"));
            Dictionary<string, object> schema = Map(Get(Map(serializer.DeserializeObject(schemaJson)), "client.dll"));
            Dictionary<string, object> classes = Map(Get(schema, "classes"));
            Offsets value = new Offsets();
            value.EntityList = Number(globals, "dwEntityList", 0x40000000);
            value.LocalPawn = Number(globals, "dwLocalPlayerPawn", 0x40000000);
            value.LocalController = Number(globals, "dwLocalPlayerController", 0x40000000);
            value.ViewMatrix = Number(globals, "dwViewMatrix", 0x40000000);
            value.Health = Field(classes, "C_BaseEntity", "m_iHealth");
            value.Team = Field(classes, "C_BaseEntity", "m_iTeamNum");
            value.LifeState = Field(classes, "C_BaseEntity", "m_lifeState");
            value.SceneNode = Field(classes, "C_BaseEntity", "m_pGameSceneNode");
            value.Origin = Field(classes, "CGameSceneNode", "m_vecAbsOrigin");
            value.PlayerPawn = Field(classes, "CCSPlayerController", "m_hPlayerPawn");
            try { value.Dormant = Field(classes, "CGameSceneNode", "m_bDormant"); }
            catch (InvalidDataException) { value.Dormant = 0; }
            value.Revision = revision;
            return value;
        }
        internal static async Task<Offsets> DownloadAsync()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CS2Overlay/0.1.0");
                string commitJson = await client.GetStringAsync("https://api.github.com/repos/a2x/cs2-dumper/commits/main");
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> commit = Map(serializer.DeserializeObject(commitJson));
                string revision = Get(commit, "sha") as string;
                if (revision == null || !Regex.IsMatch(revision, "^[0-9a-fA-F]{40}$"))
                    throw new InvalidDataException("GitHub returned an invalid offset revision.");
                string root = "https://raw.githubusercontent.com/a2x/cs2-dumper/" + revision + "/output/";
                Task<string> globals = client.GetStringAsync(root + "offsets.json");
                Task<string> schema = client.GetStringAsync(root + "client_dll.json");
                await Task.WhenAll(globals, schema);
                return Parse(globals.Result, schema.Result, revision);
            }
        }
    }

    internal sealed class Player { internal float X, Y, Z; internal int Health; }
    internal sealed class Frame
    {
        internal float[] Matrix;
        internal readonly List<Player> Opponents = new List<Player>();
        internal string Status = "Waiting for a live player";
        internal int PlayersRead;
    }

    internal static class Projection
    {
        internal static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        internal static bool WorldToScreen(float[] matrix, float x, float y, float z, Size size, out PointF point)
        {
            point = new PointF();
            if (matrix == null || matrix.Length != 16 || size.Width <= 0 || size.Height <= 0) return false;
            float w = matrix[12] * x + matrix[13] * y + matrix[14] * z + matrix[15];
            if (!Finite(w) || w <= 0.01f) return false;
            float sx = (matrix[0] * x + matrix[1] * y + matrix[2] * z + matrix[3]) / w;
            float sy = (matrix[4] * x + matrix[5] * y + matrix[6] * z + matrix[7]) / w;
            sx = (sx + 1) * size.Width / 2f;
            sy = (1 - sy) * size.Height / 2f;
            if (!Finite(sx) || !Finite(sy)) return false;
            point = new PointF(sx, sy);
            return true;
        }
    }

    internal sealed class Session : IDisposable
    {
        private Process game;
        private MemoryReader reader;
        private long client;
        private int stride;
        internal IntPtr Window { get; private set; }
        internal bool Attached { get { return game != null; } }
        internal bool HasExited
        {
            get
            {
                if (game == null) return true;
                try { return game.HasExited; } catch (InvalidOperationException) { return true; }
            }
        }
        internal bool TryAttach(Offsets offsets, out string status)
        {
            Dispose();
            Process[] candidates = Process.GetProcessesByName("cs2");
            status = "Waiting for CS2. Open the game to continue.";
            foreach (Process candidate in candidates)
            {
                if (game != null) { candidate.Dispose(); continue; }
                IntPtr handle = IntPtr.Zero;
                bool adopted = false;
                try
                {
                    candidate.Refresh();
                    IntPtr window = candidate.MainWindowHandle;
                    if (window == IntPtr.Zero) { status = "Waiting for CS2's game window."; continue; }
                    handle = Native.OpenProcess(Native.ReadAccess, false, candidate.Id);
                    if (handle == IntPtr.Zero)
                    {
                        status = "Windows denied read access to CS2: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
                        continue;
                    }
                    status = "Waiting for client.dll to load.";
                    foreach (ProcessModule module in candidate.Modules)
                    {
                        if (!String.Equals(module.ModuleName, "client.dll", StringComparison.OrdinalIgnoreCase)) continue;
                        long size = module.ModuleMemorySize;
                        if (offsets.EntityList + 8 >= size || offsets.LocalPawn + 8 >= size ||
                            offsets.LocalController + 8 >= size || offsets.ViewMatrix + 64 >= size)
                        {
                            status = "The published offsets do not fit this CS2 build. Try Refresh offsets.";
                            break;
                        }
                        game = candidate; Window = window; client = module.BaseAddress.ToInt64();
                        reader = new MemoryReader(handle); adopted = true;
                        status = "Attached to CS2. Join a match and select a team.";
                        break;
                    }
                }
                catch (Exception error) { status = "Could not attach: " + error.Message; }
                finally
                {
                    if (!adopted)
                    {
                        if (handle != IntPtr.Zero) Native.CloseHandle(handle);
                        candidate.Dispose();
                    }
                }
            }
            return Attached;
        }
        private long Entity(long list, int index, int entrySize)
        {
            if (list <= 0) return 0;
            index &= 0x7FFF;
            long page = reader.Pointer(list + 0x10 + 8L * (index >> 9));
            return page <= 0 ? 0 : reader.Pointer(page + entrySize * (index & 0x1FF));
        }
        private int FindStride(long list, long controller)
        {
            if (controller <= 0) return 0;
            foreach (int size in new int[] { 0x78, 0x70 })
                for (int slot = 1; slot <= 64; slot++)
                    if (Entity(list, slot, size) == controller) return size;
            return 0;
        }
        internal Frame ReadFrame(Offsets offsets)
        {
            Frame frame = new Frame();
            if (!Attached) return frame;
            long list = reader.Pointer(client + offsets.EntityList);
            long localPawn = reader.Pointer(client + offsets.LocalPawn);
            long controller = reader.Pointer(client + offsets.LocalController);
            frame.Matrix = reader.Floats(client + offsets.ViewMatrix, 16);
            if (list <= 0 || frame.Matrix == null)
            {
                frame.Status = "Game data could not be read. The offsets may not match this build.";
                return frame;
            }
            byte localTeam;
            if (localPawn <= 0 || !reader.TryByte(localPawn + offsets.Team, out localTeam) || (localTeam != 2 && localTeam != 3))
            {
                frame.Status = "Attached. Join a live match and select a team.";
                return frame;
            }
            if (stride == 0) stride = FindStride(list, controller);
            if (stride == 0) { frame.Status = "The entity layout did not match this CS2 build."; return frame; }
            for (int slot = 1; slot <= 64; slot++)
            {
                long playerController = Entity(list, slot, stride);
                if (playerController <= 0) continue;
                int handle;
                if (!reader.TryInt32(playerController + offsets.PlayerPawn, out handle) || handle == 0 || handle == -1) continue;
                long pawn = Entity(list, handle, stride);
                if (pawn <= 0) continue;
                int health;
                byte team, life;
                if (!reader.TryInt32(pawn + offsets.Health, out health) || health <= 0 || health > 1000 ||
                    !reader.TryByte(pawn + offsets.Team, out team) || (team != 2 && team != 3) ||
                    !reader.TryByte(pawn + offsets.LifeState, out life) || life != 0) continue;
                frame.PlayersRead++;
                if (pawn == localPawn || team == localTeam) continue;
                long scene = reader.Pointer(pawn + offsets.SceneNode);
                if (scene <= 0) continue;
                byte dormant;
                if (offsets.Dormant != 0 && (!reader.TryByte(scene + offsets.Dormant, out dormant) || dormant != 0)) continue;
                float[] position = reader.Floats(scene + offsets.Origin, 3);
                if (position == null) continue;
                bool valid = true;
                foreach (float coordinate in position)
                    if (!Projection.Finite(coordinate) || Math.Abs(coordinate) > 1000000) valid = false;
                if (valid) frame.Opponents.Add(new Player { X = position[0], Y = position[1], Z = position[2], Health = health });
            }
            frame.Status = "Attached. " + frame.PlayersRead + " live players read; " + frame.Opponents.Count + " opponents available.";
            return frame;
        }
        internal bool TryBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (Window == IntPtr.Zero || Native.IsIconic(Window)) return false;
            Native.Rect rectangle;
            Native.Point origin = new Native.Point();
            if (!Native.GetClientRect(Window, out rectangle) || !Native.ClientToScreen(Window, ref origin)) return false;
            int width = rectangle.Right - rectangle.Left, height = rectangle.Bottom - rectangle.Top;
            if (width <= 0 || height <= 0) return false;
            bounds = new Rectangle(origin.X, origin.Y, width, height);
            return true;
        }
        public void Dispose()
        {
            if (reader != null) reader.Dispose();
            if (game != null) game.Dispose();
            reader = null; game = null; Window = IntPtr.Zero; client = 0; stride = 0;
        }
    }

    internal sealed class OverlayForm : Form
    {
        private Frame frame;
        private readonly Font caption = new Font("Segoe UI", 10f, FontStyle.Bold);
        internal OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual; BackColor = TransparencyKey = Color.Magenta;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x20 | 0x80 | 0x08000000; return value; }
        }
        internal void Present(Frame value, Rectangle bounds)
        {
            frame = value;
            if (Bounds != bounds) Bounds = bounds;
            if (!Visible) Show();
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (frame == null) return;
            Graphics graphics = args.Graphics;
            graphics.FillRectangle(Brushes.Black, 12, 12, 480, 48);
            graphics.DrawString("OVERLAY ON   |   F6 toggle   |   F8 quit", caption, Brushes.White, 20, 18);
            graphics.DrawString(frame.Status, SystemFonts.MessageBoxFont, Brushes.White, 20, 40);
            using (Pen background = new Pen(Color.Black, 4))
            using (Pen outline = new Pen(Color.FromArgb(255, 87, 87), 2))
            {
                foreach (Player player in frame.Opponents)
                {
                    PointF feet, head;
                    if (!Projection.WorldToScreen(frame.Matrix, player.X, player.Y, player.Z, ClientSize, out feet) ||
                        !Projection.WorldToScreen(frame.Matrix, player.X, player.Y, player.Z + 72, ClientSize, out head)) continue;
                    float height = feet.Y - head.Y;
                    if (height < 4 || height > ClientSize.Height * 2) continue;
                    float width = height * 0.45f, left = (head.X + feet.X) / 2f - width / 2f;
                    if (left > ClientSize.Width || left + width < 0 || feet.Y < 0 || head.Y > ClientSize.Height) continue;
                    graphics.DrawRectangle(background, left, head.Y, width, height);
                    graphics.DrawRectangle(outline, left, head.Y, width, height);
                    graphics.FillRectangle(Brushes.Black, left - 7, head.Y, 4, height);
                    float healthHeight = height * Math.Min(player.Health, 100) / 100f;
                    graphics.FillRectangle(Brushes.LimeGreen, left - 7, feet.Y - healthHeight, 4, healthHeight);
                    graphics.DrawString(player.Health + " HP", caption, Brushes.White, left, feet.Y + 2);
                }
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) caption.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class ControllerForm : Form
    {
        private readonly Label state, version;
        private readonly CheckBox toggle;
        private readonly Button refresh;
        private readonly RichTextBox log;
        private readonly Timer pulse;
        private readonly Session session = new Session();
        private readonly OverlayForm overlay = new OverlayForm();
        private Offsets offsets;
        private bool closing, loading;
        private DateTime nextAttach = DateTime.MinValue, nextStatus = DateTime.MinValue;
        private string lastLog = "";
        internal ControllerForm()
        {
            Text = "CS2 Overlay"; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 440); MinimumSize = new Size(636, 479);
            Font = new Font("Segoe UI", 10f); BackColor = Color.FromArgb(23, 27, 34);
            ForeColor = Color.FromArgb(235, 239, 245); AutoScaleMode = AutoScaleMode.Dpi;
            Controls.Add(new Label { Text = "CS2 Overlay", AutoSize = true, Font = new Font("Segoe UI", 21f, FontStyle.Bold), Location = new Point(22, 18) });
            state = new Label { Text = "Starting...", AutoSize = false, Location = new Point(24, 74), Size = new Size(572, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(state);
            version = new Label { Text = "Offset data not loaded", AutoSize = true, Location = new Point(24, 125), ForeColor = Color.FromArgb(161, 176, 196) };
            Controls.Add(version);
            toggle = new CheckBox { Text = "Overlay on  (F6)", Checked = true, Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat, Location = new Point(24, 158), Size = new Size(180, 39) };
            toggle.CheckedChanged += delegate { toggle.Text = toggle.Checked ? "Overlay on  (F6)" : "Overlay off  (F6)"; };
            Controls.Add(toggle);
            refresh = new Button { Text = "Refresh offsets", FlatStyle = FlatStyle.Flat, Location = new Point(216, 158), Size = new Size(180, 39) };
            refresh.Click += async delegate { await LoadOffsets(); };
            Controls.Add(refresh);
            Button quit = new Button { Text = "Quit  (F8)", FlatStyle = FlatStyle.Flat, Location = new Point(408, 158), Size = new Size(180, 39) };
            quit.Click += delegate { Close(); }; Controls.Add(quit);
            Controls.Add(new Label { Text = "Open CS2, join a match, then switch back to the game.\nUse Fullscreen Windowed so the overlay can appear above it.",
                Location = new Point(24, 215), Size = new Size(568, 46), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right });
            log = new RichTextBox { ReadOnly = true, BackColor = Color.FromArgb(14, 17, 22), ForeColor = Color.FromArgb(184, 200, 221),
                BorderStyle = BorderStyle.None, DetectUrls = false, Location = new Point(24, 278), Size = new Size(568, 136),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(log);
            pulse = new Timer { Interval = 33 }; pulse.Tick += UpdateFrame; pulse.Start();
        }
        protected override async void OnShown(EventArgs args)
        {
            base.OnShown(args);
            if (!Native.RegisterHotKey(Handle, Native.ToggleHotkey, 0x4000, 0x75)) Log("F6 is already registered elsewhere. Use the Overlay button.");
            if (!Native.RegisterHotKey(Handle, Native.QuitHotkey, 0x4000, 0x77)) Log("F8 is already registered elsewhere. Use Quit.");
            await LoadOffsets();
        }
        private void Log(string message)
        {
            if (message == lastLog || closing) return;
            lastLog = message;
            if (log.TextLength > 16000) log.Clear();
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }
        private async Task LoadOffsets()
        {
            if (loading || closing) return;
            loading = true; refresh.Enabled = false; offsets = null; session.Dispose(); overlay.Hide();
            state.Text = "Loading published offset data..."; Log(state.Text);
            try
            {
                Offsets downloaded = await Offsets.DownloadAsync();
                if (closing) return;
                offsets = downloaded; version.Text = "Offset revision " + offsets.Revision.Substring(0, 12);
                state.Text = "Offsets loaded. Looking for CS2..."; Log(state.Text); nextAttach = DateTime.MinValue;
            }
            catch (Exception error)
            {
                if (!closing) { state.Text = "Offset download failed. Select Refresh offsets to retry."; Log(error.Message); }
            }
            finally { loading = false; if (!closing) refresh.Enabled = true; }
        }
        private void UpdateFrame(object sender, EventArgs args)
        {
            if (closing || loading || offsets == null) return;
            if (session.Attached && session.HasExited) { session.Dispose(); overlay.Hide(); Log("CS2 closed. Waiting for the next session."); }
            if (!session.Attached)
            {
                overlay.Hide();
                if (DateTime.UtcNow >= nextAttach)
                {
                    nextAttach = DateTime.UtcNow.AddSeconds(2);
                    string message; session.TryAttach(offsets, out message); state.Text = message; Log(message);
                }
                return;
            }
            if (!toggle.Checked) { overlay.Hide(); state.Text = "Overlay off. Press F6 or select Overlay on."; return; }
            if (Native.GetForegroundWindow() != session.Window) { overlay.Hide(); state.Text = "Attached. Switch back to CS2 to show the overlay."; return; }
            Rectangle bounds;
            if (!session.TryBounds(out bounds)) { overlay.Hide(); return; }
            Frame frame = session.ReadFrame(offsets); overlay.Present(frame, bounds);
            if (DateTime.UtcNow >= nextStatus) { nextStatus = DateTime.UtcNow.AddSeconds(1); state.Text = frame.Status; Log(frame.Status); }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WmHotkey)
            {
                int key = message.WParam.ToInt32();
                if (key == Native.ToggleHotkey) toggle.Checked = !toggle.Checked;
                if (key == Native.QuitHotkey) Close();
            }
            base.WndProc(ref message);
        }
        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            closing = true; pulse.Stop();
            Native.UnregisterHotKey(Handle, Native.ToggleHotkey); Native.UnregisterHotKey(Handle, Native.QuitHotkey);
            overlay.Dispose(); session.Dispose(); base.OnFormClosing(args);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { pulse.Dispose(); overlay.Dispose(); session.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cs2Overlay
{
    internal sealed class ControllerForm : RoundedWindow
    {
        private static readonly Color Green = Color.FromArgb(78, 232, 134), Muted = Color.FromArgb(151, 177, 160), CardColor = Color.FromArgb(17, 37, 26);
        private AppSettings settings = new AppSettings();
        private readonly List<Action> bindings = new List<Action>();
        private readonly List<string> boundNames = new List<string>();
        private readonly Dictionary<string, FlowLayoutPanel> pages = new Dictionary<string, FlowLayoutPanel>();
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Panel host;
        private readonly Label title, state, sessionInfo, version, footer;
        private readonly RichTextBox log;
        private readonly Button refresh;
        private readonly PacedPulse pulse;
        private readonly Session session = new Session();
        private readonly OverlayForm overlay = new OverlayForm();
        private readonly PhysicalSpace space = new PhysicalSpace();
        private readonly RepeatGate jump = new RepeatGate(), fire = new RepeatGate();
        private readonly AimEngine aim = new AimEngine();
        private readonly RecoilCompensation recoil = new RecoilCompensation();
        private readonly SpinCameraMonitor spinCamera = new SpinCameraMonitor();
        private readonly FlashLimiter flash = new FlashLimiter();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Offsets offsets;
        private bool closing, loading, syncing, cameraBusy;
        private bool lastSpinRequested, spinCameraReady, lastNoKeybinds;
        private int spinSetupEpoch;
        private int spinCameraProcess;
        private bool spinMayaRequested;
        private bool lastSpinCameraAutomatic;
        private int lastSpinDistance, lastSpinCameraPitch;
        private DateTime nextAttach = DateTime.MinValue, nextWindow = DateTime.MinValue, nextStatus = DateTime.MinValue;
        private long lastTick, cadenceStart;
        private int frames;
        private string lastLog = "", cadence = "";
        private readonly bool previewMode;
        internal IList<string> BoundSettings { get { return boundNames.AsReadOnly(); } }
        internal int PageCount { get { return pages.Count; } }
        internal AppSettings Configuration { get { return settings; } }

        internal ControllerForm() : this(false) { }
        internal ControllerForm(bool offlinePreview)
        {
            previewMode = offlinePreview;
            Text = "ModafenClientXZERO"; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 780); MinimumSize = new Size(860, 640);
            Font = new Font("Segoe UI", 10f); BackColor = Color.FromArgb(9, 24, 16); ForeColor = Color.FromArgb(235, 247, 238);
            AutoScaleMode = AutoScaleMode.Dpi;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            Controls.Add(root);
            Panel side = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(5, 18, 11) };
            root.Controls.Add(side, 0, 0); root.SetRowSpan(side, 3);
            FlowLayoutPanel nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 8, 8, 0), AutoScroll = true };
            side.Controls.Add(nav);
            Label sideFoot = new Label { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(16, 12, 8, 0), ForeColor = Muted, Text = "XZERO  /  0.4.0\nExternal Windows client\n\nF8  Show / hide menu" };
            side.Controls.Add(sideFoot);
            Label brand = new Label { Dock = DockStyle.Top, Height = 110, Padding = new Padding(16, 18, 0, 0), ForeColor = Green, Font = new Font("Segoe UI", 17f, FontStyle.Bold), Text = "MODAFEN\nCLIENT XZERO" };
            side.Controls.Add(brand);
            Panel header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(20, 14, 18, 8) };
            root.Controls.Add(header, 1, 0);
            title = new Label { Dock = DockStyle.Top, Height = 40, Text = "Dashboard", Font = new Font("Segoe UI", 21f, FontStyle.Bold), ForeColor = Green };
            state = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "Starting...", ForeColor = Muted, AutoEllipsis = true };
            header.Controls.Add(state); header.Controls.Add(title);
            FlowLayoutPanel windowButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 124, Height = 32, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = BackColor };
            Button minimize = NewButton("—", 36), maximize = NewButton("□", 36), close = NewButton("×", 36);
            minimize.Height = maximize.Height = close.Height = 28;
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            maximize.Click += delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
            close.Click += delegate { Close(); };
            windowButtons.Controls.Add(minimize); windowButtons.Controls.Add(maximize); windowButtons.Controls.Add(close); header.Controls.Add(windowButtons); windowButtons.BringToFront();
            title.MouseDown += DragWindow; header.MouseDown += DragWindow; brand.MouseDown += DragWindow;
            host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; root.Controls.Add(host, 1, 1);
            footer = new Label { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(20, 8, 0, 0), Font = new Font("Segoe UI", 9f), ForeColor = Green, BackColor = Color.FromArgb(5, 18, 11) };
            root.Controls.Add(footer, 1, 2);
            foreach (string name in new string[] { "Dashboard", "Wallhack", "Aimbot + assist", "Movement", "Spinbot", "Practice camera", "Keybinds", "Profiles" })
            {
                FlowLayoutPanel page = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 4, 0, 12), AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Visible = false, Name = "page." + name };
                host.Controls.Add(page); pages.Add(name, page);
                string target = name;
                Button button = NewButton(name, 162); button.Height = 39; button.Margin = new Padding(0, 0, 0, 7); button.TextAlign = ContentAlignment.MiddleLeft;
                button.Click += delegate { SelectPage(target); }; nav.Controls.Add(button); navigation.Add(name, button);
            }
            FlowLayoutPanel card = Card("Dashboard", "Session", "Open CS2 in Fullscreen Windowed, join a match, and switch back to the game.");
            sessionInfo = Note(card, "Waiting for CS2.", 60);
            version = Note(card, "Offset data not loaded", 30);
            refresh = AddButton(card, "Refresh offsets", async delegate { await LoadOffsets(); });
            AddButton(card, "Pause all automation", delegate { settings.DisableInputs(); SyncControls(); Log("Aim, assist, bunnyhop, spin and flash limiting paused."); });
            AddButton(card, "Quit client", delegate { Close(); });
            card = Card("Dashboard", "Performance", "Higher refresh targets update more often. The counter measures this client, not CS2's FPS.");
            Number(card, "RefreshHz", "Refresh target (Hz)", 30, 240, 1, 0);
            Number(card, "IdleHz", "Idle refresh (Hz)", 2, 30, 1, 0);
            Check(card, "Antialias", "Smooth overlay lines"); Check(card, "KeepPanelOnTop", "Keep this panel on top"); Check(card, "Hud", "Show in-game status HUD");
            card = Card("Dashboard", "Diagnostics", "Connection, input and offset messages appear here.");
            log = new RichTextBox { Width = 334, Height = 190, ReadOnly = true, BackColor = Color.FromArgb(5, 18, 11), ForeColor = Muted, BorderStyle = BorderStyle.None, DetectUrls = false, Font = new Font("Consolas", 9f) }; card.Controls.Add(log);

            card = Card("Wallhack", "Player style", "Boxes, skeletons and the bone silhouette can be switched independently. The silhouette follows bones; it is not a game material replacement.");
            Check(card, "EspEnabled", "Enable wallhack"); Check(card, "Boxes", "Player boxes"); Choice(card, "BoxStyle", "Box style", new string[] { "Full", "Corners" });
            Check(card, "Skeleton", "Skeleton / bones"); Check(card, "Silhouette", "Body silhouette from bones"); Check(card, "Outline", "Black outline");
            Check(card, "FilledBoxes", "Fill player boxes"); Number(card, "FillOpacity", "Fill intensity (%)", 5, 80, 5, 0); Number(card, "BoxThickness", "Line thickness", 1, 4, 1, 0);
            ColorSetting(card, "EnemyColor", "Enemy color"); ColorSetting(card, "TeamColor", "Teammate color");
            card = Card("Wallhack", "Flashbang white limit", "Reduce flash opacity. 255 is normal strength; lower values reduce the white layer. This changes only your local player's flash-opacity field while enabled.");
            Check(card, "FlashLimitEnabled", "Limit flashbang white"); Number(card, "FlashWhiteLimit", "Maximum flash opacity", 0, 255, 5, 0);
            card = Card("Wallhack", "Bomb location + timer", "Show planted C4 at site A/B, its world marker, time until explosion, and the latest start for a 5-second kit or 10-second normal defuse. Active defuses show their own countdown.");
            Check(card, "BombInfo", "Show planted bomb + defuse timers");
            Note(card, "Works independently of player wallhack. Timers follow the game clock, including pauses. Missing timing data is shown as unavailable.", 86);
            card = Card("Wallhack", "Labels + filters", "Hostage carriers are marked by HOSTAGE CARRIER text and an orange radar dot when the relevant game data is available.");
            Check(card, "HealthBar", "Health bar"); Check(card, "HealthText", "Health numbers"); Check(card, "Names", "Player names"); Check(card, "Distances", "Distance labels");
            Check(card, "HostageCarrier", "Mark hostage carriers"); Check(card, "Teammates", "Include teammates"); Number(card, "EspDistance", "Max range (m)", 10, 500, 10, 0);
            Number(card, "LabelSize", "Label font size", 8, 18, 1, 0); Check(card, "Snaplines", "Snaplines"); Choice(card, "SnaplineAnchor", "Line origin", new string[] { "Bottom", "Center", "Top" });
            card = Card("Wallhack", "Radar overlay", "This radar appears with wallhack and uses available player positions. Players beyond its range are pinned to its edge.");
            Check(card, "Radar", "Radar with wallhack"); Check(card, "RadarRotate", "Rotate with your view"); Number(card, "RadarSize", "Radar size (px)", 120, 350, 10, 0); Number(card, "RadarRange", "Radar range (m)", 10, 200, 5, 0);
            card = Card("Wallhack", "Crosshair", "A separate center-screen crosshair.");
            Check(card, "Crosshair", "Show crosshair"); Number(card, "CrosshairSize", "Arm length (px)", 2, 25, 1, 0); Number(card, "CrosshairGap", "Center gap (px)", 0, 20, 1, 0); ColorSetting(card, "CrosshairColor", "Crosshair color");

            card = Card("Aimbot + assist", "Aimbot", "Hold the chosen activation key to move toward a target inside the FOV circle.");
            Risk(card, "RISK", "Aim automation can lead to reports or anti-cheat action. There is no ban protection.", "risk.aim");
            Check(card, "AimEnabled", "Enable aimbot"); KeyChoice(card, "AimKey", "Hold key", KeyOptions.Activation);
            Number(card, "AimRadius", "Aim FOV radius (px)", 10, 1000, 10, 0); Number(card, "AimSmoothMs", "Smoothing (ms)", 0, 500, 10, 0);
            Choice(card, "AimPoint", "Target point", new string[] { "Head", "Chest", "Pelvis" });
            Number(card, "AimDistance", "Target range (m)", 10, 500, 10, 0); Check(card, "TargetLock", "Keep current target");
            Choice(card, "TargetPriority", "Target priority", new string[] { "Crosshair", "Nearest", "Lowest health" });
            Check(card, "AimVisibleOnly", "Require spotted by local player"); Number(card, "AimDeadzone", "Deadzone (px)", 0, 20, 1, 0); Number(card, "AimMaxStep", "Max mouse step", 1, 500, 5, 0);
            Check(card, "RecoilControl", "Aimbot recoil control"); Number(card, "RecoilStrength", "Recoil strength (1 = full)", 0m, 2m, 0.05m, 2);
            card = Card("Aimbot + assist", "Aim assist", "A smaller FOV, slower smoothing and adjustable strength give a lighter pull while its activation key is held. Aimbot takes priority when both are active.");
            Check(card, "AssistEnabled", "Enable aim assist"); KeyChoice(card, "AssistKey", "Hold key", KeyOptions.Activation);
            Number(card, "AssistRadius", "Assist FOV radius (px)", 10, 250, 5, 0); Number(card, "AssistSmoothMs", "Assist smoothing (ms)", 30, 500, 10, 0); Number(card, "AssistStrength", "Assist strength", 0.05m, 1m, 0.05m, 2);
            card = Card("Aimbot + assist", "Calibration + display", "Automatic sensitivity uses the game's reported value. Use the manual value when your setup needs calibration. Spotted data is not a geometric wall trace.");
            Check(card, "AutoSensitivity", "Read game sensitivity"); Number(card, "Sensitivity", "Manual sensitivity", 0.05m, 20m, 0.05m, 2); Check(card, "DrawFov", "Show FOV circle (also when aim is off)"); Check(card, "TargetIndicator", "Mark selected target"); ColorSetting(card, "FovColor", "FOV circle color");

            card = Card("Movement", "Bunnyhop", "Enable, then hold physical Space in CS2 to repeat Jump. Jump must be bound to Space. Repetition stops on release or when CS2 loses focus.");
            Check(card, "BhopEnabled", "Enable bunnyhop"); Number(card, "JumpInterval", "Jump interval (ms)", 10, 150, 5, 0); Check(card, "GroundOnly", "Jump only while on ground");
            Note(card, "Spinbot can also jump automatically without holding Space when its Auto bunnyhop option is enabled.", 75);

            card = Card("Spinbot", "Spinbot / Scout only", "Only active while the SSG 08 (Scout) is equipped. Spin, automatic jumping, targeting and fire all pause when you change weapon or weapon data is unavailable.");
            Risk(card, "HIGHER RISK", "Spinning, repeated jumping and automatic fire are conspicuous automation. There is no ban protection.", "risk.spin");
            Check(card, "SpinEnabled", "Enable spinbot"); Number(card, "SpinSpeed", "Spin speed (degrees/s)", 90, 3600, 90, 0); Number(card, "SpinPitch", "Look pitch (down = 89)", -89, 89, 1, 0);
            Check(card, "SpinJump", "Auto bunnyhop"); Check(card, "SpinTarget", "Aim at spotted on-screen enemies"); Check(card, "SpinShoot", "Auto-fire at crosshair enemy"); Number(card, "FireInterval", "Fire interval (ms)", 50, 1000, 10, 0);
            Note(card, "All spin inputs require a live local player and CS2 in the foreground. Opening the menu stops input immediately.", 75);
            card = Card("Spinbot", "Chase camera", "A spectator-style view following your living character from behind. Its viewing direction stays steady while your Scout spins. Adjust distance and viewing pitch here.");
            Check(card, "SpinPracticeCamera", "Automatic chase camera (practice)");
            Number(card, "SpinCameraDistance", "Chase distance", 50, 400, 10, 0);
            Number(card, "SpinCameraPitch", "Chase viewing pitch", -60, 60, 1, 0);
            AddButton(card, "Restore first person (3 seconds)", async delegate { settings.ThirdPerson = settings.CustomFov = false; SyncControls(); await ApplyCamera(); });
            Note(card, "Requires the developer console and a server allowing sv_cheats. Uses third person with steady orbit controls, rather than spectator status. Spin stops if the camera follows the turning view; fire waits for a steady camera.", 160);

            card = Card("Practice camera", "Third person + FOV", "Practice server only: thirdperson and fov_cs_debug require sv_cheats 1 on the server. Public servers can reject these commands. These controls do not enable server cheats.");
            Check(card, "ThirdPerson", "Third-person camera"); Number(card, "CameraDistance", "Third-person distance", 50, 400, 10, 0);
            Check(card, "CustomFov", "Custom camera FOV"); Number(card, "CameraFov", "Camera FOV", 54, 200, 1, 0); KeyChoice(card, "ConsoleKey", "Game console key", KeyOptions.Console);
            Note(card, "Enable the developer console first; start with an empty prompt. Apply opens it after 3 seconds and pauses automation. Values up to 200 can be requested; the engine may clamp FOV.", 110);
            AddButton(card, "Apply in CS2 (3 second countdown)", async delegate { await ApplyCamera(); });
            AddButton(card, "Copy camera commands", delegate { Run(delegate { Clipboard.SetText(PracticeCamera.Command(settings)); Log("Camera commands copied."); }); });
            AddButton(card, "Export practice camera .cfg", delegate { ExportCamera(); });
            AddButton(card, "Restore first person + default FOV", async delegate { settings.ThirdPerson = settings.CustomFov = false; SyncControls(); await ApplyCamera(); });

            card = Card("Keybinds", "Global toggles", "Pick a unique key for every action, then select Apply keybinds. The main controls also work when a key is already used by another app.");
            Check(card, "NoKeybinds", "NO KEYBINDS (menu key only)");
            KeyChoice(card, "MenuBind", "Show / hide menu", KeyOptions.Globals); KeyChoice(card, "EspBind", "Wallhack", KeyOptions.Globals); KeyChoice(card, "AimBind", "Aimbot", KeyOptions.Globals); KeyChoice(card, "AssistBind", "Aim assist", KeyOptions.Globals);
            KeyChoice(card, "BhopBind", "Bunnyhop", KeyOptions.Globals); KeyChoice(card, "SpinBind", "Spinbot", KeyOptions.Globals); KeyChoice(card, "PanicBind", "Everything off", KeyOptions.Globals); KeyChoice(card, "QuitBind", "Quit client", KeyOptions.Globals);
            AddButton(card, "Apply keybinds", delegate { Run(delegate { settings.Validate(); RegisterKeys(); Log("Keybinds applied."); }); });

            card = Card("Profiles", "Saved setups", "Save one of three local profiles. Loading keeps aim, assist, bunnyhop and spin off until you enable them. Nothing loads automatically at startup.");
            for (int i = 1; i <= 3; i++)
            {
                int slot = i;
                AddButton(card, "Save profile " + slot, delegate { Run(delegate { Profiles.Save(slot, settings); Log("Profile " + slot + " saved."); }); });
                AddButton(card, "Load profile " + slot, delegate { Run(delegate { settings = Profiles.Load(slot); SyncControls(); if (IsHandleCreated) RegisterKeys(); Log("Profile " + slot + " loaded. Automation is off."); }); });
            }
            AddButton(card, "Reset all settings", delegate { settings = new AppSettings(); SyncControls(); if (IsHandleCreated) RegisterKeys(); Log("Default settings restored."); });
            pulse = new PacedPulse(this, UpdateFrame);
            SyncControls(); SelectPage("Dashboard");
        }
        private Button NewButton(string text, int width)
        {
            Button button = new RoundedButton { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(25, 61, 39), ForeColor = ForeColor, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(42, 91, 59); return button;
        }
        private FlowLayoutPanel Card(string page, string heading, string description)
        {
            FlowLayoutPanel card = new RoundedCard { Width = 370, MinimumSize = new Size(370, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 14, 16, 14), Margin = new Padding(0, 0, 14, 14), BackColor = CardColor };
            card.Controls.Add(new Label { Text = heading, Width = 334, Height = 32, ForeColor = Green, Font = new Font("Segoe UI", 13f, FontStyle.Bold) });
            int height = TextRenderer.MeasureText(description, Font, new Size(334, 0), TextFormatFlags.WordBreak).Height + 12;
            Note(card, description, height); pages[page].Controls.Add(card); return card;
        }
        private Label Note(Control parent, string text, int height)
        {
            Label label = new Label { Text = text, Width = 334, Height = height, ForeColor = Muted, Margin = new Padding(0, 3, 0, 7) }; parent.Controls.Add(label); return label;
        }
        private void Risk(Control card, string heading, string detail, string name)
        {
            Label label = Note(card, heading + "\n" + detail, 98); label.Name = name; label.ForeColor = Color.FromArgb(255, 190, 83); label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        }
        private Button AddButton(Control card, string text, EventHandler click)
        {
            Button button = NewButton(text, 334); button.Click += click; card.Controls.Add(button); return button;
        }
        private PropertyInfo Property(string name)
        {
            PropertyInfo property = typeof(AppSettings).GetProperty(name);
            if (property == null) throw new InvalidOperationException("Missing setting: " + name);
            if (!boundNames.Contains(name)) boundNames.Add(name); return property;
        }
        private Panel Row(Control card, string label, out Label caption)
        {
            Panel row = new Panel { Width = 334, Height = 36, Margin = new Padding(0, 2, 0, 2) };
            caption = new Label { Text = label, Location = new Point(0, 7), Size = new Size(210, 26), ForeColor = ForeColor, AutoEllipsis = true };
            row.Controls.Add(caption); card.Controls.Add(row); return row;
        }
        private void Check(Control card, string name, string label)
        {
            PropertyInfo property = Property(name);
            CheckBox control = new CheckBox { Text = label, Name = "setting." + name, Width = 334, Height = 30, ForeColor = ForeColor, FlatStyle = FlatStyle.Standard, Margin = new Padding(0, 2, 0, 2) };
            control.CheckedChanged += delegate { if (syncing) return; property.SetValue(settings, control.Checked, null); SettingsChanged(); };
            bindings.Add(delegate { control.Checked = (bool)property.GetValue(settings, null); }); card.Controls.Add(control);
        }
        private void Number(Control card, string name, string label, decimal minimum, decimal maximum, decimal increment, int decimals)
        {
            PropertyInfo property = Property(name); Label caption; Panel row = Row(card, label, out caption);
            NumericUpDown control = new NumericUpDown { Name = "setting." + name, Minimum = minimum, Maximum = maximum, Increment = increment, DecimalPlaces = decimals,
                Location = new Point(218, 3), Size = new Size(116, 28), BackColor = Color.FromArgb(9, 24, 16), ForeColor = Green, BorderStyle = BorderStyle.FixedSingle };
            control.ValueChanged += delegate
            {
                if (syncing) return;
                object value = property.PropertyType == typeof(decimal) ? (object)control.Value : (object)(int)control.Value;
                property.SetValue(settings, value, null); SettingsChanged();
            };
            bindings.Add(delegate { control.Value = Math.Max(minimum, Math.Min(maximum, Convert.ToDecimal(property.GetValue(settings, null)))); }); row.Controls.Add(control);
        }
        private void Choice(Control card, string name, string label, string[] options)
        {
            int[] values = new int[options.Length]; for (int i = 0; i < values.Length; i++) values[i] = i;
            SelectSetting(card, name, label, options, values);
        }
        private void KeyChoice(Control card, string name, string label, int[] keys)
        {
            string[] labels = new string[keys.Length]; for (int i = 0; i < labels.Length; i++) labels[i] = KeyOptions.Label(keys[i]);
            SelectSetting(card, name, label, labels, keys);
        }
        private void SelectSetting(Control card, string name, string label, string[] options, int[] values)
        {
            PropertyInfo property = Property(name); Label caption; Panel row = Row(card, label, out caption);
            ComboBox control = new ComboBox { Name = "setting." + name, DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(202, 2), Size = new Size(132, 28), BackColor = Color.FromArgb(9, 24, 16), ForeColor = ForeColor, FlatStyle = FlatStyle.Flat };
            control.DrawMode = DrawMode.OwnerDrawFixed; control.ItemHeight = 22;
            control.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                if (args.Index < 0) return;
                Color background = (args.State & DrawItemState.Selected) != 0 ? Color.FromArgb(28, 78, 44) : Color.FromArgb(9, 24, 16);
                using (Brush brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
                Rectangle text = args.Bounds; text.X += 5;
                TextRenderer.DrawText(args.Graphics, options[args.Index], Font, text, control.Enabled ? ForeColor : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            caption.Width = 198; control.Items.AddRange(options);
            control.SelectedIndexChanged += delegate { if (syncing || control.SelectedIndex < 0) return; property.SetValue(settings, values[control.SelectedIndex], null); SettingsChanged(); };
            bindings.Add(delegate { control.SelectedIndex = Array.IndexOf(values, (int)property.GetValue(settings, null)); }); row.Controls.Add(control);
        }
        private void ColorSetting(Control card, string name, string label)
        {
            PropertyInfo property = Property(name); Label caption; Panel row = Row(card, label, out caption);
            Button control = NewButton("Choose", 116); control.Name = "setting." + name; control.Location = new Point(218, 0);
            Action update = delegate
            {
                Color c = AppSettings.Opaque((int)property.GetValue(settings, null)); control.BackColor = c;
                control.ForeColor = c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 150 ? Color.Black : Color.White;
            };
            control.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog { Color = AppSettings.Opaque((int)property.GetValue(settings, null)), FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { property.SetValue(settings, dialog.Color.ToArgb(), null); update(); SettingsChanged(); }
            };
            bindings.Add(update); row.Controls.Add(control);
        }
        private void SyncControls()
        {
            syncing = true;
            try { foreach (Action binding in bindings) binding(); }
            finally { syncing = false; }
            SettingsChanged();
        }
        private void SettingsChanged()
        {
            if (!settings.FlashLimitEnabled) flash.Disable();
            TopMost = settings.KeepPanelOnTop;
            footer.Text = settings.NoKeybinds ? KeyOptions.Label(settings.MenuBind) + " menu    /    NO KEYBINDS enabled" : KeyOptions.Label(settings.MenuBind) + " menu    " + KeyOptions.Label(settings.EspBind) + " wallhack    " + KeyOptions.Label(settings.AimBind) + " aim    " + KeyOptions.Label(settings.BhopBind) + " hop    " + KeyOptions.Label(settings.SpinBind) + " spin    " + KeyOptions.Label(settings.QuitBind) + " quit";
            foreach (string name in new string[] { "EspBind", "AimBind", "AssistBind", "BhopBind", "SpinBind", "QuitBind", "PanicBind" })
                foreach (Control key in Controls.Find("setting." + name, true)) key.Enabled = !settings.NoKeybinds;
            try { if (!previewMode) space.Enable(settings.BhopEnabled); }
            catch (Win32Exception error) { settings.BhopEnabled = false; if (log != null) Log(error.Message); SyncControls(); }
            if (settings.NoKeybinds != lastNoKeybinds)
            {
                lastNoKeybinds = settings.NoKeybinds;
                if (!previewMode && IsHandleCreated) RegisterKeys();
            }
            if (settings.SpinEnabled != lastSpinRequested || settings.SpinPracticeCamera != lastSpinCameraAutomatic ||
                settings.SpinCameraDistance != lastSpinDistance || settings.SpinCameraPitch != lastSpinCameraPitch)
            {
                lastSpinRequested = settings.SpinEnabled; lastSpinCameraAutomatic = settings.SpinPracticeCamera;
                lastSpinDistance = settings.SpinCameraDistance; lastSpinCameraPitch = settings.SpinCameraPitch;
                spinSetupEpoch++; spinCameraReady = false;
                spinCamera.Reset();
            }
        }
        internal void SelectPage(string name)
        {
            if (!pages.ContainsKey(name)) throw new ArgumentOutOfRangeException("name");
            title.Text = name;
            foreach (KeyValuePair<string, FlowLayoutPanel> page in pages) page.Value.Visible = page.Key == name;
            foreach (KeyValuePair<string, Button> button in navigation)
            {
                bool active = button.Key == name; button.Value.BackColor = active ? Color.FromArgb(27, 92, 48) : Color.FromArgb(12, 31, 19); button.Value.ForeColor = active ? Green : Muted;
            }
            pages[name].BringToFront();
        }
        protected override async void OnShown(EventArgs args)
        {
            base.OnShown(args); if (previewMode) return; RegisterKeys(); pulse.Start(100); await LoadOffsets();
        }
        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args); ClientTheme.SoftenWindow(Handle);
        }
        private void RegisterKeys()
        {
            if (!IsHandleCreated) return;
            int[] ids = new int[] { Native.MenuHotkey, Native.ToggleHotkey, Native.AimHotkey, Native.AssistHotkey, Native.JumpHotkey, Native.SpinHotkey, Native.QuitHotkey, Native.PanicHotkey };
            int[] keys = new int[] { settings.MenuBind, settings.EspBind, settings.AimBind, settings.AssistBind, settings.BhopBind, settings.SpinBind, settings.QuitBind, settings.PanicBind };
            foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
            for (int i = 0; i < ids.Length; i++)
                if (KeyOptions.ActionEnabled(settings, ids[i]) && !Native.RegisterHotKey(Handle, ids[i], 0x4000, (uint)keys[i])) Log(KeyOptions.Label(keys[i]) + " is already registered elsewhere. Use the panel controls.");
        }
        private void UnregisterKeys()
        {
            if (!IsHandleCreated) return;
            foreach (int id in new int[] { Native.MenuHotkey, Native.ToggleHotkey, Native.AimHotkey, Native.AssistHotkey, Native.JumpHotkey, Native.SpinHotkey, Native.QuitHotkey, Native.PanicHotkey }) Native.UnregisterHotKey(Handle, id);
        }
        private void SetState(string text) { if (state.Text != text) state.Text = text; }
        private void Log(string message)
        {
            if (message == lastLog || closing || log == null) return;
            lastLog = message; if (log.TextLength > 18000) log.Clear();
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }
        private void Run(Action action) { try { action(); } catch (Exception error) { Log(error.Message); SetState(error.Message); } }
        private async Task LoadOffsets()
        {
            if (loading || closing) return;
            loading = true; refresh.Enabled = false; flash.Disable(); offsets = null; session.Dispose(); overlay.Hide(); ResetAutomation();
            SetState("Loading published offset data..."); Log(state.Text);
            try
            {
                Offsets downloaded = await Offsets.DownloadAsync(); if (closing) return;
                offsets = downloaded; version.Text = "Offset revision " + offsets.Revision.Substring(0, 12);
                SetState("Offsets loaded. Looking for CS2..."); Log(state.Text); nextAttach = DateTime.MinValue;
                if (offsets.ModelState == 0) Log("Skeleton data is not present in this offset revision.");
                if (offsets.ViewAngles == 0) Log("View angles are unavailable. Aim and spin will stay inactive.");
                if (offsets.HostageServices == 0 || offsets.CarriedHostage == 0) Log("Hostage-carrier data is unavailable in this revision.");
                if (!BombReader.Available(offsets)) Log("Bomb location/timer data is unavailable in this offset revision.");
            }
            catch (Exception error) { if (!closing) { SetState("Offset download failed. Select Refresh offsets."); Log(error.Message); } }
            finally { loading = false; if (!closing) refresh.Enabled = true; }
        }
        private void ResetAutomation() { aim.Reset(); recoil.Reset(); jump.Reset(); fire.Reset(); spinCamera.Reset(); }
        private void Idle(string message)
        {
            overlay.Hide(); flash.Disable(); ResetAutomation(); pulse.SetPeriod(1000 / settings.IdleHz); if (message != null) SetState(message);
            cadenceStart = clock.ElapsedMilliseconds; frames = 0;
        }
        private void UpdateFrame()
        {
            if (closing) return;
            long now = clock.ElapsedMilliseconds; double seconds = Math.Max(0.001, Math.Min(0.05, (now - lastTick) / 1000.0)); lastTick = now;
            if (loading || offsets == null || cameraBusy) { Idle(null); return; }
            if (session.Attached && session.HasExited) { session.Dispose(); Log("CS2 closed. Waiting for the next session."); }
            if (!session.Attached)
            {
                Idle(null);
                if (DateTime.UtcNow >= nextAttach)
                {
                    nextAttach = DateTime.UtcNow.AddSeconds(2); string message;
                    session.TryAttach(offsets, out message); SetState(message); sessionInfo.Text = message; Log(message);
                }
                return;
            }
            if (DateTime.UtcNow >= nextWindow) { nextWindow = DateTime.UtcNow.AddSeconds(1); session.RefreshWindow(); }
            if (session.Window == IntPtr.Zero || Native.GetForegroundWindow() != session.Window) { Idle("Attached. Switch to CS2; " + KeyOptions.Label(settings.MenuBind) + " opens the panel."); return; }
            bool any = OverlayOptions.ShouldDraw(settings) || settings.AimEnabled || settings.AssistEnabled || settings.BhopEnabled || settings.SpinEnabled || settings.FlashLimitEnabled;
            if (!any) { Idle("All features are off."); return; }
            pulse.SetPeriod((int)Math.Round(1000.0 / settings.RefreshHz));
            Rectangle bounds; if (!session.TryBounds(out bounds)) { Idle("Waiting for a visible CS2 window."); return; }
            bool needPlayers = settings.EspEnabled || settings.AimEnabled || settings.AssistEnabled || (settings.SpinEnabled && (settings.SpinTarget || settings.SpinShoot));
            Frame frame = session.ReadFrame(offsets, settings, needPlayers); AimSolution solution = null;
            if (settings.FlashLimitEnabled && frame.Alive)
            {
                string error;
                if (!flash.Apply(session.ProcessId, frame.LocalPawn, session.ClientBase + offsets.LocalPawn, offsets.FlashMaxAlpha, settings.FlashWhiteLimit, out error))
                { settings.FlashLimitEnabled = false; flash.Disable(); SyncControls(); Log(error); }
            }
            else flash.Disable();
            if (settings.SpinPracticeCamera && !spinCameraReady && InputPolicy.SpinAllowed(settings, frame)) { PrepareSpinCamera(frame); return; }
            try { solution = Automate(frame, bounds.Size, seconds, now); }
            catch (Win32Exception error) { settings.DisableInputs(); ResetAutomation(); SyncControls(); Log(error.Message); }
            frames++;
            if (now - cadenceStart >= 1000)
            {
                cadence = Math.Round(frames * 1000.0 / Math.Max(1, now - cadenceStart)) + " updates/s"; cadenceStart = now; frames = 0;
            }
            bool draw = OverlayOptions.ShouldDraw(settings);
            if (draw) overlay.Present(frame, bounds, settings, solution, cadence); else overlay.Hide();
            if (DateTime.UtcNow >= nextStatus)
            {
                nextStatus = DateTime.UtcNow.AddSeconds(1);
                string status = frame.Status;
                if ((settings.AimEnabled || settings.AssistEnabled || settings.SpinEnabled) && !frame.AnglesValid) status += " | Aim/spin waiting for valid camera data";
                if (settings.SpinEnabled && !InputPolicy.SpinAllowed(settings, frame)) status += frame.WeaponKnown ? " | Spin paused: equip SSG 08" : " | Spin paused: weapon data unavailable";
                if (settings.AimEnabled && settings.RecoilControl && frame.ShotsFired > 0 && !frame.PunchKnown) status += " | Recoil data unavailable";
                SetState(status); sessionInfo.Text = status + "\n" + cadence; Log(status);
            }
        }
        private AimSolution Automate(Frame frame, Size viewport, double seconds, long now)
        {
            if (!frame.Alive) { ResetAutomation(); return null; }
            bool spin = InputPolicy.SpinAllowed(settings, frame) && (!settings.SpinPracticeCamera || spinCameraReady);
            if (spin && settings.SpinPracticeCamera)
            {
                SpinCameraState camera = spinCamera.Observe(frame);
                if (camera == SpinCameraState.FollowingView || camera == SpinCameraState.Unavailable)
                {
                    settings.SpinEnabled = false; SyncControls(); ResetAutomation();
                    Log("Spin stopped: a steady third-person camera could not be verified. The practice server may have rejected the command."); return null;
                }
            }
            bool grounded = !settings.GroundOnly || (frame.GroundKnown && frame.OnGround);
            bool jumpHeld = (settings.BhopEnabled && space.State.Held) || (spin && settings.SpinJump);
            if (jump.Pulse(settings.BhopEnabled || (spin && settings.SpinJump), true, frame.Alive && grounded, jumpHeld, now, settings.JumpInterval)) InputSender.Jump(session.Window);
            if (spin)
            {
                recoil.Reset();
                bool enemyAtCrosshair = InputPolicy.CanFire(frame);
                bool cameraReady = !settings.SpinPracticeCamera || spinCamera.Verified;
                if (fire.Pulse(settings.SpinShoot && cameraReady, true, frame.Alive, enemyAtCrosshair, now, settings.FireInterval)) InputSender.Click(session.Window);
                AimSolution target = settings.SpinTarget && cameraReady ? aim.Step(frame, settings, viewport, seconds, 2) : null;
                InputSender.Move(session.Window, target != null ? target.Move : AimEngine.Spin(frame, settings, seconds));
                return target;
            }
            fire.Reset();
            if (settings.AimEnabled && InputSender.Held(settings.AimKey))
            {
                AimSolution target = aim.Step(frame, settings, viewport, seconds, 0);
                Point compensation = recoil.Step(frame, settings, InputSender.Held((int)Keys.LButton), target == null);
                InputSender.Move(session.Window, target != null ? target.Move : compensation); return target;
            }
            recoil.Reset();
            if (settings.AssistEnabled && InputSender.Held(settings.AssistKey))
            {
                AimSolution target = aim.Step(frame, settings, viewport, seconds, 1); if (target != null) InputSender.Move(session.Window, target.Move); return target;
            }
            aim.Reset();
            if (settings.AimEnabled || settings.AssistEnabled) return AimEngine.Select(frame, settings, viewport, settings.AimEnabled ? 0 : 1, 0);
            return null;
        }
        private async Task ApplyCamera()
        {
            if (cameraBusy || closing) return;
            if (!session.Attached) { Log("Open CS2 before applying camera commands."); return; }
            settings.DisableInputs(); SyncControls(); cameraBusy = true;
            try
            {
                string command = (spinMayaRequested && spinCameraProcess == session.ProcessId ? PracticeCamera.SteadyChaseCommand + "; " : "") + PracticeCamera.Command(settings);
                for (int i = 3; i >= 1; i--)
                {
                    SetState("Switch to CS2. Applying camera in " + i + "..."); await Task.Delay(1000); if (closing) return;
                }
                await InputSender.CameraCommand(session.Window, settings.ConsoleKey, command, delegate { return !closing; });
                spinMayaRequested = false;
                if (!closing) Log("Camera command sent. The server must allow sv_cheats; check the game console for its response.");
            }
            catch (Exception error) { if (!closing) { SetState(error.Message); Log(error.Message); } }
            finally { cameraBusy = false; }
        }
        private async void PrepareSpinCamera(Frame frame)
        {
            if (cameraBusy || !session.Attached || closing)
            {
                settings.SpinEnabled = false; SyncControls(); Log("Open CS2 and finish any camera operation before enabling spin."); return;
            }
            int epoch = spinSetupEpoch; cameraBusy = true; ResetAutomation();
            Func<bool> permitted = delegate { return !closing && settings.SpinEnabled && spinSetupEpoch == epoch; };
            try
            {
                if (spinCameraProcess != session.ProcessId) { spinCameraProcess = session.ProcessId; spinMayaRequested = false; }
                string command = (spinMayaRequested ? PracticeCamera.SteadyChaseCommand + "; " : "") + PracticeCamera.SpinCommand(settings, frame.Pitch);
                spinMayaRequested = false;
                if (Native.GetForegroundWindow() != session.Window)
                {
                    for (int i = 3; i >= 1; i--)
                    {
                        SetState("Switch to CS2. Requesting Scout spin camera in " + i + "..."); await Task.Delay(1000);
                        if (!permitted()) throw new OperationCanceledException();
                    }
                }
                await InputSender.CameraCommand(session.Window, settings.ConsoleKey, command, permitted, PracticeCamera.SteadyChaseCommand);
                spinMayaRequested = true;
                if (permitted()) { spinCameraReady = true; Log("Scout chase camera requested. Checking that its direction stays steady while your character turns."); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!closing && spinSetupEpoch == epoch) { settings.SpinEnabled = false; SyncControls(); Log(error.Message); }
            }
            finally { cameraBusy = false; }
        }
        private void ExportCamera()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { FileName = "ModafenClientXZERO.cfg", Filter = "CS2 config (*.cfg)|*.cfg", AddExtension = true, DefaultExt = "cfg" })
                if (dialog.ShowDialog(this) == DialogResult.OK) Run(delegate { File.WriteAllText(dialog.FileName, PracticeCamera.Config(settings)); Log("Practice camera config saved. Run it using exec ModafenClientXZERO on a practice server."); });
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WmHotkey)
            {
                int key = message.WParam.ToInt32();
                if (!KeyOptions.ActionEnabled(settings, key)) { base.WndProc(ref message); return; }
                if (key == Native.MenuHotkey)
                {
                    if (Visible && WindowState != FormWindowState.Minimized && ContainsFocus) Hide();
                    else { Show(); WindowState = FormWindowState.Normal; Activate(); }
                }
                if (key == Native.ToggleHotkey) settings.EspEnabled = !settings.EspEnabled;
                if (key == Native.AimHotkey) settings.AimEnabled = !settings.AimEnabled;
                if (key == Native.AssistHotkey) settings.AssistEnabled = !settings.AssistEnabled;
                if (key == Native.JumpHotkey) settings.BhopEnabled = !settings.BhopEnabled;
                if (key == Native.SpinHotkey) { settings.SpinEnabled = !settings.SpinEnabled; aim.Reset(); }
                if (key == Native.PanicHotkey) { settings.DisableInputs(); settings.EspEnabled = settings.Crosshair = settings.Hud = settings.DrawFov = settings.BombInfo = false; ResetAutomation(); overlay.Hide(); }
                if (key == Native.QuitHotkey) { Close(); return; }
                SyncControls();
            }
            base.WndProc(ref message);
        }
        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            closing = true; pulse.Dispose(); UnregisterKeys(); space.Dispose(); flash.Dispose(); overlay.Dispose(); session.Dispose(); base.OnFormClosing(args);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true; if (pulse != null) pulse.Dispose(); UnregisterKeys(); space.Dispose(); flash.Dispose(); overlay.Dispose(); session.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

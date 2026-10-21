using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Cs2Overlay
{
    public sealed class AppSettings
    {
        public bool EspEnabled { get; set; }
        public bool Boxes { get; set; }
        public int BoxStyle { get; set; }
        public bool Skeleton { get; set; }
        public bool Silhouette { get; set; }
        public bool HealthBar { get; set; }
        public bool HealthText { get; set; }
        public bool Names { get; set; }
        public bool Distances { get; set; }
        public bool Snaplines { get; set; }
        public int SnaplineAnchor { get; set; }
        public bool Teammates { get; set; }
        public bool HostageCarrier { get; set; }
        public bool BombInfo { get; set; }
        public bool FlashLimitEnabled { get; set; }
        public int FlashWhiteLimit { get; set; }
        public bool FilledBoxes { get; set; }
        public int FillOpacity { get; set; }
        public bool Outline { get; set; }
        public int EnemyColor { get; set; }
        public int TeamColor { get; set; }
        public int BoxThickness { get; set; }
        public int EspDistance { get; set; }
        public int LabelSize { get; set; }
        public bool Hud { get; set; }
        public bool Radar { get; set; }
        public int RadarSize { get; set; }
        public int RadarRange { get; set; }
        public bool RadarRotate { get; set; }
        public bool Crosshair { get; set; }
        public int CrosshairSize { get; set; }
        public int CrosshairGap { get; set; }
        public int CrosshairColor { get; set; }
        public bool AimEnabled { get; set; }
        public bool RecoilControl { get; set; }
        public decimal RecoilStrength { get; set; }
        public int AimKey { get; set; }
        public int AimRadius { get; set; }
        public bool DrawFov { get; set; }
        public int AimPoint { get; set; }
        public int AimSmoothMs { get; set; }
        public decimal Sensitivity { get; set; }
        public bool AutoSensitivity { get; set; }
        public int AimDistance { get; set; }
        public bool TargetLock { get; set; }
        public int TargetPriority { get; set; }
        public int AimDeadzone { get; set; }
        public int AimMaxStep { get; set; }
        public bool AimVisibleOnly { get; set; }
        public bool TargetIndicator { get; set; }
        public int FovColor { get; set; }
        public bool AssistEnabled { get; set; }
        public int AssistKey { get; set; }
        public int AssistRadius { get; set; }
        public int AssistSmoothMs { get; set; }
        public decimal AssistStrength { get; set; }
        public bool BhopEnabled { get; set; }
        public int JumpInterval { get; set; }
        public bool GroundOnly { get; set; }
        public bool SpinEnabled { get; set; }
        public bool SpinPracticeCamera { get; set; }
        public int SpinCameraDistance { get; set; }
        public int SpinCameraPitch { get; set; }
        public int SpinSpeed { get; set; }
        public int SpinPitch { get; set; }
        public bool SpinJump { get; set; }
        public bool SpinShoot { get; set; }
        public bool SpinTarget { get; set; }
        public int FireInterval { get; set; }
        public bool ThirdPerson { get; set; }
        public bool CustomFov { get; set; }
        public int CameraFov { get; set; }
        public int CameraDistance { get; set; }
        public int ConsoleKey { get; set; }
        public int RefreshHz { get; set; }
        public int IdleHz { get; set; }
        public bool Antialias { get; set; }
        public bool KeepPanelOnTop { get; set; }
        public int MenuBind { get; set; }
        public bool NoKeybinds { get; set; }
        public int EspBind { get; set; }
        public int AimBind { get; set; }
        public int AssistBind { get; set; }
        public int BhopBind { get; set; }
        public int SpinBind { get; set; }
        public int QuitBind { get; set; }
        public int PanicBind { get; set; }

        public AppSettings()
        {
            EspEnabled = Boxes = HealthBar = HealthText = Distances = Outline = Hud = true;
            EnemyColor = Color.Red.ToArgb(); TeamColor = Color.CornflowerBlue.ToArgb();
            BoxThickness = 2; EspDistance = 250; LabelSize = 10; FillOpacity = 20;
            HostageCarrier = true; RadarSize = 200; RadarRange = 60; RadarRotate = true;
            FlashWhiteLimit = 180;
            CrosshairSize = 6; CrosshairGap = 4; CrosshairColor = Color.LimeGreen.ToArgb();
            AimKey = (int)Keys.RButton; AimRadius = 140; DrawFov = true; AimSmoothMs = 120;
            RecoilControl = true; RecoilStrength = 1m; SpinPracticeCamera = true;
            SpinCameraDistance = 150; SpinCameraPitch = 12;
            Sensitivity = 1m; AutoSensitivity = true; AimDistance = 200; TargetLock = true;
            AimMaxStep = 100; AimDeadzone = 2; TargetIndicator = true; FovColor = Color.LimeGreen.ToArgb();
            AimVisibleOnly = true; AssistKey = (int)Keys.LButton; AssistRadius = 45; AssistSmoothMs = 220; AssistStrength = 0.35m;
            JumpInterval = 20; SpinSpeed = 1440; SpinPitch = 85; SpinJump = SpinTarget = SpinShoot = true; FireInterval = 120;
            CameraFov = 90; CameraDistance = 150; ConsoleKey = (int)Keys.Oemtilde;
            RefreshHz = 120; IdleHz = 10; Antialias = true;
            MenuBind = (int)Keys.F8; EspBind = (int)Keys.F6; AimBind = (int)Keys.F5; AssistBind = (int)Keys.F4;
            BhopBind = (int)Keys.F7; SpinBind = (int)Keys.F9; QuitBind = (int)Keys.F10; PanicBind = (int)Keys.F12;
        }
        private static int Limit(int n, int min, int max) { return Math.Max(min, Math.Min(max, n)); }
        internal void Validate()
        {
            BoxStyle = Limit(BoxStyle, 0, 1); SnaplineAnchor = Limit(SnaplineAnchor, 0, 2);
            FlashWhiteLimit = Limit(FlashWhiteLimit, 0, 255);
            FillOpacity = Limit(FillOpacity, 5, 80); BoxThickness = Limit(BoxThickness, 1, 4);
            EspDistance = Limit(EspDistance, 10, 500); LabelSize = Limit(LabelSize, 8, 18);
            RadarSize = Limit(RadarSize, 120, 350); RadarRange = Limit(RadarRange, 10, 200);
            CrosshairSize = Limit(CrosshairSize, 2, 25); CrosshairGap = Limit(CrosshairGap, 0, 20);
            AimRadius = Limit(AimRadius, 10, 1000); AimPoint = Limit(AimPoint, 0, 2); AimSmoothMs = Limit(AimSmoothMs, 0, 500);
            Sensitivity = Math.Max(0.05m, Math.Min(20m, Sensitivity)); AimDistance = Limit(AimDistance, 10, 500);
            TargetPriority = Limit(TargetPriority, 0, 2); AimDeadzone = Limit(AimDeadzone, 0, 20); AimMaxStep = Limit(AimMaxStep, 1, 500);
            AssistRadius = Limit(AssistRadius, 10, 250); AssistSmoothMs = Limit(AssistSmoothMs, 30, 500);
            AssistStrength = Math.Max(0.05m, Math.Min(1m, AssistStrength)); JumpInterval = Limit(JumpInterval, 10, 150);
            RecoilStrength = Math.Max(0m, Math.Min(2m, RecoilStrength));
            SpinSpeed = Limit(SpinSpeed, 90, 3600); SpinPitch = Limit(SpinPitch, -89, 89); FireInterval = Limit(FireInterval, 50, 1000);
            SpinCameraDistance = Limit(SpinCameraDistance, 50, 400); SpinCameraPitch = Limit(SpinCameraPitch, -60, 60);
            CameraFov = Limit(CameraFov, 54, 200); CameraDistance = Limit(CameraDistance, 50, 400);
            RefreshHz = Limit(RefreshHz, 30, 240); IdleHz = Limit(IdleHz, 2, 30);
            if (!KeyOptions.IsActivation(AimKey)) AimKey = (int)Keys.RButton;
            if (!KeyOptions.IsActivation(AssistKey)) AssistKey = (int)Keys.LButton;
            if (!KeyOptions.IsConsole(ConsoleKey)) ConsoleKey = (int)Keys.Oemtilde;
            int[] binds = new int[] { MenuBind, EspBind, AimBind, AssistBind, BhopBind, SpinBind, QuitBind, PanicBind };
            System.Collections.Generic.HashSet<int> unique = new System.Collections.Generic.HashSet<int>();
            foreach (int key in binds)
                if (!KeyOptions.IsGlobal(key) || !unique.Add(key)) throw new InvalidDataException("Choose a different key for each global toggle.");
        }
        internal void DisableInputs() { AimEnabled = AssistEnabled = BhopEnabled = SpinEnabled = FlashLimitEnabled = false; }
        internal static Color Opaque(int argb) { Color c = Color.FromArgb(argb); return Color.FromArgb(c.R, c.G, c.B); }
    }

    internal static class KeyOptions
    {
        internal static bool ActionEnabled(AppSettings settings, int id) { return !settings.NoKeybinds || id == Native.MenuHotkey; }
        internal static readonly int[] Activation = new int[] { (int)Keys.RButton, (int)Keys.LButton, (int)Keys.XButton1, (int)Keys.XButton2, (int)Keys.LMenu, (int)Keys.LShiftKey };
        internal static readonly int[] Globals = new int[] { (int)Keys.F1, (int)Keys.F2, (int)Keys.F3, (int)Keys.F4, (int)Keys.F5, (int)Keys.F6, (int)Keys.F7, (int)Keys.F8, (int)Keys.F9, (int)Keys.F10, (int)Keys.F11, (int)Keys.F12, (int)Keys.Insert, (int)Keys.Home, (int)Keys.End, (int)Keys.PageUp, (int)Keys.PageDown, (int)Keys.Pause };
        internal static readonly int[] Console = new int[] { (int)Keys.Oemtilde, (int)Keys.OemQuotes, (int)Keys.OemPipe, (int)Keys.OemBackslash };
        internal static bool IsActivation(int key) { return Array.IndexOf(Activation, key) >= 0; }
        internal static bool IsGlobal(int key) { return Array.IndexOf(Globals, key) >= 0; }
        internal static bool IsConsole(int key) { return Array.IndexOf(Console, key) >= 0; }
        internal static string Label(int key)
        {
            if (key == (int)Keys.RButton) return "Right mouse";
            if (key == (int)Keys.LButton) return "Left mouse";
            if (key == (int)Keys.XButton1) return "Mouse 4";
            if (key == (int)Keys.XButton2) return "Mouse 5";
            if (key == (int)Keys.LMenu) return "Left Alt";
            if (key == (int)Keys.LShiftKey) return "Left Shift";
            if (key == (int)Keys.Oemtilde) return "Grave / tilde";
            return ((Keys)key).ToString();
        }
    }

    internal static class Profiles
    {
        internal static string Encode(AppSettings settings) { settings.Validate(); return new JavaScriptSerializer().Serialize(settings); }
        internal static AppSettings Decode(string json)
        {
            if (json == null || json.Length > 65536) throw new InvalidDataException("Invalid profile size.");
            AppSettings settings = new JavaScriptSerializer().Deserialize<AppSettings>(json);
            if (settings == null) throw new InvalidDataException("Invalid profile data.");
            settings.Validate(); settings.DisableInputs(); return settings;
        }
        private static string PathFor(int slot)
        {
            if (slot < 1 || slot > 3) throw new ArgumentOutOfRangeException("slot");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModafenClientXZERO", "profile" + slot.ToString(CultureInfo.InvariantCulture) + ".json");
        }
        internal static void Save(int slot, AppSettings settings)
        {
            string path = PathFor(slot); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Encode(settings));
        }
        internal static AppSettings Load(int slot) { return Decode(File.ReadAllText(PathFor(slot))); }
    }

    internal static class PracticeCamera
    {
        internal const string SteadyChaseCommand = "thirdperson_mayamode";
        internal static string SpinCommand(AppSettings settings, float playerPitch)
        {
            settings.Validate();
            // Enter third person first. Maya mode is a separate toggle, applied after
            // the chase camera has settled; it keeps camera direction independent.
            float pitchOffset = settings.SpinCameraPitch - (Projection.Finite(playerPitch) ? playerPitch : 0);
            return "thirdperson; cam_collision 1; cam_snapto 1; c_minpitch -180; c_maxpitch 180; c_maxdistance 400; cam_idealyaw 0; cam_idealpitch " + pitchOffset.ToString("0.##", CultureInfo.InvariantCulture) +
                "; cam_idealdist " + settings.SpinCameraDistance.ToString(CultureInfo.InvariantCulture) +
                "; fov_cs_debug " + (settings.CustomFov ? settings.CameraFov.ToString(CultureInfo.InvariantCulture) : "0");
        }
        internal static string Command(AppSettings settings)
        {
            settings.Validate();
            return (settings.ThirdPerson ? "thirdperson; cam_idealdist " + settings.CameraDistance.ToString(CultureInfo.InvariantCulture) : "firstperson; c_minpitch 0; c_maxpitch 90; c_maxdistance 200") +
                "; fov_cs_debug " + (settings.CustomFov ? settings.CameraFov.ToString(CultureInfo.InvariantCulture) : "0");
        }
        internal static string Config(AppSettings settings)
        {
            return "// ModafenClientXZERO practice camera. Requires a server with sv_cheats 1.\r\n" + Command(settings) + "\r\n";
        }
    }
}

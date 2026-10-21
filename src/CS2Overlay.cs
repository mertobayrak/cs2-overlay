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

[assembly: AssemblyTitle("ModafenClientXZERO")]
[assembly: AssemblyDescription("CS2 external overlay and configurable Windows input controls.")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]

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
                MessageBox.Show(args.Exception.Message, "ModafenClientXZERO error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new ControllerForm());
        }
    }

    internal static partial class Native
    {
        internal const uint ReadAccess = 0x0010 | 0x0400;
        internal const int ToggleHotkey = 601, QuitHotkey = 602, AimHotkey = 603, JumpHotkey = 604, MenuHotkey = 605, SpinHotkey = 606, AssistHotkey = 607, PanicHotkey = 608, WmHotkey = 0x0312;
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

    internal sealed class Offsets
    {
        internal long EntityList, LocalPawn, LocalController, ViewMatrix;
        internal long Health, Team, LifeState, SceneNode, Origin, PlayerPawn, Dormant;
        internal long Flags, ViewOffset, PlayerName, ViewAngles, Sensitivity, SensitivityValue;
        internal long ModelState, HostageServices, CarriedHostage, SpottedState, SpottedMask, CrosshairEntity;
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
        private static long OptionalField(Dictionary<string, object> classes, string type, string field)
        {
            try { return Field(classes, type, field); } catch (InvalidDataException) { return 0; }
        }
        private static long OptionalNumber(Dictionary<string, object> map, string key)
        {
            try { return Number(map, key, 0x40000000); } catch (InvalidDataException) { return 0; }
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
            value.Flags = OptionalField(classes, "C_BaseEntity", "m_fFlags");
            value.ViewOffset = OptionalField(classes, "C_BaseModelEntity", "m_vecViewOffset");
            value.PlayerName = OptionalField(classes, "CBasePlayerController", "m_iszPlayerName");
            value.ModelState = OptionalField(classes, "CSkeletonInstance", "m_modelState");
            value.HostageServices = OptionalField(classes, "C_CSPlayerPawn", "m_pHostageServices");
            value.CarriedHostage = OptionalField(classes, "CCSPlayer_HostageServices", "m_hCarriedHostage");
            value.SpottedState = OptionalField(classes, "C_CSPlayerPawn", "m_entitySpottedState");
            value.SpottedMask = OptionalField(classes, "EntitySpottedState_t", "m_bSpottedByMask");
            value.CrosshairEntity = OptionalField(classes, "C_CSPlayerPawn", "m_iIDEntIndex");
            value.ViewAngles = OptionalNumber(globals, "dwViewAngles");
            value.Sensitivity = OptionalNumber(globals, "dwSensitivity");
            value.SensitivityValue = OptionalNumber(globals, "dwSensitivity_sensitivity");
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
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ModafenClientXZERO/0.3.0");
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

}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Cs2Overlay
{
    internal sealed class MemoryReader : IDisposable
    {
        private IntPtr handle;
        private readonly byte[] scalar = new byte[8];
        internal long ReadCalls { get; private set; }
        internal MemoryReader(IntPtr processHandle) { handle = processHandle; }
        internal bool TryReadInto(long address, byte[] buffer, int size)
        {
            if (handle == IntPtr.Zero || address <= 0 || buffer == null || size <= 0 || size > 65536 || size > buffer.Length) return false;
            UIntPtr read; ReadCalls++;
            return Native.ReadProcessMemory(handle, new IntPtr(address), buffer, new UIntPtr((uint)size), out read) && read.ToUInt64() == (ulong)size;
        }
        internal bool TryBytes(long address, int size, out byte[] bytes)
        {
            bytes = null;
            if (size <= 0 || size > 65536) return false;
            byte[] buffer = new byte[size];
            if (!TryReadInto(address, buffer, size)) return false;
            bytes = buffer; return true;
        }
        internal bool TryInt32(long address, out int value)
        {
            bool ok = TryReadInto(address, scalar, 4); value = ok ? BitConverter.ToInt32(scalar, 0) : 0; return ok;
        }
        internal bool TryByte(long address, out byte value)
        {
            bool ok = TryReadInto(address, scalar, 1); value = ok ? scalar[0] : (byte)0; return ok;
        }
        internal long Pointer(long address) { return TryReadInto(address, scalar, 8) ? BitConverter.ToInt64(scalar, 0) : 0; }
        internal float[] Floats(long address, int count)
        {
            byte[] bytes;
            if (count <= 0 || count > 16384 || !TryBytes(address, count * 4, out bytes)) return null;
            float[] result = new float[count]; Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length); return result;
        }
        public void Dispose() { if (handle != IntPtr.Zero) { Native.CloseHandle(handle); handle = IntPtr.Zero; } }
    }

    internal sealed class EntityPages
    {
        private sealed class Page { internal byte[] Bytes; internal long Address; internal int Generation; internal bool Valid; }
        private readonly MemoryReader reader;
        private readonly Dictionary<int, Page> pages = new Dictionary<int, Page>();
        private int generation;
        private long root;
        internal EntityPages(MemoryReader memory) { reader = memory; }
        internal void BeginFrame(long list)
        {
            if (list != root || generation == Int32.MaxValue) { pages.Clear(); generation = 0; }
            root = list; generation++;
        }
        internal long Get(int index, int stride)
        {
            if (root <= 0 || (stride != 0x78 && stride != 0x70)) return 0;
            index &= 0x7FFF;
            int pageIndex = index >> 9, key = pageIndex * 256 + stride;
            Page page;
            if (!pages.TryGetValue(key, out page)) { page = new Page { Bytes = new byte[512 * stride] }; pages.Add(key, page); }
            if (page.Generation != generation)
            {
                page.Generation = generation;
                page.Address = reader.Pointer(root + 0x10 + 8L * pageIndex);
                page.Valid = page.Address > 0 && reader.TryReadInto(page.Address, page.Bytes, page.Bytes.Length);
            }
            int offset = (index & 511) * stride;
            if (page.Valid) return BitConverter.ToInt64(page.Bytes, offset);
            return page.Address > 0 ? reader.Pointer(page.Address + offset) : 0;
        }
    }

    internal struct Vec3
    {
        internal float X, Y, Z;
        internal Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        internal float Distance(Vec3 other)
        {
            double x = X - other.X, y = Y - other.Y, z = Z - other.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
        internal bool Valid { get { return Projection.Finite(X) && Projection.Finite(Y) && Projection.Finite(Z) && Math.Abs(X) < 1000000 && Math.Abs(Y) < 1000000 && Math.Abs(Z) < 1000000; } }
    }
    internal sealed class Player
    {
        internal long Id;
        internal Vec3 Position;
        internal Vec3[] Bones;
        internal float Height = 72, Distance;
        internal int Health;
        internal bool Teammate, Carrier, SpottedByLocal;
        internal string Name = "";
        internal Vec3 Target(int point)
        {
            int bone = point == 0 ? 6 : (point == 1 ? 4 : 0);
            if (Bones != null && Bones.Length > bone) return Bones[bone];
            return new Vec3(Position.X, Position.Y, Position.Z + (point == 0 ? Height - 4 : Height * (point == 1 ? 0.65f : 0.42f)));
        }
    }
    internal sealed class Frame
    {
        internal float[] Matrix;
        internal readonly List<Player> Players = new List<Player>();
        internal string Status = "Waiting for a live player";
        internal int PlayersRead, SkeletonsRead;
        internal bool Alive, GroundKnown, OnGround, AnglesValid, PositionValid;
        internal float Pitch, Yaw, Sensitivity;
        internal Vec3 Position, Eye;
        internal long CrosshairPawn;
    }

    internal static class BoneData
    {
        internal static readonly int[,] Links = new int[,] { {6,5},{5,4},{4,3},{3,2},{2,0},{5,8},{8,9},{9,10},{5,13},{13,14},{14,15},{0,22},{22,23},{23,24},{0,25},{25,26},{26,27} };
        internal static Vec3[] Decode(byte[] bytes, Vec3 origin)
        {
            if (bytes == null || bytes.Length < 28 * 32) return null;
            Vec3[] result = new Vec3[28];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Vec3(BitConverter.ToSingle(bytes, i * 32), BitConverter.ToSingle(bytes, i * 32 + 4), BitConverter.ToSingle(bytes, i * 32 + 8));
            foreach (int i in Links)
                if (!result[i].Valid || result[i].Distance(origin) > 180) return null;
            if (result[6].Distance(result[0]) < 8 || result[6].Distance(result[0]) > 110) return null;
            return result;
        }
    }

    internal sealed class Session : IDisposable
    {
        private sealed class NameEntry { internal string Value; internal DateTime Expires; }
        private Process game;
        private MemoryReader reader;
        private EntityPages pages;
        private readonly Dictionary<long, NameEntry> names = new Dictionary<long, NameEntry>();
        private readonly byte[] pawnBytes = new byte[4096], boneBytes = new byte[28 * 32];
        private long client;
        private int stride, localSlot;
        internal IntPtr Window { get; private set; }
        internal bool Attached { get { return game != null; } }
        internal bool HasExited
        {
            get { if (game == null) return true; try { return game.HasExited; } catch (InvalidOperationException) { return true; } }
        }
        internal void RefreshWindow()
        {
            if (game == null) return;
            try { game.Refresh(); Window = game.MainWindowHandle; } catch (InvalidOperationException) { Window = IntPtr.Zero; }
        }
        internal bool TryAttach(Offsets offsets, out string status)
        {
            Dispose();
            Process[] candidates = Process.GetProcessesByName("cs2");
            status = "Waiting for CS2. Open the game to continue.";
            foreach (Process candidate in candidates)
            {
                if (game != null) { candidate.Dispose(); continue; }
                IntPtr handle = IntPtr.Zero; bool adopted = false;
                try
                {
                    candidate.Refresh(); IntPtr window = candidate.MainWindowHandle;
                    if (window == IntPtr.Zero) { status = "Waiting for CS2's game window."; continue; }
                    handle = Native.OpenProcess(Native.ReadAccess, false, candidate.Id);
                    if (handle == IntPtr.Zero) { status = "Read access denied: " + new Win32Exception(Marshal.GetLastWin32Error()).Message; continue; }
                    status = "Waiting for client.dll to load.";
                    foreach (ProcessModule module in candidate.Modules)
                    {
                        if (!String.Equals(module.ModuleName, "client.dll", StringComparison.OrdinalIgnoreCase)) continue;
                        long size = module.ModuleMemorySize;
                        if (offsets.EntityList + 8 >= size || offsets.LocalPawn + 8 >= size || offsets.LocalController + 8 >= size || offsets.ViewMatrix + 64 >= size)
                        { status = "Published offsets do not fit this CS2 build. Try Refresh offsets."; break; }
                        game = candidate; Window = window; client = module.BaseAddress.ToInt64();
                        reader = new MemoryReader(handle); pages = new EntityPages(reader); adopted = true;
                        status = "Attached. Join a match and select a team."; break;
                    }
                }
                catch (Exception error) { status = "Could not attach: " + error.Message; }
                finally { if (!adopted) { if (handle != IntPtr.Zero) Native.CloseHandle(handle); candidate.Dispose(); } }
            }
            return Attached;
        }
        private int FindStride(long controller)
        {
            if (controller <= 0) return 0;
            foreach (int size in new int[] { 0x78, 0x70 })
                for (int slot = 1; slot <= 64; slot++)
                    if (pages.Get(slot, size) == controller) { localSlot = slot; return size; }
            return 0;
        }
        private bool ReadPawn(long pawn, Offsets offsets, out int health, out byte team, out byte life, out long scene, out int flags)
        {
            health = 0; team = 0; life = 0; scene = 0; flags = 0;
            long start = Math.Min(offsets.SceneNode, Math.Min(offsets.Health, Math.Min(offsets.Team, offsets.LifeState)));
            long end = Math.Max(offsets.SceneNode + 8, Math.Max(offsets.Health + 4, Math.Max(offsets.Team + 1, offsets.LifeState + 1)));
            if (offsets.Flags > 0) { start = Math.Min(start, offsets.Flags); end = Math.Max(end, offsets.Flags + 4); }
            long length = end - start;
            if (length <= 0 || length > pawnBytes.Length || !reader.TryReadInto(pawn + start, pawnBytes, (int)length)) return false;
            health = BitConverter.ToInt32(pawnBytes, (int)(offsets.Health - start));
            team = pawnBytes[(int)(offsets.Team - start)]; life = pawnBytes[(int)(offsets.LifeState - start)];
            scene = BitConverter.ToInt64(pawnBytes, (int)(offsets.SceneNode - start));
            if (offsets.Flags > 0) flags = BitConverter.ToInt32(pawnBytes, (int)(offsets.Flags - start));
            return true;
        }
        private bool Position(long address, out Vec3 value)
        {
            float[] p = reader.Floats(address, 3); value = p == null ? new Vec3() : new Vec3(p[0], p[1], p[2]);
            return p != null && value.Valid;
        }
        private string PlayerName(long controller, Offsets offsets)
        {
            if (offsets.PlayerName == 0) return "";
            NameEntry entry; DateTime now = DateTime.UtcNow;
            if (names.TryGetValue(controller, out entry) && now < entry.Expires) return entry.Value;
            byte[] bytes; string value = "";
            if (reader.TryBytes(controller + offsets.PlayerName, 128, out bytes))
            {
                int end = Array.IndexOf(bytes, (byte)0); if (end < 0) end = bytes.Length;
                StringBuilder clean = new StringBuilder();
                foreach (char c in Encoding.UTF8.GetString(bytes, 0, end)) if (!Char.IsControl(c)) clean.Append(c);
                value = clean.ToString(); if (value.Length > 28) value = value.Substring(0, 28);
            }
            if (names.Count > 256) names.Clear();
            names[controller] = new NameEntry { Value = value, Expires = now.AddSeconds(1) }; return value;
        }
        internal Frame ReadFrame(Offsets offsets, AppSettings settings, bool needPlayers)
        {
            Frame frame = new Frame(); if (!Attached) return frame;
            long list = reader.Pointer(client + offsets.EntityList), localPawn = reader.Pointer(client + offsets.LocalPawn);
            frame.Matrix = reader.Floats(client + offsets.ViewMatrix, 16);
            if (list <= 0 || frame.Matrix == null) { frame.Status = "Game data could not be read. Try Refresh offsets."; return frame; }
            int localHealth, localFlags; byte localTeam, localLife; long localScene;
            if (localPawn <= 0 || !ReadPawn(localPawn, offsets, out localHealth, out localTeam, out localLife, out localScene, out localFlags) || (localTeam != 2 && localTeam != 3))
            { frame.Status = "Attached. Join a match and select a team."; return frame; }
            frame.Alive = localHealth > 0 && localHealth <= 1000 && localLife == 0;
            frame.GroundKnown = offsets.Flags > 0; frame.OnGround = (localFlags & 1) != 0;
            Vec3 origin = new Vec3();
            frame.PositionValid = localScene > 0 && Position(localScene + offsets.Origin, out origin);
            if (frame.PositionValid)
            {
                frame.Position = origin;
                frame.Eye = new Vec3(origin.X, origin.Y, origin.Z + 64);
                Vec3 eye;
                if (offsets.ViewOffset > 0 && Position(localPawn + offsets.ViewOffset, out eye) && eye.Z >= 10 && eye.Z <= 80)
                    frame.Eye = new Vec3(origin.X + eye.X, origin.Y + eye.Y, origin.Z + eye.Z);
            }
            float[] angles = offsets.ViewAngles > 0 ? reader.Floats(client + offsets.ViewAngles, 2) : null;
            frame.AnglesValid = frame.PositionValid && angles != null && Projection.Finite(angles[0]) && Projection.Finite(angles[1]) && Math.Abs(angles[0]) <= 90 && Math.Abs(angles[1]) <= 360;
            if (frame.AnglesValid) { frame.Pitch = angles[0]; frame.Yaw = angles[1]; }
            if (settings.AutoSensitivity && (settings.AimEnabled || settings.AssistEnabled || settings.SpinEnabled) && offsets.Sensitivity > 0 && offsets.SensitivityValue > 0)
            {
                long sensitivity = reader.Pointer(client + offsets.Sensitivity);
                float[] value = sensitivity > 0 ? reader.Floats(sensitivity + offsets.SensitivityValue, 1) : null;
                if (value != null && Projection.Finite(value[0]) && value[0] >= 0.05f && value[0] <= 20) frame.Sensitivity = value[0];
            }
            if (!needPlayers) { frame.Status = frame.Alive ? "Attached. Movement controls ready." : "Attached. Waiting for a live local player."; return frame; }
            pages.BeginFrame(list);
            long controller = reader.Pointer(client + offsets.LocalController);
            if (stride == 0 || localSlot <= 0 || pages.Get(localSlot, stride) != controller) stride = FindStride(controller);
            if (stride == 0) { frame.Status = "The entity layout did not match this CS2 build."; return frame; }
            int crosshair;
            if (offsets.CrosshairEntity > 0 && reader.TryInt32(localPawn + offsets.CrosshairEntity, out crosshair) && crosshair > 0 && crosshair < 32768)
                frame.CrosshairPawn = pages.Get(crosshair, stride);
            foreach (int slot in Slots)
            {
                long playerController = pages.Get(slot, stride); if (playerController <= 0) continue;
                int handle;
                if (!reader.TryInt32(playerController + offsets.PlayerPawn, out handle) || handle == 0 || handle == -1) continue;
                long pawn = pages.Get(handle, stride); if (pawn <= 0) continue;
                int health, flags; byte team, life; long scene;
                if (!ReadPawn(pawn, offsets, out health, out team, out life, out scene, out flags) || health <= 0 || health > 1000 || (team != 2 && team != 3) || life != 0) continue;
                frame.PlayersRead++; if (pawn == localPawn || scene <= 0) continue;
                byte dormant;
                if (offsets.Dormant != 0 && (!reader.TryByte(scene + offsets.Dormant, out dormant) || dormant != 0)) continue;
                Vec3 position; if (!Position(scene + offsets.Origin, out position)) continue;
                Player player = new Player { Id = pawn, Position = position, Health = health, Teammate = team == localTeam,
                    Distance = frame.PositionValid ? position.Distance(frame.Position) * 0.0254f : 0 };
                Vec3 eye;
                if (offsets.ViewOffset > 0 && Position(pawn + offsets.ViewOffset, out eye) && eye.Z >= 10 && eye.Z <= 80) player.Height = eye.Z + 8;
                if (settings.Names) player.Name = PlayerName(playerController, offsets);
                if (settings.HostageCarrier && offsets.HostageServices > 0 && offsets.CarriedHostage > 0)
                {
                    long service = reader.Pointer(pawn + offsets.HostageServices); int hostage;
                    player.Carrier = service > 0 && reader.TryInt32(service + offsets.CarriedHostage, out hostage) && hostage != 0 && hostage != -1 && (hostage & 0x7FFF) != 0x7FFF;
                }
                if (localSlot > 0 && localSlot <= 64 && offsets.SpottedState > 0 && offsets.SpottedMask > 0)
                {
                    byte[] mask;
                    if (reader.TryBytes(pawn + offsets.SpottedState + offsets.SpottedMask, 8, out mask))
                        player.SpottedByLocal = (BitConverter.ToUInt64(mask, 0) & (1UL << (localSlot - 1))) != 0;
                }
                if ((settings.Skeleton || settings.Silhouette || settings.AimEnabled || settings.AssistEnabled || (settings.SpinEnabled && settings.SpinTarget)) && offsets.ModelState > 0)
                {
                    long bones = reader.Pointer(scene + offsets.ModelState + 0x80);
                    if (bones > 0 && reader.TryReadInto(bones, boneBytes, boneBytes.Length)) player.Bones = BoneData.Decode(boneBytes, position);
                    if (player.Bones != null) frame.SkeletonsRead++;
                }
                frame.Players.Add(player);
            }
            frame.Status = "Attached | " + frame.PlayersRead + " live | " + frame.Players.Count + " tracked";
            if ((settings.Skeleton || settings.Silhouette) && frame.Players.Count > 0 && frame.SkeletonsRead == 0) frame.Status += " | Bone data unavailable";
            return frame;
        }
        private static readonly int[] Slots = BuildSlots();
        private static int[] BuildSlots() { int[] slots = new int[64]; for (int i = 0; i < slots.Length; i++) slots[i] = i + 1; return slots; }
        internal bool TryBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty; if (Window == IntPtr.Zero || Native.IsIconic(Window)) return false;
            Native.Rect rectangle; Native.Point origin = new Native.Point();
            if (!Native.GetClientRect(Window, out rectangle) || !Native.ClientToScreen(Window, ref origin)) return false;
            int width = rectangle.Right - rectangle.Left, height = rectangle.Bottom - rectangle.Top;
            if (width <= 0 || height <= 0) return false;
            bounds = new Rectangle(origin.X, origin.Y, width, height); return true;
        }
        public void Dispose()
        {
            if (reader != null) reader.Dispose(); if (game != null) game.Dispose();
            reader = null; game = null; pages = null; Window = IntPtr.Zero; client = 0; stride = localSlot = 0; names.Clear();
        }
    }
}

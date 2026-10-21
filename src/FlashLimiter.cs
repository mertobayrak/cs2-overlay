using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Cs2Overlay
{
    internal static partial class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr size, out UIntPtr written);
    }
    // The only memory-writing feature. Write access is opened on demand; every
    // write is limited by this helper to the current pawn's flash-opacity field.
    internal sealed class FlashLimiter : IDisposable
    {
        private IntPtr handle;
        private int process;
        private long pawn, globalPawn, address;
        private float original, applied;
        private bool modified;
        private bool Read(long location, int count, out byte[] data)
        {
            data = new byte[count]; UIntPtr read;
            return handle != IntPtr.Zero && location > 0 && Native.ReadProcessMemory(handle, new IntPtr(location), data, new UIntPtr((uint)count), out read) && read.ToUInt64() == (ulong)count;
        }
        private bool SamePawn()
        {
            byte[] data; return Read(globalPawn, 8, out data) && BitConverter.ToInt64(data, 0) == pawn;
        }
        private bool Current(out float value)
        {
            byte[] data; bool ok = Read(address, 4, out data);
            value = ok ? BitConverter.ToSingle(data, 0) : Single.NaN;
            return ok && Projection.Finite(value) && value >= 0 && value <= 255;
        }
        private bool Write(float value)
        {
            if (!SamePawn()) return false;
            UIntPtr written;
            return Native.WriteProcessMemory(handle, new IntPtr(address), BitConverter.GetBytes(value), new UIntPtr(4), out written) && written.ToUInt64() == 4;
        }
        internal bool Apply(int processId, long localPawn, long localPawnGlobal, long field, int cap, out string error)
        {
            error = "";
            if (field <= 0 || field >= 0x100000 || processId <= 0 || localPawn <= 0 || localPawnGlobal <= 0)
            { Disable(); error = "Flash opacity data is unavailable for this build."; return false; }
            cap = Math.Max(0, Math.Min(255, cap));
            if (cap == 255) { Disable(); return true; }
            if (handle != IntPtr.Zero && (process != processId || pawn != localPawn || globalPawn != localPawnGlobal || address != localPawn + field)) Disable();
            if (handle == IntPtr.Zero)
            {
                handle = Native.OpenProcess(Native.ReadAccess | 0x0020 | 0x0008, false, processId);
                if (handle == IntPtr.Zero) { error = "Flash limit could not open write access: " + new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
                process = processId; pawn = localPawn; globalPawn = localPawnGlobal; address = pawn + field;
            }
            float current;
            if (!SamePawn() || !Current(out current)) { error = "Flash opacity could not be read. Flash limit paused."; Disable(); return false; }
            if (modified && Math.Abs(current - applied) > 0.01f) modified = false;
            float target = Math.Min(modified ? original : current, cap);
            if (Math.Abs(current - target) <= 0.01f) return true;
            if (!modified) original = current;
            modified = true;
            if (!Write(target)) { error = "Windows rejected the flash opacity change. Flash limit paused."; Disable(); return false; }
            applied = target; return true;
        }
        internal void Disable()
        {
            if (handle == IntPtr.Zero) return;
            float current;
            if (modified && SamePawn() && Current(out current) && Math.Abs(current - applied) <= 0.01f) Write(original);
            Native.CloseHandle(handle); handle = IntPtr.Zero; modified = false; process = 0; pawn = globalPawn = address = 0;
        }
        public void Dispose() { Disable(); }
    }
}

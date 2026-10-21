using System;
using System.Drawing;

namespace Cs2Overlay
{
    internal static class WeaponReader
    {
        internal const int Scout = 40;
        internal static bool Read(MemoryReader memory, EntityPages pages, long pawn, Offsets o, int stride, out ushort definition)
        {
            definition = 0;
            if (o.WeaponServices <= 0 || o.ActiveWeapon <= 0 || o.AttributeManager <= 0 || o.Item <= 0 || o.ItemDefinition <= 0) return false;
            long service = memory.Pointer(pawn + o.WeaponServices); int handle;
            if (service <= 0 || !memory.TryInt32(service + o.ActiveWeapon, out handle) || handle == 0 || handle == -1) return false;
            long weapon = pages.Get(handle, stride);
            return weapon > 0 && memory.TryUInt16(weapon + o.AttributeManager + o.Item + o.ItemDefinition, out definition) && definition > 0;
        }
    }
    internal static class RecoilReader
    {
        internal static void Read(MemoryReader memory, long pawn, Offsets o, Frame frame)
        {
            int shots;
            if (o.ShotsFired <= 0 || !memory.TryInt32(pawn + o.ShotsFired, out shots) || shots < 0 || shots > 1000) return;
            frame.ShotsFired = shots;
            if (shots == 0) return;
            float[] value = null;
            if (o.AimPunchServices > 0 && o.PredictablePunch > 0)
            {
                long service = memory.Pointer(pawn + o.AimPunchServices);
                if (service > 0) value = memory.Floats(service + o.PredictablePunch, 3);
            }
            if (value == null && o.LegacyPunch > 0) value = memory.Floats(pawn + o.LegacyPunch, 3);
            if (value == null || !Projection.Finite(value[0]) || !Projection.Finite(value[1]) || Math.Abs(value[0]) > 45 || Math.Abs(value[1]) > 45) return;
            frame.Punch = new Vec3(value[0], value[1], 0); frame.PunchKnown = true;
        }
    }
    internal sealed class RecoilCompensation
    {
        private double oldPitch, oldYaw, carryX, carryY;
        internal void Reset() { oldPitch = oldYaw = carryX = carryY = 0; }
        internal Point Step(Frame frame, AppSettings settings, bool firing, bool apply)
        {
            if (!frame.Alive || !settings.RecoilControl || !firing || !frame.PunchKnown || frame.ShotsFired <= 0) { Reset(); return Point.Empty; }
            double scale = 2 * (double)settings.RecoilStrength;
            double pitch = frame.Punch.X * scale, yaw = frame.Punch.Y * scale;
            double sensitivity = settings.AutoSensitivity && frame.Sensitivity > 0 ? frame.Sensitivity : (double)settings.Sensitivity;
            double dx = (yaw - oldYaw) / (0.022 * sensitivity) + carryX;
            double dy = -(pitch - oldPitch) / (0.022 * sensitivity) + carryY;
            oldPitch = pitch; oldYaw = yaw;
            if (!apply) { carryX = carryY = 0; return Point.Empty; }
            int x = (int)Math.Round(Math.Max(-settings.AimMaxStep, Math.Min(settings.AimMaxStep, dx)));
            int y = (int)Math.Round(Math.Max(-settings.AimMaxStep, Math.Min(settings.AimMaxStep, dy)));
            carryX = dx - x; carryY = dy - y; return new Point(x, y);
        }
    }
    internal enum SpinCameraState { Pending, Steady, FollowingView, Unavailable }
    internal sealed class SpinCameraMonitor
    {
        private bool sampled;
        private double oldPitch, oldYaw, oldCameraPitch, oldCameraYaw, playerTravel, cameraTravel;
        internal bool Verified { get; private set; }
        internal void Reset() { sampled = false; Verified = false; playerTravel = cameraTravel = 0; }
        internal SpinCameraState Observe(Frame frame)
        {
            if (!frame.AnglesValid || frame.Matrix == null || frame.Matrix.Length != 16) return SpinCameraState.Unavailable;
            double x = frame.Matrix[12], y = frame.Matrix[13], z = frame.Matrix[14];
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (Double.IsNaN(length) || Double.IsInfinity(length) || length < 0.5 || length > 2) return SpinCameraState.Unavailable;
            double cameraPitch = -Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180 / Math.PI;
            double cameraYaw = Math.Atan2(y, x) * 180 / Math.PI;
            if (sampled)
            {
                playerTravel += Math.Abs(AimEngine.Wrap(frame.Pitch - oldPitch)) + Math.Abs(AimEngine.Wrap(frame.Yaw - oldYaw));
                cameraTravel += Math.Abs(AimEngine.Wrap(cameraPitch - oldCameraPitch)) + Math.Abs(AimEngine.Wrap(cameraYaw - oldCameraYaw));
            }
            sampled = true; oldPitch = frame.Pitch; oldYaw = frame.Yaw; oldCameraPitch = cameraPitch; oldCameraYaw = cameraYaw;
            if (playerTravel >= 35)
            {
                if (cameraTravel > 12 && cameraTravel / playerTravel > 0.4) return SpinCameraState.FollowingView;
                if (cameraTravel <= 8 && cameraTravel / playerTravel < 0.2) Verified = true;
                else if (playerTravel > 90) return SpinCameraState.Unavailable;
                if (Verified) playerTravel = cameraTravel = 0;
            }
            return Verified ? SpinCameraState.Steady : SpinCameraState.Pending;
        }
    }
}

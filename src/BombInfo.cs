using System;
using System.Globalization;

namespace Cs2Overlay
{
    internal enum BombPhase { Planted, Defused, Exploded }

    internal sealed class BombSnapshot
    {
        internal BombPhase Phase;
        internal string Site;
        internal Vec3 Position;
        internal bool PositionValid, TimerKnown, BeingDefused, DefuseTimerKnown, CannotDefuse;
        internal float SecondsLeft, TimerLength, DefuseSeconds;
        internal bool DefuseInTime;
        internal float LatestStart(bool kit) { return SecondsLeft - (kit ? 5 : 10); }
        internal string StartLabel(bool kit)
        {
            if (!TimerKnown) return "--";
            float remaining = LatestStart(kit);
            return remaining > 0 ? remaining.ToString("0.0", CultureInfo.InvariantCulture) + "s" : "too late";
        }
    }

    internal static class BombReader
    {
        // x64 CGlobalVarsBase::curtime, from alliedmodders/hl2sdk cs2/public/globalvars_base.h.
        // Validate it against the networked bomb deadline before displaying a countdown.
        private const int CurrentTime = 0x30;
        internal static bool Available(Offsets o)
        {
            return o.PlantedC4 > 0 && o.SceneNode > 0 && o.Origin > 0 && o.BombTicking > 0 && o.BombSite > 0 &&
                o.BombBlowTime > 0 && o.BombTimerLength > 0 && o.BombDefused > 0 && o.BombExploded > 0;
        }
        internal static BombSnapshot Read(MemoryReader memory, long client, Offsets o)
        {
            if (!Available(o)) return null;
            long root = memory.Pointer(client + o.PlantedC4); if (root <= 0) return null;
            // The global has used a pointer-to-pointer; validate both supported representations.
            long entity = memory.Pointer(root);
            BombSnapshot result = entity > 0 ? ReadEntity(memory, entity, client, o) : null;
            return result ?? ReadEntity(memory, root, client, o);
        }
        private static BombSnapshot ReadEntity(MemoryReader memory, long entity, long client, Offsets o)
        {
            long start = Int64.MaxValue, end = 0;
            long[] bytes = new long[] { o.BombTicking, o.BombDefused, o.BombExploded, o.BombDefusing, o.BombCannotDefuse };
            long[] words = new long[] { o.BombSite, o.BombBlowTime, o.BombTimerLength, o.BombDefuseLength, o.BombDefuseEnd };
            foreach (long field in bytes) if (field > 0) { start = Math.Min(start, field); end = Math.Max(end, field + 1); }
            foreach (long field in words) if (field > 0) { start = Math.Min(start, field); end = Math.Max(end, field + 4); }
            if (start <= 0 || end <= start || end - start > 4096) return null;
            byte[] data; if (!memory.TryBytes(entity + start, (int)(end - start), out data)) return null;
            foreach (long field in bytes) if (field > 0 && data[(int)(field - start)] > 1) return null;
            bool ticking = data[(int)(o.BombTicking - start)] != 0;
            bool defused = data[(int)(o.BombDefused - start)] != 0, exploded = data[(int)(o.BombExploded - start)] != 0;
            if ((!ticking && !defused && !exploded) || (defused && exploded)) return null;
            int site = BitConverter.ToInt32(data, (int)(o.BombSite - start));
            float blow = BitConverter.ToSingle(data, (int)(o.BombBlowTime - start));
            float length = BitConverter.ToSingle(data, (int)(o.BombTimerLength - start));
            if (site < -1 || site > 16 || !Projection.Finite(blow) || blow <= 0 || !Projection.Finite(length) || length < 1 || length > 1800) return null;
            BombSnapshot snapshot = new BombSnapshot { Phase = defused ? BombPhase.Defused : (exploded ? BombPhase.Exploded : BombPhase.Planted),
                Site = site == 0 ? "A" : (site == 1 ? "B" : "?"), TimerLength = length,
                CannotDefuse = o.BombCannotDefuse > 0 && data[(int)(o.BombCannotDefuse - start)] != 0 };
            long scene = memory.Pointer(entity + o.SceneNode);
            float[] position = scene > 0 ? memory.Floats(scene + o.Origin, 3) : null;
            if (position != null)
            {
                snapshot.Position = new Vec3(position[0], position[1], position[2]); snapshot.PositionValid = snapshot.Position.Valid;
            }
            long global = o.GlobalVars > 0 ? memory.Pointer(client + o.GlobalVars) : 0;
            float[] clock = global > 0 ? memory.Floats(global + CurrentTime, 1) : null;
            float now = clock != null ? clock[0] : Single.NaN;
            snapshot.TimerKnown = Projection.Finite(now) && now >= 0 && now <= blow + 5 && now >= blow - length - 2;
            if (snapshot.TimerKnown) snapshot.SecondsLeft = Math.Max(0, blow - now);
            snapshot.BeingDefused = !defused && !exploded && o.BombDefusing > 0 && data[(int)(o.BombDefusing - start)] != 0;
            if (snapshot.BeingDefused && snapshot.TimerKnown && o.BombDefuseEnd > 0 && o.BombDefuseLength > 0)
            {
                float finish = BitConverter.ToSingle(data, (int)(o.BombDefuseEnd - start));
                float duration = BitConverter.ToSingle(data, (int)(o.BombDefuseLength - start));
                snapshot.DefuseTimerKnown = Projection.Finite(finish) && Projection.Finite(duration) && duration > 0 && duration <= 60 && finish >= now - 1 && finish - now <= duration + 1;
                if (snapshot.DefuseTimerKnown) { snapshot.DefuseSeconds = Math.Max(0, finish - now); snapshot.DefuseInTime = finish < blow; }
            }
            return snapshot;
        }
    }

    internal static class OverlayOptions
    {
        internal static bool ShouldDraw(AppSettings s)
        {
            return s.EspEnabled || s.Hud || s.Crosshair || s.DrawFov || s.BombInfo ||
                (s.TargetIndicator && (s.AimEnabled || s.AssistEnabled || s.SpinEnabled));
        }
        internal static int CircleRadius(AppSettings s) { return s.AssistEnabled && !s.AimEnabled ? s.AssistRadius : s.AimRadius; }
    }
}

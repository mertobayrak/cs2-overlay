using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Cs2Overlay
{
    internal static class RadarProjection
    {
        internal static PointF Point(Vec3 player, Vec3 local, float yaw, bool rotate, float range, RectangleF area)
        {
            double x = (player.X - local.X) * 0.0254, y = (player.Y - local.Y) * 0.0254;
            double right = x, forward = y;
            if (rotate)
            {
                double radians = yaw * Math.PI / 180;
                right = Math.Sin(radians) * x - Math.Cos(radians) * y;
                forward = Math.Cos(radians) * x + Math.Sin(radians) * y;
            }
            double radius = Math.Max(1, area.Width / 2 - 10), scale = radius / Math.Max(1, range);
            double distance = Math.Sqrt(right * right + forward * forward);
            if (distance * scale > radius) scale = radius / Math.Max(0.001, distance);
            return new PointF(area.X + area.Width / 2 + (float)(right * scale), area.Y + area.Height / 2 - (float)(forward * scale));
        }
    }

    internal sealed class OverlayForm : Form
    {
        private Frame frame;
        private AppSettings settings;
        private AimSolution aim;
        private Font labelFont;
        private int fontSize;
        private readonly Font caption = new Font("Segoe UI", 10f, FontStyle.Bold);
        private string cadence = "";
        internal OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual; BackColor = TransparencyKey = Color.Magenta;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x20 | 0x80 | 0x08000000; return value; }
        }
        internal void Present(Frame value, Rectangle bounds, AppSettings configuration, AimSolution solution, string measuredCadence)
        {
            frame = value; settings = configuration; aim = solution; cadence = measuredCadence;
            if (labelFont == null || fontSize != settings.LabelSize)
            {
                if (labelFont != null) labelFont.Dispose();
                fontSize = settings.LabelSize; labelFont = new Font("Segoe UI", fontSize, FontStyle.Bold);
            }
            if (Bounds != bounds) Bounds = bounds;
            if (!Visible) Show();
            Invalidate();
        }
        private static void Box(Graphics g, Pen pen, RectangleF r, int style)
        {
            if (style == 0) { g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height); return; }
            float x = r.Width * 0.26f, y = r.Height * 0.2f;
            g.DrawLine(pen, r.Left, r.Top, r.Left + x, r.Top); g.DrawLine(pen, r.Left, r.Top, r.Left, r.Top + y);
            g.DrawLine(pen, r.Right, r.Top, r.Right - x, r.Top); g.DrawLine(pen, r.Right, r.Top, r.Right, r.Top + y);
            g.DrawLine(pen, r.Left, r.Bottom, r.Left + x, r.Bottom); g.DrawLine(pen, r.Left, r.Bottom, r.Left, r.Bottom - y);
            g.DrawLine(pen, r.Right, r.Bottom, r.Right - x, r.Bottom); g.DrawLine(pen, r.Right, r.Bottom, r.Right, r.Bottom - y);
        }
        private void TextAt(Graphics g, string text, float x, float y, Color color)
        {
            SizeF size = g.MeasureString(text, labelFont);
            g.FillRectangle(Brushes.Black, x - 1, y, size.Width + 2, size.Height);
            using (Brush brush = new SolidBrush(color)) g.DrawString(text, labelFont, brush, x, y);
        }
        private bool Screen(Vec3 position, out PointF point)
        {
            return Projection.WorldToScreen(frame.Matrix, position.X, position.Y, position.Z, ClientSize, out point);
        }
        private void Bones(Graphics g, Player player, Color color, float height)
        {
            if (player.Bones == null) return;
            PointF[] points = new PointF[player.Bones.Length]; bool[] valid = new bool[points.Length];
            for (int i = 0; i < points.Length; i++) valid[i] = Screen(player.Bones[i], out points[i]);
            if (settings.Silhouette)
            {
                float width = Math.Max(3, Math.Min(80, height * 0.075f));
                using (Pen body = new Pen(color, width))
                using (Brush brush = new SolidBrush(color))
                {
                    body.StartCap = body.EndCap = LineCap.Round;
                    for (int i = 1; i < BoneData.Links.GetLength(0); i++)
                    {
                        int a = BoneData.Links[i, 0], b = BoneData.Links[i, 1];
                        if (valid[a] && valid[b]) g.DrawLine(body, points[a], points[b]);
                    }
                    if (valid[8] && valid[13] && valid[22] && valid[25])
                        g.FillPolygon(brush, new PointF[] { points[8], points[13], points[25], points[22] });
                    if (valid[6]) { float radius = width * 0.75f; g.FillEllipse(brush, points[6].X - radius, points[6].Y - radius, radius * 2, radius * 2); }
                }
            }
            if (settings.Skeleton)
            {
                using (Pen background = new Pen(Color.Black, settings.BoxThickness + 2))
                using (Pen bone = new Pen(color, settings.BoxThickness))
                {
                    for (int i = 0; i < BoneData.Links.GetLength(0); i++)
                    {
                        int a = BoneData.Links[i, 0], b = BoneData.Links[i, 1];
                        if (!valid[a] || !valid[b]) continue;
                        if (settings.Outline) g.DrawLine(background, points[a], points[b]);
                        g.DrawLine(bone, points[a], points[b]);
                    }
                }
            }
        }
        private void Person(Graphics g, Player player)
        {
            if ((player.Teammate && !settings.Teammates) || player.Distance > settings.EspDistance) return;
            PointF feet, head;
            if (!Screen(player.Position, out feet) || !Screen(new Vec3(player.Position.X, player.Position.Y, player.Position.Z + player.Height), out head)) return;
            float height = feet.Y - head.Y;
            if (height < 3 || height > ClientSize.Height * 3) return;
            float width = height * 0.45f, left = (head.X + feet.X) / 2 - width / 2;
            RectangleF rectangle = new RectangleF(left, head.Y, width, height);
            if (rectangle.Right < 0 || rectangle.Left > ClientSize.Width || rectangle.Bottom < 0 || rectangle.Top > ClientSize.Height) return;
            Color color = AppSettings.Opaque(player.Teammate ? settings.TeamColor : settings.EnemyColor);
            if (settings.Boxes)
            {
                if (settings.FilledBoxes)
                {
                    float shade = settings.FillOpacity / 100f;
                    using (Brush fill = new SolidBrush(Color.FromArgb((int)(color.R * shade), (int)(color.G * shade), (int)(color.B * shade)))) g.FillRectangle(fill, rectangle);
                }
                using (Pen background = new Pen(Color.Black, settings.BoxThickness + 2))
                using (Pen outline = new Pen(color, settings.BoxThickness))
                {
                    if (settings.Outline) Box(g, background, rectangle, settings.BoxStyle);
                    Box(g, outline, rectangle, settings.BoxStyle);
                }
            }
            Bones(g, player, color, height);
            if (settings.HealthBar)
            {
                g.FillRectangle(Brushes.Black, left - 7, head.Y, 4, height);
                float amount = height * Math.Min(player.Health, 100) / 100f;
                g.FillRectangle(player.Health > 35 ? Brushes.LimeGreen : Brushes.OrangeRed, left - 7, feet.Y - amount, 4, amount);
            }
            if (settings.Snaplines)
            {
                float y = settings.SnaplineAnchor == 0 ? ClientSize.Height : (settings.SnaplineAnchor == 1 ? ClientSize.Height / 2 : 0);
                using (Pen line = new Pen(color, 1)) g.DrawLine(line, ClientSize.Width / 2, y, feet.X, feet.Y);
            }
            if (settings.Names && player.Name.Length > 0) TextAt(g, player.Name, left, head.Y - labelFont.Height - 2, Color.White);
            string text = settings.HealthText ? player.Health + " HP" : "";
            if (settings.Distances) text += (text.Length > 0 ? "  " : "") + Math.Round(player.Distance) + " m";
            if (text.Length > 0) TextAt(g, text, left, feet.Y + 2, Color.White);
            if (settings.HostageCarrier && player.Carrier) TextAt(g, "HOSTAGE CARRIER", left, feet.Y + labelFont.Height + 3, Color.Orange);
        }
        private void Radar(Graphics g)
        {
            if (!frame.PositionValid) return;
            int size = settings.RadarSize;
            RectangleF area = new RectangleF(ClientSize.Width - size - 18, 24, size, size);
            g.FillRectangle(Brushes.Black, area);
            using (Pen border = new Pen(Color.FromArgb(70, 210, 120), 1))
            {
                g.DrawRectangle(border, area.X, area.Y, area.Width, area.Height);
                g.DrawLine(border, area.X, area.Y + size / 2, area.Right, area.Y + size / 2);
                g.DrawLine(border, area.X + size / 2, area.Top, area.X + size / 2, area.Bottom);
            }
            g.DrawString("RADAR  " + settings.RadarRange + "m", SystemFonts.MessageBoxFont, Brushes.White, area.X + 5, area.Y + 3);
            foreach (Player player in frame.Players)
            {
                if ((player.Teammate && !settings.Teammates) || player.Distance > settings.EspDistance) continue;
                PointF point = RadarProjection.Point(player.Position, frame.Position, frame.Yaw, settings.RadarRotate, settings.RadarRange, area);
                Color color = settings.HostageCarrier && player.Carrier ? Color.Orange : AppSettings.Opaque(player.Teammate ? settings.TeamColor : settings.EnemyColor);
                using (Brush brush = new SolidBrush(color)) g.FillEllipse(brush, point.X - 3, point.Y - 3, 6, 6);
            }
            if (settings.BombInfo && frame.Bomb != null && frame.Bomb.PositionValid && frame.Bomb.Phase == BombPhase.Planted)
            {
                PointF bomb = RadarProjection.Point(frame.Bomb.Position, frame.Position, frame.Yaw, settings.RadarRotate, settings.RadarRange, area);
                g.FillRectangle(Brushes.Orange, bomb.X - 4, bomb.Y - 4, 8, 8);
                g.DrawString("C4", SystemFonts.MessageBoxFont, Brushes.Orange, bomb.X + 4, bomb.Y - 7);
            }
            g.FillEllipse(Brushes.White, area.X + size / 2 - 3, area.Y + size / 2 - 3, 6, 6);
        }
        private static string Seconds(float value) { return value.ToString("0.0", CultureInfo.InvariantCulture) + "s"; }
        private void Bomb(Graphics g)
        {
            if (frame.Bomb == null && !frame.BombInfoUnavailable) return;
            BombSnapshot bomb = frame.Bomb;
            float width = Math.Min(400, ClientSize.Width - 32), left = (ClientSize.Width - width) / 2f;
            float height = bomb != null && bomb.Phase == BombPhase.Planted ? (bomb.BeingDefused ? 132 : 108) : 52;
            RectangleF panel = new RectangleF(left, 92, width, height);
            Color color = bomb == null ? Color.Orange : (bomb.Phase == BombPhase.Defused ? Color.LimeGreen : (bomb.Phase == BombPhase.Exploded ? Color.OrangeRed : Color.Orange));
            g.FillRectangle(Brushes.Black, panel);
            using (Pen border = new Pen(color, 1)) g.DrawRectangle(border, panel.X, panel.Y, panel.Width, panel.Height);
            if (bomb == null)
            {
                g.DrawString("Bomb data unavailable for this build", caption, Brushes.Orange, left + 12, panel.Top + 14); return;
            }
            string phase = bomb.Phase == BombPhase.Defused ? "DEFUSED" : (bomb.Phase == BombPhase.Exploded ? "EXPLODED" : "PLANTED");
            using (Brush brush = new SolidBrush(color)) g.DrawString("C4 " + phase + "  /  SITE " + bomb.Site, caption, brush, left + 12, panel.Top + 10);
            if (bomb.Phase == BombPhase.Planted)
            {
                g.DrawString(bomb.TimerKnown ? "Explodes in: " + Seconds(bomb.SecondsLeft) : "Explosion timer unavailable", caption, Brushes.White, left + 12, panel.Top + 34);
                string start = bomb.CannotDefuse ? "This bomb cannot be defused" : "Start by: kit " + bomb.StartLabel(true) + "  |  no kit " + bomb.StartLabel(false);
                Brush startColor = bomb.TimerKnown && bomb.LatestStart(true) <= 0 ? Brushes.OrangeRed : Brushes.White;
                g.DrawString(start, SystemFonts.MessageBoxFont, startColor, left + 12, panel.Top + 60);
                if (bomb.BeingDefused)
                {
                    string defuse = bomb.DefuseTimerKnown ? "Defusing: " + Seconds(bomb.DefuseSeconds) + " left  /  " + (bomb.DefuseInTime ? "on time" : "too late") : "Defusing: timer unavailable";
                    g.DrawString(defuse, caption, bomb.DefuseTimerKnown && bomb.DefuseInTime ? Brushes.LimeGreen : Brushes.OrangeRed, left + 12, panel.Top + 82);
                }
                if (bomb.TimerKnown)
                {
                    float fraction = Math.Max(0, Math.Min(1, bomb.SecondsLeft / bomb.TimerLength));
                    using (Brush progress = new SolidBrush(color)) g.FillRectangle(progress, left + 12, panel.Bottom - 10, (width - 24) * fraction, 4);
                }
            }
            PointF position;
            if (bomb.PositionValid && Screen(bomb.Position, out position) && position.X >= 0 && position.X <= ClientSize.Width && position.Y >= 0 && position.Y <= ClientSize.Height)
            {
                using (Pen marker = new Pen(color, 2))
                    g.DrawPolygon(marker, new PointF[] { new PointF(position.X, position.Y - 9), new PointF(position.X + 9, position.Y), new PointF(position.X, position.Y + 9), new PointF(position.X - 9, position.Y) });
                string label = "C4 / " + bomb.Site + " / " + (bomb.Phase == BombPhase.Planted ? (bomb.TimerKnown ? Seconds(bomb.SecondsLeft) : "timer unavailable") : phase);
                TextAt(g, label, Math.Max(8, Math.Min(ClientSize.Width - 240, position.X + 13)), Math.Max(8, Math.Min(ClientSize.Height - labelFont.Height - 8, position.Y - 12)), color);
            }
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args); if (frame == null || settings == null) return;
            Graphics g = args.Graphics; g.SmoothingMode = settings.Antialias ? SmoothingMode.AntiAlias : SmoothingMode.None;
            if (settings.EspEnabled) foreach (Player player in frame.Players) Person(g, player);
            if (settings.EspEnabled && settings.Radar) Radar(g);
            if (settings.DrawFov)
            {
                int radius = OverlayOptions.CircleRadius(settings);
                using (Pen circle = new Pen(AppSettings.Opaque(settings.FovColor), 1)) g.DrawEllipse(circle, ClientSize.Width / 2 - radius, ClientSize.Height / 2 - radius, radius * 2, radius * 2);
            }
            if (settings.TargetIndicator && aim != null && (settings.AimEnabled || settings.AssistEnabled || settings.SpinEnabled))
            {
                using (Pen target = new Pen(Color.Yellow, 2)) g.DrawEllipse(target, aim.Screen.X - 5, aim.Screen.Y - 5, 10, 10);
            }
            if (settings.Crosshair)
            {
                int x = ClientSize.Width / 2, y = ClientSize.Height / 2, gap = settings.CrosshairGap, length = settings.CrosshairSize;
                using (Pen cross = new Pen(AppSettings.Opaque(settings.CrosshairColor), 2))
                {
                    g.DrawLine(cross, x - gap - length, y, x - gap, y); g.DrawLine(cross, x + gap, y, x + gap + length, y);
                    g.DrawLine(cross, x, y - gap - length, x, y - gap); g.DrawLine(cross, x, y + gap, x, y + gap + length);
                }
            }
            if (settings.Hud)
            {
                g.FillRectangle(Brushes.Black, 12, 12, Math.Min(690, ClientSize.Width - 24), 64);
                g.DrawString("ModafenClientXZERO  |  " + KeyOptions.Label(settings.MenuBind) + " menu  |  " + cadence, caption, Brushes.LimeGreen, 20, 17);
                g.DrawString(frame.Status, SystemFonts.MessageBoxFont, Brushes.White, 20, 39);
                g.DrawString("ESP " + (settings.EspEnabled ? "ON" : "OFF") + "   AIM " + (settings.AimEnabled ? "ON" : "OFF") + "   ASSIST " + (settings.AssistEnabled ? "ON" : "OFF") + "   HOP " + (settings.BhopEnabled ? "ON" : "OFF") + "   SPIN " + (settings.SpinEnabled ? "ON" : "OFF"), SystemFonts.MessageBoxFont, Brushes.White, 20, 56);
            }
            if (settings.BombInfo) Bomb(g);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { caption.Dispose(); if (labelFont != null) labelFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

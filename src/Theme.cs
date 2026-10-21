using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Cs2Overlay
{
    internal static class ClientTheme
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        internal static void SoftenWindow(IntPtr window)
        {
            try
            {
                int rounded = 2, dark = 1;
                DwmSetWindowAttribute(window, 33, ref rounded, 4);
                DwmSetWindowAttribute(window, 20, ref dark, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        internal static GraphicsPath Shape(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter < 2) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
        }
        internal static void Round(Control control, int radius)
        {
            if (control.Width < 2 || control.Height < 2) return;
            Region old = control.Region;
            using (GraphicsPath path = Shape(new Rectangle(0, 0, control.Width, control.Height), radius)) control.Region = new Region(path);
            if (old != null) old.Dispose();
        }
    }
    internal sealed class RoundedCard : FlowLayoutPanel
    {
        internal RoundedCard() { DoubleBuffered = true; }
        protected override void OnSizeChanged(EventArgs args) { base.OnSizeChanged(args); ClientTheme.Round(this, 14); }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (Width < 3 || Height < 3) return;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = ClientTheme.Shape(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (Pen border = new Pen(Color.FromArgb(36, 70, 47), 1)) args.Graphics.DrawPath(border, path);
        }
    }
    internal sealed class RoundedButton : Button
    {
        internal RoundedButton()
        {
            DoubleBuffered = true; FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 100, 59);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(44, 129, 72);
        }
        protected override void OnSizeChanged(EventArgs args) { base.OnSizeChanged(args); ClientTheme.Round(this, 8); }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (Width < 3 || Height < 3) return;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = ClientTheme.Shape(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            using (Pen border = new Pen(Focused ? Color.FromArgb(100, 245, 160) : Color.FromArgb(48, 105, 67), 1)) args.Graphics.DrawPath(border, path);
        }
    }
    internal class RoundedWindow : Form
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        internal RoundedWindow()
        {
            FormBorderStyle = FormBorderStyle.None; Padding = new Padding(2); DoubleBuffered = true;
        }
        protected void DragWindow(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }
        protected override void OnSizeChanged(EventArgs args)
        {
            base.OnSizeChanged(args);
            if (WindowState == FormWindowState.Maximized)
            {
                Region old = Region; Region = null; if (old != null) old.Dispose();
            }
            else ClientTheme.Round(this, 16);
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (Width < 3 || Height < 3 || WindowState == FormWindowState.Maximized) return;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = ClientTheme.Shape(new Rectangle(0, 0, Width - 1, Height - 1), 16))
            using (Pen border = new Pen(Color.FromArgb(52, 119, 76), 1)) args.Graphics.DrawPath(border, path);
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != 0x84 || WindowState != FormWindowState.Normal || message.Result.ToInt32() != 1) return;
            long coordinates = message.LParam.ToInt64();
            Point point = PointToClient(new Point((short)(coordinates & 0xFFFF), (short)((coordinates >> 16) & 0xFFFF)));
            const int grip = 7;
            bool left = point.X < grip, right = point.X >= ClientSize.Width - grip, top = point.Y < grip, bottom = point.Y >= ClientSize.Height - grip;
            int hit = top ? (left ? 13 : (right ? 14 : 12)) : (bottom ? (left ? 16 : (right ? 17 : 15)) : (left ? 10 : (right ? 11 : 1)));
            message.Result = new IntPtr(hit);
        }
    }
}

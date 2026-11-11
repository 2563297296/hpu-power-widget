using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    public enum IconKind { Refresh, Settings }

    public sealed class IconButton : Control
    {
        private bool hover;
        private bool pressed;
        private bool busy;
        private const double TurnDuration = 900;
        private readonly Stopwatch spinClock = new Stopwatch();
        private Timer spinTimer;
        private double finishAt;
        private float angle;
        public Color ButtonFill { get; set; }

        public IconKind Kind { get; set; }
        public bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value) return;
                busy = value;
                if (busy) StartSpin();
                else if (spinClock.IsRunning)
                    finishAt = Math.Max(1, Math.Ceiling(spinClock.Elapsed.TotalMilliseconds / TurnDuration));
                Invalidate();
            }
        }

        public IconButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(30, 62, 98);
            ForeColor = Color.FromArgb(225, 236, 250);
            ButtonFill = Color.FromArgb(25, 54, 86);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnClick(EventArgs e)
        {
            StartSpin();
            base.OnClick(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) StopSpin();
            else if (busy) StartSpin();
        }

        private void StartSpin()
        {
            if (Kind != IconKind.Refresh || !Visible || spinClock.IsRunning || !SystemInformation.IsMenuAnimationEnabled)
                return;

            if (spinTimer == null)
            {
                spinTimer = new Timer { Interval = 30 };
                spinTimer.Tick += delegate { AnimateSpin(); };
            }
            finishAt = 1;
            spinClock.Restart();
            spinTimer.Start();
        }

        private void AnimateSpin()
        {
            double turns = spinClock.Elapsed.TotalMilliseconds / TurnDuration;
            if (!busy && turns >= finishAt)
            {
                StopSpin();
                return;
            }

            double progress = turns % 1;
            angle = (float)(360 * progress * progress * (3 - 2 * progress));
            Invalidate();
        }

        private void StopSpin()
        {
            if (spinTimer == null) return;
            spinTimer.Stop();
            spinClock.Reset();
            angle = 0;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && spinTimer != null)
            {
                spinTimer.Dispose();
                spinTimer = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = ButtonFill;
            if (busy || pressed || hover) background = ControlPaint.Light(ButtonFill, pressed ? .24f : .14f);
            if (hover && SystemInformation.IsMenuAnimationEnabled)
            {
                e.Graphics.TranslateTransform(Width / 2f, Height / 2f);
                e.Graphics.ScaleTransform(1.055f, 1.055f);
                e.Graphics.TranslateTransform(-Width / 2f, -Height / 2f);
            }
            using (GraphicsPath path = Rounded(new Rectangle(1, 1, Width - 2, Height - 2), 9))
            using (SolidBrush brush = new SolidBrush(background))
                e.Graphics.FillPath(brush, path);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(ForeColor, 2.2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (Kind == IconKind.Refresh)
                {
                    e.Graphics.TranslateTransform(Width / 2f, Height / 2f);
                    e.Graphics.RotateTransform(angle);
                    e.Graphics.TranslateTransform(-Width / 2f, -Height / 2f);
                    DrawRefresh(e.Graphics, pen);
                }
                else DrawSettings(e.Graphics, pen);
            }
        }

        private void DrawRefresh(Graphics g, Pen pen)
        {
            float cx = Width / 2f;
            float cy = Height / 2f;
            float r = Math.Min(Width, Height) * 0.25f;
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 35, 125);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 215, 125);
            DrawArrow(g, pen, cx + r * 0.70f, cy - r * 0.72f, 1);
            DrawArrow(g, pen, cx - r * 0.70f, cy + r * 0.72f, -1);
        }

        private void DrawArrow(Graphics g, Pen pen, float x, float y, int direction)
        {
            pen.Width = 2.1f;
            g.DrawLine(pen, x - 3.5f, y - 2.5f, x, y + 2.5f);
            g.DrawLine(pen, x, y + 2.5f, x + 4.0f * direction, y + 1.0f);
            pen.Width = 2.2f;
        }

        private void DrawSettings(Graphics g, Pen pen)
        {
            float left = Width * 0.27f;
            float right = Width * 0.73f;
            float[] ys = { Height * 0.32f, Height * 0.50f, Height * 0.68f };
            float[] knobs = { Width * 0.62f, Width * 0.38f, Width * 0.58f };
            for (int i = 0; i < ys.Length; i++)
            {
                g.DrawLine(pen, left, ys[i], right, ys[i]);
                using (SolidBrush brush = new SolidBrush(ForeColor))
                    g.FillEllipse(brush, knobs[i] - 3.2f, ys[i] - 3.2f, 6.4f, 6.4f);
            }
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

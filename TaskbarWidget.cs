using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    internal sealed class TaskbarWidget : Form
    {
        public static readonly string[] AppearanceNames = { "磨砂浅色", "深色胶囊", "控制中心" };
        private const int ControlCenterAppearance = 2;
        private const int LogicalWidth = 258;
        private const int LogicalHeight = 29;
        private const double PressDuration = 185;
        private readonly Timer positionTimer = new Timer { Interval = 750 };
        private readonly Timer stackingTimer = new Timer { Interval = 50 };
        private readonly Timer pressTimer = new Timer { Interval = 16 };
        private readonly Stopwatch pressClock = new Stopwatch();
        private readonly ToolTip details = new ToolTip();
        private readonly Font caption = new Font("Microsoft YaHei UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font value = new Font("Microsoft YaHei UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font headline = new Font("Microsoft YaHei UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly float scale;
        private Rectangle anchorBounds;
        private IntPtr shellTaskbar;
        private Color taskbarBackground;
        private bool alignRight;
        private int appearance;
        private int severity;
        private float pressProgress;
        private string days, lighting, air;
        private string status = "等待用电数据";
        public event EventHandler SettingsRequested;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr dc, int x, int y);

        public TaskbarWidget(float scale)
        {
            this.scale = scale;
            Text = "HPU 用电 · 任务栏";
            AccessibleName = Text;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            ClientSize = new Size(Scaled(LogicalWidth), Scaled(LogicalHeight));
            positionTimer.Tick += delegate { FollowTaskbar(); };
            stackingTimer.Tick += delegate { RestoreAboveTaskbar(); };
            pressTimer.Tick += delegate { AnimatePress(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return parameters;
            }
        }

        public void StartFollowing(bool right, int style)
        {
            alignRight = right;
            SetAppearance(style);
            FollowTaskbar();
            positionTimer.Start();
            stackingTimer.Start();
            Invalidate();
        }

        internal static int NormalizeAppearance(int style)
        {
            return Math.Max(0, Math.Min(AppearanceNames.Length - 1, style));
        }

        public void SetAppearance(int style)
        {
            int next = NormalizeAppearance(style);
            if (appearance == next) return;
            appearance = next;
            if (appearance == ControlCenterAppearance) RefreshPlacement();
            Invalidate();
        }

        public void StopFollowing()
        {
            stackingTimer.Stop();
            positionTimer.Stop();
            shellTaskbar = IntPtr.Zero;
            ResetPress();
            Hide();
        }

        public void RefreshPlacement()
        {
            if (positionTimer.Enabled && !IsDisposed) FollowTaskbar();
        }

        private void FollowTaskbar()
        {
            shellTaskbar = FindWindow("Shell_TrayWnd", null);
            NativeRect rect;
            anchorBounds = Rectangle.Empty;
            if (shellTaskbar != IntPtr.Zero && IsWindowVisible(shellTaskbar) && GetWindowRect(shellTaskbar, out rect))
            {
                Rectangle taskbar = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                anchorBounds = Place(taskbar, Screen.FromRectangle(taskbar).Bounds, scale, alignRight);
                if (appearance == ControlCenterAppearance && !anchorBounds.IsEmpty)
                    UpdateTaskbarBackground(taskbar);
            }
            if (anchorBounds.IsEmpty)
            {
                ResetPress();
                if (Visible)
                {
                    DiagnosticLog.Write("taskbar.hidden; no usable taskbar bounds");
                    Hide();
                }
                return;
            }
            if (Bounds != anchorBounds) Bounds = anchorBounds;
            if (!Visible)
            {
                DiagnosticLog.Write("taskbar.show; bounds=" + anchorBounds);
                // Set the final native bounds before Explorer can paint the first frame.
                IntPtr window = Handle;
                if (Bounds != anchorBounds) Bounds = anchorBounds;
                Show();
            }

            RestoreAboveTaskbar();
        }

        private void UpdateTaskbarBackground(Rectangle taskbar)
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return;

            var samples = new List<Color>(6);
            try
            {
                int y = anchorBounds.Top + anchorBounds.Height / 2;
                for (int step = 1; step <= 3; step++)
                {
                    int gap = Scaled(step * 4);
                    AddTaskbarSample(dc, taskbar, new Point(anchorBounds.Left - gap, y), samples);
                    AddTaskbarSample(dc, taskbar, new Point(anchorBounds.Right + gap, y), samples);
                }
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }

            if (samples.Count == 0) return;
            // The median rejects isolated highlights from nearby taskbar buttons.
            samples.Sort((a, b) => Luminance(a).CompareTo(Luminance(b)));
            Color background = samples[samples.Count / 2];
            if (taskbarBackground == background) return;
            taskbarBackground = background;
            Invalidate();
        }

        private void AddTaskbarSample(IntPtr dc, Rectangle taskbar, Point point, List<Color> samples)
        {
            if (!taskbar.Contains(point)) return;
            IntPtr window = WindowFromPoint(point);
            if (window == IntPtr.Zero || GetAncestor(window, 2) != shellTaskbar) return;
            uint pixel = GetPixel(dc, point.X, point.Y);
            if (pixel != UInt32.MaxValue) samples.Add(ColorTranslator.FromWin32(unchecked((int)pixel)));
        }

        private static double Luminance(Color color)
        {
            return 0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);
        }

        private static double LinearChannel(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private void RestoreAboveTaskbar()
        {
            if (shellTaskbar == IntPtr.Zero || anchorBounds.IsEmpty || !Visible || !IsHandleCreated)
                return;

            Point center = new Point(anchorBounds.Left + anchorBounds.Width / 2,
                anchorBounds.Top + anchorBounds.Height / 2);
            IntPtr front = WindowFromPoint(center);
            if (front == IntPtr.Zero || front == Handle || GetAncestor(front, 2) != shellTaskbar)
                return;

            // Show Desktop can move Explorer's taskbar above the pill without changing either window's bounds.
            const uint noMove = 0x0002, noSize = 0x0001, noActivate = 0x0010;
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, noMove | noSize | noActivate);
        }

        internal static Rectangle Place(Rectangle taskbar, Rectangle screen, float scale, bool right)
        {
            Rectangle area = Rectangle.Intersect(taskbar, screen);
            int height = (int)Math.Round(LogicalHeight * scale);
            int width = (int)Math.Round(LogicalWidth * scale);
            int leftInset = (int)Math.Round(55 * scale);
            int rightInset = (int)Math.Round(190 * scale);
            if (taskbar.Width <= taskbar.Height || area.Height < height ||
                area.Width < width + leftInset + rightInset) return Rectangle.Empty;
            int x = right ? area.Right - rightInset - width : area.Left + leftInset;
            return new Rectangle(x, area.Top + (area.Height - height) / 2, width, height);
        }

        public void UpdateReading(string days, string lighting, string air, string status, string tooltip, int severity)
        {
            if (this.days != days || this.lighting != lighting || this.air != air ||
                this.status != status || this.severity != severity)
            {
                this.days = days;
                this.lighting = lighting;
                this.air = air;
                this.status = status;
                this.severity = severity;
                AccessibleDescription = days == null ? status : days + " · 照明 " + lighting + " · 空调 " + air;
                Invalidate();
            }
            if (details.GetToolTip(this) != tooltip) details.SetToolTip(this, tooltip);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || pressTimer.Enabled) return;
            DiagnosticLog.Write("taskbar.click.left; bounds=" + Bounds);
            if (anchorBounds.IsEmpty || !SystemInformation.IsMenuAnimationEnabled)
            {
                RequestSettings();
                return;
            }
            DiagnosticLog.Write("taskbar.animation.begin");
            pressClock.Restart();
            pressTimer.Start();
        }

        private int Scaled(int logical) { return (int)Math.Round(logical * scale); }

        private void ResetPress()
        {
            pressTimer.Stop();
            pressClock.Reset();
            pressProgress = 0;
            Invalidate();
        }

        private void AnimatePress()
        {
            double progress = Math.Min(1, pressClock.Elapsed.TotalMilliseconds / PressDuration);
            if (progress < 1)
            {
                pressProgress = (float)Math.Sin(Math.PI * progress);
                Invalidate();
                return;
            }
            ResetPress();
            DiagnosticLog.Write("taskbar.animation.end");
            RequestSettings();
        }

        private RectangleF ContentViewport()
        {
            float factor = Math.Min(ClientSize.Width / (float)LogicalWidth,
                ClientSize.Height / (float)LogicalHeight);
            float width = LogicalWidth * factor;
            float height = LogicalHeight * factor;
            return new RectangleF((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        }

        private void RequestSettings()
        {
            DiagnosticLog.Write("taskbar.settings.request");
            EventHandler callback = SettingsRequested;
            if (callback != null) callback(this, EventArgs.Empty);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            using (GraphicsPath shape = Capsule(ContentViewport()))
            {
                Region previous = Region;
                Region = new Region(shape);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.PageUnit = GraphicsUnit.Pixel;
            RectangleF viewport = ContentViewport();
            float factor = viewport.Width / LogicalWidth;
            g.TranslateTransform(viewport.Left, viewport.Top);
            g.ScaleTransform(factor, factor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            bool dark = appearance == 1;
            bool segmented = appearance == ControlCenterAppearance;
            Color top = dark ? Color.FromArgb(37, 39, 46) : Color.FromArgb(251, 253, 255);
            Color bottom = dark ? Color.FromArgb(16, 18, 22) : Color.FromArgb(224, 231, 243);
            Color border = dark ? Color.FromArgb(74, 78, 87) : Color.FromArgb(255, 255, 255);
            Color ink = dark ? Color.FromArgb(247, 249, 253) : Color.FromArgb(31, 43, 61);
            Color muted = dark ? Color.FromArgb(183, 191, 203) : Color.FromArgb(75, 87, 107);
            Color accent = dark ? Color.FromArgb(82, 233, 168) : segmented
                ? Color.FromArgb(22, 91, 203) : Color.FromArgb(76, 63, 211);
            if (severity == 2) accent = dark ? Color.LightCoral : Color.FromArgb(174, 39, 54);
            else if (severity == 1) accent = dark ? Color.Khaki : Color.FromArgb(127, 77, 0);

            if (segmented && !taskbarBackground.IsEmpty)
            {
                top = bottom = taskbarBackground;
                ink = muted = Luminance(taskbarBackground) >= 0.179 ? Color.Black : Color.White;
            }

            var surface = new RectangleF(0, 0, LogicalWidth, LogicalHeight);
            using (GraphicsPath outline = Capsule(new RectangleF(.5f, .5f, LogicalWidth - 1, LogicalHeight - 1)))
            using (LinearGradientBrush fill = new LinearGradientBrush(surface, top, bottom, 90f))
            using (Pen rim = new Pen(border))
            {
                g.FillRectangle(fill, surface);
                g.DrawPath(rim, outline);
            }

            ApplyPressFeedback(g);
            if (days == null)
            {
                DrawText(g, status, value, ink, surface, StringAlignment.Center);
                return;
            }

            if (segmented)
            {
                using (GraphicsPath badge = Capsule(new RectangleF(4, 4, 67, 21)))
                using (SolidBrush tint = new SolidBrush(Color.FromArgb(211, 229, 255)))
                    g.FillPath(tint, badge);
                using (Pen separator = new Pen(Color.FromArgb(169, 182, 201)))
                {
                    g.DrawLine(separator, 76, 8, 76, 21);
                    g.DrawLine(separator, 161, 8, 161, 21);
                }
                using (SolidBrush bolt = new SolidBrush(accent))
                    g.FillPolygon(bolt, new[] { new PointF(17, 7), new PointF(10, 16), new PointF(15, 16),
                        new PointF(14, 22), new PointF(22, 12), new PointF(17, 12) });
            }

            DrawText(g, days, headline, accent, new RectangleF(segmented ? 23 : 7, 0, segmented ? 45 : 63, LogicalHeight),
                StringAlignment.Center);
            DrawBalance(g, "照明", lighting, 83, ink, muted);
            DrawBalance(g, "空调", air, 168, ink, muted);
        }

        private void ApplyPressFeedback(Graphics g)
        {
            if (pressProgress <= 0) return;
            float factor = 1 - .04f * pressProgress;
            g.TranslateTransform(LogicalWidth / 2f, LogicalHeight / 2f);
            g.ScaleTransform(factor, factor);
            g.TranslateTransform(-LogicalWidth / 2f, -LogicalHeight / 2f);
        }

        private void DrawBalance(Graphics g, string label, string amount, int x, Color ink, Color muted)
        {
            DrawText(g, label, caption, muted, new RectangleF(x, 0, 26, LogicalHeight), StringAlignment.Near);
            DrawText(g, amount, value, ink, new RectangleF(x + 26, 0, 57, LogicalHeight), StringAlignment.Near);
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF area, StringAlignment alignment)
        {
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (SolidBrush brush = new SolidBrush(color))
            {
                format.Alignment = alignment;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, font, brush, area, format);
            }
        }

        private static GraphicsPath Capsule(RectangleF bounds)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(bounds.Width, bounds.Height);
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 90, 180);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 180);
            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DiagnosticLog.Write("taskbar.closing; reason=" + e.CloseReason);
            base.OnFormClosing(e);
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                StopFollowing();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stackingTimer.Dispose();
                positionTimer.Dispose();
                pressTimer.Dispose();
                details.Dispose();
                caption.Dispose();
                value.Dispose();
                headline.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

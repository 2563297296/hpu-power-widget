using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    public sealed partial class WidgetForm
    {
        internal static readonly string[] AppearanceNames = { "暖白墨绿", "深蓝薄荷", "白底钴蓝", "暖白动态", "iOS 控制中心" };

        private sealed class Appearance
        {
            public readonly Color Surface, Ink, Muted, Accent, Rule, Tile;
            public Appearance(int surface, int ink, int muted, int accent, int rule, int tile)
            {
                Surface = Rgb(surface); Ink = Rgb(ink); Muted = Rgb(muted);
                Accent = Rgb(accent); Rule = Rgb(rule); Tile = Rgb(tile);
            }
            private static Color Rgb(int rgb) { return Color.FromArgb(rgb | unchecked((int)0xff000000)); }
        }

        private const int IosAppearance = 4;

        private static readonly Appearance[] Appearances =
        {
            new Appearance(0xfaf7ee, 0x242820, 0x596258, 0x22543b, 0xcbd5c7, 0xfaf7ee),
            new Appearance(0x111a2b, 0xf1f5fc, 0xb0bfd3, 0xa2f0bf, 0x324259, 0x1d2a40),
            new Appearance(0xf8fbff, 0x172b45, 0x51647b, 0x154fc8, 0xcdd8e7, 0xf8fbff),
            new Appearance(0xfbfaf5, 0x222d26, 0x58645b, 0x245440, 0xdce3d7, 0xedf2e9),
            new Appearance(0xf1f5fc, 0x1d2c44, 0x526582, 0x175ed1, 0xdce5f3, 0xffffff)
        };

        private const double LowBalance = 10;
        private const double CriticalBalance = 3;
        private readonly Stopwatch animationClock = Stopwatch.StartNew();
        private readonly Timer appearanceTimer = new Timer { Interval = 33 };
        private long refreshStarted, transitionStarted;
        private bool hovered;
        private UsageSnapshot transitionFrom;

        private int AppearanceIndex { get { return Math.Max(0, Math.Min(Appearances.Length - 1, config.appearance)); } }
        private bool Animated { get { return AppearanceIndex == 3 && SystemInformation.IsMenuAnimationEnabled; } }

        private void ApplyAppearance()
        {
            Appearance look = Appearances[AppearanceIndex];
            BackColor = look.Surface;
            refreshIcon.BackColor = settingsIcon.BackColor = look.Surface;
            refreshIcon.ForeColor = settingsIcon.ForeColor = look.Ink;
            refreshIcon.ButtonFill = settingsIcon.ButtonFill = look.Tile;
            UpdateAppearanceTimer();
            Invalidate(true);
        }

        private void UpdateAppearanceTimer()
        {
            appearanceTimer.Enabled = Animated && Visible && !settingsOpen && !exiting;
        }

        private void AnimateAppearance()
        {
            if (!Visible || !Animated) return;
            if (refreshing || transitionFrom != null || (snapshot != null &&
                (BalanceLevel(snapshot.lightingBalance) > 0 || BalanceLevel(snapshot.airBalance) > 0)))
                Invalidate();
            if (TransitionProgress() >= 1) transitionFrom = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.PageUnit = GraphicsUnit.Pixel;
            g.ScaleTransform(widgetScale, widgetScale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int style = AppearanceIndex;
            Appearance look = Appearances[style];
            bool horizontal = style >= 2;
            int cardWidth = CardWidth;
            int cardHeight = CardHeight;
            int contentWidth = cardWidth - 36;
            int middle = cardWidth / 2;
            int split = (int)Math.Round(cardWidth * 171.0 / 410);
            int bodyOffset = (cardHeight - WidgetConfig.DefaultCardHeight) / 2;
            string title = room == null ? "未选择房间" : room.CampusLabel + " · " + room.name;
            string status = UpdateStatus();
            using (Font heading = MakeFont(style == 0 ? "SimSun" : "Microsoft YaHei UI", 10.5f, FontStyle.Bold))
            using (Font label = MakeFont("Microsoft YaHei UI", 9f, FontStyle.Regular))
            using (Font footer = MakeFont("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Pen rule = new Pen(look.Rule))
            using (GraphicsPath outline = RoundedPath(new Rectangle(0, 0, cardWidth - 1, cardHeight - 1), 18))
            {
                rule.Color = style == 3 && hovered ? Blend(look.Rule, look.Accent, .33) : look.Rule;
                g.DrawPath(rule, outline);
                rule.Color = look.Rule;
                DrawText(g, title, heading, look.Ink, new RectangleF(18, 11, cardWidth - 135, 32), StringAlignment.Near);
                g.DrawLine(rule, 18, 51, cardWidth - 18, 51);
                double progress = TransitionProgress();
                double dayValue = snapshot == null ? Double.NaN : snapshot.daysLeft;
                double oldDays = transitionFrom == null ? dayValue : transitionFrom.daysLeft;
                string daysText = dayValue >= 0 && !Double.IsInfinity(dayValue)
                    ? FormatDays(Interpolate(oldDays, dayValue, progress)) + " 天" : "—";
                Color dayColor = dayValue >= 0 && dayValue < 7
                    ? WarningColor(style, dayValue < 3 ? 2 : 1, look.Accent) : look.Accent;
                Color dayInk = LoadingInk(dayColor, look.Surface);
                RectangleF caption = horizontal ? new RectangleF(18, 67 + bodyOffset, split - 21, 23)
                    : new RectangleF(18, 56 + bodyOffset, contentWidth, 22);
                RectangleF figure = horizontal ? new RectangleF(16, 87 + bodyOffset, split - 18, 57)
                    : new RectangleF(18, 75 + bodyOffset, contentWidth, 52);
                StringAlignment align = horizontal ? StringAlignment.Near : StringAlignment.Center;
                if (style == IosAppearance)
                {
                    using (GraphicsPath hero = RoundedPath(new Rectangle(18, 64 + bodyOffset, split - 18, 89), 16))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(219, 234, 255)))
                        g.FillPath(fill, hero);
                    caption = new RectangleF(29, caption.Y + 3, split - 41, caption.Height);
                    figure = new RectangleF(27, figure.Y + 1, split - 31, figure.Height);
                }
                DrawText(g, "预计还能用", label, look.Muted, caption, align);
                DrawDayValue(g, daysText, style, dayInk, figure, align);

                if (horizontal)
                {
                    if (style == 2) g.DrawLine(rule, split, 65 + bodyOffset, split, 160 + bodyOffset);
                    DrawBalance(g, look, style, true, new Rectangle(split + 12, 67 + bodyOffset, cardWidth - split - 30, 39), progress);
                    DrawBalance(g, look, style, false, new Rectangle(split + 12, 114 + bodyOffset, cardWidth - split - 30, 39), progress);
                }
                else
                {
                    if (style == 0) g.DrawLine(rule, middle, 131 + bodyOffset, middle, 183 + bodyOffset);
                    DrawBalance(g, look, style, true, new Rectangle(18, 127 + bodyOffset, middle - 25, 57), progress);
                    DrawBalance(g, look, style, false, new Rectangle(middle + 7, 127 + bodyOffset, cardWidth - middle - 25, 57), progress);
                }

                string average = snapshot == null || Double.IsNaN(snapshot.avgDaily7)
                    ? "" : "近 7 日日均 " + FormatNumber(snapshot.avgDaily7, "0.##") + " 度";
                DrawText(g, average, footer, look.Muted, new RectangleF(18, cardHeight - 31, 145, 23), StringAlignment.Near);
                DrawText(g, status, footer, look.Muted, new RectangleF(165, cardHeight - 31, cardWidth - 183, 23), StringAlignment.Far);
            }
        }

        private static void DrawDayValue(Graphics g, string text, int style, Color ink,
            RectangleF area, StringAlignment alignment)
        {
            string family = style == 0 ? "Georgia" : "Segoe UI";
            float size = 38f;
            using (Font font = new Font(family, size, FontStyle.Bold, GraphicsUnit.Pixel))
                size *= Math.Min(1f, area.Width / Math.Max(1f, Measure(g, text, font).Width));

            using (Font font = new Font(family, size, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(ink))
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.Alignment = alignment;
                format.LineAlignment = StringAlignment.Center;
                float x = alignment == StringAlignment.Center ? area.Left + area.Width / 2 : area.Left;
                g.DrawString(text, font, brush, new PointF(x, area.Top + area.Height / 2), format);
            }
        }

        private void DrawBalance(Graphics g, Appearance look, int style, bool lighting, Rectangle area, double progress)
        {
            double value = snapshot == null ? Double.NaN : lighting ? snapshot.lightingBalance : snapshot.airBalance;
            double old = transitionFrom == null ? value
                : lighting ? transitionFrom.lightingBalance : transitionFrom.airBalance;
            int level = BalanceLevel(value);
            int oldLevel = BalanceLevel(old);
            Color warning = WarningColor(style, level, look.Accent);
            Color oldWarning = WarningColor(style, oldLevel, look.Accent);
            Color fill = Blend(BalanceFill(look, oldWarning, oldLevel), BalanceFill(look, warning, level), progress);
            Color ink = Blend(oldWarning, warning, progress);
            if (style == 3 && Animated && level > 0)
            {
                double pulse = .14 * (1 - Math.Cos(animationClock.ElapsedMilliseconds * 2 * Math.PI / 2500)) / 2;
                ink = Blend(ink, fill, pulse);
            }
            ink = LoadingInk(ink, fill);
            if (style == 1 || style == 3 || style == IosAppearance || level > 0 || oldLevel > 0 && progress < 1)
            {
                using (GraphicsPath shape = RoundedPath(area, style == IosAppearance ? 14 : 10))
                using (SolidBrush background = new SolidBrush(fill))
                    g.FillPath(background, shape);
            }

            bool horizontal = style >= 2;
            int left = area.Left + 11;
            if (style == 3)
            {
                using (SolidBrush dot = new SolidBrush(ink))
                    g.FillEllipse(dot, left, area.Top + 16, 7, 7);
                left += 13;
            }
            string name = lighting ? "照明余额" : "空调余额";
            string amount = Double.IsNaN(value) || Double.IsInfinity(value)
                ? "—" : FormatNumber(Interpolate(old, value, progress), "0.##") + " 度";
            using (Font label = MakeFont("Microsoft YaHei UI", 9f, FontStyle.Regular))
            using (Font balance = MakeFont(style == 0 ? "Georgia" : "Segoe UI", horizontal ? 13f : 15f, FontStyle.Bold))
            {
                DrawText(g, name, label, look.Ink, new RectangleF(left, area.Top + 3, 68, horizontal ? 33 : 20), StringAlignment.Near);
                RectangleF number = horizontal ? new RectangleF(left + 68, area.Top, area.Right - left - 76, area.Height)
                    : new RectangleF(left, area.Top + 23, area.Right - left - 8, 30);
                DrawText(g, amount, balance, ink, number, horizontal ? StringAlignment.Far : StringAlignment.Near);
            }
        }

        private double TransitionProgress()
        {
            if (!Animated || transitionFrom == null) return 1;
            double elapsed = Math.Min(1, (animationClock.ElapsedMilliseconds - transitionStarted) / 640.0);
            return 1 - Math.Pow(1 - elapsed, 3);
        }

        private Color LoadingInk(Color ink, Color surface)
        {
            if (!Animated || !refreshing) return ink;
            double phase = (animationClock.ElapsedMilliseconds - refreshStarted) * 2 * Math.PI / 1250;
            return Blend(ink, surface, .24 * (1 - Math.Cos(phase)) / 2);
        }

        private static int BalanceLevel(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return 0;
            return value < CriticalBalance ? 2 : value < LowBalance ? 1 : 0;
        }

        private static Color WarningColor(int style, int level, Color normal)
        {
            if (level == 2) return style == 1 ? Color.LightCoral : Color.FromArgb(166, 47, 50);
            if (level == 1) return style == 1 ? Color.Khaki : Color.FromArgb(133, 86, 0);
            return normal;
        }

        private static Color BalanceFill(Appearance look, Color ink, int level)
        {
            return level == 0 ? look.Tile : Blend(look.Tile, ink, .10);
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        private static double Interpolate(double from, double to, double t)
        {
            if (Double.IsNaN(from) || Double.IsInfinity(from)) return to;
            return from + (to - from) * t;
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GammaDeck
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(22, 24, 29);
        public static readonly Color Panel = Color.FromArgb(31, 34, 41);
        public static readonly Color Panel2 = Color.FromArgb(42, 46, 55);
        public static readonly Color Hover = Color.FromArgb(54, 59, 70);
        public static readonly Color Border = Color.FromArgb(60, 65, 78);
        public static readonly Color Text = Color.FromArgb(230, 232, 236);
        public static readonly Color Dim = Color.FromArgb(150, 157, 170);
        public static readonly Color Accent = Color.FromArgb(118, 185, 0);
        public static readonly Color Danger = Color.FromArgb(239, 83, 80);
        public static readonly Color Warn = Color.FromArgb(245, 180, 60);

        public static readonly Font Ui = new Font("Segoe UI", 9.5f);
        public static readonly Font UiBold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 15f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 11f);
        public static readonly Font Osd = new Font("Segoe UI Semibold", 16f);

        public static readonly string[] Palette =
        {
            "#76B900", "#F97316", "#22C55E", "#EC4899", "#8B5CF6", "#F59E0B", "#3B82F6", "#EF4444", "#14B8A6", "#EAB308"
        };

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch { return fallback; }
        }

        public static string ToHex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }

        public static Button Btn(string text, EventHandler click, bool accent = false)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = accent ? Accent : Panel2;
            b.ForeColor = accent ? Color.Black : Text;
            b.FlatAppearance.BorderColor = accent ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(140, 210, 20) : Hover;
            b.Font = accent ? UiBold : Ui;
            b.Height = 32;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            int w = TextRenderer.MeasureText(text, b.Font).Width + 28;
            b.Width = Math.Max(90, w);
            if (click != null) b.Click += click;
            return b;
        }

        public static Label Lbl(string text, Font font, Color color)
        {
            var l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static CheckBox Check(string text, bool value, Action<bool> changed)
        {
            var c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.AutoSize = true;
            c.ForeColor = Text;
            c.Font = Ui;
            c.Cursor = Cursors.Hand;
            c.CheckedChanged += delegate { changed(c.Checked); };
            return c;
        }

        public static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var br = new LinearGradientBrush(new Rectangle(1, 1, 30, 30), Color.FromArgb(255, 70, 70), Color.FromArgb(60, 120, 255), 45f))
                    {
                        var blend = new ColorBlend(3);
                        blend.Colors = new[] { Color.FromArgb(255, 70, 70), Accent, Color.FromArgb(60, 120, 255) };
                        blend.Positions = new[] { 0f, 0.5f, 1f };
                        br.InterpolationColors = blend;
                        g.FillEllipse(br, 1, 1, 30, 30);
                    }
                    using (var br = new SolidBrush(Color.FromArgb(28, 30, 36)))
                        g.FillEllipse(br, 9, 9, 14, 14);
                    using (var br = new SolidBrush(Color.White))
                        g.FillEllipse(br, 13, 13, 6, 6);
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }
    }

    /// <summary>Тёмный ползунок. Двойной клик — значение по умолчанию.</summary>
    internal sealed class DarkSlider : Control
    {
        private int min, max = 100, val, def;
        private bool dragging;

        public event EventHandler ValueChanged;

        public DarkSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Height = 28;
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Color.Transparent;
        }

        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        public int DefaultValue { get { return def; } set { def = value; Invalidate(); } }

        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void SetSilently(int v)
        {
            val = Math.Max(min, Math.Min(max, v));
            Invalidate();
        }

        private Rectangle Track { get { return new Rectangle(10, Height / 2 - 3, Math.Max(1, Width - 20), 6); } }

        private int XFor(int v)
        {
            var t = Track;
            return t.X + (int)Math.Round((v - min) * (double)t.Width / Math.Max(1, max - min));
        }

        private int ValueAt(int x)
        {
            var t = Track;
            return (int)Math.Round((x - t.X) / (double)t.Width * (max - min) + min);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Track;
            Color fill = Enabled ? Theme.Accent : Theme.Border;
            using (var p = Theme.Round(t, 3))
            using (var b = new SolidBrush(Theme.Panel2))
                g.FillPath(b, p);
            int x = XFor(val);
            if (x > t.X)
            {
                using (var p = Theme.Round(new Rectangle(t.X, t.Y, x - t.X, t.Height), 3))
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, p);
            }
            int dx = XFor(def);
            using (var pen = new Pen(Theme.Dim, 1))
                g.DrawLine(pen, dx, t.Bottom + 3, dx, t.Bottom + 7);
            var thumb = new Rectangle(x - 8, Height / 2 - 8, 16, 16);
            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Dim))
                g.FillEllipse(b, thumb);
            using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border, Focused ? 2.5f : 1.5f))
                g.DrawEllipse(pen, thumb);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            dragging = true;
            Value = ValueAt(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Value = ValueAt(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Value = def;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            int step = (ModifierKeys & Keys.Shift) != 0 ? 10 : 1;
            Value += e.Delta > 0 ? step : -step;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value -= e.Shift ? 10 : 1; break;
                case Keys.Right: case Keys.Up: Value += e.Shift ? 10 : 1; break;
                case Keys.PageDown: Value -= 10; break;
                case Keys.PageUp: Value += 10; break;
                case Keys.Home: Value = min; break;
                case Keys.End: Value = max; break;
                case Keys.Delete: Value = def; break;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>Строка «Название — ползунок — значение».</summary>
    internal sealed class SliderRow : Panel
    {
        public readonly DarkSlider Slider = new DarkSlider();
        private readonly Label title;
        private readonly Label value;
        private readonly Func<int, string> format;

        public event EventHandler ValueChanged;

        public SliderRow(string text, int min, int max, int def, Func<int, string> format)
        {
            this.format = format;
            Height = 34;
            BackColor = Color.Transparent;
            title = Theme.Lbl(text, Theme.Ui, Theme.Text);
            title.AutoSize = false;
            title.TextAlign = ContentAlignment.MiddleLeft;
            value = Theme.Lbl("", Theme.UiBold, Theme.Text);
            value.AutoSize = false;
            value.TextAlign = ContentAlignment.MiddleRight;
            Slider.Minimum = min;
            Slider.Maximum = max;
            Slider.DefaultValue = def;
            Slider.SetSilently(def);
            Slider.ValueChanged += delegate
            {
                value.Text = format(Slider.Value);
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };
            value.Text = format(def);
            Controls.Add(title);
            Controls.Add(Slider);
            Controls.Add(value);
        }

        public int Value
        {
            get { return Slider.Value; }
            set { Slider.SetSilently(value); this.value.Text = format(Slider.Value); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (title == null) return;
            title.SetBounds(0, 0, 170, Height);
            value.SetBounds(Width - 70, 0, 70, Height);
            Slider.SetBounds(170, (Height - 28) / 2, Math.Max(40, Width - 170 - 76), 28);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            title.ForeColor = Enabled ? Theme.Text : Theme.Dim;
            value.ForeColor = Enabled ? Theme.Text : Theme.Dim;
        }
    }

    /// <summary>Кнопка профиля в стиле «плитки Stream Deck».</summary>
    internal sealed class ProfileTile : Control
    {
        public readonly Profile Profile;
        private bool hover;
        public bool Active;
        public bool Modified;
        public string Monitors = "";   // номера мониторов, где профиль сейчас включён, через пробел

        public ProfileTile(Profile p)
        {
            Profile = p;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(164, 72);
            Margin = new Padding(0, 0, 8, 8);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            Color c = Theme.ParseColor(Profile.Color, Theme.Accent);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Round(r, 8))
            {
                using (var b = new SolidBrush(hover ? Theme.Hover : Theme.Panel2))
                    g.FillPath(b, path);
                g.SetClip(path);
                using (var b = new SolidBrush(c))
                    g.FillRectangle(b, r.X, r.Y, 6, r.Height);
                g.ResetClip();
                if (Active)
                    using (var pen = new Pen(c, 2.5f))
                        g.DrawPath(pen, path);
            }
            string name = Loc.ProfileName(Profile.Name) + (Active && Modified ? " *" : "");
            var nameRect = new Rectangle(14, 8, Width - 22, 38);
            TextRenderer.DrawText(g, name, Theme.UiBold, nameRect, Theme.Text,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // значки мониторов справа внизу, перед точкой активного профиля
            string[] nums = string.IsNullOrEmpty(Monitors) ? new string[0] : Monitors.Split(' ');
            int bx = Width - 22;
            for (int i = nums.Length - 1; i >= 0; i--)
            {
                int bw = Math.Max(16, TextRenderer.MeasureText(nums[i], Theme.Small).Width + 2);
                bx -= bw + 3;
                var br = new Rectangle(bx, Height - 23, bw, 16);
                using (var path = Theme.Round(br, 3))
                using (var b = new SolidBrush(c))
                    g.FillPath(b, path);
                TextRenderer.DrawText(g, nums[i], Theme.Small, br, Theme.Bg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            string hk = string.IsNullOrEmpty(Profile.Hotkey) ? Loc.T("no hotkey") : Profile.Hotkey;
            TextRenderer.DrawText(g, hk, Theme.Small, new Rectangle(14, Height - 24, Math.Max(20, bx - 18), 18),
                string.IsNullOrEmpty(Profile.Hotkey) ? Theme.Border : Theme.Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Active)
            {
                using (var b = new SolidBrush(c))
                    g.FillEllipse(b, Width - 16, Height - 18, 8, 8);
            }
        }
    }

    /// <summary>Карточка монитора над профилями: номер, имя, текущий профиль. Бывает ещё «Все мониторы» и «+».</summary>
    internal sealed class MonitorCard : Control
    {
        public enum Kind { Monitor, All, Add }

        public readonly Kind Type;
        public readonly string Device;
        public string Badge = "", Title = "", Detail = "", Profile = "";
        public Color ProfileColor = Theme.Dim;
        public bool Selected;
        private bool hover;

        public MonitorCard(Kind type, string device)
        {
            Type = type;
            Device = device;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(type == Kind.Add ? 120 : 256, 62);
            Margin = new Padding(0, 0, 10, 0);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Round(r, 9))
            {
                if (Type == Kind.Add)
                {
                    if (hover) using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, path);
                    using (var pen = new Pen(hover ? Theme.Accent : Theme.Border, 1.5f) { DashStyle = DashStyle.Dash })
                        g.DrawPath(pen, path);
                    TextRenderer.DrawText(g, "+", Theme.H1, new Rectangle(0, 4, Width, 32), Theme.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, Loc.T("Add monitor"), Theme.Small, new Rectangle(0, 36, Width, 18), Theme.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                    return;
                }
                using (var b = new SolidBrush(Selected ? Color.FromArgb(37, 48, 26) : hover ? Theme.Hover : Theme.Panel2))
                    g.FillPath(b, path);
                using (var pen = new Pen(Selected ? Theme.Accent : Theme.Border, Selected ? 2.2f : 1f))
                    g.DrawPath(pen, path);
            }
            var badge = new Rectangle(12, 10, 24, 22);
            using (var path = Theme.Round(badge, 5))
            using (var b = new SolidBrush(Selected ? Theme.Accent : Theme.Border))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, Badge, Theme.UiBold, badge, Selected ? Theme.Bg : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Title, Theme.UiBold, new Rectangle(44, 9, Width - 54, 22), Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            using (var b = new SolidBrush(ProfileColor))
                g.FillEllipse(b, 14, 41, 8, 8);
            int detailW = string.IsNullOrEmpty(Detail) ? 0 : TextRenderer.MeasureText(Detail, Theme.Small).Width + 4;
            TextRenderer.DrawText(g, Profile, Theme.Small, new Rectangle(28, 36, Math.Max(20, Width - 40 - detailW), 18), Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            if (detailW > 0)
                TextRenderer.DrawText(g, Detail, Theme.Small, new Rectangle(Width - 12 - detailW, 36, detailW, 18), Theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Поле для записи сочетания клавиш.</summary>
    internal sealed class HotkeyBox : TextBox
    {
        private string hotkey = "";

        public event EventHandler HotkeyChanged;

        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = Theme.Panel2;
            ForeColor = Theme.Text;
            BorderStyle = BorderStyle.FixedSingle;
            Cursor = Cursors.Hand;
            ShortcutsEnabled = false;
            Font = Theme.Ui;
            Width = 210;
            TextAlign = HorizontalAlignment.Center;
            Hotkey = "";
        }

        public string Hotkey
        {
            get { return hotkey; }
            set
            {
                hotkey = value ?? "";
                Text = hotkey.Length == 0 ? Loc.T("— not set —") : hotkey;
                ForeColor = hotkey.Length == 0 ? Theme.Dim : Theme.Text;
            }
        }

        protected override void OnEnter(EventArgs e) { base.OnEnter(e); BackColor = Theme.Hover; }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); BackColor = Theme.Panel2; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return true;
            bool win = Hotkeys.IsWinDown();
            bool noMods = mods == Keys.None && !win;
            if (noMods && (key == Keys.Back || key == Keys.Delete || key == Keys.Escape))
            {
                Hotkey = "";
                if (HotkeyChanged != null) HotkeyChanged(this, EventArgs.Empty);
                return true;
            }
            if (noMods && Hotkeys.NeedsModifier(key))
            {
                Text = Loc.T("add Ctrl / Alt / Shift");
                ForeColor = Theme.Warn;
                return true;
            }
            Hotkey = Hotkeys.Format(mods, win, key);
            if (HotkeyChanged != null) HotkeyChanged(this, EventArgs.Empty);
            return true;
        }
    }

    /// <summary>График гамма-кривых R, G, B.</summary>
    internal sealed class CurveView : Control
    {
        public ColorSettings Settings = ColorSettings.Default();

        public CurveView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Panel2);
            var r = new Rectangle(8, 8, Width - 17, Height - 17);
            using (var pen = new Pen(Theme.Border, 1))
            {
                for (int i = 1; i < 4; i++)
                {
                    int x = r.X + r.Width * i / 4, y = r.Y + r.Height * i / 4;
                    g.DrawLine(pen, x, r.Y, x, r.Bottom);
                    g.DrawLine(pen, r.X, y, r.Right, y);
                }
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, r.X, r.Bottom, r.Right, r.Y);
            }
            if (Settings.Red.SameAs(Settings.Green) && Settings.Red.SameAs(Settings.Blue))
            {
                Draw(g, r, Settings.Red, Theme.Text);
                return;
            }
            Draw(g, r, Settings.Blue, Color.FromArgb(220, 80, 140, 255));
            Draw(g, r, Settings.Green, Color.FromArgb(220, 90, 210, 90));
            Draw(g, r, Settings.Red, Color.FromArgb(220, 255, 85, 85));
        }

        private static void Draw(Graphics g, Rectangle r, ChannelSettings c, Color color)
        {
            var pts = new PointF[64];
            for (int i = 0; i < pts.Length; i++)
            {
                double x = i / (double)(pts.Length - 1);
                double y = GammaMath.Eval(c, x);
                pts[i] = new PointF(r.X + (float)(x * r.Width), r.Bottom - (float)(y * r.Height));
            }
            using (var pen = new Pen(color, 2f))
                g.DrawLines(pen, pts);
        }
    }

    /// <summary>Всплывающая подсказка при смене профиля. Можно скрыть от захвата OBS.</summary>
    internal sealed class OsdForm : Form
    {
        private readonly Timer hideTimer = new Timer();
        private string title = "", sub = "";
        private Color accent = Theme.Accent;

        public bool ExcludeFromCapture;

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Panel;
            DoubleBuffered = true;
            hideTimer.Interval = 1500;
            hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x20 | 0x8; // TOOLWINDOW | NOACTIVATE | TRANSPARENT | TOPMOST
                return cp;
            }
        }

        public void ShowMessage(string title, string sub, Color accent, Screen screen)
        {
            this.title = title;
            this.sub = sub;
            this.accent = accent;
            int w = Math.Max(TextRenderer.MeasureText(title, Theme.Osd).Width, TextRenderer.MeasureText(sub, Theme.Small).Width) + 48;
            w = Math.Max(220, w);
            Size = new Size(w, 70);
            Region = new Region(Theme.Round(new Rectangle(0, 0, w, 70), 12));
            var b = screen.Bounds;
            Location = new Point(b.X + (b.Width - w) / 2, b.Y + 48);
            if (!Visible) Show();
            ApplyAffinity();
            Invalidate();
            hideTimer.Stop();
            hideTimer.Start();
        }

        public void ApplyAffinity()
        {
            if (IsHandleCreated)
                Native.SetWindowDisplayAffinity(Handle, ExcludeFromCapture ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(accent))
                g.FillRectangle(b, 0, 0, 6, Height);
            TextRenderer.DrawText(g, title, Theme.Osd, new Rectangle(20, 8, Width - 28, 32), Theme.Text, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(21, 42, Width - 28, 20), Theme.Dim, TextFormatFlags.NoPrefix);
        }
    }

    internal static class InputDialog
    {
        public static string Ask(IWin32Window owner, string caption, string prompt, string initial)
        {
            using (var f = new Form())
            {
                f.Text = caption;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.BackColor = Theme.Panel;
                f.ClientSize = new Size(380, 130);
                var l = Theme.Lbl(prompt, Theme.Ui, Theme.Text);
                l.Location = new Point(14, 14);
                var t = new TextBox();
                t.Text = initial ?? "";
                t.SetBounds(16, 40, 348, 26);
                t.BackColor = Theme.Panel2;
                t.ForeColor = Theme.Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                t.Font = Theme.Ui;
                var ok = Theme.Btn("OK", null, true);
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(380 - 16 - ok.Width - 100, 84);
                var cancel = Theme.Btn(Loc.T("Cancel"), null);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(380 - 16 - cancel.Width, 84);
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                string s = t.Text.Trim();
                return s.Length == 0 ? null : s;
            }
        }
    }
}

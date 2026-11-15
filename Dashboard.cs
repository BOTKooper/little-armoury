using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TinyAsus
{
    internal static class Theme
    {
        // Three depths: the window, panels and controls resting on it, and a control under the pointer.
        internal static readonly Color Background = Color.FromArgb(18, 18, 17), Surface = Color.FromArgb(30, 29, 27), Raised = Color.FromArgb(44, 42, 39);
        internal static readonly Color Border = Color.FromArgb(52, 50, 46), Card = Surface;
        internal static readonly Color Text = Color.FromArgb(236, 232, 224), Muted = Color.FromArgb(154, 148, 137);
        internal static readonly Color Accent = Color.FromArgb(233, 164, 62), Cool = Color.FromArgb(124, 168, 204), Green = Cool;
        internal static readonly Color Hot = Color.FromArgb(232, 96, 68);
        internal static readonly Color Disabled = Color.FromArgb(98, 94, 87), Line = Color.FromArgb(200, 196, 188);
        internal const string Sans = "Segoe UI", Semibold = "Segoe UI Semibold";
        // Words are set in Segoe UI; standalone readings use the DIN-style figures that ship with Windows.
        internal static readonly string Digits = Face("Bahnschrift SemiLight", Sans), DigitsLight = Face("Bahnschrift Light", "Segoe UI Light");
        internal static readonly string Icons = Face("Segoe MDL2 Assets", null);
        // Built from code points so the sources stay pure ASCII for csc.
        internal static readonly string Dash = ((char)0x2014).ToString(), Ellipsis = ((char)0x2026).ToString();
        internal static readonly string Degrees = (char)0xB0 + "C", Dot = " " + (char)0xB7 + " ", EditGlyph = ((char)0xE70F).ToString();
        private static readonly StringFormat Typographic = MakeFormat();
        private static float scale;

        // Layout is written in 96-dpi units and scaled once; forms use AutoScaleMode.None.
        internal static float Scale
        {
            get
            {
                if (scale <= 0)
                {
                    try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f; } catch { scale = 1; }
                    if (scale <= 0) scale = 1;
                }
                return scale;
            }
        }
        internal static int Px(float value) { return (int)Math.Round(value * Scale); }
        private static string Face(string name, string fallback)
        {
            try { using (var family = new FontFamily(name)) return family.Name.Length > 0 ? name : fallback; }
            catch (ArgumentException) { return fallback; }
        }
        // Fonts for native controls, sized in pixels so they follow the same scale as the drawn layout.
        internal static Font Font(float size, FontStyle style) { return Font(Sans, size, style); }
        internal static Font Font(string face, float size, FontStyle style) { return new Font(face, size * 96f / 72f * Scale, style, GraphicsUnit.Pixel); }
        internal static Color Blend(Color top, Color under, float amount)
        {
            return Color.FromArgb((int)(under.R + (top.R - under.R) * amount), (int)(under.G + (top.G - under.G) * amount), (int)(under.B + (top.B - under.B) * amount));
        }

        internal static GraphicsPath Rounded(RectangleF r, float radius) { return Rounded(r, radius, 15); }
        // corners: 1 top left, 2 top right, 4 bottom right, 8 bottom left.
        internal static GraphicsPath Rounded(RectangleF r, float radius, int corners)
        {
            var p = new GraphicsPath(); float d = Math.Max(0.5f, radius) * 2;
            if ((corners & 1) != 0) p.AddArc(r.X, r.Y, d, d, 180, 90); else p.AddLine(r.X, r.Y, r.X, r.Y);
            if ((corners & 2) != 0) p.AddArc(r.Right - d, r.Y, d, d, 270, 90); else p.AddLine(r.Right, r.Y, r.Right, r.Y);
            if ((corners & 4) != 0) p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if ((corners & 8) != 0) p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure(); return p;
        }
        internal static void Box(Graphics g, RectangleF r, float radius, int corners, Color fill, Color edge)
        {
            using (var path = Rounded(r, radius, corners))
            {
                if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (edge.A > 0) using (var pen = new Pen(edge)) g.DrawPath(pen, path);
            }
        }
        internal static void Hairline(Graphics g, float x1, float y1, float x2, float y2)
        {
            SmoothingMode before = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Border)) g.DrawLine(pen, x1, y1, x2, y2);
            g.SmoothingMode = before;
        }

        private static StringFormat MakeFormat()
        {
            var format = new StringFormat(StringFormat.GenericTypographic);
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            return format;
        }
        private static Font Canvas(string face, float size) { return new Font(face, size * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel); }
        // Baseline that centres capital letters of the given size on a line.
        internal static float Base(float centre, float size) { return centre + size * 96f / 72f * 0.36f; }
        internal static float Measure(Graphics g, string text, string face, float size)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            g.TextRenderingHint = size < 14 ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAlias;
            using (var f = Canvas(face, size)) return g.MeasureString(text, f, 100000, Typographic).Width;
        }
        // Draws text on a baseline without GDI+ side padding. align: 0 left, 1 right, 2 centre. Returns the drawn width.
        internal static float Put(Graphics g, string text, string face, float size, Color color, float x, float baseline, int align, float max)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            g.TextRenderingHint = size < 14 ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAlias;
            using (var f = Canvas(face, size))
            using (var b = new SolidBrush(color))
            {
                float width = g.MeasureString(text, f, 100000, Typographic).Width;
                float top = baseline - f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style);
                if (max > 0 && width > max)
                {
                    using (var clipped = new StringFormat(Typographic))
                    {
                        clipped.Trimming = StringTrimming.EllipsisCharacter;
                        g.DrawString(text, f, b, new RectangleF(align == 1 ? x - max : align == 2 ? x - max / 2 : x, top, max, f.Size * 1.6f), clipped);
                    }
                    return max;
                }
                g.DrawString(text, f, b, align == 1 ? x - width : align == 2 ? x - width / 2 : x, top, Typographic);
                return width;
            }
        }

        // Frame for a borderless native input. The frame brightens while the input has focus.
        internal static void Field(Graphics g, float x, float y, float width, float height, Control input)
        {
            Box(g, new RectangleF(x + 0.5f, y + 0.5f, width - 1, height - 1), 6, 15, Surface, input != null && input.ContainsFocus ? Text : Border);
        }
        // Places a native input, whose height follows its font, in the middle of its frame.
        internal static void Seat(Control input, float x, float y, float width, float height, float inset)
        {
            input.SetBounds(Px(x + inset), Px(y) + (Px(height) - input.Height) / 2, Px(width - inset * 2), input.Height);
            input.Enter += delegate { if (input.Parent != null) input.Parent.Invalidate(); };
            input.Leave += delegate { if (input.Parent != null) input.Parent.Invalidate(); };
        }
        // The caption takes the window colour so the title bar and content read as one surface.
        internal static void DarkTitle(Form form)
        {
            try
            {
                int dark = 1, caption = ColorTranslator.ToWin32(Background);
                DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
            }
            catch { }
        }
        internal static void DarkScroll(Control control) { try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch { } }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string application, string idList);
    }

    internal sealed class ModeButton : Button
    {
        internal const int Plain = 0, Segment = 1, Link = 2, Row = 3, Ghost = 4, Glyph = 5;
        // Marked puts a dot before the label; Danger turns the label red under the pointer.
        internal bool Selected, Stale, Marked, Danger;
        internal int Kind, Corners = 15;
        internal string Detail;
        private bool hover, down;
        internal ModeButton(string text)
        {
            Text = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseMnemonic = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand; Font = Theme.Font(9, FontStyle.Regular); AccessibleName = text;
            BackColor = Theme.Background; ForeColor = Theme.Text;
        }
        internal void SetSelected(bool value)
        {
            if (Selected == value) return;
            Selected = value; AccessibleDescription = value ? "Selected" : null; Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            float w = Width / Theme.Scale, h = Height / Theme.Scale, line = Theme.Base(h / 2, 9);
            bool live = Enabled, lit = hover && live;
            // The active choice is inverted; colour is kept for heat and warnings.
            Color fill, ink, edge = Color.Empty;
            if (Selected) { fill = live ? Theme.Text : Theme.Disabled; ink = Theme.Background; }
            else
            {
                bool quiet = Kind == Link || Kind == Ghost;
                ink = !live ? Theme.Disabled : Danger && lit ? Theme.Hot : quiet && !lit ? Theme.Muted : Theme.Text;
                fill = Kind == Link ? Color.Empty : lit ? (down ? Theme.Border : Theme.Raised) : Kind == Segment || Kind == Ghost ? Color.Empty : Theme.Surface;
                if (Kind == Plain) edge = lit ? Theme.Disabled : Theme.Border;
            }
            var r = new RectangleF(0.5f, 0.5f, w - 1, h - 1);
            float radius = Kind == Segment || Kind == Ghost ? 6 : 7;
            if (Kind != Link) Theme.Box(g, r, radius, Corners, fill, edge);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(r, radius, Corners)) using (var pen = new Pen(Theme.Accent, 1.5f)) g.DrawPath(pen, path);

            string face = Selected ? Theme.Semibold : Theme.Sans;
            if (Kind == Row)
            {
                float room = w - 26;
                if (!String.IsNullOrEmpty(Detail))
                {
                    Color soft = !live ? ink : Selected ? (Stale ? Color.FromArgb(150, 88, 8) : Color.FromArgb(96, 91, 82)) : Stale ? Theme.Accent : Theme.Muted;
                    room -= Theme.Put(g, Detail, Theme.Sans, 8.5f, soft, w - 13, line, 1, w * 0.62f) + 16;
                }
                Theme.Put(g, Text, face, 9, ink, 13, line, 0, Math.Max(20, room));
            }
            else if (Kind == Link)
            {
                float width = Theme.Put(g, Text, face, 9, ink, w - 1, line, 1, w - 2);
                if (lit) using (var pen = new Pen(ink)) g.DrawLine(pen, w - 1 - width, line + 2.5f, w - 1, line + 2.5f);
            }
            else if (Kind == Ghost)
            {
                using (var pen = new Pen(ink, 1.4f)) { g.DrawLine(pen, 10, h / 2, 19, h / 2); g.DrawLine(pen, 14.5f, h / 2 - 4.5f, 14.5f, h / 2 + 4.5f); }
                Theme.Put(g, Text, face, 9, ink, 27, line, 0, w - 33);
            }
            else if (Kind == Glyph && Theme.Icons != null)
                Theme.Put(g, Text, Theme.Icons, 9.5f, live && !lit ? Theme.Muted : ink, w / 2, h / 2 + 9.5f * 96f / 72f / 2, 2, 0);
            else
            {
                float width = Theme.Measure(g, Text, face, 9), mark = Marked ? 11 : 0;
                float left = Math.Max(6, (w - width - mark) / 2);
                if (Marked) using (var b = new SolidBrush(live ? Theme.Accent : Theme.Disabled)) g.FillEllipse(b, left, h / 2 - 2.5f, 5, 5);
                Theme.Put(g, Text, face, 9, ink, left + mark, line, 0, w - 12 - mark);
            }
        }
    }

    // One filled track with joined segments, used for the three firmware presets.
    internal sealed class SegmentBar : Panel
    {
        internal readonly ModeButton[] Items;
        internal SegmentBar(params string[] names)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Items = new ModeButton[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                Items[i] = new ModeButton(names[i]); Items[i].Kind = ModeButton.Segment; Items[i].BackColor = Theme.Surface; Controls.Add(Items[i]);
            }
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Items == null) return;
            int pad = Math.Max(2, Theme.Px(3)), width = Width - pad * 2, count = Items.Length;
            for (int i = 0; i < count; i++)
            {
                if (Items[i] == null) continue;
                int left = pad + width * i / count, right = pad + width * (i + 1) / count;
                Items[i].SetBounds(left, pad, right - left, Height - pad * 2);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            Theme.Box(g, new RectangleF(0.5f, 0.5f, Width / Theme.Scale - 1, Height / Theme.Scale - 1), 9, 15, Theme.Surface, Theme.Border);
        }
    }

    // A check box drawn as a switch at the right edge. Snug sets the label against the switch instead of the left edge.
    internal sealed class Toggle : CheckBox
    {
        internal bool Snug;
        private bool hover;
        internal Toggle(string text)
        {
            Text = text; UseMnemonic = false; AccessibleName = text; Cursor = Cursors.Hand; AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font(9, FontStyle.Regular);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            float w = Width / Theme.Scale, h = Height / Theme.Scale, size = Snug ? 8.5f : 9;
            var track = new RectangleF(w - 29.5f, (h - 16) / 2, 28, 16);
            Color ink = !Enabled ? Theme.Disabled : hover && Snug ? Theme.Text : ForeColor;
            if (Snug) Theme.Put(g, Text, Theme.Sans, size, ink, track.Left - 8, Theme.Base(h / 2, size), 1, track.Left - 8);
            else Theme.Put(g, Text, Theme.Sans, size, ink, 0, Theme.Base(h / 2, size), 0, track.Left - 8);
            Theme.Box(g, track, 8, 15, Checked ? (Enabled ? Theme.Text : Theme.Disabled) : hover ? Theme.Border : Theme.Raised, Checked ? Color.Empty : Theme.Disabled);
            using (var knob = new SolidBrush(Checked ? Theme.Background : Theme.Muted))
                g.FillEllipse(knob, Checked ? track.Right - 13 : track.Left + 3, track.Top + 3, 10, 10);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Accent, 1.5f)) using (var path = Theme.Rounded(RectangleF.Inflate(track, 2.5f, 2.5f), 10.5f)) g.DrawPath(pen, path);
        }
    }

    // The native trackbar keeps its keyboard, mouse and accessibility behaviour; only its drawing is replaced.
    internal sealed class Slider : TrackBar
    {
        private bool hover;
        internal Slider()
        {
            AutoSize = false; TickStyle = TickStyle.None; BackColor = Theme.Background;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnValueChanged(EventArgs e) { Invalidate(); base.OnValueChanged(e); }
        protected override void OnScroll(EventArgs e) { Invalidate(); base.OnScroll(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            // The drawn thumb follows the native one, stretched so that it ends flush with the control's edges.
            Edges thumb = new Edges(), channel = new Edges();
            SendMessage(Handle, 0x419, IntPtr.Zero, ref thumb); SendMessage(Handle, 0x41A, IntPtr.Zero, ref channel);
            float s = Theme.Scale, y = Height / 2f, half = (thumb.Right - thumb.Left) / 2f, reach = 8 * s;
            float low = channel.Left + half, high = Math.Max(low + 1, channel.Right - half);
            float x = reach + ((thumb.Left + thumb.Right) / 2f - low) / (high - low) * (Width - reach * 2);
            float from = 2 * s, to = Width - 2 * s;
            using (var pen = new Pen(Theme.Border, 3 * s)) { pen.StartCap = pen.EndCap = LineCap.Round; g.DrawLine(pen, from, y, to, y); }
            using (var pen = new Pen(Enabled ? Theme.Text : Theme.Disabled, 3 * s)) { pen.StartCap = pen.EndCap = LineCap.Round; g.DrawLine(pen, from, y, Math.Max(from, x), y); }
            float r = (hover || Focused ? 8 : 7) * s;
            using (var gap = new SolidBrush(BackColor)) g.FillEllipse(gap, x - r - 2 * s, y - r - 2 * s, (r + 2 * s) * 2, (r + 2 * s) * 2);
            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Disabled)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
            if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Accent, 1.5f * s)) g.DrawEllipse(pen, x - r - 2 * s, y - r - 2 * s, (r + 2 * s) * 2, (r + 2 * s) * 2);
        }
        [StructLayout(LayoutKind.Sequential)] private struct Edges { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref Edges edges);
    }

    // A number input without the system frame and spin buttons; arrow keys and the wheel still step the value.
    internal sealed class NumberBox : NumericUpDown
    {
        internal NumberBox()
        {
            BorderStyle = BorderStyle.None; BackColor = Theme.Surface; ForeColor = Theme.Text; TextAlign = HorizontalAlignment.Center;
            Font = Theme.Font(Theme.Digits, 10, FontStyle.Regular);
            if (Controls.Count > 1) Controls[0].Visible = false;
        }
        // The base class still reserves the spin buttons' width on the right. The control is widened by
        // that much and the surplus clipped away, so the text fills exactly the requested width.
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            int spare = Controls.Count > 1 ? Math.Max(0, Width - Controls[1].Width) : 0;
            if ((specified & BoundsSpecified.Width) == 0) width -= spare;
            base.SetBoundsCore(x, y, width + spare, height, specified);
            Region = new Region(new Rectangle(0, 0, Math.Max(1, Width - spare), Height));
        }
    }

    internal sealed class Dashboard : Form
    {
        // 96-dpi units. One panel holds the CPU and GPU halves over a memory and disk strip.
        private const int Edge = 16, Far = 524, Middle = 270, PanelTop = 14, Split = 150, PanelBottom = 194;
        private const int First = 32, Second = 286, ColumnWidth = 222, ControlsTop = 206, RowsTop = 250, RowPitch = 36, MaxRows = 5;
        private static readonly int[] PresetFirmware = { 2, 0, 1 };
        private static readonly Color NoteFill = Theme.Blend(Theme.Accent, Theme.Background, 0.13f);
        private Sensors sensors;
        private Snapshot data = new Snapshot();
        private readonly Timer timer = new Timer();
        private readonly List<double> cpuHistory = new List<double>(), gpuHistory = new List<double>();
        private readonly SegmentBar presets = new SegmentBar("Silent", "Performance", "Turbo");
        private readonly ModeButton curves = new ModeButton("Fan curves" + Theme.Ellipsis);
        private readonly ModeButton newMode = new ModeButton("New mode");
        private readonly ModeButton powerLink = new ModeButton("Windows power settings");
        private readonly Toggle gpuBox = new Toggle("Sensors");
        private readonly Panel modePanel = new Panel();
        private readonly Label note = new Label();
        private readonly ToolTip tips = new ToolTip();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly List<ModeButton> rows = new List<ModeButton>(), edits = new List<ModeButton>();
        private readonly NotifyIcon tray;
        private readonly ProfileStore store;
        private readonly CpuPolicy policy;
        private CustomMode applied;
        private bool sampling, changing, ready, initializing = true, pollGpu = true, closePending;
        private int baseFirmware = -1, listRows;
        private bool noted;
        private Rectangle noteBox;
        private string status, startupProblem;
        private readonly string screenshotPath;
        private byte[] manualCpu, manualGpu;

        internal Dashboard(string[] args)
        {
            Text = "Little Armoury"; BackColor = Theme.Background; ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true; Font = Theme.Font(9, FontStyle.Regular);
            Icon = MakeIcon(); status = "Connecting" + Theme.Ellipsis;

            // Loading profiles applies no mode. CpuPolicy also recovers an interrupted prior session.
            try { store = ProfileStore.Load(); } catch (Exception ex) { startupProblem = "Custom modes could not be loaded: " + ex.Message; }
            try { policy = new CpuPolicy(); } catch (Exception ex) { startupProblem = "CPU limits are unavailable: " + ex.Message; }

            presets.SetBounds(Theme.Px(Edge), Theme.Px(ControlsTop), Theme.Px(396), Theme.Px(36));
            for (int i = 0; i < presets.Items.Length; i++)
            {
                int firmware = PresetFirmware[i];
                presets.Items[i].Enabled = false;
                presets.Items[i].Click += async delegate { await ChangePreset(firmware); };
            }
            Controls.Add(presets);
            curves.SetBounds(Theme.Px(Far - 104), Theme.Px(ControlsTop), Theme.Px(104), Theme.Px(36)); curves.Enabled = false;
            curves.Click += async delegate { await OpenCurves(); }; Controls.Add(curves);
            modePanel.BackColor = Theme.Background; modePanel.HandleCreated += delegate { Theme.DarkScroll(modePanel); };
            Controls.Add(modePanel);
            newMode.Kind = ModeButton.Ghost; newMode.AccessibleName = "New custom mode";
            newMode.Click += async delegate { await EditMode(null); }; Controls.Add(newMode);
            powerLink.Kind = ModeButton.Link; powerLink.Click += delegate { OpenPowerSettings(); }; Controls.Add(powerLink);
            note.ForeColor = Theme.Accent; note.BackColor = NoteFill; note.Font = Theme.Font(8.5f, FontStyle.Regular);
            note.AutoEllipsis = true; note.UseMnemonic = false; note.AccessibleName = "Status"; Controls.Add(note);
            gpuBox.Snug = true; gpuBox.ForeColor = Theme.Muted; gpuBox.BackColor = Theme.Surface; gpuBox.Checked = true;
            gpuBox.AccessibleName = "GPU sensors"; gpuBox.SetBounds(Theme.Px(Second + ColumnWidth - 84), Theme.Px(PanelTop + 12), Theme.Px(84), Theme.Px(20));
            gpuBox.CheckedChanged += async delegate { pollGpu = gpuBox.Checked; Invalidate(); await Sample(); };
            Controls.Add(gpuBox);
            tips.SetToolTip(gpuBox, "GPU sensor polling can keep the NVIDIA GPU awake.");

            tray = new NotifyIcon { Icon = Icon, Text = "Little Armoury", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Reveal(); };
            menu.Opening += delegate { RebuildMenu(); };
            LayoutModes(); RebuildMenu(); Render();

            timer.Interval = 2000; timer.Tick += async delegate { await Sample(); };
            if (args.Length > 1 && args[0] == "--capture") screenshotPath = args[1];
            Shown += async delegate
            {
                try
                {
                    sensors = await Task.Run(delegate { return new Sensors(); }); ready = true; initializing = false;
                    status = sensors.Asus.Available ? startupProblem ?? "" : "ASUS driver unavailable. Try Run as administrator.";
                    if (!String.IsNullOrEmpty(sensors.Model)) Text = "Little Armoury" + Theme.Dot + sensors.Model;
                    Render(); await Sample(); timer.Start();
                    if (screenshotPath != null)
                    {
                        await Task.Delay(2200); await Sample();
                        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(screenshotPath); }
                        Close();
                    }
                }
                catch (Exception ex) { status = "Could not initialize sensors: " + ex.Message; Render(); }
                finally { initializing = false; }
            };
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }

        internal static string PresetName(int firmware) { return firmware == 2 ? "Silent" : firmware == 1 ? "Turbo" : "Performance"; }
        private static string Describe(CustomMode mode)
        { return PresetName(mode.FirmwareMode) + Theme.Dot + "CPU max " + mode.CpuLimitPercent + " %" + (mode.DisableBoost ? Theme.Dot + "boost off" : ""); }
        private static bool OnAc() { return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online; }
        private bool AsusReady { get { return sensors != null && sensors.Asus.Available; } }
        private bool CanApply(CustomMode mode)
        { return ready && !changing && AsusReady && policy != null && (mode.FirmwareMode != 1 || data.OnAc); }
        private void Reveal() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        private async Task Sample()
        {
            if (!ready || sampling || changing || IsDisposed) return;
            sampling = true;
            try
            {
                // Background work keeps firmware calls and NVIDIA initialization off the UI thread.
                bool gpu = pollGpu && Visible && WindowState != FormWindowState.Minimized;
                Snapshot next = await Task.Run(delegate { return sensors.Read(gpu); });
                if (IsDisposed) return;
                data = next;
                if (applied != null && policy != null && !policy.IsActive)
                {
                    applied = null;
                    status = "Windows power plan changed; custom CPU settings are no longer active.";
                }
                AddPoint(cpuHistory, data.CpuUsage); AddPoint(gpuHistory, data.GpuUsage);
                tray.Text = "Little Armoury" + Theme.Dot + "CPU " + Format(data.CpuTemp, "0", " " + Theme.Degrees) + Theme.Dot + "GPU " + Format(data.GpuTemp, "0", " " + Theme.Degrees);
                AccessibleDescription = "CPU " + Format(data.CpuUsage, "0", "%") + ", " + Format(data.CpuTemp, "0", " degrees") + "; GPU " + Format(data.GpuUsage, "0", "%") + ", " + Format(data.GpuTemp, "0", " degrees") + "; CPU fan " + data.CpuRpm + "; GPU fan " + data.GpuRpm;
                Render();
            }
            catch (Exception ex) { status = "Sensor update failed: " + ex.Message; Render(); }
            finally { sampling = false; }
        }

        private void Render()
        {
            if (IsDisposed) return;
            UpdateButtons(); UpdateNote(); Invalidate();
        }

        // DSTS on this laptop returns capabilities, not the active preset.
        // Only what this app successfully requested is highlighted.
        private void UpdateButtons()
        {
            bool idle = ready && !changing && AsusReady;
            for (int i = 0; i < presets.Items.Length; i++)
            {
                int firmware = PresetFirmware[i];
                presets.Items[i].Enabled = idle && (firmware != 1 || data.OnAc);
                presets.Items[i].SetSelected(applied == null && baseFirmware == firmware);
            }
            curves.Enabled = idle;
            // A dot marks curves edited in this session; the mode itself stays the selected control.
            if (curves.Marked != (manualCpu != null))
            {
                curves.Marked = manualCpu != null; curves.AccessibleDescription = curves.Marked ? "Custom curves applied" : null; curves.Invalidate();
            }
            newMode.Enabled = store != null && !changing;
            foreach (var edit in edits) edit.Enabled = !changing;
            for (int i = 0; i < rows.Count; i++)
            {
                var mode = (CustomMode)rows[i].Tag;
                bool active = applied != null && applied.Id == mode.Id;
                bool stale = active && (applied.FirmwareMode != mode.FirmwareMode || applied.CpuLimitPercent != mode.CpuLimitPercent || applied.DisableBoost != mode.DisableBoost);
                string detail = stale ? "Changed" + Theme.Dot + "click to apply" : Describe(mode);
                if (rows[i].Detail != detail || rows[i].Stale != stale)
                {
                    rows[i].Detail = detail; rows[i].Stale = stale; rows[i].AccessibleName = mode.Name + ", " + detail; rows[i].Invalidate();
                }
                rows[i].Enabled = CanApply(mode); rows[i].SetSelected(active);
            }
        }

        private void UpdateNote()
        {
            string text = status;
            if (String.IsNullOrEmpty(text))
            {
                text = sensors != null && sensors.ArmouryRunning ? "Armoury Crate service is running and may override modes." : "";
                if (ready && AsusReady && !data.OnAc) text = (text.Length > 0 ? text + " " : "") + "Turbo needs AC power.";
            }
            if (note.Text == text) return;
            note.Text = text; tips.SetToolTip(note, text); Arrange();
        }

        private void LayoutModes()
        {
            modePanel.SuspendLayout();
            while (modePanel.Controls.Count > 0) { Control old = modePanel.Controls[0]; modePanel.Controls.RemoveAt(0); old.Dispose(); }
            rows.Clear(); edits.Clear();
            int count = store == null ? 0 : store.Modes.Count;
            bool scroll = count > MaxRows;
            int width = Theme.Px(Far - Edge) - (scroll ? SystemInformation.VerticalScrollBarWidth + Theme.Px(4) : 0);
            int editWidth = Theme.Px(Theme.Icons != null ? 36 : 48), height = Theme.Px(RowPitch - 2);
            for (int i = 0; i < count; i++)
            {
                CustomMode mode = store.Modes[i];
                // The row and its edit button are drawn as the two halves of one item.
                var row = new ModeButton(mode.Name); row.Kind = ModeButton.Row; row.Corners = 9; row.Tag = mode;
                row.SetBounds(0, Theme.Px(i * RowPitch), width - editWidth - Math.Max(1, Theme.Px(1)), height);
                row.Click += async delegate { await ApplyCustom(mode); };
                if ((mode.Name ?? "").Length > 24) tips.SetToolTip(row, mode.Name);
                var edit = new ModeButton(Theme.Icons != null ? Theme.EditGlyph : "Edit"); edit.Kind = ModeButton.Glyph; edit.Corners = 6;
                edit.AccessibleName = "Edit " + mode.Name; tips.SetToolTip(edit, "Edit");
                edit.SetBounds(width - editWidth, Theme.Px(i * RowPitch), editWidth, height);
                edit.Click += async delegate { await EditMode(mode); };
                modePanel.Controls.Add(row); modePanel.Controls.Add(edit); rows.Add(row); edits.Add(edit);
            }
            listRows = Math.Min(count, MaxRows);
            modePanel.AutoScroll = scroll;
            modePanel.SetBounds(Theme.Px(Edge), Theme.Px(RowsTop), Theme.Px(Far - Edge), Theme.Px(listRows * RowPitch));
            modePanel.ResumeLayout();
            Arrange();
        }

        // Places what follows the mode list. The notice takes room only while there is something to say.
        private void Arrange()
        {
            int top = Theme.Px(RowsTop + listRows * RowPitch + (listRows > 0 ? 2 : 0));
            newMode.SetBounds(Theme.Px(Edge), top, Theme.Px(108), Theme.Px(28));
            powerLink.SetBounds(Theme.Px(Far - 170), top + Theme.Px(3), Theme.Px(170), Theme.Px(22));
            int end = top + Theme.Px(28);
            // Label.Visible reads false until the window is shown, so the text decides.
            noted = note.Text.Length > 0; note.Visible = noted;
            if (noted)
            {
                int width = Theme.Px(Far - Edge - 24), line = Theme.Px(17);
                int wanted = TextRenderer.MeasureText(note.Text, note.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                int height = wanted > line + Theme.Px(4) ? line * 2 : line;
                noteBox = new Rectangle(Theme.Px(Edge), end + Theme.Px(8), Theme.Px(Far - Edge), height + Theme.Px(14));
                note.SetBounds(noteBox.Left + Theme.Px(12), noteBox.Top + Theme.Px(7), width, height);
                end = noteBox.Bottom;
            }
            ClientSize = new Size(Theme.Px(Far + Edge), end + Theme.Px(14));
            Invalidate();
        }

        private void RebuildMenu()
        {
            while (menu.Items.Count > 0) { ToolStripItem old = menu.Items[0]; menu.Items.RemoveAt(0); old.Dispose(); }
            menu.Items.Add("Open Little Armoury", null, delegate { Reveal(); });
            menu.Items.Add(new ToolStripSeparator());
            bool idle = ready && !changing && AsusReady;
            for (int i = 0; i < PresetFirmware.Length; i++)
            {
                int firmware = PresetFirmware[i];
                var item = new ToolStripMenuItem(PresetName(firmware));
                item.Checked = applied == null && baseFirmware == firmware; item.Enabled = idle && (firmware != 1 || data.OnAc);
                item.Click += async delegate { await ChangePreset(firmware); };
                menu.Items.Add(item);
            }
            if (store != null && store.Modes.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                for (int i = 0; i < store.Modes.Count; i++)
                {
                    CustomMode mode = store.Modes[i];
                    var item = new ToolStripMenuItem((mode.Name ?? "").Replace("&", "&&"));
                    item.Checked = applied != null && applied.Id == mode.Id; item.Enabled = CanApply(mode);
                    item.Click += async delegate { await ApplyCustom(mode); };
                    menu.Items.Add(item);
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Windows power settings", null, delegate { OpenPowerSettings(); });
            menu.Items.Add("Quit", null, delegate { Close(); });
        }

        private void OpenPowerSettings()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true }); }
            catch (Exception ex) { status = "Could not open Windows power settings: " + ex.Message; Render(); }
        }

        // Claims the hardware for one change, then waits out a sensor read already in flight.
        private async Task<bool> Begin()
        {
            if (!ready || changing || closePending || IsDisposed || !AsusReady) return false;
            changing = true; UpdateButtons();
            while (sampling) await Task.Delay(20);
            return true;
        }

        private async Task ChangePreset(int firmware)
        {
            if (!ready || changing) return;
            if (firmware == 1 && !OnAc()) { status = "Connect the power adapter to use Turbo."; Render(); return; }
            if (!await Begin()) return;
            bool restored = policy == null;
            try
            {
                // The temporary Windows plan of a custom mode is removed before the firmware preset is requested.
                await Task.Run(delegate
                {
                    if (policy != null) { policy.Restore(); restored = true; }
                    sensors.Asus.Write(AsusDevice.Mode, firmware);
                });
                baseFirmware = firmware; applied = null; manualCpu = manualGpu = null; status = "";
            }
            catch (Exception ex)
            {
                if (!restored) status = "Windows power plan was not restored: " + ex.Message;
                else
                {
                    status = "Mode was not applied: " + ex.Message + (applied != null ? " The custom CPU limit was removed." : "");
                    applied = null;
                }
            }
            finally { changing = false; Render(); }
        }

        private async Task ApplyCustom(CustomMode source)
        {
            if (!ready || changing || policy == null) return;
            CustomMode mode;
            try { mode = source.Clone(); mode.Validate(); }
            catch (Exception ex) { status = "Mode is not valid: " + ex.Message; Render(); return; }
            if (mode.FirmwareMode == 1 && !OnAc()) { status = "Connect the power adapter to use a Turbo-based mode."; Render(); return; }
            if (!await Begin()) return;
            bool firmwareSet = false;
            try
            {
                await Task.Run(delegate
                {
                    sensors.Asus.Write(AsusDevice.Mode, mode.FirmwareMode); firmwareSet = true;
                    policy.Apply(mode);
                });
                baseFirmware = mode.FirmwareMode; applied = mode; manualCpu = manualGpu = null; status = "";
            }
            catch (Exception ex)
            {
                if (firmwareSet)
                {
                    // The preset write went through and reset the fan curves; only the CPU policy failed.
                    baseFirmware = mode.FirmwareMode; applied = null; manualCpu = manualGpu = null;
                    status = "CPU settings were not changed: " + ex.Message;
                    if (policy.IsActive) status += " Previous CPU limits remain active.";
                }
                else status = "Mode was not applied: " + ex.Message;
            }
            finally { changing = false; Render(); }
        }

        // Editing only changes the saved list. Hardware and power settings change when a mode is applied.
        private async Task EditMode(CustomMode existing)
        {
            if (store == null || changing || closePending) return;
            CustomMode saved = null; bool apply = false;
            using (var editor = new CustomModeEditor(existing))
            {
                DialogResult result = editor.ShowDialog(this);
                if (IsDisposed || result != DialogResult.OK) return;
                int index = existing == null ? -1 : store.Modes.IndexOf(existing);
                if (existing != null && index < 0) return;
                if (editor.DeleteRequested) store.Modes.RemoveAt(index);
                else if (index < 0) store.Modes.Add(editor.Result);
                else store.Modes[index] = editor.Result;
                try { store.Save(); }
                catch (Exception ex)
                {
                    if (editor.DeleteRequested) store.Modes.Insert(index, existing);
                    else if (index < 0) store.Modes.Remove(editor.Result);
                    else store.Modes[index] = existing;
                    MessageBox.Show(this, "Custom modes were not saved: " + ex.Message, "Little Armoury", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (editor.DeleteRequested)
                {
                    if (applied != null && applied.Id == existing.Id) status = existing.Name + " was deleted. Its CPU limit stays until you choose another mode.";
                }
                else { saved = editor.Result; apply = editor.ApplyRequested; }
            }
            LayoutModes(); RebuildMenu(); Render();
            if (apply && saved != null) await ApplyCustom(saved);
        }

        private async Task OpenCurves()
        {
            if (!await Begin()) return;
            int mode = baseFirmware >= 0 ? baseFirmware : 0;
            timer.Stop();
            try
            {
                if (mode == 1 && !OnAc()) throw new InvalidOperationException("Connect the power adapter to edit Turbo fan curves.");
                byte[] cpu = manualCpu ?? sensors.Asus.ReadCurve(AsusDevice.CpuCurve, mode);
                byte[] gpu = manualGpu ?? sensors.Asus.ReadCurve(AsusDevice.GpuCurve, mode);
                using (var editor = new CurveEditor(cpu, gpu, applied != null ? applied.Name : PresetName(mode)))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    // The preset write restores factory curves first. A failed partial write is rolled back.
                    try
                    {
                        sensors.Asus.Write(AsusDevice.Mode, mode);
                        sensors.Asus.Write(AsusDevice.CpuCurve, editor.Cpu);
                        sensors.Asus.Write(AsusDevice.GpuCurve, editor.Gpu);
                    }
                    catch (Exception applyError)
                    {
                        manualCpu = manualGpu = null;
                        try { sensors.Asus.Write(AsusDevice.Mode, mode); }
                        catch (Exception restoreError) { throw new IOException("Curve application failed: " + applyError.Message + " Factory-profile restore also failed: " + restoreError.Message); }
                        baseFirmware = mode;
                        throw new IOException("Curve application failed; factory profile restored. " + applyError.Message);
                    }
                    baseFirmware = mode; manualCpu = editor.Cpu; manualGpu = editor.Gpu;
                    status = "";
                }
            }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Fan curves", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { changing = false; if (!closePending && !IsDisposed) timer.Start(); Render(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (noted) Theme.Box(g, new RectangleF(noteBox.X + 0.5f, noteBox.Y + 0.5f, noteBox.Width - 1, noteBox.Height - 1), Theme.Px(7), 15, NoteFill, Color.Empty);
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            Theme.Box(g, new RectangleF(Edge + 0.5f, PanelTop + 0.5f, Far - Edge - 1, PanelBottom - PanelTop - 1), 10, 15, Theme.Surface, Theme.Border);
            Theme.Hairline(g, Middle, PanelTop + 1, Middle, PanelBottom - 1);
            Theme.Hairline(g, Edge + 1, Split, Far - 1, Split);

            string cpuName = sensors != null ? Tidy(sensors.CpuName) : "";
            string gpuName = pollGpu && data.GpuName != null ? data.GpuName.Replace(" Laptop GPU", "") : "";
            Column(g, First, "CPU", cpuName, ColumnWidth, data.CpuTemp, data.CpuUsage, cpuHistory);
            Column(g, Second, "GPU", gpuName, ColumnWidth - 92, data.GpuTemp, data.GpuUsage, gpuHistory);
            Stat(g, "Fan", Whole(data.CpuRpm), "rpm", First, 62);
            Stat(g, "Clock", Format(data.CpuMhz, "0", ""), "MHz", First + 70, 62);
            Stat(g, "Memory", sensors != null && sensors.MemorySpeed > 0 ? sensors.MemorySpeed.ToString() : Theme.Dash, "MT/s", First + 144, 78);
            Stat(g, "Fan", Whole(data.GpuRpm), "rpm", Second, 48);
            Stat(g, "Power / limit", Pair(data.GpuWatts, data.GpuLimit), "W", Second + 60, 64);
            Stat(g, "Clock / memory", Pair(data.GpuMhz, data.GpuMemoryMhz), "MHz", Second + 136, 86);
            Meter(g, "RAM", data.RamUsed, data.RamTotal, First);
            Meter(g, "System disk", data.DiskUsed, data.DiskTotal, Second);
        }

        // Temperature leads each half because heat is what the modes below act on.
        private static void Column(Graphics g, float x, string title, string name, float nameRoom, double? temp, double? load, List<double> history)
        {
            float used = Theme.Put(g, title, Theme.Semibold, 9, Theme.Text, x, PanelTop + 25, 0, 0) + 8;
            Theme.Put(g, name, Theme.Sans, 9, Theme.Muted, x + used, PanelTop + 25, 0, nameRoom - used);
            float width = Theme.Put(g, temp.HasValue ? temp.Value.ToString("0") : Theme.Dash, Theme.DigitsLight, temp.HasValue ? 33 : 20, TempColor(temp), x - 1, temp.HasValue ? 90 : 84, 0, 0);
            if (temp.HasValue) Theme.Put(g, Theme.Degrees, Theme.Sans, 10.5f, Theme.Muted, x + width + 3, 90, 0, 0);

            var plot = new RectangleF(x + 104, 67, ColumnWidth - 104, 23);
            Theme.Put(g, "Load", Theme.Sans, 8.25f, Theme.Muted, plot.Left, 60, 0, 0);
            float unit = load.HasValue ? Theme.Put(g, "%", Theme.Sans, 8.25f, Theme.Muted, plot.Right, 60, 1, 0) + 3 : 0;
            Theme.Put(g, Format(load, "0", ""), Theme.Digits, 10.5f, Theme.Text, plot.Right - unit, 60, 1, 0);
            Theme.Hairline(g, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            var run = new List<PointF>();
            for (int i = 0; i <= history.Count; i++)
            {
                if (i < history.Count && !Double.IsNaN(history[i]))
                {
                    // Newest sample sits at the right edge; a missing reading breaks the line.
                    run.Add(new PointF(plot.Left + (i + 30 - history.Count) / 29f * plot.Width, plot.Bottom - (float)(history[i] / 100 * plot.Height)));
                    continue;
                }
                if (run.Count > 1)
                {
                    var area = new List<PointF>(run);
                    area.Add(new PointF(run[run.Count - 1].X, plot.Bottom)); area.Add(new PointF(run[0].X, plot.Bottom));
                    using (var fill = new SolidBrush(Color.FromArgb(34, Theme.Text))) g.FillPolygon(fill, area.ToArray());
                    using (var pen = new Pen(Theme.Line, 1.25f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, run.ToArray()); }
                }
                run.Clear();
            }
        }
        private static Color TempColor(double? temp)
        { return !temp.HasValue ? Theme.Muted : temp.Value >= 88 ? Theme.Hot : temp.Value >= 72 ? Theme.Accent : Theme.Text; }
        // A reading under its name; the unit is set quieter than the figure.
        private static void Stat(Graphics g, string name, string value, string unit, float x, float room)
        {
            Theme.Put(g, name, Theme.Sans, 8.25f, Theme.Muted, x, 115, 0, room);
            float unitWidth = value == Theme.Dash ? 0 : Theme.Measure(g, unit, Theme.Sans, 8.25f) + 3;
            float available = Math.Max(12, room - unitWidth), size = 10.5f;
            float naturalWidth = Theme.Measure(g, value, Theme.Digits, size);
            if (naturalWidth > available) size = Math.Max(8.25f, size * available / naturalWidth);
            float used = Theme.Put(g, value, Theme.Digits, size, value == Theme.Dash ? Theme.Muted : Theme.Text, x, 134, 0, available);
            if (value != Theme.Dash) Theme.Put(g, unit, Theme.Sans, 8.25f, Theme.Muted, x + used + 3, 134, 0, Math.Max(0, room - used - 3));
        }
        private static void Meter(Graphics g, string name, double used, double total, float x)
        {
            Theme.Put(g, name, Theme.Sans, 8.25f, Theme.Muted, x, 170, 0, 0);
            if (total > 0)
            {
                float unit = Theme.Put(g, "GiB", Theme.Sans, 8.25f, Theme.Muted, x + ColumnWidth, 170, 1, 0) + 3;
                Theme.Put(g, used.ToString("0.0") + " / " + total.ToString("0.0"), Theme.Digits, 10.5f, Theme.Text, x + ColumnWidth - unit, 170, 1, 0);
            }
            else Theme.Put(g, Theme.Dash, Theme.Digits, 10.5f, Theme.Muted, x + ColumnWidth, 170, 1, 0);
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(Theme.Border)) g.FillRectangle(b, x, 177, ColumnWidth, 3);
            if (total > 0)
            {
                float part = (float)Math.Max(0, Math.Min(1, used / total));
                using (var b = new SolidBrush(part >= 0.9f ? Theme.Accent : Theme.Line)) g.FillRectangle(b, x, 177, part * ColumnWidth, 3);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }
        private static string Tidy(string cpu)
        {
            if (cpu == null) return "";
            cpu = cpu.Replace("(R)", "").Replace("(TM)", "").Replace("  ", " ");
            int cut = cpu.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
            return (cut > 0 ? cpu.Substring(0, cut) : cpu).Trim();
        }
        private static string Whole(int? value) { return value.HasValue ? value.Value.ToString() : Theme.Dash; }
        private static string Pair(double? first, double? second)
        { return first.HasValue || second.HasValue ? Format(first, "0", "") + " / " + Format(second, "0", "") : Theme.Dash; }
        private static string Format(double? value, string format, string suffix) { return value.HasValue ? value.Value.ToString(format) + suffix : Theme.Dash; }
        private static void AddPoint(List<double> list, double? value) { list.Add(value ?? Double.NaN); if (list.Count > 30) list.RemoveAt(0); }

        internal static Icon MakeIcon()
        {
            using (var b = new Bitmap(32, 32)) using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Theme.Background)) using (var p = Theme.Rounded(new RectangleF(1, 1, 30, 30), 7)) g.FillPath(brush, p);
                using (var pen = new Pen(Theme.Text, 3f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawArc(pen, 7, 8, 18, 18, 150, 240); }
                using (var pen = new Pen(Theme.Accent, 3f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawLine(pen, 16, 17, 21, 11); }
                IntPtr h = b.GetHicon(); try { using (var icon = Icon.FromHandle(h)) return (Icon)icon.Clone(); } finally { DestroyIcon(h); }
            }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tray == null) return;
            if (WindowState == FormWindowState.Minimized) { Hide(); timer.Interval = 10000; }
            else timer.Interval = 2000;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (sampling || changing || initializing)
            {
                e.Cancel = true;
                if (!closePending)
                {
                    closePending = true; timer.Stop();
                    BeginInvoke(new Action(async delegate { while (sampling || changing || initializing) await Task.Delay(50); Close(); }));
                }
                return;
            }
            if (policy != null)
            {
                try { policy.Restore(); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Windows power settings could not be restored: " + ex.Message + "\nReopen Little Armoury to retry recovery, or choose a power plan in Windows settings.", "Little Armoury", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); tips.Dispose();
            // Restores the Windows power plan that was active before a custom mode.
            if (policy != null) try { policy.Dispose(); } catch { }
            if (sensors != null) sensors.Dispose(); Icon.Dispose(); base.OnFormClosed(e);
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }
}

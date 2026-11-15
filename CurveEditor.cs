using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TinyAsus
{
    internal sealed class CurveEditor : Form
    {
        // Plot area in 96-dpi units inside the graph control: 10..110 degrees across, 0..100 % up.
        private const float PlotLeft = 46, PlotTop = 20, PlotRight = 520, PlotBottom = 174;
        // The table under the graph: one column per point, a pair of rows per fan.
        private const int Edge = 20, Far = 560, TableLeft = 96, CellWidth = 58, CellHeight = 28, CpuTop = 258, GpuTop = 326, ButtonTop = 398;
        private readonly NumericUpDown[,] inputs = new NumericUpDown[4, 8];
        private readonly Plot graph = new Plot();
        private int dragFan = -1, dragPoint = -1, hotFan = -1, hotPoint = -1;
        internal byte[] Cpu { get; private set; }
        internal byte[] Gpu { get; private set; }

        private sealed class Plot : Control
        {
            internal Plot()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                BackColor = Theme.Background; TabStop = false; AccessibleName = "Fan curve graph";
            }
        }

        internal CurveEditor(byte[] cpu, byte[] gpu, string preset)
        {
            cpu = PrepareDraft(cpu); gpu = PrepareDraft(gpu);
            Text = "Fan curves" + Theme.Dot + preset; ClientSize = new Size(Theme.Px(Far + Edge), Theme.Px(ButtonTop + 50));
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font(9, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true;

            graph.SetBounds(Theme.Px(Edge), Theme.Px(16), Theme.Px(Far - Edge), Theme.Px(208));
            graph.Paint += DrawGraph; graph.MouseDown += GraphDown; graph.MouseMove += GraphMove;
            graph.MouseUp += delegate { dragFan = dragPoint = -1; };
            graph.MouseLeave += delegate { if (dragFan < 0) SetHot(-1, -1); };
            Controls.Add(graph);

            string[] names = { "CPU " + Theme.Degrees, "CPU %", "GPU " + Theme.Degrees, "GPU %" };
            for (int row = 0; row < 4; row++)
                for (int i = 0; i < 8; i++)
                {
                    var n = new NumberBox();
                    n.Minimum = row % 2 == 0 ? 10 : 0; n.Maximum = row % 2 == 0 ? 110 : 100;
                    n.Value = row < 2 ? cpu[i + (row % 2) * 8] : gpu[i + (row % 2) * 8];
                    n.AccessibleName = names[row] + " point " + (i + 1);
                    n.ValueChanged += delegate { graph.Invalidate(); };
                    inputs[row, i] = n; Controls.Add(n);
                    Theme.Seat(n, TableLeft + i * CellWidth, RowTop(row), CellWidth, CellHeight, 5);
                }
            var cancel = new ModeButton("Cancel"); cancel.SetBounds(Theme.Px(Far - 200), Theme.Px(ButtonTop), Theme.Px(80), Theme.Px(32));
            cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;
            var apply = new ModeButton("Apply curves"); apply.Selected = true; apply.SetBounds(Theme.Px(Far - 112), Theme.Px(ButtonTop), Theme.Px(112), Theme.Px(32));
            apply.Click += Apply; Controls.Add(apply); AcceptButton = apply;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }
        private static int RowTop(int row) { return (row < 2 ? CpuTop : GpuTop) + row % 2 * CellHeight; }

        // The table frame, its labels and the footnote. The row labels double as the graph legend.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            for (int i = 0; i < 8; i++)
                Theme.Put(g, (i + 1).ToString(), Theme.Digits, 8.5f, Theme.Muted, TableLeft + i * CellWidth + CellWidth / 2f, CpuTop - 9, 2, 0);
            for (int fan = 0; fan < 2; fan++)
            {
                int top = fan == 0 ? CpuTop : GpuTop;
                Theme.Box(g, new RectangleF(TableLeft + 0.5f, top + 0.5f, CellWidth * 8 - 1, CellHeight * 2 - 1), 7, 15, Theme.Surface, Theme.Border);
                Theme.Hairline(g, TableLeft + 1, top + CellHeight, TableLeft + CellWidth * 8 - 1, top + CellHeight);
                for (int i = 1; i < 8; i++) Theme.Hairline(g, TableLeft + i * CellWidth, top + 1, TableLeft + i * CellWidth, top + CellHeight * 2 - 1);
                using (var b = new SolidBrush(fan == 0 ? Theme.Accent : Theme.Cool)) g.FillEllipse(b, Edge, top + CellHeight / 2f - 3.5f, 7, 7);
                Theme.Put(g, fan == 0 ? "CPU" : "GPU", Theme.Semibold, 9, Theme.Text, Edge + 13, Theme.Base(top + CellHeight / 2f, 9), 0, 0);
                Theme.Put(g, Theme.Degrees, Theme.Sans, 8.5f, Theme.Muted, TableLeft - 10, Theme.Base(top + CellHeight / 2f, 8.5f), 1, 0);
                Theme.Put(g, "%", Theme.Sans, 8.5f, Theme.Muted, TableLeft - 10, Theme.Base(top + CellHeight * 1.5f, 8.5f), 1, 0);
            }
            for (int row = 0; row < 4; row++)
                for (int i = 0; i < 8; i++)
                    if (inputs[row, i] != null && inputs[row, i].ContainsFocus)
                        using (var pen = new Pen(Theme.Text)) using (var path = Theme.Rounded(new RectangleF(TableLeft + i * CellWidth + 1.5f, RowTop(row) + 1.5f, CellWidth - 3, CellHeight - 3), 4))
                            g.DrawPath(pen, path);
            Theme.Put(g, "Choosing a mode restores factory curves.", Theme.Sans, 8.5f, Theme.Muted, Edge, Theme.Base(ButtonTop + 16, 8.5f), 0, 0);
        }

        private byte[] Values(int offset)
        {
            var result = new byte[16];
            for (int i = 0; i < 8; i++) { result[i] = (byte)inputs[offset, i].Value; result[i + 8] = (byte)inputs[offset + 1, i].Value; }
            return result;
        }
        internal static string ValidateCurve(byte[] curve)
        {
            if (!AsusDevice.CurveLooksValid(curve)) return "Temperatures and fan speeds must increase from left to right.";
            for (int i = 1; i < 8; i++) if (curve[i] <= curve[i - 1]) return "Each temperature must be higher than the previous point.";
            for (int i = 9; i < 16; i++) if (curve[i] < curve[i - 1]) return "Fan speeds must never fall as temperature rises.";
            if (curve[15] != 100) return "The final fan speed must be 100%.";
            return null;
        }
        internal static byte[] PrepareDraft(byte[] factory)
        {
            if (!AsusDevice.CurveLooksValid(factory)) throw new ArgumentException("No valid factory fan curve is available.");
            byte[] draft = (byte[])factory.Clone();
            // Some factory curves decrease at one point. Show a monotonic draft before applying.
            for (int i = 9; i < 16; i++) draft[i] = Math.Max(draft[i], draft[i - 1]);
            draft[15] = 100;
            return draft;
        }
        private void Apply(object sender, EventArgs e)
        {
            byte[] cpu = Values(0), gpu = Values(2);
            string problem = ValidateCurve(cpu) ?? ValidateCurve(gpu);
            if (problem != null) { MessageBox.Show(this, problem, "Fan curves", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Cpu = cpu; Gpu = gpu; DialogResult = DialogResult.OK; Close();
        }

        private PointF Map(int fan, int point)
        {
            float temp = (float)inputs[fan * 2, point].Value, speed = (float)inputs[fan * 2 + 1, point].Value;
            return new PointF(PlotLeft + (temp - 10) / 100 * (PlotRight - PlotLeft), PlotBottom - speed / 100 * (PlotBottom - PlotTop));
        }
        private bool Hit(Point mouse, out int fan, out int point)
        {
            float x = mouse.X / Theme.Scale, y = mouse.Y / Theme.Scale; double best = 13 * 13;
            fan = -1; point = -1;
            for (int f = 0; f < 2; f++)
                for (int i = 0; i < 8; i++)
                {
                    PointF p = Map(f, i); double distance = (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
                    if (distance < best) { best = distance; fan = f; point = i; }
                }
            return fan >= 0;
        }
        private void SetHot(int fan, int point)
        {
            if (hotFan == fan && hotPoint == point) return;
            hotFan = fan; hotPoint = point; graph.Cursor = fan >= 0 ? Cursors.Hand : Cursors.Default; graph.Invalidate();
        }
        private void GraphDown(object sender, MouseEventArgs e)
        {
            int fan, point;
            if (e.Button != MouseButtons.Left || !Hit(e.Location, out fan, out point)) return;
            dragFan = fan; dragPoint = point; SetHot(fan, point);
        }
        private void GraphMove(object sender, MouseEventArgs e)
        {
            int fan, point;
            if (dragFan < 0) { Hit(e.Location, out fan, out point); SetHot(fan, point); return; }
            // A dragged point stays between its neighbours, so the curve is always one that can be applied.
            int row = dragFan * 2, i = dragPoint;
            int temp = (int)Math.Round(10 + (e.X / Theme.Scale - PlotLeft) / (PlotRight - PlotLeft) * 100);
            int speed = (int)Math.Round((PlotBottom - e.Y / Theme.Scale) / (PlotBottom - PlotTop) * 100);
            int low = i > 0 ? (int)inputs[row, i - 1].Value + 1 : 10, high = i < 7 ? (int)inputs[row, i + 1].Value - 1 : 110;
            if (low <= high) inputs[row, i].Value = Math.Max(low, Math.Min(high, temp));
            low = i == 7 ? 100 : i > 0 ? (int)inputs[row + 1, i - 1].Value : 0; high = i < 7 ? (int)inputs[row + 1, i + 1].Value : 100;
            if (low <= high) inputs[row + 1, i].Value = Math.Max(low, Math.Min(high, speed));
        }

        private void DrawGraph(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Background); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            Theme.Box(g, new RectangleF(0.5f, 0.5f, graph.Width / Theme.Scale - 1, graph.Height / Theme.Scale - 1), 10, 15, Theme.Surface, Theme.Border);
            for (int i = 0; i <= 4; i++)
            {
                float y = PlotBottom - i * (PlotBottom - PlotTop) / 4;
                Theme.Hairline(g, PlotLeft, y, PlotRight, y);
                if (i % 2 == 0) Theme.Put(g, i == 4 ? "100 %" : (i * 25).ToString(), Theme.Digits, 8.5f, Theme.Muted, PlotLeft - 8, y + 4, 1, 0);
            }
            for (int t = 20; t <= 100; t += 20)
            {
                float x = PlotLeft + (t - 10) / 100f * (PlotRight - PlotLeft);
                Theme.Hairline(g, x, PlotTop, x, PlotBottom);
                Theme.Put(g, t == 100 ? "100 " + Theme.Degrees : t.ToString(), Theme.Digits, 8.5f, Theme.Muted, x, PlotBottom + 18, 2, 0);
            }
            if (inputs[3, 7] == null) return;
            for (int fan = 0; fan < 2; fan++)
            {
                var points = new PointF[8];
                for (int i = 0; i < 8; i++) points[i] = Map(fan, i);
                Color color = fan == 0 ? Theme.Accent : Theme.Cool;
                using (var pen = new Pen(color, 1.75f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, points); }
                // Points are open rings; the one under the pointer fills in.
                using (var b = new SolidBrush(color)) using (var hole = new SolidBrush(Theme.Surface)) using (var ring = new Pen(color, 1.75f))
                    for (int i = 0; i < 8; i++)
                    {
                        bool hot = fan == hotFan && i == hotPoint; float r = hot ? 5.5f : 3.5f;
                        g.FillEllipse(hot ? b : hole, points[i].X - r, points[i].Y - r, r * 2, r * 2);
                        g.DrawEllipse(ring, points[i].X - r, points[i].Y - r, r * 2, r * 2);
                        if (hot) using (var halo = new Pen(Theme.Text, 1.5f)) g.DrawEllipse(halo, points[i].X - r - 1.5f, points[i].Y - r - 1.5f, r * 2 + 3, r * 2 + 3);
                    }
            }
            if (hotFan >= 0)
            {
                string reading = (hotFan == 0 ? "CPU " : "GPU ") + (hotPoint + 1) + Theme.Dot + inputs[hotFan * 2, hotPoint].Value + " " + Theme.Degrees + Theme.Dot + inputs[hotFan * 2 + 1, hotPoint].Value + " %";
                Theme.Put(g, reading, Theme.Sans, 9, hotFan == 0 ? Theme.Accent : Theme.Cool, PlotLeft + 10, PlotTop + 18, 0, 0);
            }
        }
    }
}

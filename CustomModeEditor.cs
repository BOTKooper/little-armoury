using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TinyAsus
{
    // Edits one saved mode. Nothing is applied here; the dashboard saves the result and applies it only on request.
    internal sealed class CustomModeEditor : Form
    {
        // 96-dpi units: one 380-wide column of name, preset, CPU limit and boost over the button row.
        private const int Near = 20, Far = 400, NameTop = 40, PresetTop = 106, LimitTop = 160, BoostTop = 228, Rule = 270, ButtonTop = 284;
        private static readonly int[] Firmware = { 2, 0, 1 };
        private readonly CustomMode source;
        private readonly TextBox name = new TextBox();
        private readonly SegmentBar preset = new SegmentBar("Silent", "Performance", "Turbo");
        private readonly Slider slider = new Slider();
        private readonly NumberBox percent = new NumberBox();
        private readonly Toggle boost = new Toggle("Disable CPU boost");
        private int firmware;
        internal CustomMode Result { get; private set; }
        internal bool DeleteRequested { get; private set; }
        internal bool ApplyRequested { get; private set; }

        internal CustomModeEditor(CustomMode existing)
        {
            source = existing;
            Text = existing == null ? "New mode" : "Edit mode"; ClientSize = new Size(Theme.Px(Far + Near), Theme.Px(ButtonTop + 50));
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font(9, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true;

            name.BackColor = Theme.Surface; name.ForeColor = Theme.Text; name.BorderStyle = BorderStyle.None; name.MaxLength = 40;
            name.Font = Theme.Font(10, FontStyle.Regular); name.AccessibleName = "Name"; name.Text = existing == null ? "" : existing.Name ?? "";
            Controls.Add(name); Theme.Seat(name, Near, NameTop, Far - Near, 34, 11);

            preset.SetBounds(Theme.Px(Near), Theme.Px(PresetTop), Theme.Px(Far - Near), Theme.Px(36));
            for (int i = 0; i < preset.Items.Length; i++)
            {
                int value = Firmware[i];
                preset.Items[i].Click += delegate { Choose(value); };
            }
            Controls.Add(preset);

            int limit = existing == null ? 100 : Math.Max(20, Math.Min(100, existing.CpuLimitPercent));
            percent.Minimum = 20; percent.Maximum = 100; percent.Value = limit;
            percent.AccessibleName = "Maximum CPU performance, percent";
            Controls.Add(percent); Theme.Seat(percent, Far - 66, LimitTop, 48, 28, 4);
            slider.SetBounds(Theme.Px(Near), Theme.Px(LimitTop + 32), Theme.Px(Far - Near), Theme.Px(28));
            slider.Minimum = 20; slider.Maximum = 100; slider.SmallChange = 1; slider.LargeChange = 10;
            slider.Value = limit; slider.AccessibleName = "Maximum CPU performance, percent";
            slider.ValueChanged += delegate { percent.Value = slider.Value; };
            percent.ValueChanged += delegate { slider.Value = (int)percent.Value; };
            Controls.Add(slider);

            boost.SetBounds(Theme.Px(Near), Theme.Px(BoostTop), Theme.Px(Far - Near), Theme.Px(28));
            boost.Checked = existing != null && existing.DisableBoost;
            Controls.Add(boost);

            var everyday = new ModeButton("Everyday defaults"); everyday.Kind = ModeButton.Link;
            everyday.AccessibleName = "Everyday defaults: Performance, 80 percent, boost disabled";
            everyday.SetBounds(Theme.Px(Far - 130), Theme.Px(16), Theme.Px(130), Theme.Px(20));
            everyday.Click += delegate
            {
                if (name.Text.Trim().Length == 0) name.Text = "Everyday";
                Choose(0); percent.Value = 80; boost.Checked = true;
            };
            Controls.Add(everyday);
            var tips = new ToolTip(); tips.SetToolTip(everyday, "Performance" + Theme.Dot + "80 %" + Theme.Dot + "boost off");
            Disposed += delegate { tips.Dispose(); };

            if (existing != null)
            {
                var delete = new ModeButton("Delete"); delete.Danger = true; delete.SetBounds(Theme.Px(Near), Theme.Px(ButtonTop), Theme.Px(70), Theme.Px(32));
                delete.Click += delegate
                {
                    if (MessageBox.Show(this, "Delete " + source.Name + "?", "Custom mode", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    DeleteRequested = true; DialogResult = DialogResult.OK; Close();
                };
                Controls.Add(delete);
            }
            var saveApply = new ModeButton("Save and apply"); saveApply.SetBounds(Theme.Px(Far - 282), Theme.Px(ButtonTop), Theme.Px(116), Theme.Px(32));
            saveApply.Click += delegate { Save(true); }; Controls.Add(saveApply);
            var save = new ModeButton("Save"); save.Selected = true; save.SetBounds(Theme.Px(Far - 158), Theme.Px(ButtonTop), Theme.Px(74), Theme.Px(32));
            save.Click += delegate { Save(false); }; Controls.Add(save); AcceptButton = save;
            var cancel = new ModeButton("Cancel"); cancel.SetBounds(Theme.Px(Far - 76), Theme.Px(ButtonTop), Theme.Px(76), Theme.Px(32));
            cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;

            int start = existing == null ? 0 : existing.FirmwareMode;
            Choose(start == 1 || start == 2 ? start : 0);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            Theme.Put(g, "Name", Theme.Sans, 8.5f, Theme.Muted, Near, NameTop - 9, 0, 0);
            Theme.Field(g, Near, NameTop, Far - Near, 34, name);
            Theme.Put(g, "ASUS preset", Theme.Sans, 8.5f, Theme.Muted, Near, PresetTop - 9, 0, 0);
            Theme.Put(g, "Maximum CPU performance", Theme.Sans, 9, Theme.Text, Near, Theme.Base(LimitTop + 14, 9), 0, 0);
            Theme.Field(g, Far - 66, LimitTop, 48, 28, percent);
            Theme.Put(g, "%", Theme.Sans, 9, Theme.Muted, Far, Theme.Base(LimitTop + 14, 9), 1, 0);
            Theme.Hairline(g, Near, Rule, Far, Rule);
        }

        private void Choose(int value)
        {
            firmware = value;
            for (int i = 0; i < preset.Items.Length; i++) preset.Items[i].SetSelected(Firmware[i] == value);
        }

        private void Save(bool apply)
        {
            string text = name.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show(this, "Enter a name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                name.Focus(); return;
            }
            // Cloning keeps the Id and any fields this editor does not show.
            CustomMode mode = source != null ? source.Clone() : new CustomMode();
            if (String.IsNullOrEmpty(mode.Id)) mode.Id = Guid.NewGuid().ToString("D");
            mode.Name = text; mode.FirmwareMode = firmware; mode.CpuLimitPercent = (int)percent.Value; mode.DisableBoost = boost.Checked;
            try { mode.Validate(); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Result = mode; ApplyRequested = apply; DialogResult = DialogResult.OK; Close();
        }
    }
}

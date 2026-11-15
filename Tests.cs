using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TinyAsus
{
    internal static class Tests
    {
        private static int assertions;
        private static void Assert(bool condition, string name)
        { if (!condition) throw new Exception("FAILED: " + name); assertions++; }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                byte[] preset = AsusDevice.EncodeRequest(0x53564544, AsusDevice.Mode, BitConverter.GetBytes(2));
                Assert(preset.Length == 16, "preset request length");
                Assert(BitConverter.ToUInt32(preset, 0) == 0x53564544, "DEVS method encoding");
                Assert(BitConverter.ToUInt32(preset, 4) == 8, "firmware argument byte count");
                Assert(BitConverter.ToUInt32(preset, 8) == AsusDevice.Mode, "performance device identifier");
                Assert(BitConverter.ToInt32(preset, 12) == 2, "silent preset encoding");
                byte[] cpu = { 40, 54, 58, 62, 66, 70, 74, 98, 1, 14, 17, 20, 33, 46, 53, 61 };
                byte[] gpu = { 40, 54, 58, 62, 66, 70, 74, 98, 1, 18, 19, 13, 36, 48, 54, 67 };
                Assert(AsusDevice.CurveLooksValid(cpu), "actual G713RM CPU factory curve accepted");
                Assert(AsusDevice.CurveLooksValid(gpu), "actual nonmonotonic G713RM GPU factory curve accepted for editing");
                Assert(CurveEditor.ValidateCurve(gpu) != null, "falling GPU fan speed cannot be applied");
                byte[] draft = CurveEditor.PrepareDraft(gpu);
                Assert(draft[11] == 19 && draft[15] == 100, "draft fixes falling speed and final point");
                Assert(gpu[11] == 13 && gpu[15] == 67, "factory buffer not mutated");
                Assert(CurveEditor.ValidateCurve(draft) == null, "valid draft can be applied");
                draft[1] = draft[0]; Assert(CurveEditor.ValidateCurve(draft) != null, "duplicate temperature rejected");
                draft[1] = 54; draft[15] = 99; Assert(CurveEditor.ValidateCurve(draft) != null, "final fan speed must be 100 percent");
                Assert(!AsusDevice.CurveLooksValid(new byte[16]), "empty unsupported curve rejected");
                Assert(!AsusDevice.CurveLooksValid(new byte[] { 255, 255 }), "malformed response rejected");
                byte[] write = AsusDevice.EncodeRequest(0x53564544, AsusDevice.GpuCurve, gpu);
                Assert(write.Length == 28 && BitConverter.ToUInt32(write, 4) == 20, "16-byte fan curve write framing");
                for (int i = 0; i < 16; i++) Assert(write[12 + i] == gpu[i], "curve payload byte " + i);
                Application.EnableVisualStyles();
                using (var editor = new CurveEditor(cpu, gpu, "Performance"))
                using (var bitmap = new Bitmap(editor.Width, editor.Height))
                {
                    editor.Show(); Application.DoEvents();
                    editor.DrawToBitmap(bitmap, new Rectangle(0, 0, editor.Width, editor.Height));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fan-curves.png"));
                    editor.Close();
                }
                var editable = new CustomMode { Id = Guid.NewGuid().ToString("D"), Name = "Everyday", FirmwareMode = 0, CpuLimitPercent = 80, DisableBoost = true };
                using (var editor = new CustomModeEditor(editable))
                using (var bitmap = new Bitmap(editor.Width, editor.Height))
                {
                    editor.Show(); Application.DoEvents();
                    editor.DrawToBitmap(bitmap, new Rectangle(0, 0, editor.Width, editor.Height));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom-mode.png"));
                    foreach (Control control in editor.Controls)
                    {
                        var text = control as TextBox; if (text != null) text.Text = "Quiet work";
                        var percent = control as NumericUpDown; if (percent != null) percent.Value = 60;
                    }
                    foreach (Control control in editor.Controls) { var button = control as Button; if (button != null && button.Text == "Save") { button.PerformClick(); break; } }
                    Assert(editor.Result != null && editor.Result.Id == editable.Id && editor.Result.Name == "Quiet work" && editor.Result.CpuLimitPercent == 60 && editor.Result.DisableBoost, "custom editor saves requested values and preserves identity");
                    Assert(!editor.ApplyRequested && editable.Name == "Everyday" && editable.CpuLimitPercent == 80, "saving mode does not apply it or mutate original");
                }
                assertions += ModeTests.Run();
                if (Array.IndexOf(args, "--live-power-policy") >= 0) assertions += ModeTests.VerifyWindowsPolicy();
                Console.WriteLine("PASS: " + assertions + " assertions. No firmware settings were written.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}

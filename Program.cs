using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Little Armoury")]
[assembly: System.Reflection.AssemblyProduct("Little Armoury")]
[assembly: System.Reflection.AssemblyDescription("Compact ASUS system stats and operating-mode controls")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace TinyAsus
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--diagnostics")
            {
                string output = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostics.txt");
                try
                {
                    using (var sensors = new Sensors())
                    {
                        sensors.Read(true); Thread.Sleep(1100);
                        Snapshot s = sensors.Read(true);
                        var lines = new List<string>();
                        lines.Add("Little Armoury / read-only diagnostics / " + DateTime.Now.ToString("O"));
                        lines.Add("Model: " + sensors.Model); lines.Add("CPU: " + sensors.CpuName);
                        lines.Add("ASUS driver: " + (sensors.Asus.Available ? "connected" : sensors.Asus.Error));
                        lines.Add("CPU usage: " + s.CpuUsage + "; effective MHz: " + s.CpuMhz + "; temperature: " + s.CpuTemp + "; fan RPM: " + s.CpuRpm);
                        lines.Add("GPU: " + s.GpuName + "; usage: " + s.GpuUsage + "; MHz: " + s.GpuMhz + "; memory MHz: " + s.GpuMemoryMhz);
                        lines.Add("GPU temperature: " + s.GpuTemp + "; fan RPM: " + s.GpuRpm + "; watts: " + s.GpuWatts + "; limit: " + s.GpuLimit);
                        lines.Add("RAM GiB: " + s.RamUsed + " / " + s.RamTotal + "; system disk GiB: " + s.DiskUsed + " / " + s.DiskTotal);
                        lines.Add("Firmware mode capability/status (not current mode): " + s.FirmwareMode + "; AC: " + s.OnAc + "; Armoury service running: " + sensors.ArmouryRunning);
                        try
                        {
                            var power = new WindowsPowerApi(); Guid active = power.Active();
                            lines.Add("Windows power plan: " + power.Name(active) + " (" + active + ")");
                            lines.Add("CPU min/max/boost AC: " + power.ReadProcessor(active, CpuPolicy.Minimum, true) + "/" + power.ReadProcessor(active, CpuPolicy.Maximum, true) + "/" + power.ReadProcessor(active, CpuPolicy.Boost, true));
                            lines.Add("CPU min/max/boost DC: " + power.ReadProcessor(active, CpuPolicy.Minimum, false) + "/" + power.ReadProcessor(active, CpuPolicy.Maximum, false) + "/" + power.ReadProcessor(active, CpuPolicy.Boost, false));
                        }
                        catch (Exception ex) { lines.Add("Windows power settings: " + ex.Message); }
                        foreach (uint id in new uint[] { AsusDevice.CpuCurve, AsusDevice.GpuCurve })
                        {
                            try { lines.Add("Factory fan curve " + id.ToString("X") + ": " + BitConverter.ToString(sensors.Asus.ReadRawCurve(id, 0))); }
                            catch (Exception ex) { lines.Add("Fan curve: " + ex.Message); }
                        }
                        File.WriteAllLines(output, lines);
                    }
                }
                catch (Exception ex) { File.WriteAllText(output, ex.ToString()); Environment.ExitCode = 1; }
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\LittleArmoury.Desktop", out created))
            {
                if (!created) { MessageBox.Show("Little Armoury is already running. Look for its icon in the system tray.", "Little Armoury"); return; }
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "Little Armoury", MessageBoxButtons.OK, MessageBoxIcon.Error); };
                Application.Run(new Dashboard(args));
            }
        }
    }
}

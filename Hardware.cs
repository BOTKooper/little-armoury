using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace TinyAsus
{
    // The ASUS System Control Interface driver exposes DSTS (read) / DEVS (write).
    // Device identifiers and curve format are also documented by Linux's asus-wmi driver.
    internal sealed class AsusDevice : IDisposable
    {
        internal const uint Mode = 0x00120075, CpuFan = 0x00110013, GpuFan = 0x00110014;
        internal const uint CpuTemp = 0x00120094, GpuTemp = 0x00120097;
        internal const uint CpuCurve = 0x00110024, GpuCurve = 0x00110025;
        private readonly SafeFileHandle handle;
        internal readonly string Error;
        internal bool Available { get { return handle != null && !handle.IsInvalid; } }

        internal AsusDevice()
        {
            handle = CreateFile(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (!Available) Error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        }

        private byte[] Invoke(uint method, uint device, byte[] payload)
        {
            if (!Available) throw new IOException("ASUS System Control Interface: " + Error);
            byte[] input = EncodeRequest(method, device, payload);
            byte[] output = new byte[16];
            uint returned;
            if (!DeviceIoControl(handle, 0x22240C, input, (uint)input.Length, output, 16, out returned, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (returned < 4) throw new IOException("ASUS driver returned an empty response.");
            return output;
        }

        internal static byte[] EncodeRequest(uint method, uint device, byte[] payload)
        {
            byte[] input = new byte[12 + payload.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(method), 0, input, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)(4 + payload.Length)), 0, input, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(device), 0, input, 8, 4);
            Buffer.BlockCopy(payload, 0, input, 12, payload.Length);
            return input;
        }

        internal int? Read(uint id)
        {
            if (!Available) return null;
            try
            {
                uint raw = BitConverter.ToUInt32(Invoke(0x53545344, id, new byte[4]), 0);
                if (raw == 0xFFFFFFFE || raw == 0xFFFFFFFF || (raw & 0x10000) == 0) return null;
                return (int)(raw & 0xFFFF);
            }
            catch { return null; }
        }

        internal void Write(uint id, int value) { Write(id, BitConverter.GetBytes(value)); }
        internal void Write(uint id, byte[] data)
        {
            int result = BitConverter.ToInt32(Invoke(0x53564544, id, data), 0);
            if (result != 1) throw new IOException("Firmware rejected this setting (response " + result + ").");
        }

        internal byte[] ReadRawCurve(uint id, int mode)
        {
            uint curveMode = mode == 1 ? 2u : mode == 2 ? 1u : 0u;
            return Invoke(0x53545344, id, BitConverter.GetBytes(curveMode));
        }

        internal byte[] ReadCurve(uint id, int mode)
        {
            byte[] curve = ReadRawCurve(id, mode);
            if (!CurveLooksValid(curve)) throw new IOException("This firmware does not expose a valid fan curve.");
            return curve;
        }

        internal static bool CurveLooksValid(byte[] c)
        {
            if (c == null || c.Length != 16) return false;
            for (int i = 0; i < 8; i++)
                if (c[i] < 10 || c[i] > 110 || c[i + 8] > 100 || (i > 0 && c[i] < c[i - 1])) return false;
            return c[7] > c[0] && c[15] > 0;
        }

        public void Dispose() { if (handle != null) handle.Dispose(); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    }

    internal sealed class Snapshot
    {
        internal double? CpuUsage, CpuMhz, CpuTemp, GpuUsage, GpuMhz, GpuMemoryMhz, GpuTemp, GpuWatts, GpuLimit;
        internal double RamUsed, RamTotal, DiskUsed, DiskTotal;
        internal int? CpuRpm, GpuRpm, FirmwareMode;
        internal bool OnAc;
        internal string GpuName;
    }

    internal sealed class Sensors : IDisposable
    {
        internal readonly AsusDevice Asus = new AsusDevice();
        private readonly Nvidia gpu = new Nvidia();
        private ulong previousIdle, previousKernel, previousUser;
        private IntPtr query, clockCounter;
        private uint baseMhz;
        private readonly DriveInfo disk = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
        internal string Model = "ASUS laptop", CpuName = "Processor";
        internal int MemorySpeed;
        internal bool ArmouryRunning;

        internal Sensors()
        {
            GetSystemTimes(out previousIdle, out previousKernel, out previousUser);
            try
            {
                using (var search = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem"))
                using (var items = search.Get()) foreach (ManagementObject x in items) { using (x) Model = Convert.ToString(x["Model"]).Replace("_G713RM", ""); break; }
                using (var search = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_Processor"))
                using (var items = search.Get()) foreach (ManagementObject x in items) { using (x) { CpuName = Convert.ToString(x["Name"]).Trim(); baseMhz = Convert.ToUInt32(x["MaxClockSpeed"]); } break; }
                using (var search = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                using (var items = search.Get()) foreach (ManagementObject x in items) { using (x) MemorySpeed = Convert.ToInt32(x["ConfiguredClockSpeed"]); break; }
            }
            catch { }
            try
            {
                foreach (var p in Process.GetProcessesByName("ArmouryCrate.Service")) { ArmouryRunning = true; p.Dispose(); }
                if (!ArmouryRunning)
                    using (var service = new System.ServiceProcess.ServiceController("ArmouryCrateService"))
                        ArmouryRunning = service.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            }
            catch { }
            if (PdhOpenQuery(null, IntPtr.Zero, out query) == 0 &&
                PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out clockCounter) == 0)
                PdhCollectQueryData(query);
        }

        internal Snapshot Read(bool pollGpu)
        {
            var s = new Snapshot();
            ulong idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong total = kernel - previousKernel + user - previousUser;
                if (total > 0) s.CpuUsage = Math.Max(0, Math.Min(100, 100.0 * (1.0 - (double)(idle - previousIdle) / total)));
                previousIdle = idle; previousKernel = kernel; previousUser = user;
            }
            CounterValue value;
            uint type;
            if (clockCounter != IntPtr.Zero && PdhCollectQueryData(query) == 0 &&
                PdhGetFormattedCounterValue(clockCounter, 0x200, out type, out value) == 0 && value.Status <= 1 && baseMhz > 0)
                s.CpuMhz = baseMhz * value.Value / 100.0;
            var mem = new MemoryStatus(); mem.Length = (uint)Marshal.SizeOf(mem);
            if (GlobalMemoryStatusEx(ref mem)) { s.RamTotal = mem.TotalPhysical / 1073741824.0; s.RamUsed = (mem.TotalPhysical - mem.AvailablePhysical) / 1073741824.0; }
            try { s.DiskTotal = disk.TotalSize / 1073741824.0; s.DiskUsed = (disk.TotalSize - disk.TotalFreeSpace) / 1073741824.0; } catch { }
            s.OnAc = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            s.CpuTemp = Temperature(Asus.Read(AsusDevice.CpuTemp));
            s.GpuTemp = Temperature(Asus.Read(AsusDevice.GpuTemp));
            s.CpuRpm = Fan(Asus.Read(AsusDevice.CpuFan));
            s.GpuRpm = Fan(Asus.Read(AsusDevice.GpuFan));
            s.FirmwareMode = Asus.Read(AsusDevice.Mode);
            if (pollGpu) gpu.Read(s);
            else { gpu.Pause(); s.GpuName = "GPU polling paused"; }
            return s;
        }

        private static double? Temperature(int? v) { return v.HasValue && v.Value > 0 && v.Value < 125 ? (double?)v.Value : null; }
        private static int? Fan(int? v) { return v.HasValue && v.Value <= 120 ? (int?)(v.Value * 100) : null; }
        public void Dispose() { gpu.Dispose(); Asus.Dispose(); if (query != IntPtr.Zero) PdhCloseQuery(query); }

        [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
        { internal uint Length, Load; internal ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended; }
        [StructLayout(LayoutKind.Sequential)] private struct CounterValue { internal uint Status; internal double Value; }
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    }

    internal sealed class Nvidia : IDisposable
    {
        private IntPtr library, device;
        private bool initialized, attempted;
        private string name = "NVIDIA GPU unavailable";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Shutdown();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDevice(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetName(IntPtr device, StringBuilder name, uint length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUInt(IntPtr device, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetClock(IntPtr device, uint kind, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUtilization(IntPtr device, out Utilization value);
        [StructLayout(LayoutKind.Sequential)] private struct Utilization { internal uint Gpu, Memory; }
        private T Function<T>(string symbol) where T : class
        {
            IntPtr p = GetProcAddress(library, symbol);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        private GetUInt power, limit;
        private GetClock clock, temperature;
        private GetUtilization utilization;
        private Shutdown shutdown;

        private void Connect()
        {
            if (attempted) return;
            attempted = true;
            try
            {
                string system = Environment.SystemDirectory;
                string path = Path.Combine(system, "nvml.dll");
                if (!File.Exists(path))
                {
                    using (var video = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video"))
                    {
                        if (video != null) foreach (string k in video.GetSubKeyNames())
                            using (var adapter = video.OpenSubKey(k + @"\0000"))
                            {
                                string candidate = adapter == null ? null : adapter.GetValue("NVML_PATH") as string;
                                if (!String.IsNullOrEmpty(candidate) && File.Exists(candidate)) { path = candidate; break; }
                            }
                    }
                }
                if (!File.Exists(path))
                {
                    string root = Path.Combine(system, @"DriverStore\FileRepository");
                    foreach (string dir in Directory.GetDirectories(root, "nv*", SearchOption.TopDirectoryOnly).OrderByDescending(Directory.GetLastWriteTimeUtc))
                        if (File.Exists(Path.Combine(dir, "nvml.dll"))) { path = Path.Combine(dir, "nvml.dll"); break; }
                }
                if (!File.Exists(path)) return;
                library = LoadLibraryEx(path, IntPtr.Zero, 0x1100);
                if (library == IntPtr.Zero) return;
                Init init = Function<Init>("nvmlInit_v2");
                GetDevice getDevice = Function<GetDevice>("nvmlDeviceGetHandleByIndex_v2");
                shutdown = Function<Shutdown>("nvmlShutdown");
                if (init == null || getDevice == null || init() != 0) return;
                initialized = true;
                if (getDevice(0, out device) != 0) { device = IntPtr.Zero; return; }
                GetName getName = Function<GetName>("nvmlDeviceGetName");
                var text = new StringBuilder(128);
                if (getName != null && getName(device, text, 128) == 0) name = text.ToString().Replace("NVIDIA GeForce ", "");
                power = Function<GetUInt>("nvmlDeviceGetPowerUsage");
                limit = Function<GetUInt>("nvmlDeviceGetEnforcedPowerLimit");
                clock = Function<GetClock>("nvmlDeviceGetClockInfo");
                temperature = Function<GetClock>("nvmlDeviceGetTemperature");
                utilization = Function<GetUtilization>("nvmlDeviceGetUtilizationRates");
            }
            catch { name = "NVIDIA telemetry unavailable"; }
        }

        internal void Read(Snapshot s)
        {
            Connect(); s.GpuName = name;
            if (device == IntPtr.Zero) return;
            uint v; Utilization u;
            if (utilization != null && utilization(device, out u) == 0) s.GpuUsage = u.Gpu;
            if (clock != null && clock(device, 0, out v) == 0) s.GpuMhz = v;
            if (clock != null && clock(device, 2, out v) == 0) s.GpuMemoryMhz = v;
            if (temperature != null && temperature(device, 0, out v) == 0 && v > 0 && v < 125) s.GpuTemp = v;
            if (power != null && power(device, out v) == 0) s.GpuWatts = v / 1000.0;
            if (limit != null && limit(device, out v) == 0) s.GpuLimit = v / 1000.0;
        }

        internal void Pause()
        {
            if (initialized && shutdown != null) shutdown();
            initialized = false; device = IntPtr.Zero;
            if (library != IntPtr.Zero) FreeLibrary(library);
            library = IntPtr.Zero; attempted = false;
        }
        public void Dispose() { Pause(); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace TinyAsus
{
    public sealed class CustomMode
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int FirmwareMode { get; set; }
        public int CpuLimitPercent { get; set; }
        public bool DisableBoost { get; set; }
        public CustomMode Clone() { return (CustomMode)MemberwiseClone(); }
        public void Validate()
        {
            Guid parsed;
            if (!Guid.TryParse(Id, out parsed) || parsed == Guid.Empty) throw new ArgumentException("Mode ID is invalid.");
            if (String.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 40) throw new ArgumentException("Enter a mode name of 1–40 characters.");
            if (FirmwareMode < 0 || FirmwareMode > 2) throw new ArgumentException("Choose Silent, Performance, or Turbo as the base.");
            if (CpuLimitPercent < 20 || CpuLimitPercent > 100) throw new ArgumentException("Maximum CPU performance must be 20–100%.");
            Name = Name.Trim();
        }
    }

    internal sealed class ProfileStore
    {
        private readonly string path;
        internal List<CustomMode> Modes { get; private set; }
        private ProfileStore(string file) { path = file; Modes = new List<CustomMode>(); }
        internal static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleArmoury"); } }
        internal static ProfileStore Load() { return Load(Path.Combine(DataDirectory, "modes.xml")); }
        internal static ProfileStore Load(string file)
        {
            var store = new ProfileStore(file);
            if (!File.Exists(file))
            {
                store.Modes.Add(new CustomMode { Id = Guid.NewGuid().ToString("D"), Name = "Everyday", FirmwareMode = 0, CpuLimitPercent = 80, DisableBoost = true });
                store.Save(); return store;
            }
            XmlDocument doc = ReadXml(file);
            if (doc.DocumentElement == null || doc.DocumentElement.Name != "modes" || doc.DocumentElement.GetAttribute("version") != "1")
                throw new InvalidDataException("Saved custom modes use an unsupported format.");
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                var element = node as XmlElement;
                if (element == null || element.Name != "mode") continue;
                var mode = new CustomMode { Id = element.GetAttribute("id"), Name = element.GetAttribute("name"), FirmwareMode = Int32.Parse(element.GetAttribute("firmware")), CpuLimitPercent = Int32.Parse(element.GetAttribute("cpu")), DisableBoost = Boolean.Parse(element.GetAttribute("disableBoost")) };
                mode.Validate(); store.Modes.Add(mode);
            }
            ValidateList(store.Modes);
            return store;
        }
        internal void Save()
        {
            ValidateList(Modes);
            var doc = new XmlDocument(); var root = doc.CreateElement("modes"); root.SetAttribute("version", "1"); doc.AppendChild(root);
            foreach (var mode in Modes)
            {
                var element = doc.CreateElement("mode");
                element.SetAttribute("id", mode.Id); element.SetAttribute("name", mode.Name);
                element.SetAttribute("firmware", mode.FirmwareMode.ToString(System.Globalization.CultureInfo.InvariantCulture));
                element.SetAttribute("cpu", mode.CpuLimitPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));
                element.SetAttribute("disableBoost", mode.DisableBoost.ToString()); root.AppendChild(element);
            }
            WriteXml(path, doc);
        }
        private static void ValidateList(List<CustomMode> modes)
        {
            if (modes.Count > 32) throw new ArgumentException("You can save up to 32 custom modes.");
            var ids = new HashSet<Guid>();
            foreach (var mode in modes) { mode.Validate(); if (!ids.Add(Guid.Parse(mode.Id))) throw new ArgumentException("Custom mode IDs must be unique."); }
        }
        internal static XmlDocument ReadXml(string file)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024 })) doc.Load(reader);
            return doc;
        }
        internal static void WriteXml(string file, XmlDocument doc)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) doc.Save(writer);
                if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal interface IPowerApi
    {
        Guid Active();
        Guid Duplicate(Guid source);
        string Name(Guid scheme);
        void Name(Guid scheme, string name);
        bool Exists(Guid scheme);
        void Processor(Guid scheme, Guid setting, uint ac, uint dc);
        void Activate(Guid scheme);
        void Delete(Guid scheme);
    }

    internal sealed class CpuPolicy : IDisposable
    {
        internal static readonly Guid ProcessorGroup = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        internal static readonly Guid Maximum = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");
        internal static readonly Guid Minimum = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
        internal static readonly Guid Boost = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        private const string Prefix = "Little Armoury / ";
        private readonly IPowerApi api;
        private readonly string journal;
        private readonly object gate = new object();
        private Guid original, owned, retired;
        internal CpuPolicy() : this(new WindowsPowerApi(), Path.Combine(ProfileStore.DataDirectory, "power-session.xml")) { }
        internal CpuPolicy(IPowerApi powerApi, string journalPath)
        {
            api = powerApi; journal = journalPath;
            Recover();
        }
        internal bool IsActive { get { lock (gate) { return owned != Guid.Empty && api.Active() == owned; } } }
        internal void Apply(CustomMode mode)
        {
            mode = mode.Clone(); mode.Validate();
            lock (gate)
            {
                Guid previous = api.Active();
                Guid basis = previous == owned ? original : previous;
                Guid previousOwned = owned, previousOriginal = original;
                Guid candidate = api.Duplicate(basis);
                bool journalWritten = false;
                try
                {
                    api.Name(candidate, Prefix + mode.Name);
                    // A Performance scheme may have its minimum pinned at 100%. Let custom modes idle.
                    api.Processor(candidate, Minimum, 5, 5);
                    api.Processor(candidate, Maximum, (uint)mode.CpuLimitPercent, (uint)mode.CpuLimitPercent);
                    api.Processor(candidate, Boost, mode.DisableBoost ? 0u : 2u, mode.DisableBoost ? 0u : 2u);
                    WriteSession(basis, candidate, previousOwned); journalWritten = true;
                    api.Activate(candidate);
                    if (api.Active() != candidate) throw new IOException("Windows did not activate the custom power plan.");
                    original = basis; owned = candidate; retired = previousOwned;
                }
                catch (Exception error)
                {
                    try
                    {
                        if (api.Active() == candidate) api.Activate(previous);
                        if (api.Active() == candidate) throw new IOException("The custom plan is still active.");
                        api.Delete(candidate);
                        if (journalWritten)
                        {
                            if (previousOwned != Guid.Empty) WriteSession(previousOriginal, previousOwned);
                            else DeleteJournal();
                        }
                    }
                    catch (Exception restoreError)
                    {
                        if (journalWritten) { original = basis; owned = candidate; }
                        throw new IOException("Custom mode failed: " + error.Message + " Restore failed: " + restoreError.Message, restoreError);
                    }
                    throw;
                }
                // The active plan has committed. An old inactive session plan can now be discarded.
                if (previousOwned != Guid.Empty && previousOwned != candidate)
                {
                    try { if (api.Exists(previousOwned) && api.Name(previousOwned).StartsWith(Prefix, StringComparison.Ordinal)) api.Delete(previousOwned); retired = Guid.Empty; }
                    catch { /* An inactive old plan is harmless; never undo the newly applied policy. */ }
                }
            }
        }
        internal void Restore()
        {
            lock (gate)
            {
                if (owned == Guid.Empty) return;
                if (api.Active() == owned)
                {
                    if (!api.Exists(original)) throw new IOException("The previous Windows power plan was removed. Choose a power plan in Windows settings.");
                    api.Activate(original);
                    if (api.Active() != original) throw new IOException("Windows did not restore the previous power plan.");
                }
                if (api.Exists(owned) && api.Name(owned).StartsWith(Prefix, StringComparison.Ordinal)) api.Delete(owned);
                if (retired != Guid.Empty && api.Exists(retired) && api.Name(retired).StartsWith(Prefix, StringComparison.Ordinal) && api.Active() != retired) api.Delete(retired);
                DeleteJournal(); owned = original = retired = Guid.Empty;
            }
        }
        private void Recover()
        {
            if (!File.Exists(journal)) return;
            var doc = ProfileStore.ReadXml(journal); var root = doc.DocumentElement;
            Guid savedOriginal, savedOwned;
            if (root == null || root.Name != "power-session" || root.GetAttribute("version") != "1" ||
                !Guid.TryParse(root.GetAttribute("original"), out savedOriginal) || !Guid.TryParse(root.GetAttribute("owned"), out savedOwned) ||
                savedOriginal == Guid.Empty || savedOwned == Guid.Empty || savedOriginal == savedOwned)
                throw new InvalidDataException("The saved power-session recovery record is invalid.");
            Guid prior = Guid.Empty;
            if (root.HasAttribute("prior") && (!Guid.TryParse(root.GetAttribute("prior"), out prior) || prior == savedOriginal || prior == savedOwned))
                throw new InvalidDataException("The previous temporary power plan recovery record is invalid.");
            bool hasOwned = api.Exists(savedOwned), hasPrior = prior != Guid.Empty && api.Exists(prior);
            if ((hasOwned && !api.Name(savedOwned).StartsWith(Prefix, StringComparison.Ordinal)) ||
                (hasPrior && !api.Name(prior).StartsWith(Prefix, StringComparison.Ordinal)))
                throw new InvalidDataException("The saved power plan is not owned by Little Armoury.");
            Guid active = api.Active();
            if (active == savedOwned || (prior != Guid.Empty && active == prior))
            {
                if (!api.Exists(savedOriginal)) throw new IOException("The previous Windows power plan was removed. Choose a power plan in Windows settings.");
                api.Activate(savedOriginal);
                if (api.Active() != savedOriginal) throw new IOException("Windows did not restore the previous power plan.");
            }
            if (hasOwned) api.Delete(savedOwned);
            if (hasPrior) api.Delete(prior);
            DeleteJournal();
        }
        private void WriteSession(Guid previous, Guid session) { WriteSession(previous, session, Guid.Empty); }
        private void WriteSession(Guid previous, Guid session, Guid prior)
        {
            var doc = new XmlDocument(); var root = doc.CreateElement("power-session");
            root.SetAttribute("version", "1"); root.SetAttribute("original", previous.ToString("D")); root.SetAttribute("owned", session.ToString("D"));
            if (prior != Guid.Empty) root.SetAttribute("prior", prior.ToString("D"));
            doc.AppendChild(root); ProfileStore.WriteXml(journal, doc);
        }
        private void DeleteJournal() { if (File.Exists(journal)) File.Delete(journal); }
        public void Dispose() { Restore(); }
    }

    internal sealed class WindowsPowerApi : IPowerApi
    {
        private static void Check(uint code, string operation) { if (code != 0) throw new Win32Exception((int)code, operation + ": " + new Win32Exception((int)code).Message); }
        public Guid Active()
        {
            IntPtr result; Check(PowerGetActiveScheme(IntPtr.Zero, out result), "Read active power plan");
            try { return (Guid)Marshal.PtrToStructure(result, typeof(Guid)); } finally { LocalFree(result); }
        }
        public Guid Duplicate(Guid source)
        {
            IntPtr result; Check(PowerDuplicateScheme(IntPtr.Zero, ref source, out result), "Create custom power plan");
            try { return (Guid)Marshal.PtrToStructure(result, typeof(Guid)); } finally { LocalFree(result); }
        }
        public string Name(Guid scheme)
        {
            uint size = 0;
            uint code = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (code != 234) Check(code, "Read power plan name");
            if (size < 2 || size > 64 * 1024) throw new IOException("Windows returned an invalid power plan name.");
            byte[] bytes = new byte[size]; Check(PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, bytes, ref size), "Read power plan name");
            return Encoding.Unicode.GetString(bytes, 0, (int)size).TrimEnd('\0');
        }
        public void Name(Guid scheme, string name)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(name + "\0");
            Check(PowerWriteFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, bytes, (uint)bytes.Length), "Name custom power plan");
        }
        public bool Exists(Guid scheme)
        {
            try { Name(scheme); return true; }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3) return false; throw; }
        }
        public void Processor(Guid scheme, Guid setting, uint ac, uint dc)
        {
            Guid group = CpuPolicy.ProcessorGroup;
            Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, ac), "Set plugged-in CPU policy");
            Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, dc), "Set battery CPU policy");
        }
        internal uint ReadProcessor(Guid scheme, Guid setting, bool ac)
        {
            Guid group = CpuPolicy.ProcessorGroup; uint result;
            uint code = ac ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, out result) : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, out result);
            Check(code, "Read CPU policy"); return result;
        }
        public void Activate(Guid scheme) { Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme), "Activate power plan"); }
        public void Delete(Guid scheme) { Check(PowerDeleteScheme(IntPtr.Zero, ref scheme), "Remove temporary power plan"); }
        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[] buffer, uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    }
}

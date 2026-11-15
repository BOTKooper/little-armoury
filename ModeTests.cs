using System;
using System.Collections.Generic;
using System.IO;

namespace TinyAsus
{
    internal static class ModeTests
    {
        private static int assertions;
        private static void Assert(bool result, string name) { if (!result) throw new Exception("FAILED: " + name); assertions++; }
        private static void Throws(Action action, string name)
        { bool thrown = false; try { action(); } catch { thrown = true; } Assert(thrown, name); }
        internal static int Run()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mode-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string profiles = Path.Combine(root, "modes.xml");
            ProfileStore store = ProfileStore.Load(profiles);
            Assert(store.Modes.Count == 1 && store.Modes[0].DisableBoost && store.Modes[0].CpuLimitPercent == 80, "Everyday starts with conservative CPU settings");
            string id = store.Modes[0].Id;
            store.Modes[0].Name = "Cool & quiet <work>"; store.Save();
            ProfileStore loaded = ProfileStore.Load(profiles);
            Assert(loaded.Modes[0].Id == id && loaded.Modes[0].Name == "Cool & quiet <work>", "mode IDs and escaped names survive persistence");
            CustomMode mode = loaded.Modes[0]; CustomMode copy = mode.Clone(); copy.Name = "Other";
            Assert(mode.Name != copy.Name, "editor clone does not mutate saved mode");
            copy.CpuLimitPercent = 0; Throws(copy.Validate, "invalid CPU cap rejected");
            store.Modes.Add(store.Modes[0].Clone()); Throws(store.Save, "duplicate IDs rejected before save");
            Assert(ProfileStore.Load(profiles).Modes.Count == 1, "failed save preserves previous file");
            File.WriteAllText(Path.Combine(root, "external.xml"), "<!DOCTYPE modes [<!ENTITY external SYSTEM 'file:///C:/Windows/win.ini'>]><modes version='1'>&external;</modes>");
            Throws(delegate { ProfileStore.Load(Path.Combine(root, "external.xml")); }, "external XML entities rejected");

            string journal = Path.Combine(root, "session.xml");
            var power = new FakePowerApi(); Guid original = power.Active();
            using (var policy = new CpuPolicy(power, journal))
            {
                policy.Apply(mode); Guid custom = power.Active();
                Assert(custom != original && power.Plans.Count == 2, "custom mode duplicates original plan");
                Assert(policy.IsActive, "policy identifies its active custom plan");
                Assert(power.Plans[custom].Values[CpuPolicy.Maximum] == 80 && power.Plans[custom].Values[CpuPolicy.Minimum] == 5 && power.Plans[custom].Values[CpuPolicy.Boost] == 0, "custom mode reduces max performance and minimum state and disables boost");
                Assert(power.Plans[original].Values[CpuPolicy.Minimum] == 100 && power.Plans[original].Values[CpuPolicy.Maximum] == 100, "original plan remains unchanged");
                Assert(File.Exists(journal), "recovery record exists while custom mode active");
                policy.Restore();
                Assert(power.Active() == original && power.Plans.Count == 1 && !File.Exists(journal), "preset restore reactivates original and removes owned plan and journal");
            }
            power = new FakePowerApi(); original = power.Active();
            using (var policy = new CpuPolicy(power, journal))
            {
                power.FailWrite = true; Throws(delegate { policy.Apply(mode); }, "failed power write reported");
                Assert(power.Active() == original && power.Plans.Count == 1 && !File.Exists(journal), "failed power write leaves user plan active and cleans candidate");
                power.FailWrite = false; power.FailActivationAfterSwitch = true;
                Throws(delegate { policy.Apply(mode); }, "partial activation failure reported");
                Assert(power.Active() == original && power.Plans.Count == 1 && !File.Exists(journal), "partial activation rolls back power plan and journal");
                policy.Apply(mode); Guid first = power.Active();
                copy = mode.Clone(); copy.CpuLimitPercent = 60; copy.DisableBoost = false; policy.Apply(copy);
                Assert(power.Active() != first && !power.Exists(first) && power.Plans.Count == 2, "switching custom modes removes old temporary plan");
                Assert(power.Plans[power.Active()].Values[CpuPolicy.Maximum] == 60 && power.Plans[power.Active()].Values[CpuPolicy.Boost] == 2, "replacement mode uses requested cap and boost state");
                power.FailWrite = true; Guid current = power.Active();
                Throws(delegate { policy.Apply(mode); }, "failed replacement reported");
                Assert(power.Active() == current && power.Plans.Count == 2 && File.Exists(journal), "failed replacement preserves previously active custom mode"); power.FailWrite = false;
            }
            Assert(power.Active() == original && power.Plans.Count == 1, "disposing a policy restores original plan");
            power = new FakePowerApi(); original = power.Active();
            var interrupted = new CpuPolicy(power, journal); interrupted.Apply(mode);
            using (var recovered = new CpuPolicy(power, journal))
                Assert(power.Active() == original && power.Plans.Count == 1 && !File.Exists(journal), "interrupted session recovers original plan on next launch");
            power = new FakePowerApi(); original = power.Active();
            interrupted = new CpuPolicy(power, journal); interrupted.Apply(mode); Guid prior = power.Active();
            Guid pending = power.Duplicate(original); power.Name(pending, "Little Armoury / pending replacement");
            File.WriteAllText(journal, "<power-session version='1' original='" + original + "' owned='" + pending + "' prior='" + prior + "' />");
            using (var recovered = new CpuPolicy(power, journal))
                Assert(power.Active() == original && power.Plans.Count == 1 && !File.Exists(journal), "interrupted replacement restores original even before new plan activates");
            power = new FakePowerApi(); original = power.Active();
            using (var policy = new CpuPolicy(power, journal))
            {
                policy.Apply(mode); Guid external = power.AddPlan("Selected in Windows settings"); power.Activate(external);
                Assert(!policy.IsActive, "policy detects external power plan selection"); policy.Restore();
                Assert(power.Active() == external && power.Plans.Count == 2, "restore respects power plan selected externally");
            }
            power = new FakePowerApi(); original = power.Active();
            Guid foreign = power.AddPlan("User-owned plan");
            File.WriteAllText(journal, "<power-session version='1' original='" + original + "' owned='" + foreign + "' />");
            Throws(delegate { new CpuPolicy(power, journal); }, "recovery refuses a foreign power plan");
            Assert(power.Active() == original && power.Exists(foreign), "foreign power plan remains untouched");
            return assertions;
        }

        internal static int VerifyWindowsPolicy()
        {
            int before = assertions;
            var api = new WindowsPowerApi(); Guid original = api.Active();
            uint minAc = api.ReadProcessor(original, CpuPolicy.Minimum, true), minDc = api.ReadProcessor(original, CpuPolicy.Minimum, false);
            uint maxAc = api.ReadProcessor(original, CpuPolicy.Maximum, true), maxDc = api.ReadProcessor(original, CpuPolicy.Maximum, false);
            uint boostAc = api.ReadProcessor(original, CpuPolicy.Boost, true), boostDc = api.ReadProcessor(original, CpuPolicy.Boost, false);
            string journal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "live-power-session.xml");
            Guid temporary;
            using (var policy = new CpuPolicy(api, journal))
            {
                policy.Apply(new CustomMode { Id = Guid.NewGuid().ToString("D"), Name = "Policy verification", FirmwareMode = 0, CpuLimitPercent = 80, DisableBoost = true });
                temporary = api.Active(); Assert(temporary != original, "Windows activates temporary custom plan");
                Assert(api.ReadProcessor(temporary, CpuPolicy.Minimum, true) == 5 && api.ReadProcessor(temporary, CpuPolicy.Minimum, false) == 5, "real Windows AC/DC minimum CPU readback");
                Assert(api.ReadProcessor(temporary, CpuPolicy.Maximum, true) == 80 && api.ReadProcessor(temporary, CpuPolicy.Maximum, false) == 80, "real Windows AC/DC maximum CPU readback");
                Assert(api.ReadProcessor(temporary, CpuPolicy.Boost, true) == 0 && api.ReadProcessor(temporary, CpuPolicy.Boost, false) == 0, "real Windows AC/DC boost disabled readback");
                policy.Restore();
            }
            Assert(api.Active() == original, "real Windows original active plan restored");
            Assert(!api.Exists(temporary) && !File.Exists(journal), "real Windows temporary plan and recovery record removed");
            Assert(api.ReadProcessor(original, CpuPolicy.Minimum, true) == minAc && api.ReadProcessor(original, CpuPolicy.Minimum, false) == minDc, "real Windows original minimum CPU settings unchanged");
            Assert(api.ReadProcessor(original, CpuPolicy.Maximum, true) == maxAc && api.ReadProcessor(original, CpuPolicy.Maximum, false) == maxDc, "real Windows original maximum CPU settings unchanged");
            Assert(api.ReadProcessor(original, CpuPolicy.Boost, true) == boostAc && api.ReadProcessor(original, CpuPolicy.Boost, false) == boostDc, "real Windows original boost settings unchanged");
            Console.WriteLine("Windows policy apply, AC/DC readback, and original-plan restoration verified.");
            return assertions - before;
        }

        private sealed class Plan
        {
            internal string Name;
            internal Dictionary<Guid, uint> Values = new Dictionary<Guid, uint>();
        }
        private sealed class FakePowerApi : IPowerApi
        {
            internal readonly Dictionary<Guid, Plan> Plans = new Dictionary<Guid, Plan>();
            private Guid active;
            internal bool FailWrite, FailActivationAfterSwitch;
            internal FakePowerApi()
            {
                active = AddPlan("User Performance");
                Plans[active].Values[CpuPolicy.Minimum] = 100;
                Plans[active].Values[CpuPolicy.Maximum] = 100;
                Plans[active].Values[CpuPolicy.Boost] = 2;
            }
            internal Guid AddPlan(string name) { Guid id = Guid.NewGuid(); Plans.Add(id, new Plan { Name = name }); return id; }
            public Guid Active() { return active; }
            public Guid Duplicate(Guid source)
            { Guid id = AddPlan(Plans[source].Name); foreach (var p in Plans[source].Values) Plans[id].Values[p.Key] = p.Value; return id; }
            public string Name(Guid id) { return Plans[id].Name; }
            public void Name(Guid id, string name) { Plans[id].Name = name; }
            public bool Exists(Guid id) { return Plans.ContainsKey(id); }
            public void Processor(Guid id, Guid setting, uint ac, uint dc)
            { if (FailWrite) throw new IOException("Simulated write failure"); if (ac != dc) throw new Exception("Expected both power sources"); Plans[id].Values[setting] = ac; }
            public void Activate(Guid id)
            { if (!Plans.ContainsKey(id)) throw new Exception("No plan"); active = id; if (FailActivationAfterSwitch) { FailActivationAfterSwitch = false; throw new IOException("Simulated activation failure"); } }
            public void Delete(Guid id) { if (active == id) throw new Exception("Cannot delete active plan"); Plans.Remove(id); }
        }
    }
}

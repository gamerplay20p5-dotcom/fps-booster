using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace F26Boost
{
    internal static class SelfTests
    {
        private static int assertions;
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("TEST FAILED: " + label);
            assertions++;
        }
        private static void Reject(Action action, string label)
        {
            bool threw = false;
            try { action(); } catch { threw = true; }
            Check(threw, label);
        }
        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "GameBoostTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); App.TestRoot = root;
            try {
                assertions = 0;
                foreach (int ram in new[] { 4, 8, 16, 32, 64 })
                foreach (bool dedicated in new[] { false, true })
                foreach (bool laptop in new[] { false, true })
                foreach (bool mods in new[] { false, true }) {
                    var h = new Hardware { TotalRam = (ulong)ram * 1073741824, DedicatedGpu = dedicated, Laptop = laptop, Threads = 12 };
                    var c = Calibration.For(h, true, mods);
                    Check(c.HeapMb <= ram * 512, "heap <= half RAM");
                    Check(c.HeapMb <= (mods ? 12288 : 8192), "heap upper bound");
                    Check(c.HeapMb == 0 || c.HeapMb <= (ram - (dedicated ? 4 : 5)) * 1024, "OS/native reserve");
                    Check(c.MinCpu < 100 && c.RecoverMemory > c.LowMemory, "CPU and hysteresis");
                    if (laptop) Check(c.MinCpu == 5 && c.BoostMode == 1, "laptop heat policy");
                }
                var low = Calibration.For(new Hardware { TotalRam = 8UL * 1073741824, Threads = 4 }, false, false);
                Check(low.Fifa.Largura == 1280 && low.Fifa.MaxFps == 30, "low-end FIFA");
                var high = Calibration.For(new Hardware { TotalRam = 32UL * 1073741824, Threads = 16, DedicatedGpu = true }, false, false);
                Check(high.Fifa.Largura == 1920 && high.Fifa.MaxFps == 60, "dedicated FIFA");
                Check(Hardware.LooksDedicated("NVIDIA GeForce RTX 4060") && Hardware.LooksDedicated("AMD Radeon RX 6600"), "dedicated GPUs");
                Check(!Hardware.LooksDedicated("Intel Arc 140V") && !Hardware.LooksDedicated("AMD Radeon 780M") && !Hardware.LooksDedicated("Intel UHD Graphics"), "integrated GPUs");

                string json = "{\r\n  \"vmArgs\": [\"-Xmx3g\", \"-Xms8g\", \"-XX:+UseZGC\"],\r\n  \"classpath\": [\"a.jar\"], \"extra\": true\r\n}";
                string patched = GameZomboid.PatchHeap(json, 4096);
                Check(patched == json.Replace("-Xmx3g", "-Xmx4096m").Replace("-Xms8g", "-Xms1024m"), "preserve JSON except heap tokens");
                Check(GameZomboid.PatchHeap(patched, 4096) == patched, "heap idempotent");
                Check(GameZomboid.PatchHeap("{\"vmArgs\":[\"-Xmx3072m\"]}", 8192).Contains("-Xmx8192m"), "no Xms required");
                Reject(() => GameZomboid.PatchHeap("{bad", 4096), "malformed JSON");
                Reject(() => GameZomboid.PatchHeap("{\"vmArgs\":[\"-Xmx3g\",\"-Xmx4g\"]}", 4096), "duplicate Xmx");
                Reject(() => GameZomboid.PatchHeap("{\"vmArgs\":[]}", 4096), "missing Xmx");
                Reject(() => GameZomboid.PatchHeap("{\"vmArgs\":[\"-Xmx3g\"],\"other\":\"-Xmx3g\"}", 4096), "ambiguous token");
                Reject(() => GameZomboid.PatchHeap(json, 99999), "invalid heap size");
                Reject(() => GameZomboid.PatchHeap("{\"vmArgs\":[\"-Xmx3g\"],\"windows\":{\"vmArgs\":[\"-Xmx2g\"]}}", 4096), "nested heap overrides");
                string modded = "{\"vmArgs\":[\"-Xmx6144m\",\"-javaagent:LightingStateGuard.jar\"],\"windows\":{\"10.0\":{\"vmArgs\":[\"-XX:+UseZGC\"]}}}";
                Check(GameZomboid.PatchHeap(modded, 7936) == modded.Replace("-Xmx6144m", "-Xmx7936m"), "mod agent and OS collector preserved");

                string ini = "; comment\r\ncloth_quality = 3 ; custom\r\nUNKNOWN = keep\r\n";
                var values = new Dictionary<string, string> { { "CLOTH_QUALITY", "0" }, { "MAX_FRAME_RATE", "30" } };
                string merged = GameFifa.MergeAssignments(ini, values, true);
                Check(merged.Contains("cloth_quality = 0 ; custom\r\n") && merged.Contains("UNKNOWN = keep\r\n"), "INI case/comments/unknown keys");
                Check(GameFifa.MergeAssignments(merged, values, true) == merged, "INI idempotent");
                Check(GameFifa.MergeAssignments("ClothQuality = 3 -- note\n", new Dictionary<string, string> { { "ClothQuality", "0" } }, false) == "ClothQuality = 0 -- note\n", "Lua comment retained");

                var gate = new PressureGate(1000, 1500);
                var m = new Native.MEMORYSTATUSEX { ullAvailPhys = 500, ullTotalPageFile = 10000, ullAvailPageFile = 5000 };
                DateTime t = new DateTime(2026, 1, 1);
                Check(!gate.Observe(m, t) && !gate.Observe(m, t.AddSeconds(5)) && gate.Observe(m, t.AddSeconds(10)), "sustained pressure");
                Check(!gate.Observe(m, t.AddSeconds(20)), "cooldown");
                Check(gate.Observe(m, t.AddSeconds(190)), "pressure after cooldown");
                m.ullAvailPhys = 2000;
                Check(!gate.Observe(m, t.AddSeconds(400)), "recovery");
                m.ullAvailPhys = 500;
                Check(!gate.Observe(m, t.AddSeconds(405)), "reset pressure streak");
                var commitGate = new PressureGate(1000, 1500);
                m.ullAvailPhys = 3000; m.ullAvailPageFile = 500;
                commitGate.Observe(m, t); commitGate.Observe(m, t.AddSeconds(5));
                Check(commitGate.Observe(m, t.AddSeconds(10)), "commit pressure");

                Backup.Load();
                string file = Path.Combine(root, "config.json");
                byte[] original = new UTF8Encoding(true).GetPreamble();
                File.WriteAllBytes(file, original);
                File.SetAttributes(file, FileAttributes.ReadOnly);
                ConfigFiles.Write(file, "first"); ConfigFiles.Write(file, "second");
                string record = Backup.Get("FILE2|" + file);
                Check((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0, "read-only attribute preserved");
                ConfigFiles.RestoreOne("FILE2|" + file, record);
                Check(Convert.ToBase64String(File.ReadAllBytes(file)) == Convert.ToBase64String(original), "restore original bytes");
                Check((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0, "restore original attributes");
                string created = Path.Combine(root, "new.ini");
                ConfigFiles.Write(created, "test");
                Check(Backup.Get("FILE2|" + created) == Backup.ABSENT, "record absence");
                ConfigFiles.RestoreOne("FILE2|" + created, Backup.ABSENT);
                Check(!File.Exists(created), "remove newly created config");
                string missing = record.Substring(record.IndexOf('|') + 1);
                File.SetAttributes(missing, FileAttributes.Normal); File.Delete(missing);
                Reject(() => ConfigFiles.Write(file, "must not write"), "missing backup stops write");
                Backup.Remember("TEST", @"C:\new\test\name" + "\tline\nnext");
                Backup.Load();
                Check(Backup.Get("TEST") == @"C:\new\test\name" + "\tline\nnext", "escaped paths roundtrip");

                string batch = Path.Combine(root, "batch.ini");
                File.WriteAllText(batch, "before");
                string invalid = Path.Combine(root, "directory-as-file"); Directory.CreateDirectory(invalid);
                Reject(() => ConfigFiles.WriteBatch(new Dictionary<string, string> { { batch, "after" }, { invalid, "invalid" } }), "batch failure");
                Check(File.ReadAllText(batch) == "before", "batch rollback");

                TestPower(root);
                Console.WriteLine("PASS: " + assertions + " assertions. No system/game settings changed.");
                return 0;
            } catch (Exception e) { Console.WriteLine(e); return 1; }
            finally {
                // Only the unique temporary directory created by this test run is removed.
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, true); App.TestRoot = null;
            }
        }

        private static void TestPower(string root)
        {
            var realRunner = PowerCpu.Execute;
            string original = Guid.NewGuid().ToString(), current = original, created = null;
            var plans = new HashSet<string> { original };
            bool failActivation = false, failRestore = false;
            var commands = new List<string>();
            PowerCpu.Execute = args => {
                commands.Add(args);
                var parts = args.Split(' ');
                switch (parts[0]) {
                    case "/getactivescheme": return current;
                    case "/list": return string.Join("\n", plans);
                    case "/duplicatescheme": created = parts[2]; plans.Add(created); break;
                    case "/setactive":
                        if ((failActivation && parts[1] == created) || (failRestore && parts[1] == original)) throw new IOException("simulated power failure");
                        current = parts[1]; break;
                    case "/delete": plans.Remove(parts[1]); break;
                }
                return "";
            };
            try {
                var c = Calibration.For(new Hardware { TotalRam = 16UL * 1073741824, Laptop = true }, false, false);
                PowerCpu.BeginSession(c);
                Check(current == created && File.Exists(Path.Combine(root, "sessao-energia.txt")), "power journal before active session");
                PowerCpu.EndSession();
                Check(current == original && !plans.Contains(created), "restore original and remove temporary plan");
                PowerCpu.EndSession();
                Check(current == original, "cleanup idempotent");
                Check(!commands.Exists(x => x.StartsWith("/setdcvalueindex")), "battery settings preserved");
                PowerCpu.BeginSession(c);
                string manual = Guid.NewGuid().ToString(); plans.Add(manual); current = manual;
                PowerCpu.EndSession();
                Check(current == manual && !plans.Contains(created), "respect user plan change");
                current = original; failActivation = true;
                Reject(() => PowerCpu.BeginSession(c), "failed activation");
                Check(current == original && !plans.Contains(created), "failed activation rollback");
                failActivation = false;
                PowerCpu.BeginSession(c); failRestore = true;
                PowerCpu.EndSession();
                Check(File.Exists(Path.Combine(root, "sessao-energia.txt")) && plans.Contains(created), "retain journal after cleanup failure");
                failRestore = false; PowerCpu.EndSession();
                Check(current == original && !File.Exists(Path.Combine(root, "sessao-energia.txt")), "recover interrupted session");
            } finally { PowerCpu.Execute = realRunner; }
        }
    }
}

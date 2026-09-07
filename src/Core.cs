using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace F26Boost
{
    internal static class App
    {
        public const string Name = "FPS Booster";
        public const string Version = "2.0";

        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        internal static string TestRoot;
        internal static bool LegacyBackup;
        public static string MachineKey {
            get {
                string id = Convert.ToString(Reg.Read("HKLM", @"SOFTWARE\Microsoft\Cryptography", "MachineGuid"));
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes((id.Length == 0 ? Environment.MachineName : id) + "|" + WindowsIdentity.GetCurrent().User.Value))).Replace("-", "").Substring(0, 20);
            }
        }
        public static string BackupDir { get { return TestRoot ?? (LegacyBackup ? Path.Combine(BaseDir, "backups") : Path.Combine(BaseDir, "backups", MachineKey)); } }
        public static string LogDir { get { return TestRoot ?? Path.Combine(BaseDir, "logs"); } }

        public static bool IsAdmin
        {
            get
            {
                try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(BackupDir);
            Directory.CreateDirectory(LogDir);
        }
    }

    // ================================================================= INTERFACE
    internal static class Ui
    {
        public static void Banner()
        {
            try { Console.Clear(); } catch { }
            var c = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine("  FPS Booster 2.0 | EA SPORTS FC 26 + Project Zomboid");
            Console.WriteLine("  Perfis por hardware e monitor de memoria");
            Console.ForegroundColor = c;
            Console.WriteLine();
        }

        public static void Title(string t)
        {
            Console.WriteLine();
            var c = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ── " + t + " " + new string('─', Math.Max(2, 62 - t.Length)));
            Console.ForegroundColor = c;
        }

        private static void Tag(string tag, ConsoleColor color, string msg)
        {
            var c = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write("  " + tag + " ");
            Console.ForegroundColor = c;
            Console.WriteLine(msg);
        }

        public static void Ok(string m) { Tag("[ OK ]", ConsoleColor.Green, m); Log.W("OK   " + m); }
        public static void Warn(string m) { Tag("[ !  ]", ConsoleColor.Yellow, m); Log.W("WARN " + m); }
        public static void Err(string m) { Tag("[ X  ]", ConsoleColor.Red, m); Log.W("ERR  " + m); }
        public static void Info(string m) { Tag("[ ·  ]", ConsoleColor.DarkGray, m); Log.W("INFO " + m); }
        public static void Skip(string m) { Tag("[ -  ]", ConsoleColor.DarkGray, m); Log.W("SKIP " + m); }

        public static void Kv(string k, string v)
        {
            var c = Console.ForegroundColor;
            Console.Write("     " + k.PadRight(30, '.'));
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" " + v);
            Console.ForegroundColor = c;
        }

        public static void Blank() { Console.WriteLine(); }

        public static void Pause()
        {
            Console.WriteLine();
            var c = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  Pressione ENTER para voltar ao menu...");
            Console.ForegroundColor = c;
            Console.ReadLine();
        }

        public static bool Confirm(string question, bool defaultYes = false)
        {
            Console.WriteLine();
            var c = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  " + question + (defaultYes ? " [S/n] " : " [s/N] "));
            Console.ForegroundColor = c;
            string input = Console.ReadLine();
            if (input == null) return false;
            string a = input.Trim().ToLowerInvariant();
            if (a.Length == 0) return defaultYes;
            return a == "s" || a == "sim" || a == "y" || a == "yes";
        }

        public static string Read(string prompt)
        {
            Console.Write("  " + prompt);
            return (Console.ReadLine() ?? "").Trim();
        }

        public static void Danger(string m)
        {
            var c = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("  ┌────────────────────────────── ATENÇÃO ──────────────────────────────┐");
            foreach (var line in Wrap(m, 66)) Console.WriteLine("  │ " + line.PadRight(66) + " │");
            Console.WriteLine("  └─────────────────────────────────────────────────────────────────────┘");
            Console.ForegroundColor = c;
        }

        public static void Note(string m)
        {
            var c = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            foreach (var line in Wrap(m, 66)) Console.WriteLine("     " + line);
            Console.ForegroundColor = c;
        }

        public static List<string> Wrap(string text, int width)
        {
            var outp = new List<string>();
            foreach (var para in text.Split('\n'))
            {
                var words = para.Split(' ');
                var sb = new StringBuilder();
                foreach (var w in words)
                {
                    if (sb.Length > 0 && sb.Length + w.Length + 1 > width) { outp.Add(sb.ToString()); sb.Clear(); }
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(w);
                }
                outp.Add(sb.ToString());
            }
            return outp;
        }
    }

    // ================================================================= LOG
    internal static class Log
    {
        private static readonly object Lock = new object();
        private static string _path;

        public static string Path_
        {
            get
            {
                if (_path == null)
                    _path = System.IO.Path.Combine(App.LogDir, "boost-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                return _path;
            }
        }

        public static void W(string msg)
        {
            try
            {
                lock (Lock)
                    File.AppendAllText(Path_, DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    // ================================================================= BACKUP
    /// <summary>
    /// Guarda o valor ORIGINAL de tudo que o programa altera, uma unica vez.
    /// Sem isso nao existe "Restaurar" confiavel.
    /// </summary>
    internal static class Backup
    {
        public const string ABSENT = "<<AUSENTE>>";
        private static readonly Dictionary<string, string> Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string FilePath { get { return Path.Combine(App.BackupDir, "estado-original.tsv"); } }

        public static void Load()
        {
            Data.Clear();
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int i = line.IndexOf('\t');
                if (i <= 0) continue;
                Data[line.Substring(0, i)] = Unescape(line.Substring(i + 1));
            }
        }

        public static void Save()
        {
            var sb = new StringBuilder();
            foreach (var kv in Data) sb.AppendLine(kv.Key + "\t" + Escape(kv.Value));
            ConfigFiles.WriteAtomic(FilePath, sb.ToString());
        }

        private static string Escape(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n"); }
        private static string Unescape(string s) {
            var result = new StringBuilder();
            for (int i = 0; i < s.Length; i++) {
                if (s[i] == '\\' && i + 1 < s.Length) {
                    char c = s[++i];
                    if (c == 'n') result.Append('\n');
                    else if (c == 't') result.Append('\t');
                    else if (c == '\\') result.Append('\\');
                    else { result.Append('\\'); result.Append(c); }
                } else result.Append(s[i]);
            }
            return result.ToString();
        }

        /// <summary>Grava so na primeira vez, preservando o valor de fabrica.</summary>
        public static void Remember(string key, string value)
        {
            if (Data.ContainsKey(key)) return;
            Data[key] = value ?? ABSENT;
            try { Save(); } catch { Data.Remove(key); throw; }
        }

        public static bool Has(string key) { return Data.ContainsKey(key); }
        public static string Get(string key) { string v; return Data.TryGetValue(key, out v) ? v : null; }
        public static void Forget(string key) {
            string old;
            if (!Data.TryGetValue(key, out old)) return;
            Data.Remove(key);
            try { Save(); } catch { Data[key] = old; throw; }
        }
        public static List<KeyValuePair<string, string>> All() { return Data.ToList(); }
        public static int Count { get { return Data.Count; } }
        public static void ClearAll() { Data.Clear(); Save(); }
    }

    // ================================================================= REGISTRO
    internal static class Reg
    {
        public static RegistryKey Root(string r)
        {
            switch (r.ToUpperInvariant())
            {
                case "HKLM": return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                case "HKCU": return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                case "HKCR": return RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
                default: throw new ArgumentException("Raiz de registro desconhecida: " + r);
            }
        }

        private static string BKey(string root, string sub, string name) { return "REG|" + root + "|" + sub + "|" + name; }

        public static object Read(string root, string sub, string name)
        {
            try
            {
                using (var b = Root(root))
                using (var k = b.OpenSubKey(sub, false))
                    return k == null ? null : k.GetValue(name);
            }
            catch { return null; }
        }

        private static void RememberCurrent(string root, string sub, string name)
        {
            string bk = BKey(root, sub, name);
            if (Backup.Has(bk)) return;
            object cur = Read(root, sub, name);
            if (cur == null) { Backup.Remember(bk, Backup.ABSENT); return; }
            if (cur is int) Backup.Remember(bk, "DWORD|" + (int)cur);
            else if (cur is long) Backup.Remember(bk, "QWORD|" + (long)cur);
            else Backup.Remember(bk, "SZ|" + Convert.ToString(cur));
        }

        public static bool SetDword(string root, string sub, string name, int value)
        {
            try
            {
                RememberCurrent(root, sub, name);
                using (var b = Root(root))
                using (var k = b.CreateSubKey(sub))
                {
                    if (k == null) return false;
                    k.SetValue(name, value, RegistryValueKind.DWord);
                }
                return true;
            }
            catch (Exception e) { Log.W("REG FAIL " + BKey(root, sub, name) + " :: " + e.Message); return false; }
        }

        public static bool SetString(string root, string sub, string name, string value)
        {
            try
            {
                RememberCurrent(root, sub, name);
                using (var b = Root(root))
                using (var k = b.CreateSubKey(sub))
                {
                    if (k == null) return false;
                    k.SetValue(name, value, RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception e) { Log.W("REG FAIL " + BKey(root, sub, name) + " :: " + e.Message); return false; }
        }

        /// <summary>Reverte uma entrada a partir do backup.</summary>
        public static bool RestoreOne(string backupKey, string stored)
        {
            try
            {
                var parts = backupKey.Split('|');
                if (parts.Length < 4 || parts[0] != "REG") return false;
                string root = parts[1], sub = parts[2], name = string.Join("|", parts.Skip(3));

                using (var b = Root(root))
                {
                    if (stored == Backup.ABSENT)
                    {
                        using (var k = b.OpenSubKey(sub, true))
                            if (k != null) k.DeleteValue(name, false);
                        return true;
                    }
                    int i = stored.IndexOf('|');
                    if (i < 0) return false;
                    string type = stored.Substring(0, i), val = stored.Substring(i + 1);
                    using (var k = b.CreateSubKey(sub))
                    {
                        if (k == null) return false;
                        if (type == "DWORD") k.SetValue(name, int.Parse(val), RegistryValueKind.DWord);
                        else if (type == "QWORD") k.SetValue(name, long.Parse(val), RegistryValueKind.QWord);
                        else k.SetValue(name, val, RegistryValueKind.String);
                    }
                }
                return true;
            }
            catch (Exception e) { Log.W("RESTORE FAIL " + backupKey + " :: " + e.Message); return false; }
        }
    }

    // ================================================================= SERVICOS
    internal static class Svc
    {
        private const string SvcRoot = @"SYSTEM\CurrentControlSet\Services\";

        public static bool Exists(string name)
        {
            try
            {
                using (var b = Reg.Root("HKLM"))
                using (var k = b.OpenSubKey(SvcRoot + name, false))
                    return k != null;
            }
            catch { return false; }
        }

        public static int GetStartMode(string name)
        {
            object v = Reg.Read("HKLM", SvcRoot + name, "Start");
            return v is int ? (int)v : -1;
        }

        public static string StartModeName(int s)
        {
            switch (s)
            {
                case 0: return "Boot";
                case 1: return "System";
                case 2: return "Automático";
                case 3: return "Manual";
                case 4: return "Desativado";
                default: return "?";
            }
        }

        public static string StatusOf(string name)
        {
            try { using (var sc = new ServiceController(name)) return sc.Status.ToString(); }
            catch { return "?"; }
        }

        public static bool IsRunning(string name)
        {
            try { using (var sc = new ServiceController(name)) return sc.Status == ServiceControllerStatus.Running; }
            catch { return false; }
        }

        /// <summary>Para o servico e opcionalmente muda o modo de inicializacao. Sempre com backup.</summary>
        public static bool Tame(string name, int newStartMode)
        {
            if (!Exists(name)) return false;
            string bk = "SVC|" + name;
            if (!Backup.Has(bk))
                Backup.Remember(bk, GetStartMode(name) + ";" + (IsRunning(name) ? "Running" : "Stopped"));

            bool any = false;
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                        any = true;
                    }
                }
            }
            catch (Exception e) { Log.W("SVC stop " + name + " :: " + e.Message); }

            if (newStartMode >= 0 && GetStartMode(name) != newStartMode)
                any |= Reg.SetDword("HKLM", SvcRoot + name, "Start", newStartMode);

            return any;
        }

        public static bool RestoreOne(string name, string stored)
        {
            try
            {
                var p = stored.Split(';');
                int mode = int.Parse(p[0]);
                if (mode < 0 || mode > 4) return false;
                using (var root = Reg.Root("HKLM"))
                using (var key = root.OpenSubKey(SvcRoot + name, true)) {
                    if (key == null) return false;
                    key.SetValue("Start", mode, RegistryValueKind.DWord);
                }
                using (var sc = new ServiceController(name)) {
                    if (p.Length > 1 && p[1] == "Running" && sc.Status != ServiceControllerStatus.Running) {
                        sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
                    } else if (p.Length > 1 && p[1] == "Stopped" && sc.Status != ServiceControllerStatus.Stopped) {
                        if (!sc.CanStop) return false;
                        sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                    }
                }
                return true;
            }
            catch (Exception e) { Log.W("SVC restore " + name + " :: " + e.Message); return false; }
        }
    }

    // ================================================================= SHELL
    internal static class Shell
    {
        public static string Run(string exe, string args, int timeoutMs = 20000)
        {
            try { return Checked(exe, args, timeoutMs); }
            catch (Exception e) { Log.W("SHELL " + exe + " :: " + e.Message); return ""; }
        }

        public static string Checked(string exe, string args, int timeoutMs = 20000)
        {
            var psi = new ProcessStartInfo(exe, args) {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            using (var p = Process.Start(psi)) {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) {
                    try { p.Kill(); } catch { }
                    throw new System.TimeoutException(exe + " excedeu o tempo limite.");
                }
                string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                if (p.ExitCode != 0) throw new InvalidOperationException(exe + " (" + p.ExitCode + "): " + output.Trim());
                return output;
            }
        }

        public static string PowerCfg(string args) { return Run("powercfg.exe", args); }

        public static string Ps(string script, int timeoutMs = 40000)
        {
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"", timeoutMs);
        }
    }

    internal static class Fmt
    {
        public static string Mb(ulong bytes) { return (bytes / 1024.0 / 1024.0).ToString("N0") + " MB"; }
        public static string Gb(ulong bytes) { return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("N2") + " GB"; }
    }
}

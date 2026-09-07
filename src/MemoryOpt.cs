using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace F26Boost
{
    // No global working-set purge, standby purge, modified-list flush or file-cache flush.
    internal static class MemoryOpt
    {
        public static readonly string[] Candidates = { "chrome", "msedge", "firefox", "brave", "opera", "Spotify", "Teams", "ms-teams" };
        private static readonly Dictionary<string, DateTime> LastTrim = new Dictionary<string, DateTime>();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        public static Native.MEMORYSTATUSEX Snapshot() { return Native.GetMemory(); }
        public static void Report()
        {
            var m = Snapshot();
            Ui.Kv("RAM total / disponível", Fmt.Gb(m.ullTotalPhys) + " / " + Fmt.Gb(m.ullAvailPhys));
            Ui.Kv("Memória comprometida / limite", Fmt.Gb(m.ullTotalPageFile - m.ullAvailPageFile) + " / " + Fmt.Gb(m.ullTotalPageFile));
            Ui.Note("Memória comprometida não mede tráfego de paginação. Cache em standby já pode ser reutilizado pelo Windows.");
        }
        public static void TopConsumers(int n)
        {
            var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) using (p) {
                try {
                    if (p.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    string name = p.ProcessName;
                    if (!totals.ContainsKey(name)) totals[name] = 0;
                    totals[name] += p.WorkingSet64;
                } catch { }
            }
            foreach (var item in totals.OrderByDescending(x => x.Value).Take(n))
                Ui.Kv(item.Key, (item.Value / 1048576.0).ToString("N0") + " MB residentes");
        }
        public static HashSet<string> ChooseBackground()
        {
            Ui.Note("Limpeza opcional: escolha aplicativos de fundo. Só reduz memória residente sob pressão sustentada; eles podem precisar recarregá-la depois. Não encerra aplicativos. ENTER mantém apenas a monitoração.");
            Ui.Info("Permitidos: " + string.Join(", ", Candidates));
            var names = Ui.Read("Aplicativos separados por vírgula: ").Split(',').Select(s => s.Trim());
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names) {
                if (Candidates.Contains(name, StringComparer.OrdinalIgnoreCase)) result.Add(name);
                else if (name.Length > 0) Ui.Warn("Ignorado (fora da lista): " + name);
            }
            return result;
        }
        public static int TrimBackground(HashSet<string> approved)
        {
            uint foreground;
            GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            if (foreground == 0) return 0;
            var all = Process.GetProcesses();
            string foregroundName = null;
            try {
                foreach (var p in all) if ((uint)p.Id == foreground) {
                    try { foregroundName = p.ProcessName; } catch { }
                }
                if (foregroundName == null) return 0;
                int count = 0, session = Process.GetCurrentProcess().SessionId;
                foreach (var p in all) {
                    try {
                        string name = p.ProcessName;
                        if (!approved.Contains(name) || !Candidates.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                        if (p.SessionId != session || string.Equals(name, foregroundName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.WorkingSet64 < 150L * 1048576) continue;
                        string key = p.Id + ":" + p.StartTime.ToUniversalTime().Ticks;
                        DateTime last;
                        if (LastTrim.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(10)) continue;
                        if (Native.TrimProcess(p.Id)) { LastTrim[key] = DateTime.UtcNow; if (++count == 3) break; }
                    } catch { }
                }
                return count;
            } finally { foreach (var p in all) p.Dispose(); }
        }
        public static void Clean(string manterProcesso = null, bool silencioso = false)
        {
            if (silencioso) return;
            TopConsumers(8);
            Ui.Info(TrimBackground(ChooseBackground()) + " processo(s) de fundo tiveram memória residente reduzida.");
            Report();
        }
        public static int EcoClear() { return 0; }
        public static void CloseHeavyApps()
        {
            TopConsumers(8);
            Ui.Note("Feche manualmente os aplicativos que não vai usar para liberar memória de forma duradoura. Salve seus documentos primeiro.");
        }
    }
    internal sealed class PressureGate
    {
        private readonly ulong low, recover;
        private int consecutive;
        private bool pressure;
        private DateTime lastAction = DateTime.MinValue;
        public PressureGate(ulong low, ulong recover) { this.low = low; this.recover = recover; }
        public bool Observe(Native.MEMORYSTATUSEX m, DateTime now)
        {
            bool commitCritical = m.ullTotalPageFile > 0 && m.ullAvailPageFile < m.ullTotalPageFile / 10;
            if (m.ullAvailPhys < low || commitCritical) { pressure = true; consecutive++; }
            else if (m.ullAvailPhys >= recover) { pressure = false; consecutive = 0; }
            else consecutive = 0;
            if (!pressure || consecutive < 3 || now - lastAction < TimeSpan.FromMinutes(3)) return false;
            lastAction = now;
            return true;
        }
    }
}

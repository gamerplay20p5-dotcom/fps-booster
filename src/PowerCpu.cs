using System;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;

namespace F26Boost
{
    internal static class PowerCpu
    {
        internal static Func<string, string> Execute = args => Shell.Checked("powercfg.exe", args);
        public const string PlanName = "FPS Booster - Sessao";
        private static string Journal { get { return Path.Combine(App.BackupDir, "sessao-energia.txt"); } }
        private static string GuidFrom(string value)
        {
            var match = Regex.Match(value, @"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b");
            return match.Success ? match.Value : null;
        }
        public static string ActiveSchemeGuid() { return GuidFrom(Execute("/getactivescheme")); }
        public static string ActiveSchemeName() { return Shell.PowerCfg("/getactivescheme").Trim(); }
        public static string BeginSession(Calibration c)
        {
            if (File.Exists(Journal)) throw new IOException("Há uma sessão de energia pendente; recupere-a antes de iniciar outra.");
            string original = ActiveSchemeGuid();
            if (original == null) throw new IOException("Plano original não identificado; mantido sem alteração.");
            string created = Guid.NewGuid().ToString();
            ConfigFiles.WriteAtomic(Journal, original + "\n" + created);
            try {
                Execute("/duplicatescheme " + original + " " + created);
                Execute("/changename " + created + " \"" + PlanName + "\"");
                Set(created, "PROCTHROTTLEMIN", c.MinCpu);
                Set(created, "PROCTHROTTLEMAX", 100);
                Optional(created, "PERFBOOSTMODE", c.BoostMode);
                Optional(created, "PERFEPP", c.Epp);
                // DC inherits original battery settings. Parking/PCIe/USB/timers remain managed by Windows.
                Execute("/setactive " + created);
                Ui.Ok("Plano temporário ativo. Ajustes de bateria herdados do seu plano original.");
                return created;
            } catch { EndSession(); throw; }
        }
        private static void Set(string plan, string setting, int value)
        {
            Execute("/setacvalueindex " + plan + " SUB_PROCESSOR " + setting + " " + value);
        }
        private static void Optional(string plan, string setting, int value)
        {
            try { Set(plan, setting, value); }
            catch (Exception e) { Ui.Warn(setting + " não aplicado neste hardware: " + e.Message); }
        }
        public static void EndSession()
        {
            if (!File.Exists(Journal)) return;
            try {
                var lines = File.ReadAllLines(Journal);
                Guid original, created;
                if (lines.Length != 2 || !Guid.TryParse(lines[0], out original) || !Guid.TryParse(lines[1], out created) || original == created)
                    throw new IOException("Registro de energia inválido; preservado para revisão.");
                if (string.Equals(ActiveSchemeGuid(), created.ToString(), StringComparison.OrdinalIgnoreCase))
                    Execute("/setactive " + original);
                string plans = Execute("/list");
                if (plans.IndexOf(created.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
                    Execute("/delete " + created);
                File.Delete(Journal);
                Ui.Ok("Plano temporário removido; energia devolvida ao plano anterior (ou à sua escolha manual).");
            } catch (Exception e) { Ui.Warn("Recuperação de energia pendente: " + e.Message); }
        }
        public static void PreventSleep(bool on)
        {
            if (Native.SetThreadExecutionState(on ? Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED : Native.ES_CONTINUOUS) == 0)
                Log.W("Windows não aceitou a solicitação de suspensão da sessão.");
        }
        public static void RestorePlan()
        {
            string original = Backup.Get("POWER|scheme_original");
            Guid parsed;
            if (Guid.TryParse(original, out parsed)) Execute("/setactive " + parsed);
        }
        public static void ShowClock()
        {
            try {
                using (var s = new ManagementObjectSearcher("SELECT Name, CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor"))
                using (var rows = s.Get()) foreach (ManagementObject o in rows) using (o) {
                    Ui.Kv("Clock informado por WMI", o["CurrentClockSpeed"] + " MHz; máximo informado " + o["MaxClockSpeed"] + " MHz");
                }
                Ui.Note("WMI pode fornecer frequência nominal/desatualizada; estes valores não medem turbo efetivo nem limites térmicos. O programa não altera BIOS, voltagem ou teto de fábrica.");
            } catch (Exception e) { Ui.Info("Clock indisponível: " + e.Message); }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace F26Boost
{
    internal static class Program
    {
        private static GameSpec selected = GameCatalog.Fifa;
        [STAThread]
        private static int Main(string[] args)
        {
            try {
                Console.OutputEncoding = Encoding.UTF8;
                Console.Title = App.Name + " " + App.Version;
                if (args.Any(a => a.Equals("/selftest", StringComparison.OrdinalIgnoreCase))) return SelfTests.Run();
                string command = args.Length == 0 ? "" : args[0].ToLowerInvariant();
                string[] known = { "", "/diag", "/jogo", "/zomboid", "/referencia", "/restaurar-legado" };
                if (!known.Contains(command) || args.Length > 1) {
                    Console.WriteLine("Uso: FPS Booster.exe [/diag | /jogo | /zomboid | /referencia | /selftest | /restaurar-legado]"); return 2;
                }
                if (command == "/diag") { GameCatalog.Detect(); Diagnose(); return 0; }
                if (!App.IsAdmin) {
                    try {
                        Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, command) { UseShellExecute = true, Verb = "runas", WorkingDirectory = App.BaseDir });
                        return 0;
                    } catch (Exception e) { Console.WriteLine("Não foi possível elevar o programa: " + e.Message); return 1; }
                }
                // One mutation session per machine/user, including separate copies of the executable.
                using (var mutex = new Mutex(false, "Global\\GameBoost-" + App.MachineKey)) {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { Console.WriteLine("Já existe uma instância do FPS Booster para este usuário."); return 1; }
                    try {
                        App.EnsureDirs(); Backup.Load(); GameCatalog.Detect();
                        PowerCpu.EndSession();
                        Console.CancelKeyPress += (s, e) => {
                            if (GameSession.Active) { e.Cancel = true; GameSession.CancelRequested = true; }
                        };
                        if (command == "/restaurar-legado") { RestoreLegacy(); return 0; }
                        if (command == "/jogo" || command == "/zomboid" || command == "/referencia") {
                            GameSession.Run(command == "/zomboid" ? GameCatalog.Zomboid : selected, command == "/referencia"); return 0;
                        }
                        Loop();
                    } finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception e) { Ui.Err(e.Message); Log.W(e.ToString()); return 1; }
        }
        private static void Diagnose()
        {
            Ui.Banner();
            var h = Hardware.Detect(); h.Show(); MemoryOpt.Report(); PowerCpu.ShowClock();
            Ui.Kv("Plano ativo", PowerCpu.ActiveSchemeName());
            foreach (var game in GameCatalog.All) {
                Ui.Title(game.Name); Ui.Kv("Pasta", game.InstallDir ?? "não detectada; use P no menu");
                Ui.Kv("Processo ativo", game.Running() ? "sim" : "não");
                Calibration.For(h, game.IsZomboid, false).Show(game.IsZomboid);
                if (!game.IsZomboid) GameFifa.CheckConsistency();
            }
            MemoryOpt.TopConsumers(8);
        }
        private static void Loop()
        {
            while (true) {
                Ui.Banner(); Ui.Kv("Jogo selecionado", selected.Name);
                Console.WriteLine("  [G] Trocar jogo      [P] Informar pasta de instalação");
                Console.WriteLine("  [1] Diagnóstico e perfis propostos");
                Console.WriteLine("  [2] Recalibrar e aplicar configuração do jogo (fechado)");
                Console.WriteLine("  [3] Ver política de CPU e energia");
                Console.WriteLine("  [4] Memória: limpeza seletiva manual");
                Console.WriteLine("  [8] Ver aplicativos que consomem RAM");
                Console.WriteLine("  [9] Perfis gráficos manuais do FC 26");
                Console.WriteLine("  [11] Ver situação dos mods do FC 26");
                Console.WriteLine("  [12] Iniciar sessão otimizada do jogo selecionado");
                Console.WriteLine("  [R] Medir sessão de referência sem ajustes");
                Console.WriteLine("  [13] Varredura de segurança (somente relatório)");
                Console.WriteLine("  [14] Listar alterações desta máquina");
                Console.WriteLine("  [0] Restaurar alterações desta versão");
                Console.WriteLine("  [L] Restaurar backups da versão antiga");
                Console.WriteLine("  [S] Sair");
                string input = Console.ReadLine();
                if (input == null) return;
                try {
                    switch (input.Trim().ToUpperInvariant()) {
                        case "S": case "Q": return;
                        case "G": selected = selected == GameCatalog.Fifa ? GameCatalog.Zomboid : GameCatalog.Fifa; continue;
                        case "P": GameCatalog.SetPath(selected, Ui.Read("Pasta que contém o executável: ")); break;
                        case "1": Diagnose(); break;
                        case "2": ApplyProfile(); break;
                        case "3": Calibration.For(Hardware.Detect(), selected.IsZomboid, false).Show(selected.IsZomboid); PowerCpu.ShowClock(); Ui.Info("A energia é aplicada temporariamente pela opção 12."); break;
                        case "4": MemoryOpt.Clean(); break;
                        case "8": MemoryOpt.CloseHeavyApps(); break;
                        case "9": ManualFifa(); break;
                        case "11": GameFifa.StatusMods(); break;
                        case "12": GameSession.Run(selected, false); break;
                        case "R": GameSession.Run(selected, true); break;
                        case "13": Security.Varredura(); break;
                        case "14": Restore.Listar(); break;
                        case "0": Restore.Tudo(); break;
                        case "L": RestoreLegacy(); break;
                        default: continue;
                    }
                } catch (Exception e) { Ui.Err(e.Message); Log.W(e.ToString()); }
                Ui.Pause();
            }
        }
        private static void ApplyProfile()
        {
            if (selected.Running()) throw new InvalidOperationException("Feche o jogo antes de recalibrar arquivos.");
            var h = Hardware.Detect();
            bool mods = selected.IsZomboid && Ui.Confirm("Usa muitos mods? (permite heap de até 12 GB se houver RAM)");
            var c = Calibration.For(h, selected.IsZomboid, mods);
            h.Show(); c.Show(selected.IsZomboid);
            if (!Ui.Confirm("Aplicar esta configuração com backup?", true)) return;
            if (selected.IsZomboid) GameZomboid.Apply(c); else GameFifa.AplicarPerfil(c.Fifa);
        }
        private static void ManualFifa()
        {
            var profiles = GameFifa.Perfis();
            for (int i = 0; i < profiles.Length; i++) Ui.Info((i + 1) + " - " + profiles[i].Nome);
            int index;
            if (!int.TryParse(Ui.Read("Perfil (ENTER cancela): "), out index) || index < 1 || index > profiles.Length) return;
            GameFifa.AplicarPerfil(profiles[index - 1]);
        }
        private static void RestoreLegacy()
        {
            Ui.Note("Backups v1 não identificam a máquina de origem. Use apenas se esta pasta de backups foi criada neste PC e neste usuário. Restaure o legado antes de aplicar perfis v2, pois as duas versões podem ter cópias dos mesmos arquivos.");
            if (Backup.Count > 0) { Ui.Warn("Restaure primeiro as alterações v2 (opção 0) para evitar sobrepor originais."); return; }
            if (!Ui.Confirm("Os backups antigos pertencem a este PC e usuário?")) return;
            try { App.LegacyBackup = true; Backup.Load(); Restore.Tudo(); }
            finally { App.LegacyBackup = false; Backup.Load(); }
        }
    }
}

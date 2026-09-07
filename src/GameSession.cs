using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace F26Boost
{
    internal static class GameSession
    {
        public static volatile bool CancelRequested;
        public static volatile bool Active;
        private sealed class Tracked : IDisposable
        {
            public Process Process;
            public ProcessPriorityClass Original;
            public bool Changed;
            public void Dispose()
            {
                try {
                    if (Changed && !Process.HasExited && Process.PriorityClass == ProcessPriorityClass.AboveNormal)
                        Process.PriorityClass = Original;
                } catch (Exception e) { Log.W("Restaurar prioridade: " + e.Message); }
                Process.Dispose();
            }
        }
        public static void Run(GameSpec game, bool baseline)
        {
            var h = Hardware.Detect();
            var c = Calibration.For(h, game.IsZomboid, false);
            h.Show(); c.Show(game.IsZomboid);
            Ui.Note(baseline ? "Medição de referência: registra a sessão sem aplicar ajustes de energia ou processos." : "Energia e prioridade temporárias; RAM monitorada por pressão. Ctrl+C encerra o monitor e restaura a sessão, deixando o jogo aberto.");
            var approved = baseline ? new HashSet<string>() : MemoryOpt.ChooseBackground();
            var tracked = new Dictionary<string, Tracked>();
            string csv = Path.Combine(App.LogDir, "sessao-" + game.Id + "-" + (baseline ? "referencia-" : "boost-") + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".csv");
            var gate = new PressureGate(c.LowMemory, c.RecoverMemory);
            var timer = Stopwatch.StartNew();
            ulong minimum = ulong.MaxValue;
            int alerts = 0, trims = 0, samples = 0;
            bool powerStarted = false, everSeen = false;
            DateTime missingSince = DateTime.UtcNow;
            Active = true; CancelRequested = false;
            try {
                using (var writer = new StreamWriter(csv, false, new System.Text.UTF8Encoding(false))) {
                    writer.AutoFlush = true;
                    writer.WriteLine("utc,elapsed_s,available_mb,commit_mb,commit_limit_mb,game_working_set_mb,game_private_mb,battery,trimmed_processes");
                    if (!baseline) {
                        try { PowerCpu.BeginSession(c); powerStarted = true; }
                        catch (Exception e) { Ui.Warn("Sessão continua sem ajuste de energia: " + e.Message); }
                        if (!h.OnBattery && !h.PowerUnknown) PowerCpu.PreventSleep(true);
                    }
                    if (!game.Running()) {
                        Ui.Info("Abrindo " + game.Name + " pela Steam. Instalações EA App/GOG: abra pelo seu launcher enquanto o monitor aguarda.");
                        try { Process.Start(new ProcessStartInfo("steam://rungameid/" + game.SteamId) { UseShellExecute = true }); }
                        catch (Exception e) { Ui.Warn("Abra o jogo manualmente: " + e.Message); }
                    }
                    Ui.Info("Aguardando o jogo por até 3 minutos. CSV: " + csv);
                    while (!CancelRequested) {
                        var processes = game.Processes();
                        long working = 0, priv = 0;
                        int live = 0;
                        foreach (var p in processes) {
                            bool retained = false;
                            try {
                                if (p.HasExited) continue;
                                live++; working += p.WorkingSet64; priv += p.PrivateMemorySize64;
                                string key = p.Id + ":" + p.StartTime.ToUniversalTime().Ticks;
                                if (!tracked.ContainsKey(key)) {
                                    var entry = new Tracked { Process = p };
                                    tracked.Add(key, entry); retained = true;
                                    if (!baseline) {
                                        try {
                                            entry.Original = p.PriorityClass;
                                            // Preserve manually elevated priority; never use realtime or hard affinity.
                                            if (entry.Original == ProcessPriorityClass.Normal || entry.Original == ProcessPriorityClass.BelowNormal || entry.Original == ProcessPriorityClass.Idle) {
                                                p.PriorityClass = ProcessPriorityClass.AboveNormal;
                                                entry.Changed = true;
                                            }
                                        } catch (Exception e) { Ui.Info("Prioridade mantida para PID " + p.Id + ": " + e.Message); }
                                    }
                                    Ui.Info("Processo do jogo detectado: " + p.ProcessName + " (" + p.Id + ")");
                                }
                            } catch { }
                            finally { if (!retained) p.Dispose(); }
                        }
                        if (live == 0) {
                            if (DateTime.UtcNow - missingSince > TimeSpan.FromSeconds(everSeen ? 15 : 180)) break;
                            if (CancelRequested) break;
                            Thread.Sleep(500); continue;
                        }
                        everSeen = true; missingSince = DateTime.UtcNow;
                        var m = MemoryOpt.Snapshot();
                        minimum = Math.Min(minimum, m.ullAvailPhys);
                        int trimmed = 0;
                        if (gate.Observe(m, DateTime.UtcNow)) {
                            alerts++;
                            if (!baseline) trimmed = MemoryOpt.TrimBackground(approved);
                            trims += trimmed;
                            Ui.Warn("Pressão de memória: " + Fmt.Gb(m.ullAvailPhys) + " disponíveis; " + trimmed + " processos de fundo ajustados. Se persistir, feche apps ou reduza mods.");
                        }
                        h.RefreshPower();
                        if (!baseline) PowerCpu.PreventSleep(!h.OnBattery && !h.PowerUnknown);
                        writer.WriteLine(string.Join(",", new[] {
                            DateTime.UtcNow.ToString("o"), timer.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
                            (m.ullAvailPhys / 1048576).ToString(), ((m.ullTotalPageFile - m.ullAvailPageFile) / 1048576).ToString(),
                            (m.ullTotalPageFile / 1048576).ToString(), (working / 1048576).ToString(), (priv / 1048576).ToString(),
                            h.PowerUnknown ? "unknown" : h.OnBattery ? "1" : "0", trimmed.ToString()
                        }));
                        if (samples++ % 6 == 0) Ui.Info("RAM disponível: " + Fmt.Gb(m.ullAvailPhys) + " | jogo residente: " + working / 1048576 + " MB");
                        for (int i = 0; i < c.PollSeconds * 2 && !CancelRequested; i++) Thread.Sleep(500);
                    }
                }
            } finally {
                foreach (var item in tracked.Values) item.Dispose();
                if (!baseline) {
                    PowerCpu.PreventSleep(false);
                    if (powerStarted) PowerCpu.EndSession();
                }
                Active = false; CancelRequested = false;
            }
            if (!everSeen) Ui.Warn("Nenhum processo do jogo detectado no prazo.");
            Ui.Kv("Duração do monitor", timer.Elapsed.ToString(@"hh\:mm\:ss"));
            if (minimum != ulong.MaxValue) Ui.Kv("Menor RAM disponível", Fmt.Gb(minimum));
            Ui.Kv("Alertas / processos ajustados", alerts + " / " + trims);
            Ui.Info("Registro da sessão: " + csv);
            Ui.Note("O CSV mede memória, não FPS nem frametime. Compare FPS/1% low com a mesma cena, resolução, mods e duração em uma ferramenta de medição do jogo.");
        }
    }
}

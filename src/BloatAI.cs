using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace F26Boost
{
    /// <summary>
    /// Desliga IA nativa do Windows, telemetria e supérfluos.
    /// Tudo aqui e REVERSIVEL: nada e desinstalado, nada e apagado.
    /// Servicos viram "Manual" (nao "Desativado") e o valor original vai pro backup.
    /// </summary>
    internal static class BloatAI
    {
        // Servicos de IA / Copilot / Recall. Detectados por nome tambem, mas estes
        // sao os conhecidos no Windows 11 24H2/25H2.
        private static readonly string[] ServicosIA =
        {
            "WSAIFabricSvc",        // Windows AI Fabric Service
            "WindowsAIService",
            "AIXHostSvc",
            "CopilotSvc"
        };

        private static readonly string[] ServicosTelemetria =
        {
            "DiagTrack",                                        // Experiências do Usuário Conectado
            "dmwappushservice",                                 // WAP Push (telemetria)
            "diagnosticshub.standardcollector.service",
            "WaaSMedicSvc",
            "RetailDemo",
            "MapsBroker",
            "lfsvc",                                            // Serviço de Geolocalização
            "wisvc",                                            // Windows Insider
            "MessagingService",
            "PhoneSvc",
            "WalletService"
        };

        // Xbox: o FC 26 aqui roda pela Steam, nao precisa de nada disso.
        private static readonly string[] ServicosXbox =
        {
            "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"
        };

        // Processos de IA / widgets / overlay. Sao reabertos pelo Windows quando
        // voce usar o recurso de novo — fechar aqui so devolve RAM e CPU agora.
        private static readonly string[] ProcessosIA =
        {
            "Copilot", "CopilotNative", "Microsoft.Copilot", "ai", "AIXHost",
            "Recall", "RecallService", "ClickToDo", "PhoneExperienceHost",
            "Widgets", "WidgetService", "WidgetBoard"
        };

        private static readonly string[] ProcessosBloat =
        {
            "msedgewebview2", "GameBar", "GameBarFTServer", "XboxGameBarWidgets",
            "SpotifyXboxGamebarWebView", "XboxPcApp", "XboxPcAppFT", "GamingServices",
            "SearchHost", "Cortana", "YourPhone", "OneDrive", "Teams", "ms-teams",
            "SecurityHealthSystray", "SystemSettings", "TextInputHost"
        };

        // ============================================================ STATUS
        public static void Status()
        {
            Ui.Title("IA NATIVA E BLOAT — SITUAÇÃO ATUAL");

            var svcIA = DescobrirServicosIA();
            if (svcIA.Count == 0) Ui.Ok("Nenhum serviço de IA encontrado neste Windows.");
            foreach (var s in svcIA)
                Ui.Kv(s, Svc.StatusOf(s) + " · início " + Svc.StartModeName(Svc.GetStartMode(s)));

            Ui.Blank();
            var vivos = ProcessosVivos(ProcessosIA.Concat(ProcessosBloat).ToArray());
            if (vivos.Count == 0) Ui.Ok("Nenhum processo de IA/bloat rodando agora.");
            foreach (var g in vivos)
                Ui.Kv(g.Key, g.Sum(p => Ws(p)) / 1024 / 1024 + " MB  (x" + g.Count() + ")");

            Ui.Blank();
            Ui.Kv("Copilot bloqueado", LidoDword("HKCU", @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot") == 1 ? "sim" : "não");
            Ui.Kv("Recall bloqueado", LidoDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis") == 1 ? "sim" : "não");
            Ui.Kv("Gravação em 2º plano", LidoDword("HKCU", @"System\GameConfigStore", "GameDVR_Enabled") == 0 ? "desligada" : "LIGADA (custa FPS)");
        }

        private static int LidoDword(string root, string sub, string name)
        {
            object v = Reg.Read(root, sub, name);
            return v is int ? (int)v : -1;
        }

        private static long Ws(Process p) { try { return p.WorkingSet64; } catch { return 0; } }

        private static List<IGrouping<string, Process>> ProcessosVivos(string[] nomes)
        {
            return Process.GetProcesses()
                .Where(p => nomes.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Sum(p => Ws(p)))
                .ToList();
        }

        /// <summary>Acha servicos de IA por nome conhecido e tambem por padrao, caso a Microsoft renomeie.</summary>
        public static List<string> DescobrirServicosIA()
        {
            var achados = new List<string>();
            foreach (var s in ServicosIA) if (Svc.Exists(s)) achados.Add(s);

            try
            {
                foreach (var sc in ServiceController.GetServices())
                {
                    string n = sc.ServiceName ?? "";
                    string d = sc.DisplayName ?? "";
                    bool ehIA =
                        n.IndexOf("WSAIFabric", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Copilot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("WindowsAI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.IndexOf("Copilot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.IndexOf("Recall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.IndexOf("AI Fabric", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (ehIA && !achados.Contains(sc.ServiceName)) achados.Add(sc.ServiceName);
                    sc.Dispose();
                }
            }
            catch (Exception e) { Log.W("DescobrirServicosIA: " + e.Message); }

            return achados;
        }

        // ============================================================ DESLIGAR IA
        public static void DesligarIA()
        {
            Ui.Title("DESLIGANDO A IA NATIVA DO WINDOWS");

            var svcIA = DescobrirServicosIA();
            foreach (var s in svcIA)
            {
                if (Svc.Tame(s, 4)) Ui.Ok("Serviço " + s + " parado e travado como Desativado.");
                else Ui.Skip("Serviço " + s + " já estava parado.");
            }
            if (svcIA.Count == 0) Ui.Info("Este Windows não tem serviço de IA registrado.");

            int mortos = FecharProcessos(ProcessosIA);
            Ui.Ok(mortos + " processo(s) de IA encerrado(s).");

            // Politicas — o mesmo que o Editor de Diretiva de Grupo faria.
            Reg.SetDword("HKCU", @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
            Ui.Ok("Copilot bloqueado por diretiva.");

            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableImageCreator", 1);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableCocreator", 1);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableGenerativeFill", 1);
            Ui.Ok("Recall, Click to Do e IA generativa do sistema bloqueados por diretiva.");

            Reg.SetDword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
            Ui.Ok("Botão do Copilot e Widgets removidos da barra de tarefas.");

            Ui.Blank();
            Ui.Note("Reversível: a opção \"Restaurar tudo\" devolve cada um desses valores ao original. "
                  + "Nenhum aplicativo foi desinstalado.");
        }

        // ============================================================ TELEMETRIA / XBOX
        public static void DesligarTelemetriaEXbox()
        {
            Ui.Title("TELEMETRIA, XBOX E GRAVAÇÃO EM SEGUNDO PLANO");

            foreach (var s in ServicosTelemetria)
                if (Svc.Exists(s) && Svc.Tame(s, 3)) Ui.Ok("Serviço " + s + " parado (início Manual).");

            foreach (var s in ServicosXbox)
                if (Svc.Exists(s) && Svc.Tame(s, 3)) Ui.Ok("Serviço Xbox " + s + " parado — o FC 26 roda pela Steam, não precisa.");

            Ui.Info("GameInputSvc foi mantido de propósito: é o que faz seu controle funcionar.");

            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
            Reg.SetDword("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0);
            Ui.Ok("Telemetria em nível mínimo por diretiva.");

            // Game DVR: grava os ultimos segundos o tempo todo. Custa FPS de verdade.
            Reg.SetDword("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0);
            Reg.SetDword("HKCU", @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2);
            Reg.SetDword("HKCU", @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1);
            Reg.SetDword("HKCU", @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
            Reg.SetDword("HKCU", @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0);
            Reg.SetDword("HKCU", @"Software\Microsoft\GameBar", "ShowStartupPanel", 0);
            Reg.SetDword("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
            Ui.Ok("Game DVR desligado e Modo Jogo ligado — a gravação em 2º plano era custo puro.");

            Reg.SetDword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
            Reg.SetDword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2);
            Ui.Ok("Aplicativos da Loja proibidos de rodar em segundo plano.");

            Reg.SetDword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0);
            Ui.Ok("Notificações desligadas (uma notificação no meio da partida derruba o frametime).");

            int n = FecharProcessos(ProcessosBloat);
            Ui.Ok(n + " processo(s) de bloat encerrado(s).");
        }

        // ============================================================ VISUAL
        public static void EfeitosVisuais()
        {
            Ui.Title("EFEITOS VISUAIS DO WINDOWS");

            Reg.SetDword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
            Reg.SetDword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
            Reg.SetString("HKCU", @"Control Panel\Desktop", "DragFullWindows", "0");
            Reg.SetString("HKCU", @"Control Panel\Desktop", "MenuShowDelay", "0");
            Reg.SetString("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0");
            Ui.Ok("Animações e transparência desligadas — a transparência do Windows é desenhada pela mesma iGPU que roda o jogo.");
        }

        // ============================================================ BUSCA (temporario)
        public static void PausarBusca()
        {
            if (!Svc.Exists("WSearch")) return;
            if (Svc.IsRunning("WSearch"))
            {
                Svc.Tame("WSearch", -1);   // so para, nao muda o modo de inicio
                Ui.Ok("Indexação do Windows Search pausada durante a partida.");
            }
        }

        public static void RetomarBusca()
        {
            try
            {
                if (!Svc.Exists("WSearch")) return;
                using (var sc = new ServiceController("WSearch"))
                    if (sc.Status == ServiceControllerStatus.Stopped) { sc.Start(); Ui.Ok("Windows Search religado."); }
            }
            catch { }
        }

        // ============================================================ util
        private static int FecharProcessos(string[] nomes)
        {
            int n = 0;
            int meuPid = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == meuPid) continue;
                    if (!nomes.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                    p.Kill();
                    n++;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return n;
        }
    }
}

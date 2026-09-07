using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace F26Boost
{
    /// <summary>Um perfil grafico. Cada campo cai nos dois arquivos de config do jogo.</summary>
    internal sealed class Perfil
    {
        public string Nome;
        public string Descricao;
        public int Largura = 1280, Altura = 720;
        public int MaxFps = 30, TargetFps = 30;
        public double EscalaRender = 0.75;
        public int ResolucaoDinamica = 1;
        public int VSync = 0;
        public int Multidao = 0, Grama = 0, Tecido = 0, Cabelo = 0;
        public int MotionBlur = 0, AO = 0, Redes3d = 0, Msaa = 0;
        public int QualidadeGeral = 0;
        public int RefreshRate = 60;
    }

    /// <summary>
    /// Tudo que e especifico do EA SPORTS FC 26 (a "BIG otimizacao" do pedido):
    /// perfis graficos,
    /// suporte ao injetor de mods e o Modo Partida com vigilante de RAM.
    /// </summary>
    internal static class GameFifa
    {
        public const string SteamAppId = "3405690";
        public const string ExeName = "FC26.exe";
        public const string ProcName = "FC26";

        // ------------------------------------------------------------ deteccao
        public static string GameDir { get { return GameCatalog.Fifa.InstallDir; } }
        public static string SettingsDir {
            get {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EA SPORTS FC 26");
                return Directory.Exists(path) ? path : null;
            }
        }

        public static string IniPath { get { return SettingsDir == null ? null : Path.Combine(SettingsDir, "fcsetup.ini"); } }
        public static string LuaPath { get { return SettingsDir == null ? null : Path.Combine(SettingsDir, "settings", "overrideAutodetect.lua"); } }
        public static string ExePath { get { return GameDir == null ? null : Path.Combine(GameDir, ExeName); } }

        public static bool JogoAberto() { return GameCatalog.Fifa.Running(); }

        public static void CheckConsistency()
        {
            if (IniPath == null || !File.Exists(IniPath) || !File.Exists(LuaPath)) return;
            string ini = File.ReadAllText(IniPath), lua = File.ReadAllText(LuaPath);
            foreach (var pair in new[] {
                new[] { "RESOLUTIONWIDTH", "ResolutionWidth" }, new[] { "RESOLUTIONHEIGHT", "ResolutionHeight" },
                new[] { "RENDERINGQUALITY", "OverallGraphicsQuality" }, new[] { "MAX_FRAME_RATE", "MaxFrameRate" }
            }) {
                var left = Regex.Match(ini, @"(?mi)^\s*" + pair[0] + @"\s*=\s*(\d+)");
                var right = Regex.Match(lua, @"(?m)^\s*" + pair[1] + @"\s*=\s*(\d+)");
                if (left.Success && right.Success && left.Groups[1].Value != right.Groups[1].Value)
                    Ui.Warn("Configurações divergentes: " + pair[0] + " INI=" + left.Groups[1].Value + ", Lua=" + right.Groups[1].Value + ". Opção 2 grava um perfil coerente.");
            }
        }

        // ------------------------------------------------------------ perfis
        public static Perfil[] Perfis()
        {
            return new[]
            {
                new Perfil {
                    Nome = "30 TRAVADO",
                    Descricao = "720p com teto de 30 FPS. Ponto de partida para GPU integrada; valide em partida.",
                    MaxFps = 30, TargetFps = 30, EscalaRender = 0.75, ResolucaoDinamica = 1
                },
                new Perfil {
                    Nome = "FPS LIVRE",
                    Descricao = "720p com teto de 60 e alvo de 45 FPS; resultado depende do hardware.",
                    MaxFps = 60, TargetFps = 45, EscalaRender = 0.72, ResolucaoDinamica = 1
                },
                new Perfil {
                    Nome = "SOBREVIVÊNCIA",
                    Descricao = "720p com escala reduzida para aliviar a GPU; nao garante FPS minimo.",
                    MaxFps = 30, TargetFps = 30, EscalaRender = 0.62, ResolucaoDinamica = 1
                }
            };
        }

        // ------------------------------------------------------------ aplicar perfil
        public static void AplicarPerfil(Perfil p)
        {
            Ui.Title("PERFIL GRÁFICO DO FC 26 — " + p.Nome);

            if (SettingsDir == null) { Ui.Err("Não encontrei a pasta de configuração do EA SPORTS FC 26."); return; }
            if (JogoAberto())
            {
                Ui.Err("O FC 26 está aberto. Feche o jogo primeiro — senão ele sobrescreve tudo ao sair.");
                return;
            }

            Ui.Note(p.Descricao);
            Ui.Blank();


            // --- fcsetup.ini
            var ini = new Dictionary<string, string>
            {
                { "FULLSCREEN",         "1" },
                { "WINDOWED_BORDERLESS","0" },
                { "RESOLUTIONWIDTH",    p.Largura.ToString() },
                { "RESOLUTIONHEIGHT",   p.Altura.ToString() },
                { "REFRESH_RATE",       p.RefreshRate.ToString() },
                { "RENDERINGQUALITY",   p.QualidadeGeral.ToString() },
                { "MSAA_LEVEL",         p.Msaa.ToString() },
                { "WAITFORVSYNC",       p.VSync.ToString() },
                { "MAX_FRAME_RATE",     p.MaxFps.ToString() },
                { "TARGET_FRAME_RATE",  p.TargetFps.ToString() },
                { "RENDERING_SCALE",    p.EscalaRender.ToString("0.0#", CultureInfo.InvariantCulture) },
                { "DYNAMIC_RESOLUTION", p.ResolucaoDinamica.ToString() },
                { "CROWD_QUALITY",      p.Multidao.ToString() },
                { "GRASS_QUALITY",      p.Grama.ToString() },
                { "CLOTH_QUALITY",      p.Tecido.ToString() },
                { "STRAND_BASED_HAIR",  p.Cabelo.ToString() },
                { "MOTION_BLUR",        p.MotionBlur.ToString() },
                { "DYNAMIC_AO_QUALITY", p.AO.ToString() },
                { "USE_GOAL_NETS_3D",   p.Redes3d.ToString() }
            };
            string iniText = MergeAssignments(File.Exists(IniPath) ? File.ReadAllText(IniPath) : "", ini, true);

            // Keep the legacy Lua mapping; support can vary with game updates.
            var lua = new Dictionary<string, string>
            {
                { "FullscreenEnabled",      "1" },
                { "WindowedBorderless",     "0" },
                { "ResolutionWidth",        p.Largura.ToString() },
                { "ResolutionHeight",       p.Altura.ToString() },
                { "FullscreenRefreshRate",  p.RefreshRate.ToString() },
                { "OverallGraphicsQuality", p.QualidadeGeral.ToString() },
                { "VSyncEnabled",           p.VSync.ToString() },
                { "PresentInterval",        p.VSync.ToString() },
                { "MaxFrameRate",           p.MaxFps.ToString() },
                { "TargetFrameRate",        p.TargetFps.ToString() },
                { "ResolutionScale",        p.EscalaRender.ToString("0.000000", CultureInfo.InvariantCulture) },
                { "DynamicResolution",      p.ResolucaoDinamica.ToString() },
                { "CrowdQuality",           p.Multidao.ToString() },
                { "GrassQuality",           p.Grama.ToString() },
                { "ClothQuality",           p.Tecido.ToString() },
                { "StrandBasedHair",        p.Cabelo.ToString() },
                { "MotionBlur",             p.MotionBlur.ToString() },
                { "DynamicAOQuality",       p.AO.ToString() },
                { "UseGoalNets3d",          p.Redes3d.ToString() },
                { "NisAtFullFps",           "0" },
                { "MinWindowWidth",         p.Largura.ToString() },
                { "MinWindowHeight",        p.Altura.ToString() }
            };
            string luaText = MergeAssignments(File.Exists(LuaPath) ? File.ReadAllText(LuaPath) : "", lua, false);
            if (JogoAberto()) throw new InvalidOperationException("O jogo abriu durante o ajuste. Tente novamente.");
            ConfigFiles.WriteBatch(new Dictionary<string, string> { { IniPath, iniText }, { LuaPath, luaText } });
            Ui.Ok("INI e Lua atualizados com backup. O suporte a cada chave depende da versao do jogo.");

            Ui.Kv("Resolucao", p.Largura + "x" + p.Altura);
            Ui.Kv("Escala / teto FPS", p.EscalaRender.ToString(CultureInfo.InvariantCulture) + " / " + p.MaxFps);
        }

        /// <summary>Le o arquivo, troca so as chaves conhecidas e preserva o resto intacto.</summary>
        internal static string MergeAssignments(string text, Dictionary<string, string> values, bool ignoreCase)
        {
            var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var next = new Dictionary<string, string>(values, comparer);
            var seen = new HashSet<string>(comparer);
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            for (int i = 0; i < lines.Count; i++) {
                var match = Regex.Match(lines[i], @"^(\s*)([A-Za-z0-9_]+)(\s*=\s*)(.*)$");
                string value;
                if (!match.Success || !next.TryGetValue(match.Groups[2].Value, out value)) continue;
                string key = match.Groups[2].Value;
                // Retain trailing comments on known numeric settings.
                var comment = Regex.Match(match.Groups[4].Value, ignoreCase ? @"(\s*[;#].*)$" : @"(\s*--.*)$");
                lines[i] = match.Groups[1].Value + key + match.Groups[3].Value + value + (comment.Success ? comment.Value : "");
                seen.Add(key);
            }
            foreach (var item in next.Where(k => !seen.Contains(k.Key))) lines.Add(item.Key + " = " + item.Value);
            return string.Join(newline, lines) + newline;
        }

        public static void StatusMods()
        {
            Ui.Title("INJETOR DE MODS");

            if (GameDir == null) { Ui.Err("Pasta do jogo não localizada."); return; }

            string preloader = Path.Combine(GameDir, "preloader_l.dll");
            string preBak = Path.Combine(GameDir, "preloader_l.dll_b");
            string modData = Path.Combine(GameDir, "FIFAModData");
            string mm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FIFA_Mod_Manager");

            Ui.Kv("Pasta do jogo", GameDir);
            Ui.Kv("preloader_l.dll", File.Exists(preloader) ? "presente (injeção ativa)" : "ausente");
            Ui.Kv("Backup do preloader", File.Exists(preBak) ? "presente" : "ausente");
            Ui.Kv("FIFAModData", Directory.Exists(modData) ? "presente" : "ausente");
            Ui.Kv("FIFA Mod Manager", Directory.Exists(mm) ? "instalado" : "não encontrado");

            if (Directory.Exists(modData))
            {
                try
                {
                    long bytes = new DirectoryInfo(modData).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    Ui.Kv("Tamanho dos mods", (bytes / 1024.0 / 1024.0).ToString("N0") + " MB");
                }
                catch { }
            }

            Ui.Blank();
            if (File.Exists(preloader) && Directory.Exists(modData))
                Ui.Ok("Sua instalação de mods está montada e o BOOST é compatível com ela.");

            Ui.Note("O monitor abre pela Steam e nao altera arquivos de mods ou anti-cheat. Para outro launcher, abra o jogo manualmente durante a espera.");
        }


        // ------------------------------------------------------------ Modo Partida
        public static void ModoPartida() { GameSession.Run(GameCatalog.Fifa, false); }

        // ------------------------------------------------------------ status
        public static void Status()
        {
            Ui.Title("EA SPORTS FC 26 — SITUAÇÃO");
            Ui.Kv("Pasta do jogo", GameDir ?? "não encontrada");
            Ui.Kv("Steam AppID", SteamAppId);
            Ui.Kv("Pasta de config", SettingsDir ?? "não encontrada");
            Ui.Kv("Jogo aberto agora", JogoAberto() ? "SIM — feche antes de aplicar perfil" : "não");

            if (IniPath != null && File.Exists(IniPath))
            {
                bool ro = (File.GetAttributes(IniPath) & FileAttributes.ReadOnly) != 0;
                Ui.Kv("Config travada", ro ? "sim (somente-leitura)" : "não");

                Ui.Blank();
                Ui.Info("Configuração atual do jogo:");
                foreach (var linha in File.ReadAllLines(IniPath))
                {
                    var m = Regex.Match(linha, @"^\s*([A-Za-z0-9_]+)\s*=\s*(.*)$");
                    if (!m.Success) continue;
                    string k = m.Groups[1].Value;
                    if (k == "RESOLUTIONWIDTH" || k == "RESOLUTIONHEIGHT" || k == "RENDERING_SCALE" ||
                        k == "MAX_FRAME_RATE" || k == "CLOTH_QUALITY" || k == "CROWD_QUALITY" ||
                        k == "DYNAMIC_RESOLUTION" || k == "WAITFORVSYNC")
                        Ui.Kv("  " + k, m.Groups[2].Value.Trim());
                }
            }
        }
    }
}

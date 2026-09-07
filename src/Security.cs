using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace F26Boost
{
    internal sealed class Achado
    {
        public string Nivel;      // ALTO / MEDIO / INFO
        public string Titulo;
        public string Detalhe;
        public string Caminho;
    }

    /// <summary>
    /// Varredura de coisas nocivas. SO RELATA — nao apaga, nao move, nao
    /// desinstala nada. E um "olha isso aqui", nao um antivirus.
    /// </summary>
    internal static class Security
    {
        private static readonly string[] PastasSuspeitas =
        {
            @"\AppData\Local\Temp\", @"\Windows\Temp\", @"\Downloads\",
            @"\AppData\Roaming\", @"\ProgramData\", @"\Users\Public\", @"\$Recycle.Bin\"
        };

        public static void Varredura()
        {
            Ui.Title("VARREDURA DE SEGURANÇA");
            Ui.Note("Isto não apaga nem move nada. Eu listo o que parece fora do lugar e "
                  + "explico o porquê; a decisão é sua.");
            Ui.Blank();

            var achados = new List<Achado>();

            StatusDefender(achados);
            Console.Write("  Analisando inicialização automática");
            Autoruns(achados);
            Console.Write(" · processos");
            ProcessosSuspeitos(achados);
            Console.Write(" · arquivo hosts");
            Hosts(achados);
            Console.Write(" · mods do jogo");
            ModsDoJogo(achados);
            Console.WriteLine(" · pronto.");
            Ui.Blank();

            Mostrar(achados);
            Salvar(achados);
        }

        // ------------------------------------------------------------ Defender
        private static void StatusDefender(List<Achado> a)
        {
            string o = Shell.Ps("$s=Get-MpComputerStatus; "
                + "'RT=' + $s.RealTimeProtectionEnabled + ';AS=' + $s.AntispywareEnabled "
                + "+ ';SIG=' + $s.AntivirusSignatureAge + ';TAMPER=' + $s.IsTamperProtected");

            bool rtOff = o.IndexOf("RT=False", StringComparison.OrdinalIgnoreCase) >= 0;
            if (rtOff)
                a.Add(new Achado
                {
                    Nivel = "ALTO",
                    Titulo = "Proteção em tempo real do Defender DESLIGADA",
                    Detalhe = "Se você não desligou de propósito, algum programa desligou por você — "
                            + "isso é comportamento típico de coisa ruim. Reative em Segurança do Windows."
                });

            var mSig = System.Text.RegularExpressions.Regex.Match(o, @"SIG=(\d+)");
            if (mSig.Success && int.Parse(mSig.Groups[1].Value) > 7)
                a.Add(new Achado
                {
                    Nivel = "MEDIO",
                    Titulo = "Assinaturas do Defender com " + mSig.Groups[1].Value + " dias",
                    Detalhe = "Atualize o Defender — assinatura velha não reconhece ameaça nova."
                });

            if (!rtOff && o.Length > 0)
                a.Add(new Achado { Nivel = "INFO", Titulo = "Defender ativo e com proteção em tempo real ligada", Detalhe = "" });
        }

        // ------------------------------------------------------------ autoruns
        private static void Autoruns(List<Achado> achados)
        {
            var chaves = new[]
            {
                new[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                new[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" },
                new[] { "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" },
                new[] { "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                new[] { "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" }
            };

            foreach (var c in chaves)
            {
                try
                {
                    using (var b = Reg.Root(c[0]))
                    using (var k = b.OpenSubKey(c[1], false))
                    {
                        if (k == null) continue;
                        foreach (var nome in k.GetValueNames())
                        {
                            string cmd = Convert.ToString(k.GetValue(nome)) ?? "";
                            AvaliarComando(achados, c[0] + @"\...\Run → " + nome, cmd);
                        }
                    }
                }
                catch { }
            }

            foreach (var pasta in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            })
            {
                try
                {
                    if (!Directory.Exists(pasta)) continue;
                    foreach (var f in Directory.GetFiles(pasta))
                    {
                        if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        AvaliarComando(achados, "Pasta Inicializar → " + Path.GetFileName(f), f);
                    }
                }
                catch { }
            }
        }

        private static void AvaliarComando(List<Achado> achados, string origem, string cmd)
        {
            string caminho = ExtrairCaminho(cmd);
            if (caminho == null) return;

            bool localRuim = PastasSuspeitas.Any(p => caminho.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
            string assinatura = Assinante(caminho);
            bool semAssinatura = assinatura == null;

            if (localRuim && semAssinatura)
                achados.Add(new Achado
                {
                    Nivel = "ALTO",
                    Titulo = "Inicia com o Windows, sem assinatura, de pasta temporária",
                    Detalhe = origem + "\nPrograma legítimo quase nunca é instalado em Temp/Downloads e "
                            + "quase sempre é assinado. Vale investigar o que é.",
                    Caminho = caminho
                });
            else if (localRuim)
                achados.Add(new Achado
                {
                    Nivel = "MEDIO",
                    Titulo = "Inicia com o Windows a partir de pasta de usuário",
                    Detalhe = origem + "\nAssinado por: " + assinatura,
                    Caminho = caminho
                });
            else if (semAssinatura && File.Exists(caminho))
                achados.Add(new Achado
                {
                    Nivel = "MEDIO",
                    Titulo = "Inicia com o Windows sem assinatura digital",
                    Detalhe = origem + "\nComum em programas pequenos e em ferramentas de mod. "
                            + "Só é problema se você não reconhece o nome.",
                    Caminho = caminho
                });
        }

        // ------------------------------------------------------------ processos
        private static void ProcessosSuspeitos(List<Achado> achados)
        {
            foreach (var p in Process.GetProcesses())
            {
                string caminho = null;
                try { caminho = p.MainModule != null ? p.MainModule.FileName : null; }
                catch { }
                finally { try { p.Dispose(); } catch { } }

                if (caminho == null) continue;
                if (!PastasSuspeitas.Any(s => caminho.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (caminho.IndexOf(App.BaseDir, StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string ass = Assinante(caminho);
                achados.Add(new Achado
                {
                    Nivel = ass == null ? "ALTO" : "MEDIO",
                    Titulo = "Processo rodando de pasta temporária" + (ass == null ? ", sem assinatura" : ""),
                    Detalhe = ass == null ? "Sem assinatura digital." : "Assinado por: " + ass,
                    Caminho = caminho
                });
            }
        }

        // ------------------------------------------------------------ hosts
        private static void Hosts(List<Achado> achados)
        {
            string h = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            try
            {
                if (!File.Exists(h)) return;
                var linhas = File.ReadAllLines(h)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .ToList();

                if (linhas.Count == 0) return;

                achados.Add(new Achado
                {
                    Nivel = linhas.Any(l => l.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0
                                         || l.IndexOf("ea.com", StringComparison.OrdinalIgnoreCase) >= 0
                                         || l.IndexOf("windowsupdate", StringComparison.OrdinalIgnoreCase) >= 0) ? "ALTO" : "MEDIO",
                    Titulo = "Arquivo hosts tem " + linhas.Count + " redirecionamento(s) manual(is)",
                    Detalhe = "Conteúdo:\n" + string.Join("\n", linhas.Take(12))
                            + "\n\nRedirecionar domínios da EA ou da Microsoft aqui é um jeito comum de "
                            + "burlar ativação ou de bloquear atualização — e também de sequestrar tráfego.",
                    Caminho = h
                });
            }
            catch { }
        }

        // ------------------------------------------------------------ mods
        private static void ModsDoJogo(List<Achado> achados)
        {
            string dir = GameFifa.GameDir;
            if (dir == null) return;

            foreach (var nome in new[] { "preloader_l.dll", "dinput8.dll", "version.dll", "winmm.dll", "d3d11.dll", "dsound.dll" })
            {
                string f = Path.Combine(dir, nome);
                if (!File.Exists(f)) continue;
                string ass = Assinante(f);
                achados.Add(new Achado
                {
                    Nivel = "INFO",
                    Titulo = "DLL de injeção de mod presente: " + nome,
                    Detalhe = (ass == null
                        ? "Sem assinatura digital — normal para carregador de mod."
                        : "Assinado por: " + ass)
                        + "\nEsperado na sua instalação: é assim que o FIFA Mod Manager entra no jogo. "
                        + "Só instale DLL desse tipo vinda de fonte que você conhece — é o mesmo mecanismo "
                        + "que um malware usaria para se pendurar no jogo.",
                    Caminho = f
                });
            }
        }

        // ------------------------------------------------------------ util
        private static string ExtrairCaminho(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int fim = cmd.IndexOf('"', 1);
                if (fim > 1) return Environment.ExpandEnvironmentVariables(cmd.Substring(1, fim - 1));
            }
            int esp = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (esp > 0) return Environment.ExpandEnvironmentVariables(cmd.Substring(0, esp + 4));
            return Environment.ExpandEnvironmentVariables(cmd.Split(' ')[0]);
        }

        /// <summary>Nome de quem assinou, ou null se nao houver assinatura. Heuristica barata.</summary>
        private static string Assinante(string caminho)
        {
            try
            {
                if (!File.Exists(caminho)) return null;
                var c = X509Certificate.CreateFromSignedFile(caminho);
                string s = c.Subject ?? "";
                var m = System.Text.RegularExpressions.Regex.Match(s, @"CN=([^,]+)");
                return m.Success ? m.Groups[1].Value.Trim('"') : s;
            }
            catch { return null; }
        }

        private static void Mostrar(List<Achado> achados)
        {
            var altos = achados.Where(x => x.Nivel == "ALTO").ToList();
            var medios = achados.Where(x => x.Nivel == "MEDIO").ToList();
            var infos = achados.Where(x => x.Nivel == "INFO").ToList();

            if (altos.Count == 0 && medios.Count == 0)
                Ui.Ok("Nada suspeito encontrado nos pontos verificados.");

            foreach (var g in new[]
            {
                new { L = altos,  C = ConsoleColor.Red,    T = "RISCO ALTO" },
                new { L = medios, C = ConsoleColor.Yellow, T = "VALE OLHAR" },
                new { L = infos,  C = ConsoleColor.DarkGray, T = "INFORMATIVO" }
            })
            {
                if (g.L.Count == 0) continue;
                Console.WriteLine();
                var old = Console.ForegroundColor;
                Console.ForegroundColor = g.C;
                Console.WriteLine("  ══ " + g.T + " (" + g.L.Count + ") ══");
                Console.ForegroundColor = old;

                foreach (var a in g.L)
                {
                    Console.ForegroundColor = g.C;
                    Console.WriteLine("   • " + a.Titulo);
                    Console.ForegroundColor = old;
                    if (!string.IsNullOrEmpty(a.Caminho)) Console.WriteLine("     " + a.Caminho);
                    foreach (var l in a.Detalhe.Split('\n'))
                        if (l.Trim().Length > 0) Console.WriteLine("     " + l.Trim());
                }
            }

            Ui.Blank();
            Ui.Note("Nada disso foi removido. Se algo aqui te parecer estranho, pesquise o nome do "
                  + "arquivo antes de mexer, ou rode uma varredura completa do Defender.");
        }

        private static void Salvar(List<Achado> achados)
        {
            try
            {
                string f = Path.Combine(App.LogDir, "relatorio-seguranca-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("RELATÓRIO DE SEGURANÇA — FPS Booster");
                sb.AppendLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                sb.AppendLine(new string('=', 70));
                foreach (var a in achados.OrderBy(x => x.Nivel))
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + a.Nivel + "] " + a.Titulo);
                    if (!string.IsNullOrEmpty(a.Caminho)) sb.AppendLine("  " + a.Caminho);
                    sb.AppendLine("  " + a.Detalhe.Replace("\n", "\n  "));
                }
                File.WriteAllText(f, sb.ToString(), Encoding.UTF8);
                Ui.Info("Relatório salvo em: " + f);
            }
            catch { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace F26Boost
{
    internal sealed class GameSpec
    {
        public string Id, Name, SteamId, Folder;
        public string[] ProcessNames;
        public bool IsZomboid { get { return Id == "zomboid"; } }
        public string InstallDir;
        public string Executable
        {
            get {
                if (InstallDir == null) return null;
                return ProcessNames.Select(n => Path.Combine(InstallDir, n + ".exe")).FirstOrDefault(File.Exists);
            }
        }
        public List<Process> Processes()
        {
            var found = new List<Process>();
            foreach (var name in ProcessNames)
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { if (p.SessionId == Process.GetCurrentProcess().SessionId) { found.Add(p); continue; } }
                    catch { }
                    p.Dispose();
                }
            // Java launchers are accepted only when their executable belongs to this installation.
            if (IsZomboid && InstallDir != null)
                foreach (string name in new[] { "java", "javaw" })
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try {
                            if (p.SessionId == Process.GetCurrentProcess().SessionId &&
                                p.MainModule.FileName.StartsWith(Path.GetFullPath(InstallDir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            { found.Add(p); continue; }
                        } catch { }
                        p.Dispose();
                    }
            return found;
        }
        public bool Running()
        {
            var ps = Processes(); bool result = ps.Count > 0;
            foreach (var p in ps) p.Dispose();
            return result;
        }
    }

    internal static class GameCatalog
    {
        public static readonly GameSpec Fifa = new GameSpec { Id = "fc26", Name = "EA SPORTS FC 26", SteamId = "3405690", Folder = "FC 26", ProcessNames = new[] { "FC26" } };
        public static readonly GameSpec Zomboid = new GameSpec { Id = "zomboid", Name = "Project Zomboid", SteamId = "108600", Folder = "ProjectZomboid", ProcessNames = new[] { "ProjectZomboid64", "ProjectZomboid32", "ProjectZomboid" } };
        public static readonly GameSpec[] All = { Fifa, Zomboid };

        public static void Detect()
        {
            foreach (var game in All)
            {
                game.InstallDir = null;
                string saved = Path.Combine(App.BackupDir, game.Id + "-path.txt");
                if (File.Exists(saved))
                {
                    string path = File.ReadAllText(saved).Trim();
                    if (Valid(game, path)) game.InstallDir = path;
                }
                if (game.InstallDir != null) continue;
                foreach (string lib in Libraries())
                {
                    try {
                        string folder = game.Folder;
                        string manifest = Path.Combine(lib, "steamapps", "appmanifest_" + game.SteamId + ".acf");
                        if (File.Exists(manifest)) {
                            var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"");
                            if (m.Success && Path.GetFileName(m.Groups[1].Value) == m.Groups[1].Value) folder = m.Groups[1].Value;
                        }
                        string dir = Path.Combine(lib, "steamapps", "common", folder);
                        if (Valid(game, dir)) { game.InstallDir = dir; break; }
                    } catch (Exception e) { Log.W("Biblioteca Steam: " + e.Message); }
                }
            }
        }

        internal static IEnumerable<string> Libraries()
        {
            string steam = Convert.ToString(Reg.Read("HKCU", @"Software\Valve\Steam", "SteamPath"));
            if (string.IsNullOrWhiteSpace(steam)) steam = Convert.ToString(Reg.Read("HKLM", @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            if (string.IsNullOrWhiteSpace(steam)) steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam.Replace('/', '\\') };
            try {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    result.Add(m.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\'));
            } catch (Exception e) { Log.W("Steam: " + e.Message); }
            return result;
        }
        private static bool Valid(GameSpec game, string path)
        {
            return Directory.Exists(path) && game.ProcessNames.Any(n => File.Exists(Path.Combine(path, n + ".exe")));
        }
        public static void SetPath(GameSpec game, string path)
        {
            path = Path.GetFullPath(path.Trim('"'));
            if (!Valid(game, path)) throw new IOException("A pasta não contém o executável de " + game.Name + ".");
            File.WriteAllText(Path.Combine(App.BackupDir, game.Id + "-path.txt"), path);
            game.InstallDir = path;
        }
    }
}

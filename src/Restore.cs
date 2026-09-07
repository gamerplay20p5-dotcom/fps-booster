using System;
using System.IO;
using System.Linq;

namespace F26Boost
{
    internal static class Restore
    {
        public static void Tudo()
        {
            if (GameCatalog.All.Any(g => g.Running())) throw new InvalidOperationException("Feche os jogos antes de restaurar configurações.");
            var items = Backup.All();
            if (items.Count == 0) { Ui.Info("Nenhuma alteração registrada neste conjunto de backups."); return; }
            Listar();
            if (!Ui.Confirm("Restaurar os itens listados?")) return;
            int ok = 0, errors = 0;
            // Restore service state before registry records for those services.
            foreach (var item in items.OrderBy(x => x.Key.StartsWith("SVC|") ? 0 : x.Key.StartsWith("POWER|plano_criado") ? 2 : 1)) {
                try {
                    bool restored = false;
                    if (item.Key.StartsWith("REG|")) restored = Reg.RestoreOne(item.Key, item.Value);
                    else if (item.Key.StartsWith("SVC|")) restored = Svc.RestoreOne(item.Key.Substring(4), item.Value);
                    else if (item.Key.StartsWith("FILE2|")) restored = ConfigFiles.RestoreOne(item.Key, item.Value);
                    else if (item.Key.StartsWith("GAMEFILE|")) {
                        string name = item.Key.Substring(9);
                        string target = name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? GameFifa.LuaPath : GameFifa.IniPath;
                        if (target == null || !File.Exists(item.Value)) throw new IOException("Backup ou destino legado não encontrado: " + name);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if (File.Exists(target)) File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
                        File.Copy(item.Value, target, true); restored = true;
                    }
                    else if (item.Key.StartsWith("DEFENDER|")) {
                        string target = item.Key.Substring(9);
                        string script = target.StartsWith("proc|")
                            ? "Remove-MpPreference -ExclusionProcess '" + target.Substring(5).Replace("'", "''") + "' -ErrorAction Stop"
                            : "Remove-MpPreference -ExclusionPath '" + target.Replace("'", "''") + "' -ErrorAction Stop";
                        Shell.Checked("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)), 40000);
                        restored = true;
                    }
                    else if (item.Key == "POWER|scheme_original") {
                        Guid original;
                        if (!Guid.TryParse(item.Value, out original)) throw new IOException("GUID original inválido.");
                        Shell.Checked("powercfg.exe", "/setactive " + original); restored = true;
                    }
                    else if (item.Key == "POWER|plano_criado") {
                        Guid created;
                        if (!Guid.TryParse(item.Value, out created)) throw new IOException("GUID do plano inválido.");
                        if (string.Equals(PowerCpu.ActiveSchemeGuid(), created.ToString(), StringComparison.OrdinalIgnoreCase))
                            throw new IOException("O plano criado ainda está ativo; restauração original pendente.");
                        if (Shell.Checked("powercfg.exe", "/list").IndexOf(created.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
                            Shell.Checked("powercfg.exe", "/delete " + created);
                        restored = true;
                    }
                    else if (item.Key == "GAME|config_travada") {
                        foreach (string path in new[] { GameFifa.IniPath, GameFifa.LuaPath })
                            if (path != null && File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                        restored = true;
                    }
                    if (!restored) throw new IOException("Item não restaurado: " + item.Key);
                    Backup.Forget(item.Key); ok++;
                } catch (Exception e) { errors++; Ui.Warn(e.Message); }
            }
            Ui.Info(ok + " itens restaurados; " + errors + " pendentes. Falhas permanecem registradas para tentar novamente.");
            if (App.LegacyBackup) Ui.Note("O legado não guardava todos os atributos nem a origem de exclusões preexistentes. Essas limitações não podem ser reconstruídas. Reinicie após restaurar ajustes antigos de serviços/escalonador.");
        }
        public static void Listar()
        {
            Ui.Title("ALTERAÇÕES REGISTRADAS");
            Ui.Kv("Pasta de backup", App.BackupDir);
            foreach (var item in Backup.All()) Console.WriteLine("  " + item.Key);
            Ui.Kv("Total", Backup.Count.ToString());
        }
    }
}

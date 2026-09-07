using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace F26Boost
{
    internal static class ConfigFiles
    {
        public static void Remember(string path)
        {
            path = Path.GetFullPath(path);
            string key = "FILE2|" + path;
            if (Backup.Has(key)) {
                string saved = Backup.Get(key);
                if (saved != Backup.ABSENT && !File.Exists(saved.Substring(saved.IndexOf('|') + 1)))
                    throw new IOException("Backup original ausente; escrita cancelada: " + path);
                return;
            }
            if (!File.Exists(path)) { Backup.Remember(key, Backup.ABSENT); return; }
            string copy = Path.Combine(App.BackupDir, Guid.NewGuid().ToString("N") + ".bak");
            File.Copy(path, copy, false);
            Backup.Remember(key, ((int)File.GetAttributes(path)).ToString() + "|" + copy);
        }

        public static void Write(string path, string text)
        {
            Remember(path);
            WriteAtomic(path, text);
        }

        internal static void WriteAtomic(string path, string text)
        {
            WriteBytesAtomic(path, new UTF8Encoding(false).GetBytes(text));
        }

        internal static void WriteBytesAtomic(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            FileAttributes attrs = File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal;
            try {
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path)) {
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                    File.Replace(tmp, path, null);
                } else File.Move(tmp, path);
            } finally {
                if (File.Exists(tmp)) File.Delete(tmp);
                if (File.Exists(path)) File.SetAttributes(path, attrs);
            }
        }

        public static void WriteBatch(Dictionary<string, string> files)
        {
            var previous = new Dictionary<string, byte[]>();
            var written = new List<string>();
            foreach (var file in files) {
                previous[file.Key] = File.Exists(file.Key) ? File.ReadAllBytes(file.Key) : null;
                Remember(file.Key);
            }
            try {
                foreach (var file in files) { WriteAtomic(file.Key, file.Value); written.Add(file.Key); }
            } catch {
                foreach (var path in written) {
                    try {
                        if (previous[path] != null) WriteBytesAtomic(path, previous[path]);
                        else if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                    } catch (Exception e) { Ui.Warn("Reversão parcial; use Restaurar: " + e.Message); }
                }
                throw;
            }
        }

        public static bool RestoreOne(string key, string value)
        {
            string path = key.Substring("FILE2|".Length);
            if (value == Backup.ABSENT) {
                if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                return true;
            }
            int split = value.IndexOf('|');
            FileAttributes attrs = (FileAttributes)int.Parse(value.Substring(0, split));
            string source = value.Substring(split + 1);
            if (!File.Exists(source)) throw new IOException("Backup ausente: " + source);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".restore";
            try {
                File.Copy(source, temp);
                File.SetAttributes(temp, FileAttributes.Normal);
                if (File.Exists(path)) {
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                    File.Replace(temp, path, null);
                } else File.Move(temp, path);
                File.SetAttributes(path, attrs);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
    }

    internal static class GameZomboid
    {
        internal static string PatchHeap(string json, int heapMb)
        {
            if (heapMb < 2048 || heapMb > 12288) throw new ArgumentOutOfRangeException("heapMb");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            object raw;
            if (root == null || !root.TryGetValue("vmArgs", out raw) || !(raw is object[]))
                throw new InvalidDataException("Formato não reconhecido: esperado vmArgs como lista. Nada alterado.");
            var args = (object[])raw;
            if (args.Any(a => !(a is string))) throw new InvalidDataException("vmArgs contém valores inválidos.");
            // Replace tokens in place; preserve formatting, classpath, collectors and every unknown setting.
            var xmx = args.Cast<string>().Where(a => a.StartsWith("-Xmx", StringComparison.Ordinal)).ToArray();
            var xms = args.Cast<string>().Where(a => a.StartsWith("-Xms", StringComparison.Ordinal)).ToArray();
            if (HeapArguments(root).Count() != xmx.Length + xms.Length)
                throw new InvalidDataException("Há limites de heap em outra seção do launcher. Ajuste automático cancelado.");
            if (xmx.Length != 1 || xms.Length > 1 || !Regex.IsMatch(xmx[0], @"^-Xmx\d+[mMgG]$"))
                throw new InvalidDataException("Limite Java ausente, duplicado ou desconhecido. Ajuste cancelado.");
            string patched = ReplaceUniqueToken(json, xmx[0], "-Xmx" + heapMb + "m", serializer);
            if (xms.Length == 1) {
                var m = Regex.Match(xms[0], @"^-Xms(\d+)([mMgG])$");
                if (!m.Success) throw new InvalidDataException("Xms não reconhecido.");
                long initial = long.Parse(m.Groups[1].Value) * (m.Groups[2].Value.Equals("g", StringComparison.OrdinalIgnoreCase) ? 1024 : 1);
                if (initial > Math.Min(1024, heapMb)) patched = ReplaceUniqueToken(patched, xms[0], "-Xms1024m", serializer);
            }
            serializer.DeserializeObject(patched);
            return patched;
        }
        private static IEnumerable<string> HeapArguments(object value)
        {
            var text = value as string;
            if (text != null) {
                if (text.StartsWith("-Xmx", StringComparison.Ordinal) || text.StartsWith("-Xms", StringComparison.Ordinal)) yield return text;
                yield break;
            }
            var map = value as Dictionary<string, object>;
            var list = value as object[];
            if (map != null) foreach (var item in map.Values) foreach (var arg in HeapArguments(item)) yield return arg;
            if (list != null) foreach (var item in list) foreach (var arg in HeapArguments(item)) yield return arg;
        }
        private static string ReplaceUniqueToken(string json, string oldValue, string newValue, JavaScriptSerializer serializer)
        {
            string token = serializer.Serialize(oldValue);
            if (Regex.Matches(json, Regex.Escape(token)).Count != 1)
                throw new InvalidDataException("Argumento Java ambíguo; arquivo preservado.");
            return json.Replace(token, serializer.Serialize(newValue));
        }

        public static void Apply(Calibration c)
        {
            var game = GameCatalog.Zomboid;
            if (game.Running()) throw new InvalidOperationException("Feche o Project Zomboid antes de ajustar a memória Java.");
            if (game.InstallDir == null) throw new IOException("Informe a pasta do Project Zomboid pela opção P.");
            if (c.HeapMb == 0) throw new InvalidOperationException("RAM insuficiente para reservar heap e margem para Windows/GPU.");
            string path = Path.Combine(game.InstallDir, "ProjectZomboid64.json");
            if (!File.Exists(path)) throw new IOException("ProjectZomboid64.json não encontrado. Launcher alternativo/32 bits não é alterado.");
            string text = PatchHeap(File.ReadAllText(path), c.HeapMb);
            if (game.Running()) throw new InvalidOperationException("O jogo abriu durante o ajuste. Tente novamente com ele fechado.");
            ConfigFiles.Write(path, text);
            Ui.Ok("Heap máximo do cliente: " + c.HeapMb + " MB. Vale no próximo lançamento padrão de 64 bits.");
            Ui.Note("O limite não reserva toda essa RAM imediatamente. Mods, memória nativa e servidor hospedado consomem memória adicional. Atualizações/verificação da Steam podem substituir este arquivo; recalibre depois.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MdViewer
{
    /// <summary>One entry of the font picker: either a system stack or one of the
    /// Google Fonts that ship inside the executable.</summary>
    internal sealed class FontChoice
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Category = string.Empty;
        public bool Arabic;

        /// <summary>The CSS font-family value used by the preview.</summary>
        public string Stack = string.Empty;

        /// <summary>True when the family is one of the bundled Google Fonts.</summary>
        public bool Embedded;

        /// <summary>Style ("regular", "bold", "italic", "bolditalic") to file name.</summary>
        public readonly Dictionary<string, string> Faces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The bundled typography. The WOFF files live inside the executable
    /// and are unpacked into the local application folder the first time a family
    /// is used, because the IE11 engine that draws the preview can only load a
    /// font from a file or from a data: URI, and files are the cheaper option.</summary>
    internal static class Fonts
    {
        private const string ResourcePrefix = "MdViewer.fonts.";

        private static readonly List<FontChoice> choices = new List<FontChoice>();
        private static readonly Dictionary<string, string> unpacked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool loaded;

        /// <summary>Every family the picker offers, system stacks first.</summary>
        public static IList<FontChoice> All
        {
            get { Load(); return choices; }
        }

        /// <summary>The families that can be used for code blocks.</summary>
        public static IList<FontChoice> Mono
        {
            get
            {
                Load();
                List<FontChoice> list = new List<FontChoice>();
                for (int i = 0; i < choices.Count; i++)
                {
                    if (choices[i].Category == "Mono") list.Add(choices[i]);
                }
                return list;
            }
        }

        public static string CacheFolder
        {
            get { return Path.Combine(Settings.AppFolder, "fonts"); }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;

            choices.Add(new FontChoice
            {
                Id = "system-serif",
                Name = "System Serif",
                Category = "Serif",
                Stack = "Georgia, 'Times New Roman', serif"
            });

            choices.Add(new FontChoice
            {
                Id = "system-sans",
                Name = "System Sans",
                Category = "Sans",
                Stack = "'Segoe UI', Arial, Helvetica, sans-serif"
            });

            choices.Add(new FontChoice
            {
                Id = "system-mono",
                Name = "System Mono",
                Category = "Mono",
                Stack = "'Cascadia Mono', Consolas, 'Courier New', monospace"
            });

            foreach (string line in ReadManifest())
            {
                string[] parts = line.Split('|');
                if (parts.Length < 5) continue;

                FontChoice choice = new FontChoice();
                choice.Id = parts[0].Trim();
                choice.Name = parts[1].Trim();
                choice.Category = parts[2].Trim();
                choice.Arabic = parts[3].Trim() == "arabic";
                choice.Embedded = true;

                foreach (string face in parts[4].Split(','))
                {
                    int colon = face.IndexOf(':');
                    if (colon <= 0) continue;
                    choice.Faces[face.Substring(0, colon).Trim()] = face.Substring(colon + 1).Trim();
                }

                if (choice.Faces.Count == 0) continue;
                choice.Stack = "'" + choice.Name + "', " + FallbackFor(choice.Category);
                choices.Add(choice);
            }
        }

        private static string FallbackFor(string category)
        {
            if (category == "Mono") return "Consolas, 'Courier New', monospace";
            if (category == "Sans") return "'Segoe UI', Arial, sans-serif";
            return "Georgia, 'Times New Roman', serif";
        }

        private static IEnumerable<string> ReadManifest()
        {
            List<string> lines = new List<string>();
            byte[] data = ReadResource("manifest.txt");
            if (data == null) return lines;

            string text = DecodeUtf8(data);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                lines.Add(line);
            }
            return lines;
        }

        public static FontChoice Find(string id)
        {
            Load();
            for (int i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return choices[i];
            }
            return null;
        }

        /// <summary>The CSS font-family value for a family id, with a safe fallback
        /// when the id is unknown.</summary>
        public static string StackFor(string id)
        {
            FontChoice choice = Find(id);
            if (choice == null) return "Georgia, 'Times New Roman', serif";
            return choice.Stack;
        }

        /// <summary>The @font-face rules for a bundled family. Empty for the
        /// system stacks, which the machine already knows.</summary>
        public static string FaceCss(string id)
        {
            FontChoice choice = Find(id);
            if (choice == null || !choice.Embedded) return string.Empty;

            StringBuilder css = new StringBuilder();
            foreach (KeyValuePair<string, string> face in choice.Faces)
            {
                string path;
                if (!Unpack(face.Value, out path)) continue;

                string weight = "400";
                string style = "normal";
                if (face.Key == "bold") weight = "700";
                else if (face.Key == "italic") style = "italic";
                else if (face.Key == "bolditalic") { weight = "700"; style = "italic"; }

                css.Append("@font-face{font-family:'").Append(choice.Name).Append("';")
                    .Append("font-style:").Append(style).Append(";")
                    .Append("font-weight:").Append(weight).Append(";")
                    .Append("src:url('").Append(ToFileUri(path)).Append("') format('woff');}\r\n");
            }
            return css.ToString();
        }

        /// <summary>Writes one font into the local cache and returns its path.</summary>
        private static bool Unpack(string fileName, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(fileName)) return false;

            string cached;
            if (unpacked.TryGetValue(fileName, out cached) && File.Exists(cached))
            {
                path = cached;
                return true;
            }

            byte[] data = ReadResource(fileName);
            if (data == null || data.Length == 0) return false;

            string folder = CacheFolder;
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            }
            catch
            {
                // Without a cache folder the family simply falls back.
                return false;
            }

            string target = Path.Combine(folder, fileName);
            try
            {
                bool needsWrite = true;
                if (File.Exists(target))
                {
                    needsWrite = new FileInfo(target).Length != data.Length;
                }

                if (needsWrite)
                {
                    string temp = target + ".tmp";
                    File.WriteAllBytes(temp, data);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(temp, target);
                }
            }
            catch
            {
                // A locked or read-only cache still leaves the system stack usable.
                return false;
            }

            unpacked[fileName] = target;
            path = target;
            return true;
        }

        private static byte[] ReadResource(string fileName)
        {
            try
            {
                Assembly assembly = typeof(Fonts).Assembly;
                string name = ResourcePrefix + fileName;
                using (Stream stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    byte[] buffer = new byte[stream.Length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int step = stream.Read(buffer, read, buffer.Length - read);
                        if (step <= 0) break;
                        read += step;
                    }
                    if (read != buffer.Length) return null;
                    return buffer;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string DecodeUtf8(byte[] data)
        {
            try
            {
                return new UTF8Encoding(false).GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string ToFileUri(string path)
        {
            try
            {
                return new Uri(path).AbsoluteUri;
            }
            catch
            {
                return path;
            }
        }
    }
}

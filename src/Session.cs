using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MdViewer
{
    /// <summary>Preferences, stored as a tiny key=value file in %LOCALAPPDATA%\MD Viewer.</summary>
    internal sealed class Settings
    {
        public string Theme = "light";
        public string View = "split";
        public double Split = 0.5;
        public bool Sync = true;
        public bool Wrap = true;
        public bool Autocomplete = true;
        public bool AutoPairs = true;
        public bool Maximized;
        public Rectangle Bounds = Rectangle.Empty;
        public string Font = "system-serif";
        public string CodeFont = "system-mono";
        public int FontSize = 100;
        public double Zoom = 1.0;

        public static string AppFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MD Viewer");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(AppFolder, "settings.ini"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "theme":
                            s.Theme = value == "dark" ? "dark" : "light";
                            break;
                        case "view":
                            if (value == "write" || value == "split" || value == "read") s.View = value;
                            break;
                        case "split":
                            double split;
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out split))
                                s.Split = Math.Min(0.82, Math.Max(0.18, split));
                            break;
                        case "sync":
                            s.Sync = value != "0";
                            break;
                        case "wrap":
                            s.Wrap = value != "0";
                            break;
                        case "autocomplete":
                            s.Autocomplete = value != "0";
                            break;
                        case "pairs":
                            s.AutoPairs = value != "0";
                            break;
                        case "maximized":
                            s.Maximized = value == "1";
                            break;
                        case "font":
                            if (value.Length > 0) s.Font = value;
                            break;
                        case "codefont":
                            if (value.Length > 0) s.CodeFont = value;
                            break;
                        case "fontsize":
                            int size;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                                s.FontSize = Math.Min(200, Math.Max(70, size));
                            break;
                        case "zoom":
                            double zoom;
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out zoom))
                                s.Zoom = Math.Min(3.0, Math.Max(0.4, zoom));
                            break;
                        case "bounds":
                            s.Bounds = ParseBounds(value);
                            break;
                    }
                }
            }
            catch
            {
                // A broken settings file should never stop the editor from opening.
            }
            return s;
        }

        private static Rectangle ParseBounds(string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length != 4) return Rectangle.Empty;
            int x, y, w, h;
            if (!int.TryParse(parts[0], out x)) return Rectangle.Empty;
            if (!int.TryParse(parts[1], out y)) return Rectangle.Empty;
            if (!int.TryParse(parts[2], out w)) return Rectangle.Empty;
            if (!int.TryParse(parts[3], out h)) return Rectangle.Empty;
            if (w < 400 || h < 300) return Rectangle.Empty;
            return new Rectangle(x, y, w, h);
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppFolder);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# MD Viewer settings");
                sb.AppendLine("theme=" + Theme);
                sb.AppendLine("view=" + View);
                sb.AppendLine("split=" + Split.ToString("0.####", CultureInfo.InvariantCulture));
                sb.AppendLine("sync=" + (Sync ? "1" : "0"));
                sb.AppendLine("wrap=" + (Wrap ? "1" : "0"));
                sb.AppendLine("autocomplete=" + (Autocomplete ? "1" : "0"));
                sb.AppendLine("pairs=" + (AutoPairs ? "1" : "0"));
                sb.AppendLine("maximized=" + (Maximized ? "1" : "0"));
                sb.AppendLine("font=" + Font);
                sb.AppendLine("codefont=" + CodeFont);
                sb.AppendLine("fontsize=" + FontSize.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("zoom=" + Zoom.ToString("0.##", CultureInfo.InvariantCulture));
                if (Bounds != Rectangle.Empty)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "bounds={0},{1},{2},{3}",
                        Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height));
                }
                Store.WriteAtomic(FilePath, sb.ToString());
            }
            catch
            {
                // Preferences are a convenience, not a requirement.
            }
        }
    }

    /// <summary>The working document, cached between runs (draft + last saved baseline).</summary>
    internal sealed class DocumentSession
    {
        public string Name = "Untitled.md";
        public string FilePath = "";
        public string Draft = "";
        public string Baseline = "";
        public bool Exists;

        private static string DraftPath { get { return Path.Combine(Settings.AppFolder, "draft.md"); } }
        private static string BaselinePath { get { return Path.Combine(Settings.AppFolder, "baseline.md"); } }
        private static string MetaPath { get { return Path.Combine(Settings.AppFolder, "document.ini"); } }

        public static DocumentSession Load()
        {
            DocumentSession s = new DocumentSession();
            try
            {
                if (File.Exists(DraftPath))
                {
                    s.Draft = File.ReadAllText(DraftPath, Encoding.UTF8);
                    s.Baseline = File.Exists(BaselinePath)
                        ? File.ReadAllText(BaselinePath, Encoding.UTF8)
                        : s.Draft;
                    s.Exists = true;
                }

                if (File.Exists(MetaPath))
                {
                    foreach (string raw in File.ReadAllLines(MetaPath, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string value = line.Substring(eq + 1).Trim();
                        if (key == "name" && value.Length > 0) s.Name = value;
                        if (key == "path") s.FilePath = value;
                    }
                }
            }
            catch
            {
                // Start clean if the cache is unreadable.
            }
            return s;
        }

        public void Save(string draft, string baseline, string name, string filePath)
        {
            try
            {
                Directory.CreateDirectory(Settings.AppFolder);
                WriteIfChanged(DraftPath, draft);
                WriteIfChanged(BaselinePath, baseline);
                Store.WriteAtomic(MetaPath, "name=" + (name ?? "") + "\r\npath=" + (filePath ?? "") + "\r\n");
                Exists = true;
            }
            catch
            {
                // Autosave is best-effort.
            }
        }

        private static void WriteIfChanged(string path, string text)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == text) return;
            }
            catch
            {
                // Compare failure means we simply rewrite the file.
            }
            Store.WriteAtomic(path, text ?? string.Empty);
        }
    }

    internal static class Store
    {
        public static void WriteAtomic(string path, string text)
        {
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, text ?? string.Empty, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.WriteAllText(path, text ?? string.Empty, new UTF8Encoding(false));
                }
                catch
                {
                    // Give up quietly; callers treat storage as optional.
                }
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}

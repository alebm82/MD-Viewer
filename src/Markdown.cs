using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer
{
    /// <summary>
    /// Dependency-free Markdown to HTML. Raw HTML in the source is escaped and
    /// shown as text; javascript:, vbscript: and non-image data: URLs are rejected.
    /// </summary>
    internal static class Markdown
    {
        private sealed class Ref
        {
            public string Href;
            public string Title;
        }

        private sealed class ListMarker
        {
            public bool Ordered;
            public int Start = 1;
            public int Indent;
            public int ContentIndent;
            public string Rest;
        }

        private sealed class ListItem
        {
            public List<string> Lines;
            public bool PendingBlank;
        }

        private sealed class LinkTarget
        {
            public string Label;
            public string Id;
            public string Href;
            public string Title;
            public int End;
        }

        private static Dictionary<string, Ref> refs;

        /// <summary>The folder of the document that is being rendered. Pictures
        /// with a relative path are resolved against it.</summary>
        private static string baseFolder = string.Empty;

        private static readonly Regex RefDef = new Regex(
            "^ {0,3}\\[([^\\]]+)\\]:[ \\t]*(?:<([^>]*)>|(\\S+))(?:[ \\t]+(?:\"([^\"]*)\"|'([^']*)'|\\(([^)]*)\\)))?[ \\t]*$",
            RegexOptions.Compiled);

        private static readonly Regex FenceOpen = new Regex("^ {0,3}(`{3,}|~{3,})(.*)$", RegexOptions.Compiled);
        private static readonly Regex Atx = new Regex("^ {0,3}(#{1,6})(?:[ \\t]+(.*?))?[ \\t]*$", RegexOptions.Compiled);
        private static readonly Regex HrLine = new Regex(
            "^ {0,3}(?:(?:\\*[ \\t]*){3,}|(?:-[ \\t]*){3,}|(?:_[ \\t]*){3,})$", RegexOptions.Compiled);
        private static readonly Regex BlockStart = new Regex(
            "^ {0,3}(?:#{1,6}(?:[ \\t]|$)|>|(?:[-*+]|\\d{1,9}[.)])[ \\t]+|`{3,}|~{3,})", RegexOptions.Compiled);
        private static readonly Regex QuoteLine = new Regex("^ {0,3}> ?(.*)$", RegexOptions.Compiled);
        private static readonly Regex SetextLine = new Regex("^ {0,3}(=+|-+)[ \\t]*$", RegexOptions.Compiled);
        private static readonly Regex TableDelimiter = new Regex(
            "^ {0,3}\\|?[ \\t]*:?-+:?[ \\t]*(?:\\|[ \\t]*:?-+:?[ \\t]*)*\\|?[ \\t]*$", RegexOptions.Compiled);
        private static readonly Regex AutoUrl = new Regex(
            "\\G<((?:https?://|mailto:)[^<>\\s]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AutoMail = new Regex(
            "\\G<([^\\s<>@]+@[^\\s<>@]+\\.[^\\s<>@]+)>", RegexOptions.Compiled);
        private static readonly Regex SchemeCheck = new Regex("^[a-z][a-z0-9+.\\-]*:", RegexOptions.IgnoreCase);
        private static readonly Regex SafeSchemes = new Regex("^(https?|mailto|file):", RegexOptions.IgnoreCase);

        private const string Escapable = "\\`*_{}[]()#+-.!>~|";

        /* ==================================================================
           ENTRY POINT
           ================================================================== */

        public static string ToHtml(string source)
        {
            return ToHtml(source, string.Empty);
        }

        /// <summary>Renders Markdown to HTML. <paramref name="folder"/> is the
        /// folder of the document: pictures that are written as a relative path
        /// are resolved against it, so a file that sits next to the document
        /// shows up in the reader.</summary>
        public static string ToHtml(string source, string folder)
        {
            if (source == null) source = string.Empty;
            string previousFolder = baseFolder;
            baseFolder = folder ?? string.Empty;
            try
            {
                string normalized = source.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
                string[] lines = CollectReferences(normalized.Split('\n'));

                StringBuilder sb = new StringBuilder(normalized.Length + 256);
                ParseBlocks(lines, 0, sb, false);
                return sb.ToString();
            }
            finally
            {
                baseFolder = previousFolder;
            }
        }

        /* ==================================================================
           REFERENCES
           ================================================================== */

        private static string NormalizeRef(string id)
        {
            if (id == null) return string.Empty;
            return Regex.Replace(id.Trim().ToLowerInvariant(), "\\s+", " ");
        }

        private static string[] CollectReferences(string[] lines)
        {
            refs = new Dictionary<string, Ref>(StringComparer.Ordinal);
            List<string> kept = new List<string>(lines.Length);
            char fenceChar = '\0';
            int fenceLen = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                Match fm = FenceOpen.Match(line);
                if (fm.Success)
                {
                    char c = fm.Groups[1].Value[0];
                    int len = fm.Groups[1].Value.Length;
                    if (fenceChar == '\0')
                    {
                        fenceChar = c;
                        fenceLen = len;
                    }
                    else if (c == fenceChar && len >= fenceLen)
                    {
                        fenceChar = '\0';
                    }
                    kept.Add(line);
                    continue;
                }
                if (fenceChar != '\0')
                {
                    kept.Add(line);
                    continue;
                }

                Match def = RefDef.Match(line);
                if (def.Success && !def.Groups[1].Value.Trim().StartsWith("^", StringComparison.Ordinal))
                {
                    string id = NormalizeRef(def.Groups[1].Value);
                    if (!refs.ContainsKey(id))
                    {
                        Ref r = new Ref();
                        r.Href = def.Groups[2].Success && def.Groups[2].Value.Length > 0
                            ? def.Groups[2].Value
                            : (def.Groups[3].Success ? def.Groups[3].Value : string.Empty);
                        r.Title = def.Groups[4].Success ? def.Groups[4].Value
                            : def.Groups[5].Success ? def.Groups[5].Value
                            : def.Groups[6].Success ? def.Groups[6].Value : string.Empty;
                        refs[id] = r;
                    }
                    kept.Add(string.Empty);
                    continue;
                }

                kept.Add(line);
            }
            return kept.ToArray();
        }

        /* ==================================================================
           BLOCK LEVEL
           ================================================================== */

        private static void ParseBlocks(string[] lines, int depth, StringBuilder sb, bool suppressParagraphs)
        {
            if (depth > 24)
            {
                sb.Append("<p>").Append(EscapeText(string.Join(" ", lines))).Append("</p>");
                return;
            }

            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                if (line.Trim().Length == 0)
                {
                    i++;
                    continue;
                }

                /* fenced code */
                Match fm = FenceOpen.Match(line);
                if (fm.Success)
                {
                    char fenceChar = fm.Groups[1].Value[0];
                    int fenceLen = fm.Groups[1].Value.Length;
                    string info = fm.Groups[2].Value.Trim();
                    List<string> body = new List<string>();
                    i++;
                    string closer = "^ {0,3}" + Regex.Escape(fenceChar.ToString()) +
                                    "{" + fenceLen + ",}[ \\t]*$";
                    Regex closeRe = new Regex(closer, RegexOptions.Compiled);
                    while (i < lines.Length)
                    {
                        if (closeRe.IsMatch(lines[i]))
                        {
                            i++;
                            break;
                        }
                        body.Add(lines[i]);
                        i++;
                    }
                    AppendCodeBlock(sb, string.Join("\n", body.ToArray()), info);
                    continue;
                }

                /* ATX heading */
                Match hm = Atx.Match(line);
                if (hm.Success)
                {
                    int level = hm.Groups[1].Value.Length;
                    string content = hm.Groups[2].Success ? hm.Groups[2].Value : string.Empty;
                    content = Regex.Replace(content, "[ \\t]+#+[ \\t]*$", "");
                    string tag = "h" + level;
                    sb.Append('<').Append(tag).Append(DirAttrs(content)).Append('>');
                    sb.Append(Inline(content, depth, false));
                    sb.Append("</").Append(tag).Append('>');
                    i++;
                    continue;
                }

                /* thematic break */
                if (HrLine.IsMatch(line))
                {
                    sb.Append("<hr>");
                    i++;
                    continue;
                }

                /* blockquote */
                if (QuoteLine.IsMatch(line))
                {
                    List<string> quoted = new List<string>();
                    while (i < lines.Length)
                    {
                        Match qm = QuoteLine.Match(lines[i]);
                        if (qm.Success)
                        {
                            quoted.Add(qm.Groups[1].Value);
                            i++;
                            continue;
                        }
                        if (lines[i].Trim().Length == 0)
                        {
                            if (i + 1 < lines.Length && QuoteLine.IsMatch(lines[i + 1]))
                            {
                                quoted.Add(string.Empty);
                                i++;
                                continue;
                            }
                            break;
                        }
                        if (!StartsBlock(lines[i]))
                        {
                            quoted.Add(lines[i]);
                            i++;
                            continue;
                        }
                        break;
                    }
                    sb.Append("<blockquote>");
                    ParseBlocks(quoted.ToArray(), depth + 1, sb, false);
                    sb.Append("</blockquote>");
                    continue;
                }

                /* list */
                ListMarker marker = ParseMarker(line);
                if (marker != null)
                {
                    i = ParseList(lines, i, marker, depth, sb);
                    continue;
                }

                /* table */
                if (IsTableStart(lines, i))
                {
                    i = ParseTable(lines, i, depth, sb);
                    continue;
                }

                /* indented code */
                if (line.StartsWith("    ", StringComparison.Ordinal))
                {
                    List<string> codeLines = new List<string>();
                    while (i < lines.Length)
                    {
                        if (lines[i].StartsWith("    ", StringComparison.Ordinal))
                        {
                            codeLines.Add(lines[i].Substring(4));
                            i++;
                            continue;
                        }
                        if (lines[i].Trim().Length == 0 &&
                            i + 1 < lines.Length && lines[i + 1].StartsWith("    ", StringComparison.Ordinal))
                        {
                            codeLines.Add(string.Empty);
                            i++;
                            continue;
                        }
                        break;
                    }
                    AppendCodeBlock(sb, string.Join("\n", codeLines.ToArray()), string.Empty);
                    continue;
                }

                /* paragraph, possibly setext heading */
                List<string> para = new List<string>();
                int setext = 0;
                while (i < lines.Length)
                {
                    string ln = lines[i];
                    if (ln.Trim().Length == 0) break;

                    if (para.Count > 0)
                    {
                        Match sm = SetextLine.Match(ln);
                        if (sm.Success)
                        {
                            setext = sm.Groups[1].Value[0] == '=' ? 1 : 2;
                            i++;
                            break;
                        }
                        if (StartsBlock(ln)) break;
                        if (IsTableStart(lines, i)) break;
                    }

                    para.Add(StripIndent(ln));
                    i++;
                }

                if (para.Count == 0)
                {
                    i++;
                    continue;
                }

                string joined = string.Join("\n", para.ToArray());
                string inlineHtml = Inline(joined, depth, false);
                if (setext > 0)
                {
                    string tag = "h" + setext;
                    sb.Append('<').Append(tag).Append(DirAttrs(joined)).Append('>');
                    sb.Append(inlineHtml);
                    sb.Append("</").Append(tag).Append('>');
                }
                else if (suppressParagraphs)
                {
                    sb.Append(inlineHtml);
                }
                else
                {
                    sb.Append("<p").Append(DirAttrs(joined)).Append('>');
                    sb.Append(inlineHtml);
                    sb.Append("</p>");
                }
            }
        }

        private static void AppendCodeBlock(StringBuilder sb, string code, string info)
        {
            string lang = string.Empty;
            if (!string.IsNullOrEmpty(info))
            {
                string[] parts = info.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) lang = parts[0];
            }

            sb.Append("<div class=\"code\"><div class=\"code-head\"><span class=\"code-lang\">");
            sb.Append(EscapeText(lang.Length > 0 ? lang : "text"));
            sb.Append("</span></div><pre><code");
            if (lang.Length > 0)
            {
                sb.Append(" class=\"language-").Append(EscapeAttr(Regex.Replace(lang, "[^\\w-]", ""))).Append('"');
            }
            sb.Append('>').Append(EscapeText(code)).Append("</code></pre></div>");
        }

        private static ListMarker ParseMarker(string line)
        {
            Match m = Regex.Match(line, "^( {0,3})([-*+])([ \\t]+)(.*)$");
            if (m.Success && m.Groups[1].Value.Length <= 3)
            {
                ListMarker lm = new ListMarker();
                lm.Ordered = false;
                lm.Indent = m.Groups[1].Value.Length;
                lm.ContentIndent = lm.Indent + 1 + m.Groups[3].Value.Length;
                lm.Rest = m.Groups[4].Value;
                return lm;
            }

            m = Regex.Match(line, "^( {0,3})(\\d{1,9})([.)])([ \\t]+)(.*)$");
            if (m.Success && m.Groups[1].Value.Length <= 3)
            {
                ListMarker lm = new ListMarker();
                lm.Ordered = true;
                int start;
                if (int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out start))
                    lm.Start = start;
                lm.Indent = m.Groups[1].Value.Length;
                lm.ContentIndent = lm.Indent + m.Groups[2].Value.Length + 1 + m.Groups[4].Value.Length;
                lm.Rest = m.Groups[5].Value;
                return lm;
            }

            return null;
        }

        private static int IndentOf(string line)
        {
            int n = 0;
            while (n < line.Length && line[n] == ' ') n++;
            return n;
        }

        private static bool StartsBlock(string line)
        {
            return BlockStart.IsMatch(line) || HrLine.IsMatch(line);
        }

        private static string StripIndent(string line)
        {
            int n = 0;
            while (n < Math.Min(3, line.Length) && line[n] == ' ') n++;
            return line.Substring(n);
        }

        private static int ParseList(string[] lines, int start, ListMarker first, int depth, StringBuilder sb)
        {
            bool ordered = first.Ordered;
            int startNum = first.Start;
            List<ListItem> items = new List<ListItem>();
            bool loose = false;
            int i = start;

            while (i < lines.Length)
            {
                ListMarker mk = ParseMarker(lines[i]);
                if (mk == null || mk.Ordered != ordered) break;

                int contentIndent = mk.ContentIndent;
                ListItem item = new ListItem();
                item.Lines = new List<string>();
                item.Lines.Add(mk.Rest);
                i++;
                bool pendingBlank = false;

                while (i < lines.Length)
                {
                    string ln = lines[i];

                    if (ln.Trim().Length == 0)
                    {
                        int j = i + 1;
                        while (j < lines.Length && lines[j].Trim().Length == 0) j++;
                        if (j < lines.Length)
                        {
                            ListMarker nextMk = ParseMarker(lines[j]);
                            if (IndentOf(lines[j]) >= contentIndent || (nextMk != null && nextMk.Indent <= 3))
                            {
                                item.Lines.Add(string.Empty);
                                i++;
                                pendingBlank = true;
                                continue;
                            }
                        }
                        break;
                    }

                    int indent = IndentOf(ln);
                    if (indent >= contentIndent)
                    {
                        item.Lines.Add(ln.Substring(contentIndent));
                        i++;
                        pendingBlank = false;
                        continue;
                    }
                    if (ParseMarker(ln) != null) break;
                    if (!pendingBlank && !StartsBlock(ln))
                    {
                        item.Lines.Add(ln.Trim());
                        i++;
                        continue;
                    }
                    break;
                }

                item.PendingBlank = pendingBlank;
                if (pendingBlank) loose = true;
                items.Add(item);
            }

            sb.Append(ordered ? "<ol" : "<ul");
            if (ordered && startNum != 1)
                sb.Append(" start=\"").Append(startNum.ToString(CultureInfo.InvariantCulture)).Append('"');
            sb.Append('>');

            for (int k = 0; k < items.Count; k++)
            {
                ListItem item = items[k];
                bool? task = null;
                int firstIdx = -1;
                for (int f = 0; f < item.Lines.Count; f++)
                {
                    if (item.Lines[f].Trim().Length > 0) { firstIdx = f; break; }
                }

                if (firstIdx >= 0)
                {
                    Match tm = Regex.Match(item.Lines[firstIdx], "^(\\s*)\\[([ xX])\\][ \\t]+");
                    if (tm.Success)
                    {
                        task = tm.Groups[2].Value.ToLowerInvariant() == "x";
                        item.Lines[firstIdx] = tm.Groups[1].Value + item.Lines[firstIdx].Substring(tm.Groups[0].Length);
                    }
                }

                string dirText = task.HasValue && firstIdx >= 0 ? item.Lines[firstIdx] : (item.Lines.Count > 0 ? item.Lines[0] : string.Empty);
                bool rtl = IsRtlText(dirText);

                string classes = string.Empty;
                if (task.HasValue) classes = task.Value ? "task task-done" : "task";
                if (rtl) classes = classes.Length > 0 ? classes + " rtl" : "rtl";

                sb.Append("<li");
                if (classes.Length > 0) sb.Append(" class=\"").Append(classes).Append('"');
                if (rtl) sb.Append(" dir=\"rtl\"");
                sb.Append('>');

                if (task.HasValue)
                {
                    sb.Append("<span class=\"task-box");
                    if (task.Value) sb.Append(" is-checked");
                    sb.Append("\"></span>");
                }

                ParseBlocks(item.Lines.ToArray(), depth + 1, sb, !loose);
                sb.Append("</li>");
            }

            sb.Append(ordered ? "</ol>" : "</ul>");
            return i;
        }

        private static bool IsTableStart(string[] lines, int index)
        {
            if (index + 1 >= lines.Length) return false;
            string head = lines[index];
            string delim = lines[index + 1];
            if (head.IndexOf('|') < 0 || delim.IndexOf('|') < 0) return false;
            if (!TableDelimiter.IsMatch(delim)) return false;
            return SplitRow(head).Length == SplitRow(delim).Length;
        }

        private static string[] SplitRow(string line)
        {
            string s = line.Trim();
            if (s.Length > 0 && s[0] == '|') s = s.Substring(1);
            if (s.Length > 0 && s[s.Length - 1] == '|' && (s.Length < 2 || s[s.Length - 2] != '\\'))
                s = s.Substring(0, s.Length - 1);

            List<string> cells = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inCode = false;

            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < s.Length && s[i + 1] == '|') { current.Append('|'); i++; continue; }
                if (ch == '`') { inCode = !inCode; current.Append(ch); continue; }
                if (ch == '|' && !inCode)
                {
                    cells.Add(current.ToString().Trim());
                    current.Length = 0;
                    continue;
                }
                current.Append(ch);
            }
            cells.Add(current.ToString().Trim());
            return cells.ToArray();
        }

        private static int ParseTable(string[] lines, int start, int depth, StringBuilder sb)
        {
            string[] header = SplitRow(lines[start]);
            string[] delimiter = SplitRow(lines[start + 1]);
            string[] aligns = new string[delimiter.Length];

            for (int c = 0; c < delimiter.Length; c++)
            {
                string t = delimiter[c].Trim();
                if (Regex.IsMatch(t, "^:-+:$")) aligns[c] = "center";
                else if (Regex.IsMatch(t, "-+:$")) aligns[c] = "right";
                else if (Regex.IsMatch(t, "^:-+")) aligns[c] = "left";
                else aligns[c] = string.Empty;
            }

            /* A table that carries Arabic (or Hebrew) text reads from right to
               left, so the whole grid is mirrored: the first column sits on the
               right and the alignment markers follow the reading direction. */
            bool rtl = TableIsRtl(header, lines, start);

            sb.Append("<div class=\"table-wrap\">");
            if (rtl)
            {
                sb.Append("<table class=\"rtl\" dir=\"rtl\">");
            }
            else
            {
                sb.Append("<table>");
            }

            sb.Append("<thead><tr>");
            for (int c = 0; c < header.Length; c++)
                sb.Append("<th").Append(CellAttrs(header[c], aligns[c], rtl)).Append('>')
                  .Append(Inline(header[c], depth, false)).Append("</th>");
            sb.Append("</tr></thead><tbody>");

            int i = start + 2;
            while (i < lines.Length && lines[i].Trim().Length > 0 && lines[i].IndexOf('|') >= 0)
            {
                string[] cells = SplitRow(lines[i]);
                sb.Append("<tr>");
                for (int c = 0; c < header.Length; c++)
                {
                    string cell = c < cells.Length ? cells[c] : string.Empty;
                    sb.Append("<td").Append(CellAttrs(cell, aligns[c], rtl)).Append('>')
                      .Append(Inline(cell, depth, false)).Append("</td>");
                }
                sb.Append("</tr>");
                i++;
            }

            sb.Append("</tbody></table></div>");
            return i;
        }

        /// <summary>True when the table is written in a right-to-left language:
        /// the header decides, and a table with an Arabic body but a Latin
        /// header still reads from the right.</summary>
        private static bool TableIsRtl(string[] header, string[] lines, int start)
        {
            for (int c = 0; c < header.Length; c++)
            {
                if (IsRtlText(header[c])) return true;
            }

            for (int i = start + 2; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0 || line.IndexOf('|') < 0) break;
                string[] cells = SplitRow(line);
                for (int c = 0; c < cells.Length; c++)
                {
                    if (IsRtlText(cells[c])) return true;
                }
            }
            return false;
        }

        /* ==================================================================
           TEXT, URLS, DIRECTION
           ================================================================== */

        private static string EscapeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string EscapeAttr(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string DecodeEntities(string value)
        {
            /* Decode once, then escape again on the way out: the browser shows
               the decoded character, while raw HTML still cannot become markup. */
            return System.Net.WebUtility.HtmlDecode(value);
        }

        private static string SafeHref(string raw)
        {
            string url = (raw ?? string.Empty).Trim();
            if (url.Length == 0) return null;
            if (SchemeCheck.IsMatch(url) && !SafeSchemes.IsMatch(url)) return null;
            return url;
        }

        private static string SafeImageSrc(string raw)
        {
            string url = (raw ?? string.Empty).Trim();
            if (url.Length == 0) return null;
            if (SchemeCheck.IsMatch(url) && !SafeSchemes.IsMatch(url) &&
                !Regex.IsMatch(url, "^data:image/", RegexOptions.IgnoreCase))
                return null;
            return url;
        }

        /// <summary>Turns the address of a picture into something the reader can
        /// load: a Windows path becomes a file: address, and a relative path is
        /// resolved against the folder of the document.</summary>
        private static string ResolveImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            /* A path such as C:\pictures\one.png is not an address the engine
               understands, so it is turned into a file: one. */
            if (url.Length > 2 && url[1] == ':' && (url[2] == '\\' || url[2] == '/'))
            {
                try
                {
                    return new Uri(Path.GetFullPath(url)).AbsoluteUri;
                }
                catch
                {
                    return url;
                }
            }

            /* http, https, file, mailto and data: are used as they are. */
            if (SchemeCheck.IsMatch(url)) return url;
            if (baseFolder.Length == 0) return url;

            try
            {
                string relative = url.Replace('/', Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(baseFolder, relative));
                return new Uri(full).AbsoluteUri;
            }
            catch
            {
                return url;
            }
        }

        private static bool IsExternal(string url)
        {
            return Regex.IsMatch(url ?? string.Empty, "^(https?:|mailto:)", RegexOptions.IgnoreCase);
        }

        private static bool IsRtlChar(char c)
        {
            return (c >= '\u0590' && c <= '\u08FF') ||
                   (c >= '\uFB1D' && c <= '\uFDFF') ||
                   (c >= '\uFE70' && c <= '\uFEFF');
        }

        private static bool IsRtlText(string text)
        {
            if (text == null) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetter(c)) return IsRtlChar(c);
            }
            return false;
        }

        private static string DirAttrs(string text)
        {
            return IsRtlText(text) ? " class=\"rtl\" dir=\"rtl\"" : string.Empty;
        }

        private static string CellAttrs(string text, string align, bool tableRtl)
        {
            bool rtl = IsRtlText(text);

            /* The alignment follows the marker when there is one, and the
               direction of the text otherwise. In a right-to-left table the
               markers follow the reading direction, so "left" and "right" are
               swapped. */
            string side;
            if (align == "center") side = "ta-center";
            else if (align == "left") side = tableRtl ? "ta-right" : "ta-left";
            else if (align == "right") side = tableRtl ? "ta-left" : "ta-right";
            else side = rtl ? "ta-right" : "ta-left";

            string attrs = " class=\"" + side + "\"";
            if (rtl) attrs += " dir=\"rtl\"";
            return attrs;
        }

        /* ==================================================================
           INLINE LEVEL
           ================================================================== */

        private static string Inline(string source, int depth, bool noLinks)
        {
            if (depth > 24) return EscapeText(source ?? string.Empty);

            string text = source ?? string.Empty;
            StringBuilder sb = new StringBuilder(text.Length + 32);
            StringBuilder buffer = new StringBuilder();
            int i = 0;
            int n = text.Length;

            Action flush = delegate
            {
                if (buffer.Length == 0) return;
                sb.Append(EscapeText(DecodeEntities(buffer.ToString())));
                buffer.Length = 0;
            };

            while (i < n)
            {
                char c = text[i];

                /* escapes */
                if (c == '\\' && i + 1 < n && Escapable.IndexOf(text[i + 1]) >= 0)
                {
                    buffer.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                /* line breaks */
                if (c == '\n')
                {
                    string b = buffer.ToString();
                    if (EndsWithSpaces(b))
                    {
                        buffer.Length = 0;
                        buffer.Append(b.TrimEnd(' '));
                        flush();
                        sb.Append("<br>");
                    }
                    else if (b.Length > 0 && b[b.Length - 1] == '\\')
                    {
                        buffer.Length = 0;
                        buffer.Append(b.Substring(0, b.Length - 1));
                        flush();
                        sb.Append("<br>");
                    }
                    else
                    {
                        buffer.Append(' ');
                    }
                    i++;
                    continue;
                }

                /* code span */
                if (c == '`')
                {
                    int run = 0;
                    while (i + run < n && text[i + run] == '`') run++;
                    int j = i + run;
                    int closeAt = -1;
                    while (j < n)
                    {
                        if (text[j] == '`')
                        {
                            int closeRun = 0;
                            while (j + closeRun < n && text[j + closeRun] == '`') closeRun++;
                            if (closeRun == run) { closeAt = j; break; }
                            j += closeRun;
                        }
                        else
                        {
                            j++;
                        }
                    }
                    if (closeAt >= 0)
                    {
                        string code = text.Substring(i + run, closeAt - (i + run)).Replace("\n", " ");
                        if (code.Length > 2 && code[0] == ' ' && code[code.Length - 1] == ' ' && code.Trim().Length > 0)
                            code = code.Substring(1, code.Length - 2);
                        flush();
                        sb.Append("<code>").Append(EscapeText(code)).Append("</code>");
                        i = closeAt + run;
                        continue;
                    }
                }

                /* image or link */
                if (c == '[' || (c == '!' && i + 1 < n && text[i + 1] == '['))
                {
                    bool isImage = c == '!';
                    if (!isImage && noLinks)
                    {
                        buffer.Append(c);
                        i++;
                        continue;
                    }

                    LinkTarget target = ReadLink(text, i, isImage);
                    if (target == null)
                    {
                        LinkTarget reference = ReadReference(text, i, isImage);
                        if (reference != null)
                        {
                            Ref definition;
                            if (refs.TryGetValue(NormalizeRef(reference.Id), out definition))
                            {
                                target = new LinkTarget();
                                target.Label = reference.Label;
                                target.Href = definition.Href;
                                target.Title = definition.Title;
                                target.End = reference.End;
                            }
                        }
                    }

                    if (target != null)
                    {
                        string url = isImage ? SafeImageSrc(target.Href) : SafeHref(target.Href);
                        if (url != null)
                        {
                            flush();
                            if (isImage)
                                sb.Append(BuildImage(ResolveImageUrl(url), target.Label, target.Title));
                            else
                                sb.Append(BuildLink(url, target.Label, target.Title, depth, noLinks));
                            i = target.End;
                            continue;
                        }
                    }

                    buffer.Append(c);
                    i++;
                    continue;
                }

                /* angle autolinks */
                if (c == '<' && !noLinks)
                {
                    Match am = AutoUrl.Match(text, i);
                    if (am.Success && am.Index == i)
                    {
                        string url = am.Groups[1].Value;
                        flush();
                        sb.Append(BuildLink(url, url, string.Empty, depth, false));
                        i += am.Length;
                        continue;
                    }
                    Match em = AutoMail.Match(text, i);
                    if (em.Success && em.Index == i)
                    {
                        string mail = em.Groups[1].Value;
                        flush();
                        sb.Append(BuildLink("mailto:" + mail, mail, string.Empty, depth, false));
                        i += em.Length;
                        continue;
                    }
                }

                /* bare URLs */
                if (!noLinks && (c == 'h' || c == 'H') && (i == 0 || IsUrlBoundary(text[i - 1])))
                {
                    bool http = StartsWith(text, i, "http://", true);
                    bool https = StartsWith(text, i, "https://", true);
                    if (http || https)
                    {
                        int end = i;
                        while (end < n && !char.IsWhiteSpace(text[end]) && text[end] != '<') end++;
                        string url = text.Substring(i, end - i);
                        string trail = string.Empty;
                        while (url.Length > 0 && ".,;:!?".IndexOf(url[url.Length - 1]) >= 0)
                        {
                            trail = url[url.Length - 1] + trail;
                            url = url.Substring(0, url.Length - 1);
                        }
                        if (url.Length > 8)
                        {
                            flush();
                            sb.Append(BuildLink(url, url, string.Empty, depth, false));
                            buffer.Append(trail);
                            i = end;
                            continue;
                        }
                    }
                }

                /* emphasis */
                if (c == '*' || c == '_' || c == '~')
                {
                    string html;
                    int end;
                    if (TryEmphasis(text, i, depth, noLinks, out html, out end))
                    {
                        flush();
                        sb.Append(html);
                        i = end;
                        continue;
                    }
                }

                buffer.Append(c);
                i++;
            }

            flush();
            return sb.ToString();
        }

        private static bool EndsWithSpaces(string value)
        {
            return value.Length >= 2 && value[value.Length - 1] == ' ' && value[value.Length - 2] == ' ';
        }

        private static bool IsUrlBoundary(char c)
        {
            return char.IsWhiteSpace(c) || c == '(' || c == '[' || c == '*' || c == '_';
        }

        private static bool StartsWith(string text, int index, string value, bool ignoreCase)
        {
            if (index + value.Length > text.Length) return false;
            return string.Compare(text, index, value, 0, value.Length, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == 0;
        }

        private static string BuildLink(string url, string label, string title, int depth, bool noLinks)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<a href=\"").Append(EscapeAttr(url)).Append('"');
            if (!string.IsNullOrEmpty(title)) sb.Append(" title=\"").Append(EscapeAttr(title)).Append('"');
            if (IsExternal(url)) sb.Append(" target=\"_blank\" rel=\"noopener noreferrer\"");
            sb.Append('>').Append(Inline(label, depth + 1, true)).Append("</a>");
            return sb.ToString();
        }

        private static string BuildImage(string url, string alt, string title)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<img src=\"").Append(EscapeAttr(url)).Append("\" alt=\"").Append(EscapeAttr(alt ?? string.Empty)).Append('"');
            if (!string.IsNullOrEmpty(title)) sb.Append(" title=\"").Append(EscapeAttr(title)).Append('"');
            sb.Append('>');
            return sb.ToString();
        }

        private static LinkTarget ReadLink(string s, int at, bool image)
        {
            int j = at + (image ? 2 : 1);
            int depth = 1;
            StringBuilder label = new StringBuilder();

            while (j < s.Length)
            {
                char ch = s[j];
                if (ch == '\\' && j + 1 < s.Length)
                {
                    label.Append(s[j + 1]);
                    j += 2;
                    continue;
                }
                if (ch == '[') depth++;
                if (ch == ']')
                {
                    depth--;
                    if (depth == 0) { j++; break; }
                }
                label.Append(ch);
                j++;
            }

            if (depth != 0 || j >= s.Length || s[j] != '(') return null;
            j++;

            while (j < s.Length && (s[j] == ' ' || s[j] == '\t' || s[j] == '\n')) j++;

            StringBuilder href = new StringBuilder();
            if (j < s.Length && s[j] == '<')
            {
                int closeAngle = s.IndexOf('>', j + 1);
                if (closeAngle < 0) return null;
                href.Append(s.Substring(j + 1, closeAngle - j - 1));
                j = closeAngle + 1;
            }
            else
            {
                int parens = 0;
                while (j < s.Length)
                {
                    char hc = s[j];
                    if (hc == '\\' && j + 1 < s.Length)
                    {
                        href.Append(s[j + 1]);
                        j += 2;
                        continue;
                    }
                    if (hc == '(') { parens++; href.Append(hc); j++; continue; }
                    if (hc == ')')
                    {
                        if (parens == 0) break;
                        parens--;
                        href.Append(hc);
                        j++;
                        continue;
                    }
                    if (hc == ' ' || hc == '\t' || hc == '\n') break;
                    href.Append(hc);
                    j++;
                }
            }

            while (j < s.Length && (s[j] == ' ' || s[j] == '\t' || s[j] == '\n')) j++;

            string title = string.Empty;
            if (j < s.Length)
            {
                char q = s[j];
                if (q == '"' || q == '\'' || q == '(')
                {
                    char close = q == '(' ? ')' : q;
                    int k = s.IndexOf(close, j + 1);
                    if (k < 0) return null;
                    title = s.Substring(j + 1, k - j - 1);
                    j = k + 1;
                    while (j < s.Length && (s[j] == ' ' || s[j] == '\t')) j++;
                }
            }

            if (j >= s.Length || s[j] != ')') return null;

            LinkTarget target = new LinkTarget();
            target.Label = label.ToString();
            target.Href = href.ToString();
            target.Title = title;
            target.End = j + 1;
            return target;
        }

        private static LinkTarget ReadReference(string s, int at, bool image)
        {
            int j = at + (image ? 2 : 1);
            int depth = 1;
            StringBuilder label = new StringBuilder();

            while (j < s.Length)
            {
                char ch = s[j];
                if (ch == '\\' && j + 1 < s.Length)
                {
                    label.Append(s[j + 1]);
                    j += 2;
                    continue;
                }
                if (ch == '[') depth++;
                if (ch == ']')
                {
                    depth--;
                    if (depth == 0) { j++; break; }
                }
                label.Append(ch);
                j++;
            }

            if (depth != 0) return null;

            if (j < s.Length && s[j] == '[')
            {
                int close = s.IndexOf(']', j + 1);
                if (close < 0) return null;
                string id = s.Substring(j + 1, close - j - 1).Trim();
                LinkTarget t = new LinkTarget();
                t.Label = label.ToString();
                t.Id = id.Length > 0 ? id : label.ToString();
                t.Href = string.Empty;
                t.Title = string.Empty;
                t.End = close + 1;
                return t;
            }
            if (j < s.Length && s[j] == '(') return null;

            LinkTarget shortcut = new LinkTarget();
            shortcut.Label = label.ToString();
            shortcut.Id = label.ToString();
            shortcut.Href = string.Empty;
            shortcut.Title = string.Empty;
            shortcut.End = j;
            return shortcut;
        }

        private static bool TryEmphasis(string text, int i, int depth, bool noLinks, out string html, out int end)
        {
            html = null;
            end = i;

            char c = text[i];
            string[] runs;

            if (c == '~')
            {
                runs = new string[] { "~~" };
            }
            else if (c == '_')
            {
                char prev = i > 0 ? text[i - 1] : '\0';
                if (prev != '\0' && IsWordChar(prev)) return false;
                runs = new string[] { "___", "__", "_" };
            }
            else
            {
                runs = new string[] { "***", "**", "*" };
            }

            for (int r = 0; r < runs.Length; r++)
            {
                string delim = runs[r];
                if (!StartsWith(text, i, delim, false)) continue;

                char afterOpen = i + delim.Length < text.Length ? text[i + delim.Length] : '\0';
                if (afterOpen == '\0' || char.IsWhiteSpace(afterOpen)) continue;

                int close = FindCloser(text, i + delim.Length, delim);
                if (close <= i + delim.Length) continue;

                string inner = text.Substring(i + delim.Length, close - (i + delim.Length));
                string innerHtml = Inline(inner, depth + 1, noLinks);

                if (delim == "~~")
                    html = "<del>" + innerHtml + "</del>";
                else if (delim == "***" || delim == "___")
                    html = "<strong><em>" + innerHtml + "</em></strong>";
                else if (delim.Length == 2)
                    html = "<strong>" + innerHtml + "</strong>";
                else
                    html = "<em>" + innerHtml + "</em>";

                end = close + delim.Length;
                return true;
            }

            return false;
        }

        private static int FindCloser(string s, int from, string delim)
        {
            for (int j = from; j <= s.Length - delim.Length; j++)
            {
                if (s[j] == '\\') { j++; continue; }
                if (!StartsWith(s, j, delim, false)) continue;

                char before = j > 0 ? s[j - 1] : '\0';
                char after = j + delim.Length < s.Length ? s[j + delim.Length] : '\0';

                if (before != '\0' && char.IsWhiteSpace(before)) continue;
                if (delim[0] == '_' && before != '\0' && after != '\0' &&
                    IsWordChar(before) && IsWordChar(after)) continue;

                return j;
            }
            return -1;
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }
}

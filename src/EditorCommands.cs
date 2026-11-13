using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer
{
    internal sealed class EditResult
    {
        public int ReplaceStart;
        public int ReplaceLength;
        public string Insert = string.Empty;
        public int SelectionStart;
        public int SelectionLength;

        public static EditResult At(int replaceStart, int replaceLength, string insert,
            int selectionStart, int selectionLength)
        {
            EditResult result = new EditResult();
            result.ReplaceStart = replaceStart;
            result.ReplaceLength = replaceLength;
            result.Insert = insert ?? string.Empty;
            result.SelectionStart = selectionStart;
            result.SelectionLength = selectionLength;
            return result;
        }
    }

    internal sealed class EditorState
    {
        public int Heading;
        public bool Bold;
        public bool Italic;
        public bool Strike;
        public bool Code;
        public bool Quote;
        public bool Bullet;
        public bool Ordered;
        public bool Task;
    }

    internal static class EditorCommands
    {
        private static readonly Regex HeadingPrefix =
            new Regex("^(\\s*)(#{1,6})[ \\t]+", RegexOptions.Compiled);

        private static readonly Regex HeadingBare =
            new Regex("^(\\s*)(#{1,6})[ \\t]*$", RegexOptions.Compiled);

        private static readonly Regex ListPrefix =
            new Regex("^(\\s*)(?:([-*+])|(\\d+)([.)]))[ \\t]+(\\[[ xX]\\][ \\t]+)?",
                RegexOptions.Compiled);

        private static readonly Regex EmphasisStrike = new Regex("~~([^~]+)~~", RegexOptions.Compiled);
        private static readonly Regex EmphasisBoldA = new Regex("\\*\\*([^*]+)\\*\\*", RegexOptions.Compiled);
        private static readonly Regex EmphasisBoldB = new Regex("__([^_]+)__", RegexOptions.Compiled);
        private static readonly Regex EmphasisCode = new Regex("`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex EmphasisItalicA = new Regex("\\*([^*]+)\\*", RegexOptions.Compiled);
        private static readonly Regex EmphasisItalicB =
            new Regex("(?<![\\p{L}\\p{N}_])_([^_]+)_(?![\\p{L}\\p{N}_])", RegexOptions.Compiled);

        public static EditResult Apply(string command, string text, int start, int length)
        {
            if (text == null) text = string.Empty;
            if (start < 0) start = 0;
            if (start > text.Length) start = text.Length;
            if (length < 0) length = 0;
            if (start + length > text.Length) length = text.Length - start;

            switch (command)
            {
                case "h1": return Heading(text, start, length, 1);
                case "h2": return Heading(text, start, length, 2);
                case "h3": return Heading(text, start, length, 3);
                case "bold": return Wrap(text, start, length, "**", "**");
                case "italic": return Wrap(text, start, length, "*", "*");
                case "bolditalic": return ApplyBoldItalic(text, start, length);
                case "strike": return Wrap(text, start, length, "~~", "~~");
                case "code": return InlineCode(text, start, length);
                case "codeblock": return CodeBlock(text, start, length);
                case "ul": return LinePrefix(text, start, length, "ul");
                case "ol": return LinePrefix(text, start, length, "ol");
                case "task": return LinePrefix(text, start, length, "task");
                case "quote": return LinePrefix(text, start, length, "quote");
                case "link": return Link(text, start, length, false);
                case "image": return Link(text, start, length, true);
                case "table": return Table(text, start, length);
                case "rule": return Rule(text, start, length);
                case "clear": return Clear(text, start, length);
            }
            return null;
        }

        private static EditResult ApplyBoldItalic(string text, int start, int length)
        {
            return Wrap(text, start, length, "***", "***");
        }

        public static EditResult InsertTable(string text, int start, int length)
        {
            const string TableText =
                "| Column 1 | Column 2 | Column 3 |\r\n" +
                "| --- | --- | --- |\r\n" +
                "|  |  |  |";

            int lineStart = LineStart(text, start);
            int lineEnd = LineEnd(text, start);
            bool emptyLine = text.Substring(lineStart, lineEnd - lineStart).Trim().Length == 0;

            if (emptyLine && length == 0)
            {
                return EditResult.At(lineStart, lineEnd - lineStart, TableText,
                    lineStart + 2, "Column 1".Length);
            }

            int at = lineEnd;
            string insert = "\r\n\r\n" + TableText + "\r\n";
            int header = at + 4 + 2;
            return EditResult.At(at, 0, insert, header, "Column 1".Length);
        }

        private static EditResult Heading(string text, int start, int length, int level)
        {
            int blockStart, blockEnd;
            Block(text, start, length, out blockStart, out blockEnd);

            string block = Normalize(text.Substring(blockStart, blockEnd - blockStart));
            string[] lines = block.Split('\n');

            bool anyContent = false;
            bool allAtLevel = true;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                anyContent = true;
                Match match = HeadingPrefix.Match(lines[i]);
                if (!match.Success || match.Groups[2].Value.Length != level)
                {
                    allAtLevel = false;
                    break;
                }
            }

            int target = anyContent && allAtLevel ? 0 : level;

            StringBuilder sb = new StringBuilder(block.Length + lines.Length * 4 + 8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim().Length > 0)
                {
                    Match match = HeadingPrefix.Match(line);
                    if (match.Success) line = match.Groups[1].Value + line.Substring(match.Length);

                    if (target > 0)
                    {
                        int indent = 0;
                        while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t')) indent++;
                        line = line.Substring(0, indent) + new string('#', target) + " " + line.Substring(indent);
                    }
                }

                sb.Append(line);
                if (i < lines.Length - 1) sb.Append('\n');
            }

            string updated = Denormalize(sb.ToString());
            return BlockResult(blockStart, blockEnd - blockStart, updated, start, length);
        }

        private static EditResult BlockResult(int blockStart, int blockLength,
            string updated, int caret, int selectionLength)
        {
            if (selectionLength == 0)
            {
                int offset = caret - blockStart + (updated.Length - blockLength);
                if (offset < 0) offset = 0;
                if (offset > updated.Length) offset = updated.Length;
                return EditResult.At(blockStart, blockLength, updated, blockStart + offset, 0);
            }

            return EditResult.At(blockStart, blockLength, updated, blockStart, updated.Length);
        }

        private static EditResult Wrap(string text, int start, int length, string open, string close)
        {
            if (length == 0)
            {
                int wordStart, wordEnd;
                if (WordBounds(text, start, out wordStart, out wordEnd))
                {
                    start = wordStart;
                    length = wordEnd - wordStart;
                }
                else
                {
                    return EditResult.At(start, 0, open + close, start + open.Length, 0);
                }
            }

            string selected = text.Substring(start, length);

            if (length >= open.Length + close.Length &&
                selected.StartsWith(open, StringComparison.Ordinal) &&
                selected.EndsWith(close, StringComparison.Ordinal))
            {
                string inner = selected.Substring(open.Length, selected.Length - open.Length - close.Length);
                return EditResult.At(start, length, inner, start, inner.Length);
            }

            if (start >= open.Length && start + length + close.Length <= text.Length &&
                string.CompareOrdinal(text, start - open.Length, open, 0, open.Length) == 0 &&
                string.CompareOrdinal(text, start + length, close, 0, close.Length) == 0)
            {
                int outer = start - open.Length;
                int total = open.Length + length + close.Length;
                return EditResult.At(outer, total, selected, outer, selected.Length);
            }

            string wrapped = open + selected + close;
            return EditResult.At(start, length, wrapped, start + open.Length, selected.Length);
        }

        private static EditResult InlineCode(string text, int start, int length)
        {
            string sample = length > 0 ? text.Substring(start, length) : string.Empty;
            int run = 0;
            int longest = 0;
            for (int i = 0; i < sample.Length; i++)
            {
                if (sample[i] == '`')
                {
                    run++;
                    if (run > longest) longest = run;
                }
                else
                {
                    run = 0;
                }
            }

            string fence = new string('`', longest + 1);
            return Wrap(text, start, length, fence, fence);
        }

        private static EditResult CodeBlock(string text, int start, int length)
        {
            if (length > 0)
            {
                string content = Denormalize(Normalize(text.Substring(start, length)));
                string fence = FenceFor(content);
                string insert = fence + "\r\n" + content + "\r\n" + fence;
                return EditResult.At(start, length, insert, start + fence.Length + 2, content.Length);
            }

            int lineStart = LineStart(text, start);
            int lineEnd = LineEnd(text, start);
            bool emptyLine = text.Substring(lineStart, lineEnd - lineStart).Trim().Length == 0;

            string fenceOnly = FenceFor(string.Empty);

            if (emptyLine)
            {
                string emptyBlock = fenceOnly + "\r\n\r\n" + fenceOnly;
                return EditResult.At(lineStart, lineEnd - lineStart, emptyBlock,
                    lineStart + fenceOnly.Length + 2, 0);
            }

            string block = "\r\n\r\n" + fenceOnly + "\r\n\r\n" + fenceOnly + "\r\n";
            return EditResult.At(lineEnd, 0, block, lineEnd + 4 + fenceOnly.Length + 2, 0);
        }

        private static string FenceFor(string content)
        {
            string fence = "```";
            while (content != null && content.IndexOf(fence, StringComparison.Ordinal) >= 0)
            {
                fence += "`";
            }
            return fence;
        }

        private sealed class LineInfo
        {
            public string Indent = string.Empty;
            public string QuoteRaw = string.Empty;
            public string ListRaw = string.Empty;
            public string ListKind = string.Empty;
            public string Content = string.Empty;
        }

        private static LineInfo ParseLine(string line)
        {
            LineInfo info = new LineInfo();
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            info.Indent = line.Substring(0, i);

            string rest = line.Substring(i);
            int q = 0;
            while (q < rest.Length && rest[q] == '>')
            {
                q++;
                if (q < rest.Length && (rest[q] == ' ' || rest[q] == '\t')) q++;
            }
            info.QuoteRaw = rest.Substring(0, q);
            rest = rest.Substring(q);

            Match match = ListPrefix.Match(rest);
            if (match.Success)
            {
                bool bullet = match.Groups[2].Success;
                bool task = match.Groups[5].Success;
                if (bullet) info.ListKind = task ? "task" : "ul";
                else info.ListKind = task ? "task" : "ol";
                info.ListRaw = match.Value;
                rest = rest.Substring(match.Length);
            }

            info.Content = rest;
            return info;
        }

        private static EditResult LinePrefix(string text, int start, int length, string kind)
        {
            int blockStart, blockEnd;
            Block(text, start, length, out blockStart, out blockEnd);

            string block = Normalize(text.Substring(blockStart, blockEnd - blockStart));
            string[] lines = block.Split('\n');
            LineInfo[] infos = new LineInfo[lines.Length];

            bool anyContent = false;
            bool allSame = true;
            bool allQuoted = true;

            for (int i = 0; i < lines.Length; i++)
            {
                infos[i] = ParseLine(lines[i]);
                if (infos[i].Content.Trim().Length == 0) continue;

                anyContent = true;
                if (infos[i].QuoteRaw.Length == 0) allQuoted = false;

                if (kind == "quote")
                {
                    if (infos[i].QuoteRaw.Length == 0) allSame = false;
                }
                else if (infos[i].ListKind != kind)
                {
                    allSame = false;
                }
            }

            bool remove = anyContent && (kind == "quote" ? allQuoted : allSame);
            int number = 1;

            StringBuilder sb = new StringBuilder(block.Length + lines.Length * 4 + 8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (infos[i].Content.Trim().Length > 0)
                {
                    string quote;
                    string listMarker;

                    if (kind == "quote")
                    {
                        quote = remove ? string.Empty : "> ";
                        listMarker = infos[i].ListRaw;
                    }
                    else
                    {
                        quote = infos[i].QuoteRaw;
                        listMarker = remove ? string.Empty : Marker(kind, ref number);
                    }

                    line = infos[i].Indent + quote + listMarker + infos[i].Content;
                }

                sb.Append(line);
                if (i < lines.Length - 1) sb.Append('\n');
            }

            string updated = Denormalize(sb.ToString());
            return BlockResult(blockStart, blockEnd - blockStart, updated, start, length);
        }

        private static string Marker(string kind, ref int number)
        {
            switch (kind)
            {
                case "ol":
                    string marker = number.ToString(CultureInfo.InvariantCulture) + ". ";
                    number++;
                    return marker;
                case "task": return "- [ ] ";
                case "quote": return "> ";
                default: return "- ";
            }
        }

        private static EditResult Link(string text, int start, int length, bool image)
        {
            string prefix = image ? "!" : string.Empty;
            string label = image ? "alt text" : "link text";
            string target = image ? "path/to/image.png" : "url";

            if (length == 0)
            {
                string insert = prefix + "[" + label + "](" + target + ")";
                int labelStart = start + prefix.Length + 1;
                return EditResult.At(start, 0, insert, labelStart, label.Length);
            }

            string selected = text.Substring(start, length);
            if (LooksLikeUrl(selected))
            {
                string insert = prefix + "[](" + selected + ")";
                return EditResult.At(start, length, insert, start + prefix.Length + 1, 0);
            }

            string wrapped = prefix + "[" + selected + "](" + target + ")";
            int urlStart = start + prefix.Length + 1 + selected.Length + 2;
            return EditResult.At(start, length, wrapped, urlStart, target.Length);
        }

        private static bool LooksLikeUrl(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length == 0 || trimmed.IndexOf(' ') >= 0 ||
                trimmed.IndexOf('\t') >= 0 || trimmed.IndexOf('\n') >= 0)
                return false;

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return true;

            return trimmed.IndexOf('.') > 0 && trimmed.IndexOf('/') != 0;
        }

        private static EditResult Table(string text, int start, int length)
        {
            return InsertTable(text, start, length);
        }

        private static EditResult Rule(string text, int start, int length)
        {
            int lineStart = LineStart(text, start);
            int lineEnd = LineEnd(text, start);
            bool emptyLine = text.Substring(lineStart, lineEnd - lineStart).Trim().Length == 0;

            if (emptyLine && length == 0)
            {
                return EditResult.At(lineStart, lineEnd - lineStart, "---", lineStart + 3, 0);
            }

            int at = start + length;
            string insert = "\r\n\r\n---\r\n\r\n";
            return EditResult.At(at, 0, insert, at + insert.Length, 0);
        }

        private static EditResult Clear(string text, int start, int length)
        {
            int blockStart, blockEnd;
            Block(text, start, length, out blockStart, out blockEnd);

            string block = Normalize(text.Substring(blockStart, blockEnd - blockStart));
            string[] lines = block.Split('\n');

            StringBuilder sb = new StringBuilder(block.Length + 8);
            for (int i = 0; i < lines.Length; i++)
            {
                LineInfo info = ParseLine(lines[i]);
                string content = info.Content;
                Match heading = HeadingPrefix.Match(content);
                if (heading.Success)
                    content = heading.Groups[1].Value + content.Substring(heading.Length);
                else
                {
                    Match bare = HeadingBare.Match(content);
                    if (bare.Success) content = bare.Groups[1].Value;
                }

                if (content.Trim().Length > 0) content = StripInline(content);
                string line = info.Indent + content;

                sb.Append(line);
                if (i < lines.Length - 1) sb.Append('\n');
            }

            string updated = Denormalize(sb.ToString());
            return BlockResult(blockStart, blockEnd - blockStart, updated, start, length);
        }

        private static string StripInline(string value)
        {
            string result = EmphasisCode.Replace(value, "$1");
            result = EmphasisStrike.Replace(result, "$1");
            result = EmphasisBoldA.Replace(result, "$1");
            result = EmphasisBoldB.Replace(result, "$1");
            result = EmphasisItalicA.Replace(result, "$1");
            result = EmphasisItalicB.Replace(result, "$1");
            return result;
        }

        public static EditorState Describe(string text, int caret)
        {
            EditorState state = new EditorState();
            if (text == null) text = string.Empty;
            if (caret < 0) caret = 0;
            if (caret > text.Length) caret = text.Length;

            int lineStart = LineStart(text, caret);
            int lineEnd = LineEnd(text, caret);
            string line = text.Substring(lineStart, lineEnd - lineStart);
            int relative = caret - lineStart;

            Match heading = HeadingPrefix.Match(line);
            if (heading.Success)
            {
                state.Heading = heading.Groups[2].Value.Length;
            }
            else
            {
                Match bare = HeadingBare.Match(line);
                if (bare.Success) state.Heading = bare.Groups[2].Value.Length;
            }

            LineInfo info = ParseLine(line);
            state.Quote = info.QuoteRaw.Length > 0;
            state.Bullet = info.ListKind == "ul" || info.ListKind == "task";
            state.Task = info.ListKind == "task";
            state.Ordered = info.ListKind == "ol";

            state.Bold = InsideDelimiter(line, relative, "**");
            state.Strike = InsideDelimiter(line, relative, "~~");
            state.Code = InsideDelimiter(line, relative, "`");
            state.Italic = !state.Bold && InsideDelimiter(line, relative, "*");

            return state;
        }

        private static bool InsideDelimiter(string line, int relative, string delimiter)
        {
            int i = 0;
            while (i < line.Length)
            {
                int open = line.IndexOf(delimiter, i, StringComparison.Ordinal);
                if (open < 0) return false;

                if (delimiter.Length == 1)
                {
                    if (open > 0 && line[open - 1] == delimiter[0])
                    {
                        i = open + 1;
                        continue;
                    }
                    if (open + 1 < line.Length && line[open + 1] == delimiter[0])
                    {
                        i = open + 2;
                        continue;
                    }
                }

                int close = line.IndexOf(delimiter, open + delimiter.Length, StringComparison.Ordinal);
                if (close < 0) return false;
                if (relative > open && relative <= close) return true;
                i = close + delimiter.Length;
            }
            return false;
        }

        private static void Block(string text, int start, int length, out int blockStart, out int blockEnd)
        {
            blockStart = LineStart(text, start);
            int end = start + length;

            if (length > 0 && end > blockStart && end <= text.Length && text[end - 1] == '\n')
            {
                end--;
                if (end > blockStart && text[end - 1] == '\r') end--;
            }

            blockEnd = LineEnd(text, end);
            if (blockEnd < blockStart) blockEnd = blockStart;
        }

        private static int LineStart(string text, int index)
        {
            if (index <= 0) return 0;
            if (index > text.Length) index = text.Length;
            int newline = text.LastIndexOf('\n', index - 1);
            return newline < 0 ? 0 : newline + 1;
        }

        private static int LineEnd(string text, int index)
        {
            if (index < 0) index = 0;
            if (index > text.Length) index = text.Length;
            int newline = text.IndexOf('\n', index);
            return newline < 0 ? text.Length : newline;
        }

        private static bool WordBounds(string text, int caret, out int start, out int end)
        {
            start = caret;
            end = caret;
            if (caret > text.Length) caret = text.Length;

            int i = caret;
            while (i > 0 && IsWordChar(text[i - 1])) i--;
            int j = caret;
            while (j < text.Length && IsWordChar(text[j])) j++;

            if (i == j) return false;
            start = i;
            end = j;
            return true;
        }

        private static bool IsWordChar(char c)
        {
            if (char.IsLetterOrDigit(c) || c == '_') return true;
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark ||
                   category == UnicodeCategory.SpacingCombiningMark;
        }

        private static string Normalize(string value)
        {
            return value.Replace("\r\n", "\n");
        }

        private static string Denormalize(string value)
        {
            return value.Replace("\n", "\r\n");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MdViewer
{
    /// <summary>One row in the suggestion popup.</summary>
    internal sealed class Suggestion
    {
        public string Insert = string.Empty;
        public int CaretBack;
        public string Display = string.Empty;
        public string Hint = string.Empty;

        public Suggestion(string insert, string display, string hint, int caretBack)
        {
            Insert = insert ?? string.Empty;
            Display = display ?? string.Empty;
            Hint = hint ?? string.Empty;
            CaretBack = caretBack;
        }
    }

    /// <summary>Suggestions plus the range of already-typed text they replace.</summary>
    internal sealed class SuggestionSet
    {
        public int Start;
        public int Length;
        public List<Suggestion> Items = new List<Suggestion>();
    }

    /// <summary>Pure suggestion rules for the Markdown editor: block starters,
    /// fenced-code languages and link targets. Kept free of WinForms so the
    /// behaviour is testable without a window.</summary>
    internal static class Autocomplete
    {
        private static readonly Regex BlockTrigger =
            new Regex("^([ \\t]*)([-*+]|>|#{1,6}|\\d+[.)]|`)$", RegexOptions.Compiled);

        private static readonly Regex UrlPattern =
            new Regex("\\((https?://[^)\\s]+)\\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] Languages =
        {
            "bash", "c", "cpp", "csharp", "css", "diff", "html", "java", "javascript",
            "json", "markdown", "php", "powershell", "python", "ruby", "sql",
            "typescript", "xml", "yaml"
        };

        public static SuggestionSet Get(string text, int caret)
        {
            if (text == null || text.Length == 0) return null;
            if (caret < 0) caret = 0;
            if (caret > text.Length) caret = text.Length;

            int lineStart = LineStart(text, caret);
            string prefix = text.Substring(lineStart, caret - lineStart);
            string trimmed = prefix.TrimStart();
            bool inFence = InsideFence(text, lineStart);

            /* ---- fenced code languages ---- */
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inFence) return null;

                string tail = trimmed.Substring(3);
                if (tail.IndexOf(' ') >= 0 || tail.IndexOf('\t') >= 0) return null;

                SuggestionSet languages = new SuggestionSet();
                languages.Start = caret - tail.Length;
                languages.Length = tail.Length;

                for (int i = 0; i < Languages.Length; i++)
                {
                    if (tail.Length == 0 ||
                        Languages[i].StartsWith(tail, StringComparison.OrdinalIgnoreCase))
                    {
                        languages.Items.Add(new Suggestion(
                            Languages[i], Languages[i], "Code language", 0));
                    }
                }

                return languages.Items.Count > 0 ? languages : null;
            }

            if (inFence) return null;

            /* ---- block starters at the beginning of a line ---- */
            Match trigger = BlockTrigger.Match(prefix);
            if (trigger.Success)
            {
                string indent = trigger.Groups[1].Value;
                string token = trigger.Groups[2].Value;
                int tokenStart = lineStart + indent.Length;

                SuggestionSet set = new SuggestionSet();
                set.Start = tokenStart;
                set.Length = token.Length;

                if (token == "-")
                {
                    set.Items.Add(new Suggestion("- ", "- List item", "Bulleted list", 0));
                    set.Items.Add(new Suggestion("- [ ] ", "- [ ] Task", "Task item", 0));
                    set.Items.Add(new Suggestion("---", "---", "Thematic break", 0));
                }
                else if (token == "*" || token == "+")
                {
                    set.Items.Add(new Suggestion(token + " ", token + " List item", "Bulleted list", 0));
                    set.Items.Add(new Suggestion("- [ ] ", "- [ ] Task", "Task item", 0));
                }
                else if (token == ">")
                {
                    set.Items.Add(new Suggestion("> ", "> Quote", "Block quote", 0));
                }
                else if (token[0] == '#')
                {
                    for (int level = token.Length; level <= 3; level++)
                    {
                        string hashes = new string('#', level);
                        set.Items.Add(new Suggestion(hashes + " ",
                            hashes + " Heading " + level, "Heading " + level, 0));
                    }
                }
                else if (token == "`")
                {
                    set.Items.Add(new Suggestion("```", "``` Fence", "Fenced code block", 0));
                }
                else
                {
                    char last = token[token.Length - 1];
                    string number = token.Substring(0, token.Length - 1);
                    if (last == '.')
                        set.Items.Add(new Suggestion(number + ". ", number + ". Item", "Numbered list", 0));
                    else
                        set.Items.Add(new Suggestion(number + ") ", number + ") Item", "Numbered list", 0));
                }

                return set.Items.Count > 0 ? set : null;
            }

            /* ---- link targets after "](" ---- */
            if (prefix.EndsWith("](", StringComparison.Ordinal))
            {
                SuggestionSet set = new SuggestionSet();
                set.Start = caret;
                set.Length = 0;

                List<string> urls = CollectUrls(text);
                for (int i = 0; i < urls.Count; i++)
                    set.Items.Add(new Suggestion(urls[i] + ")", urls[i], "Link target", 1));

                set.Items.Add(new Suggestion("https://)", "https://", "New link", 1));
                return set;
            }

            return null;
        }

        /// <summary>Bracket and backtick pairing. Returns the text that should replace
        /// the caret (the typed character included) and how far the caret sits from
        /// the end of that text.</summary>
        public static bool TryAutoPair(char typed, string text, int caret, int selectionLength,
            out string insert, out int caretBack)
        {
            insert = null;
            caretBack = 0;

            if (selectionLength > 0 || text == null) return false;
            if (caret < 0) caret = 0;
            if (caret > text.Length) caret = text.Length;

            char next = caret < text.Length ? text[caret] : '\0';

            if (typed == '(')
            {
                if (next == ')' || next == '(') return false;
                if (char.IsLetterOrDigit(next) || next == '_') return false;
                if (next != '\0' && !char.IsWhiteSpace(next) &&
                    next != '.' && next != ',' && next != ';' && next != ':' && next != '!')
                    return false;

                insert = "()";
                caretBack = 1;
                return true;
            }

            if (typed == '[')
            {
                if (next == ']' || next == '(') return false;
                if (char.IsLetterOrDigit(next) || next == '_') return false;
                insert = "[]";
                caretBack = 1;
                return true;
            }

            if (typed == '`')
            {
                if (next == '`') return false;
                if (char.IsLetterOrDigit(next) || next == '_') return false;

                int lineStart = LineStart(text, caret);
                string line = text.Substring(lineStart, caret - lineStart);
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) return false;

                insert = "``";
                caretBack = 1;
                return true;
            }

            return false;
        }

        /// <summary>Typing a closing bracket that is already there should move over it.</summary>
        public static bool ShouldSkipOver(char typed, string text, int caret, int selectionLength)
        {
            if (selectionLength > 0 || text == null) return false;
            if (typed != ')' && typed != ']' && typed != '`') return false;
            if (caret < 0 || caret >= text.Length) return false;
            return text[caret] == typed;
        }

        /* ----------------------------------------------------------------
           Helpers
           ---------------------------------------------------------------- */

        private static List<string> CollectUrls(string text)
        {
            List<string> result = new List<string>();
            Match match = UrlPattern.Match(text);
            while (match.Success)
            {
                string url = match.Groups[1].Value;
                if (!result.Contains(url)) result.Add(url);
                if (result.Count >= 6) break;
                match = match.NextMatch();
            }
            return result;
        }

        private static bool InsideFence(string text, int position)
        {
            int count = 0;
            int index = 0;
            while (index < position)
            {
                int newline = text.IndexOf('\n', index);
                int end = newline < 0 || newline > position ? position : newline;
                string line = text.Substring(index, end - index).TrimStart();
                if (line.StartsWith("```", StringComparison.Ordinal)) count++;
                if (newline < 0) break;
                index = newline + 1;
            }
            return (count % 2) == 1;
        }

        private static int LineStart(string text, int index)
        {
            if (index <= 0) return 0;
            if (index > text.Length) index = text.Length;
            int newline = text.LastIndexOf('\n', index - 1);
            return newline < 0 ? 0 : newline + 1;
        }
    }
}

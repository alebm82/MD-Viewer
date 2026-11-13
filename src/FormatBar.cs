using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MdViewer
{
    /// <summary>The formatting strip above the editor: heading levels, emphasis,
    /// lists, quotes and insert commands. Every button edits the Markdown source,
    /// so the preview stays the single source of truth. Buttons are not focusable,
    /// which keeps the editor selection alive while clicking them.</summary>
    internal sealed class FormatBar : Themed
    {
        private sealed class Item
        {
            public string Command;
            public FlatButton Button;
            public int Group;
        }

        private const int GroupCount = 5;

        private readonly List<Item> items = new List<Item>();
        private readonly List<VerticalRule> rules = new List<VerticalRule>();
        private readonly Dictionary<string, FlatButton> lookup = new Dictionary<string, FlatButton>();
        private FlatButton more;
        private ToolTip toolTip = new ToolTip();
        private ContextMenuStrip overflow;
        private int visibleGroups;

        /// <summary>Raised with the command name ("bold", "h2", "link", ...).</summary>
        public event Action<string> Command;

        public FormatBar()
        {
            BackColor = Theme.Paper2;

            more = new FlatButton();
            more.Label = "\u2026";
            more.OnPaper = true;
            more.Click += delegate { ShowOverflow(); };
            more.Visible = false;
            Controls.Add(more);
            toolTip.SetToolTip(more, "More formatting commands");

            for (int i = 0; i < GroupCount - 1; i++)
            {
                VerticalRule rule = new VerticalRule();
                rule.OnPaper = true;
                rule.Visible = false;
                rules.Add(rule);
                Controls.Add(rule);
            }

            AddText("h1", "H1", "Heading 1  (Ctrl+Alt+1)", Theme.UiSmallBold, 0);
            AddText("h2", "H2", "Heading 2  (Ctrl+Alt+2)", Theme.UiSmallBold, 0);
            AddText("h3", "H3", "Heading 3  (Ctrl+Alt+3)", Theme.UiSmallBold, 0);

            AddText("bold", "B", "Bold  (Ctrl+B)", Theme.UiSmallBold, 1);
            AddText("italic", "I", "Italic  (Ctrl+I)", Theme.UiSmallItalic, 1);
            AddText("bolditalic", "BI", "Bold Italic  (Ctrl+B + Ctrl+I)", Theme.UiSmallBold, 1);

            AddText("strike", "S", "Strikethrough  (Ctrl+Shift+X)", Theme.UiSmallStrike, 1);
            AddText("code", "</>", "Inline code  (Ctrl+Shift+K)", Theme.UiSmallBold, 1);

            AddText("ul", "\u2022", "Bulleted list  (Ctrl+Shift+8)", Theme.UiSmallBold, 2);
            AddText("ol", "1.", "Numbered list  (Ctrl+Shift+7)", Theme.UiSmallBold, 2);
            AddText("task", "\u2611", "Task list", Theme.UiSmall, 2);
            AddText("quote", "\u275D", "Block quote  (Ctrl+Shift+9)", Theme.UiSmall, 2);

            AddGlyph("link", Theme.IconLink, "Insert link", 3);
            AddGlyph("image", Theme.IconImage, "Insert image", 3);
            AddGlyph("table", Theme.IconTable, "Insert table", 3);
            AddText("rule", "\u2014", "Thematic break", Theme.UiSmallBold, 3);

            AddText("undo", "↩", "Undo  (Ctrl+Z)", Theme.UiSmall, 4);
            AddText("redo", "↪", "Redo  (Ctrl+Y)", Theme.UiSmall, 4);
            AddGlyph("emoji", "😊", "Insert Emoji", 4);
            AddText("emoji", "😊", "Insert Emoji", Theme.UiSmall, 4);
            AddText("notice-note", "📝", "Insert Note", Theme.UiSmall, 4);
            AddText("notice-tip", "💡", "Insert Tip", Theme.UiSmall, 4);
            AddText("notice-warning", "⚠️", "Insert Warning", Theme.UiSmall, 4);
            AddText("notice-important", "❗", "Insert Important", Theme.UiSmall, 4);

            visibleGroups = GroupCount;
        }

        private void AddText(string command, string label, string tip, Font font, int group)
        {
            FlatButton button = new FlatButton();
            button.Label = label;
            button.LabelFont = font;
            button.OnPaper = true;
            button.Tag = tip;
            button.Size = new Size(Ui.S(30), Ui.S(30));
            Register(command, button, group);
            toolTip.SetToolTip(button, tip);
        }

        private void AddGlyph(string command, string glyph, string tip, int group)
        {
            FlatButton button = new FlatButton();
            button.Glyph = glyph;
            button.OnPaper = true;
            button.Tag = tip;
            button.Size = new Size(Ui.S(30), Ui.S(30));
            Register(command, button, group);
            toolTip.SetToolTip(button, tip);
        }

        private void Register(string command, FlatButton button, int group)
        {
            Item item = new Item();
            item.Command = command;
            item.Button = button;
            item.Group = group;
            items.Add(item);
            lookup[command] = button;

            Controls.Add(button);
            button.Click += delegate
            {
                if (Command != null) Command(item.Command);
            };
        }

        public void SetState(EditorState state)
        {
            if (state == null) return;
            Set("h1", state.Heading == 1);
            Set("h2", state.Heading == 2);
            Set("h3", state.Heading == 3);
            Set("bold", state.Bold);
            Set("italic", state.Italic);
            Set("bolditalic", state.Bold && state.Italic);
            Set("strike", state.Strike);
            Set("code", state.Code);
            Set("ul", state.Bullet && !state.Task);
            Set("task", state.Task);
            Set("ol", state.Ordered);
            Set("quote", state.Quote);
        }

        private void Set(string command, bool active)
        {
            FlatButton button;
            if (lookup.TryGetValue(command, out button) && button.Active != active) button.Active = active;
        }

        public override void ApplyTheme()
        {
            BackColor = Theme.Paper2;
            more.ApplyTheme();
            for (int i = 0; i < items.Count; i++) items[i].Button.ApplyTheme();
            for (int i = 0; i < rules.Count; i++) rules[i].ApplyTheme();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            using (Pen pen = new Pen(Theme.Line))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutButtons();
        }

        private void LayoutButtons()
        {
            if (Width <= 0 || Height <= 0) return;

            int pad = Ui.S(14);
            int gap = Ui.S(3);
            int ruleWidth = Ui.S(9);
            int buttonSize = Ui.S(30);
            int y = (Height - buttonSize) / 2;

            int available = Width - pad * 2;
            if (available < buttonSize) available = buttonSize;

            int size = buttonSize;
            y = (Height - size) / 2;

            int x = pad;
            int stop = 0;

            for (int group = 0; group < GroupCount; group++)
            {
                int leading = group > 0 ? ruleWidth + gap : 0;
                int groupWidth = GroupWidth(group, size, gap) + leading;
                int reserve = group < GroupCount - 1 ? size + gap : 0;

                if (x + groupWidth + reserve > pad + available)
                    break;

                if (group > 0)
                {
                    VerticalRule rule = rules[group - 1];
                    rule.Visible = true;
                    rule.SetBounds(x, y, ruleWidth, size);
                    x += ruleWidth + gap;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Group != group) continue;
                    FlatButton button = items[i].Button;
                    button.Visible = true;
                    button.SetBounds(x, y, size, size);
                    x += size + gap;
                }

                stop = group + 1;
            }

            visibleGroups = stop;

            for (int group = stop; group < GroupCount; group++)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Group == group) items[i].Button.Visible = false;
                }
            }

            for (int i = 0; i < rules.Count; i++)
            {
                bool used = i + 1 < stop;
                rules[i].Visible = used;
            }

            if (stop < GroupCount)
            {
                more.Visible = true;
                more.SetBounds(x, y, size, size);
            }
            else
            {
                more.Visible = false;
            }
        }

        private int GroupWidth(int group, int size, int gap)
        {
            int count = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Group == group) count++;
            }
            return count * size + (count - 1) * gap;
        }

        private void ShowOverflow()
        {
            if (overflow == null)
            {
                overflow = new ContextMenuStrip();
                overflow.Opening += delegate
                {
                    overflow.Items.Clear();
                    for (int group = visibleGroups; group < GroupCount; group++)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            if (items[i].Group == group)
                            {
                                ToolStripMenuItem item = new ToolStripMenuItem(items[i].Command);
                                item.Tag = items[i].Command;
                                item.Click += delegate { if (Command != null) Command((string)item.Tag); };
                                overflow.Items.Add(item);
                            }
                        }
                        if (group < GroupCount - 1) overflow.Items.Add(new ToolStripSeparator());
                    }
                };
            }
            overflow.Show(this, new Point(0, Height));
        }
    }
}

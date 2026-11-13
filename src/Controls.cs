using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MdViewer
{
    /// <summary>Base for the small painted controls. Double buffered so hover
    /// states never flicker, and never part of the tab order.</summary>
    internal abstract class Themed : Control
    {
        protected Themed()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        public virtual void ApplyTheme()
        {
            Invalidate();
        }
    }

    /// <summary>A borderless toolbar button. Either a Segoe MDL2 glyph or a short
    /// text label; "Primary" paints the accent pill, "Active" marks a toggle.</summary>
    internal sealed class FlatButton : Themed
    {
        private bool hot;
        private bool down;
        private string glyph = string.Empty;
        private string label = string.Empty;
        private bool primary;
        private bool active;
        private bool onPaper;
        private Font labelFont;

        public FlatButton()
        {
            Cursor = Cursors.Hand;
            Size = new Size(Ui.S(34), Ui.S(34));
            BackColor = Theme.Chrome;
        }

        public string Glyph
        {
            get { return glyph; }
            set { glyph = value ?? string.Empty; Invalidate(); }
        }

        public string Label
        {
            get { return label; }
            set { label = value ?? string.Empty; Invalidate(); }
        }

        public bool Primary
        {
            get { return primary; }
            set { primary = value; Invalidate(); }
        }

        public bool Active
        {
            get { return active; }
            set { if (active != value) { active = value; Invalidate(); } }
        }

        /// <summary>Paints against the paper surface of the format bar instead of the
        /// graphite toolbar, so hover and active tints read correctly in both themes.</summary>
        public bool OnPaper
        {
            get { return onPaper; }
            set { if (onPaper != value) { onPaper = value; ApplyTheme(); } }
        }

        public Font LabelFont
        {
            get { return labelFont ?? Theme.UiSmallBold; }
            set { labelFont = value; Invalidate(); }
        }

        public override void ApplyTheme()
        {
            BackColor = onPaper ? Theme.Paper2 : Theme.Chrome;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = false;
            down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                down = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            Rectangle r = new Rectangle(Ui.S(1), Ui.S(1),
                Math.Max(1, Width - Ui.S(2)), Math.Max(1, Height - Ui.S(2)));

            Color baseColor = onPaper ? Theme.Paper2 : Theme.Chrome;
            Color lift = onPaper && !Theme.Dark ? Color.Black : Color.White;

            Color fill = Color.Empty;
            if (primary)
            {
                fill = down ? Ui.Mix(Theme.Accent, Color.Black, 0.20)
                     : hot ? Ui.Mix(Theme.Accent, Color.White, 0.14)
                     : Theme.Accent;
            }
            else if (active)
            {
                fill = Ui.Mix(baseColor, Theme.Accent, down ? 0.30 : hot ? 0.26 : 0.20);
            }
            else if (down)
            {
                fill = Ui.Mix(baseColor, lift, onPaper ? 0.11 : 0.15);
            }
            else if (hot)
            {
                fill = Ui.Mix(baseColor, lift, onPaper ? 0.06 : 0.08);
            }

            if (!fill.IsEmpty)
                Ui.FillRounded(g, r, Ui.S(onPaper ? 6 : 7), fill);

            Color ink = primary ? Theme.AccentInk
                       : active ? (onPaper ? Theme.Accent : Theme.AccentBright)
                       : (onPaper ? Theme.Ink : Theme.ChromeInk);

            int dy = down ? Ui.S(1) : 0;
            Rectangle full = new Rectangle(0, dy, Width, Height);

            if (glyph.Length > 0 && label.Length == 0)
            {
                Ui.Text(g, glyph, Theme.Glyph, full, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else if (glyph.Length > 0)
            {
                Size icon = Ui.Measure(glyph, Theme.Glyph);
                Size text = Ui.Measure(label, LabelFont);
                int total = icon.Width + Ui.S(6) + text.Width;
                int left = Math.Max(0, (Width - total) / 2);

                Rectangle ir = new Rectangle(left, dy, icon.Width + Ui.S(2), Height);
                Ui.Text(g, glyph, Theme.Glyph, ir, ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                Rectangle tr = new Rectangle(left + icon.Width + Ui.S(6), dy, text.Width + Ui.S(4), Height);
                Ui.Text(g, label, LabelFont, tr, ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                Ui.Text(g, label, LabelFont, full, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    /// <summary>Write / Split / Read switch. One track, one accent pill.</summary>
    internal sealed class SegmentedControl : Themed
    {
        private string[] options = new string[0];
        private int selected;
        private int hotIndex = -1;

        public event EventHandler SelectionChanged;

        public SegmentedControl()
        {
            BackColor = Theme.Chrome;
            Cursor = Cursors.Hand;
            Size = new Size(Ui.S(216), Ui.S(30));
        }

        public string[] Options
        {
            get { return options; }
            set { options = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selected; }
        }

        public void SetSelected(int index)
        {
            if (index < 0 || index >= options.Length || index == selected) return;
            selected = index;
            Invalidate();
        }

        public override void ApplyTheme()
        {
            BackColor = Theme.Chrome;
            Invalidate();
        }

        private int IndexAt(Point p)
        {
            if (options.Length == 0) return -1;
            int inset = Ui.S(3);
            int track = Width - inset * 2;
            if (track <= 0) return -1;
            int seg = track / options.Length;
            if (seg <= 0) return -1;
            if (p.X < inset || p.X >= inset + seg * options.Length) return -1;
            int index = (p.X - inset) / seg;
            return index < 0 ? -1 : (index >= options.Length ? options.Length - 1 : index);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexAt(e.Location);
            if (index != hotIndex)
            {
                hotIndex = index;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hotIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int index = IndexAt(e.Location);
                if (index >= 0 && index != selected)
                {
                    selected = index;
                    Invalidate();
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Ui.EnableSmooth(g);

            int inset = Ui.S(1);
            Rectangle track = new Rectangle(inset, inset,
                Math.Max(1, Width - inset * 2), Math.Max(1, Height - inset * 2));
            Ui.FillRounded(g, track, Ui.S(8), Ui.Mix(Theme.Chrome, Color.White, 0.055));
            Ui.DrawRounded(g, track, Ui.S(8), Theme.ChromeLine, 1f);

            if (options.Length == 0) return;

            int pad = Ui.S(3);
            int segW = Math.Max(1, (Width - pad * 2) / options.Length);
            for (int i = 0; i < options.Length; i++)
            {
                Rectangle cell = new Rectangle(pad + i * segW, pad, segW, Height - pad * 2);
                if (i == selected)
                {
                    Rectangle pill = new Rectangle(cell.X + Ui.S(1), cell.Y, Math.Max(1, cell.Width - Ui.S(2)), cell.Height);
                    Ui.FillRounded(g, pill, Ui.S(6), Theme.Accent);
                    Ui.Text(g, options[i], Theme.UiSmallBold, cell, Theme.AccentInk,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                else
                {
                    if (i == hotIndex)
                        Ui.FillRounded(g, new Rectangle(cell.X + Ui.S(1), cell.Y, Math.Max(1, cell.Width - Ui.S(2)), cell.Height),
                            Ui.S(6), Ui.Mix(Theme.Chrome, Color.White, 0.07));
                    Ui.Text(g, options[i], Theme.UiSmall, cell, i == hotIndex ? Theme.ChromeInk : Theme.ChromeMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
        }
    }

    /// <summary>The 34px strip above each pane: a small accent tick, a quiet
    /// uppercase title, and one meta value on the right.</summary>
    internal sealed class PaneHeader : Themed
    {
        private string title = string.Empty;
        private string meta = string.Empty;

        public PaneHeader()
        {
            BackColor = Theme.Paper2;
        }

        public string Title
        {
            get { return title; }
            set { title = value ?? string.Empty; Invalidate(); }
        }

        public string Meta
        {
            get { return meta; }
            set { if (meta != value) { meta = value ?? string.Empty; Invalidate(); } }
        }

        public override void ApplyTheme()
        {
            BackColor = Theme.Paper2;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Ui.EnableSmooth(g);

            int pad = Ui.S(16);
            int dot = Ui.S(4);
            int cy = Height / 2;
            Rectangle mark = new Rectangle(pad, cy - dot / 2, dot, dot);
            Ui.FillRounded(g, mark, dot, Theme.Accent);

            int textX = pad + dot + Ui.S(8);
            Rectangle titleRect = new Rectangle(textX, 0, Math.Max(1, Width - textX - pad), Height);
            Ui.Text(g, title.ToUpperInvariant(), Theme.UiSmallBold, titleRect, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);

            if (meta.Length > 0)
            {
                Size size = Ui.Measure(meta.ToUpperInvariant(), Theme.UiSmall);
                Rectangle metaRect = new Rectangle(Math.Max(textX, Width - pad - size.Width - Ui.S(2)), 0,
                    size.Width + Ui.S(2), Height);
                Ui.Text(g, meta.ToUpperInvariant(), Theme.UiSmall, metaRect, Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            using (Pen pen = new Pen(Theme.Line))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    /// <summary>Graphite strip along the bottom: document state on the left,
    /// caret and document measurements on the right.</summary>
    internal sealed class StatusBar : Themed
    {
        private string fileName = "Untitled.md";
        private bool dirty;
        private bool hasFile;
        private int words;
        private int chars;
        private int line = 1;
        private int column = 1;
        private string message = string.Empty;

        public StatusBar()
        {
            BackColor = Theme.Chrome;
        }

        public string FileName
        {
            get { return fileName; }
            set { fileName = value ?? string.Empty; Invalidate(); }
        }

        public bool Dirty
        {
            get { return dirty; }
            set { if (dirty != value) { dirty = value; Invalidate(); } }
        }

        public bool HasFile
        {
            get { return hasFile; }
            set { if (hasFile != value) { hasFile = value; Invalidate(); } }
        }

        public int Words
        {
            get { return words; }
            set { if (words != value) { words = value; Invalidate(); } }
        }

        public int Chars
        {
            get { return chars; }
            set { if (chars != value) { chars = value; Invalidate(); } }
        }

        public int Line
        {
            get { return line; }
            set { if (line != value) { line = value; Invalidate(); } }
        }

        public int Column
        {
            get { return column; }
            set { if (column != value) { column = value; Invalidate(); } }
        }

        public string Message
        {
            get { return message; }
            set { message = value ?? string.Empty; Invalidate(); }
        }

        public override void ApplyTheme()
        {
            BackColor = Theme.Chrome;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Ui.EnableSmooth(g);

            using (Pen pen = new Pen(Theme.ChromeLine))
            {
                g.DrawLine(pen, 0, 0, Width, 0);
            }

            int pad = Ui.S(16);
            Color dot = dirty ? Theme.Accent : hasFile ? Theme.Clean : Theme.ChromeMuted;
            Rectangle dotRect = new Rectangle(pad, (Height - Ui.S(6)) / 2, Ui.S(6), Ui.S(6));
            Ui.FillRounded(g, dotRect, Ui.S(6), dot);

            int x = pad + Ui.S(6) + Ui.S(8);
            int rightLimit = Width - pad;

            /* right side first, to know how much room the name may use */
            string right = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                "Ln {0}, Col {1}   \u00b7   {2} words   \u00b7   {3} chars   \u00b7   UTF-8",
                line, column, words.ToString("N0"), chars.ToString("N0"));
            Size rightSize = Ui.Measure(right, Theme.UiSmall);
            int rightX = Math.Max(x + Ui.S(60), rightLimit - rightSize.Width);

            bool showRight = rightX > x + Ui.S(80);
            if (showRight)
            {
                Rectangle rr = new Rectangle(rightX, 0, rightSize.Width + Ui.S(2), Height);
                Ui.Text(g, right, Theme.UiSmall, rr, Theme.ChromeMuted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                rightLimit = rightX - Ui.S(16);
            }

            Size nameSize = Ui.Measure(fileName, Theme.UiSmallBold);
            int nameWidth = Math.Min(nameSize.Width, Math.Max(0, rightLimit - x));
            Rectangle nameRect = new Rectangle(x, 0, nameWidth + Ui.S(2), Height);
            Ui.Text(g, fileName, Theme.UiSmallBold, nameRect, Theme.ChromeInk,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);

            string state = message.Length > 0
                ? message
                : dirty ? "Not saved"
                : hasFile ? "Saved"
                : "Draft";
            Color stateColor = message.Length > 0 ? Theme.AccentBright : Theme.ChromeMuted;

            int stateX = x + nameWidth + Ui.S(10);
            if (stateX < rightLimit - Ui.S(30))
            {
                Size stateSize = Ui.Measure(state, Theme.UiSmall);
                Rectangle sr = new Rectangle(stateX, 0,
                    Math.Max(0, Math.Min(stateSize.Width + Ui.S(2), rightLimit - stateX)), Height);
                Ui.Text(g, state, Theme.UiSmall, sr, stateColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Thin separator between toolbar groups.</summary>
    internal sealed class VerticalRule : Themed
    {
        private bool onPaper;

        public VerticalRule()
        {
            BackColor = Theme.Chrome;
            Size = new Size(Ui.S(11), Ui.S(34));
        }

        public bool OnPaper
        {
            get { return onPaper; }
            set { if (onPaper != value) { onPaper = value; ApplyTheme(); } }
        }

        public override void ApplyTheme()
        {
            BackColor = onPaper ? Theme.Paper2 : Theme.Chrome;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int h = Ui.S(16);
            int y = (Height - h) / 2;
            using (Pen pen = new Pen(onPaper ? Theme.Line : Theme.ChromeLine))
            {
                g.DrawLine(pen, Width / 2, y, Width / 2, y + h);
            }
        }
    }

    /// <summary>The draggable gap between the two panes. A quiet hairline that
    /// grows an accent grip under the pointer; double-click resets to 50/50.</summary>
    internal sealed class SplitHandle : Themed
    {
        private bool hot;
        private bool dragging;

        public SplitHandle()
        {
            BackColor = Theme.Chrome;
            Cursor = Cursors.SizeWE;
            Size = new Size(Ui.S(11), Ui.S(100));
        }

        public bool Dragging
        {
            get { return dragging; }
            set { if (dragging != value) { dragging = value; Invalidate(); } }
        }

        public override void ApplyTheme()
        {
            BackColor = Theme.Chrome;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Ui.EnableSmooth(g);

            int x = Width / 2;
            using (Pen pen = new Pen(Theme.ChromeLine))
            {
                g.DrawLine(pen, x, 0, x, Height);
            }

            if (hot || dragging)
            {
                int gripH = Ui.S(46);
                int gripW = dragging ? Ui.S(4) : Ui.S(3);
                Rectangle grip = new Rectangle(x - gripW / 2, (Height - gripH) / 2, gripW, gripH);
                Color color = dragging ? Theme.AccentBright : Theme.Accent;
                Ui.FillRounded(g, grip, gripW, color);
            }
        }
    }

    /// <summary>The 56px graphite title strip. Drawn text (monogram + wordmark)
    /// lives here; the real buttons are placed on top by the form.</summary>
    internal sealed class TopBar : Themed
    {
        public TopBar()
        {
            BackColor = Theme.Chrome;
            Size = new Size(Ui.S(800), Ui.S(56));
        }

        public override void ApplyTheme()
        {
            BackColor = Theme.Chrome;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Ui.EnableSmooth(g);

            using (Pen pen = new Pen(Theme.ChromeLine))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }

            int pad = Ui.S(16);
            int box = Ui.S(28);
            Rectangle mark = new Rectangle(pad, (Height - box) / 2, box, box);

            Color a = Theme.Accent;
            Color b = Ui.Mix(Theme.Accent, Color.Black, 0.32);
            using (GraphicsPath path = Ui.Rounded(mark, Ui.S(7)))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(mark.X, mark.Y, mark.Width, mark.Height), a, b, LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(brush, path);
            }

            Rectangle letter = new Rectangle(mark.X, mark.Y - Ui.S(1), mark.Width, mark.Height);
            Ui.Text(g, "M", Theme.Mark, letter, Theme.AccentInk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            Rectangle word = new Rectangle(pad + box + Ui.S(11), 0,
                Math.Max(1, Width - (pad + box + Ui.S(11)) - Ui.S(6)), Height);
            Ui.Text(g, "MD Viewer", Theme.Brand, word, Theme.ChromeInk,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>The Markdown source box. The only change from a plain TextBox is
    /// that pasted text is converted to CRLF first: the native edit control
    /// needs CR+LF pairs, so a clipboard that only carries LF would otherwise
    /// collapse onto one visual line even though the text itself is intact.</summary>
    internal sealed class MarkdownBox : TextBox
    {
        private const int WM_PASTE = 0x0302;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int EMUNDO = 0x00C7;
        private const int EMREDO = 0x0454;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Raised after the caret scrolls, so a floating suggestion list can close.</summary>
        public event EventHandler Scrolled;

        public bool CanUndo
        {
            get { return SendMessage(Handle, EMUNDO, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero; }
        }

        public bool CanRedo
        {
            get { return SendMessage(Handle, EMREDO, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero; }
        }

        public void Undo()
        {
            SendMessage(Handle, EMUNDO, IntPtr.Zero, IntPtr.Zero);
        }

        public void Redo()
        {
            SendMessage(Handle, EMREDO, IntPtr.Zero, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PASTE)
            {
                string text;
                try
                {
                    text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                }
                catch
                {
                    text = null;
                }

                if (text != null)
                {
                    if (text.Length > 0) SelectedText = ToCrlf(text);
                    return;
                }
            }

            base.WndProc(ref m);

            if ((m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL) && Scrolled != null)
                Scrolled(this, EventArgs.Empty);
        }

        private static string ToCrlf(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        }
    }

    /// <summary>A double-buffered panel used as the editor's paper surface.</summary>
    internal sealed class Surface : Panel
    {
        public Surface()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Paper;
        }
    }
}

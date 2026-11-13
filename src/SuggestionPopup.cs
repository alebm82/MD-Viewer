using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdViewer
{
    /// <summary>A small, focus-free suggestion list pinned under the caret.
    /// WS_EX_NOACTIVATE keeps the TextBox focused, so typing continues normally;
    /// the editor handles Arrow/Tab/Enter/Escape and calls back into this form.</summary>
    internal sealed class SuggestionPopup : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly List<Suggestion> items = new List<Suggestion>();
        private int selected;
        private int rowHeight;
        private int padX;

        /// <summary>Raised when a row is clicked with the mouse.</summary>
        public event EventHandler Accepted;

        public SuggestionPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Paper;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            rowHeight = Ui.S(25);
            padX = Ui.S(12);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public bool HasItems
        {
            get { return items.Count > 0; }
        }

        public Suggestion Current
        {
            get { return items.Count == 0 ? null : items[selected]; }
        }

        public void ShowItems(List<Suggestion> list, Point caretBottom)
        {
            items.Clear();
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) items.Add(list[i]);
            }

            if (items.Count == 0)
            {
                Hide();
                return;
            }

            selected = 0;

            int width = Ui.S(216);
            for (int i = 0; i < items.Count; i++)
            {
                int w = Ui.Measure(items[i].Display, Theme.UiSmallBold).Width +
                        Ui.Measure(items[i].Hint, Theme.UiSmall).Width +
                        padX * 3;
                if (w > width) width = w;
            }
            if (width > Ui.S(460)) width = Ui.S(460);

            int height = items.Count * rowHeight + Ui.S(2);

            Rectangle work = Screen.FromPoint(caretBottom).WorkingArea;
            int x = caretBottom.X;
            int y = caretBottom.Y + Ui.S(2);

            if (x + width > work.Right) x = work.Right - width;
            if (x < work.Left) x = work.Left;

            if (y + height > work.Bottom)
                y = caretBottom.Y - height - rowHeight - Ui.S(4);
            if (y < work.Top) y = work.Top;

            Bounds = new Rectangle(x, y, width, height);
            if (!Visible) Show();
            Invalidate();
        }

        public void MoveSelection(int delta)
        {
            if (items.Count == 0) return;
            selected = (selected + delta + items.Count) % items.Count;
            Invalidate();
        }

        public void Dismiss()
        {
            if (Visible) Hide();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Ui.Rounded(ClientRectangle, Ui.S(6)))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Paper);
            Ui.EnableSmooth(g);

            for (int i = 0; i < items.Count; i++)
            {
                Rectangle row = new Rectangle(Ui.S(1), Ui.S(1) + i * rowHeight,
                    Math.Max(1, Width - Ui.S(2)), rowHeight);

                if (i == selected)
                {
                    Ui.FillRounded(g, row, Ui.S(5), Ui.Mix(Theme.Paper, Theme.Accent, 0.16));
                    Ui.FillRounded(g, new Rectangle(row.X + Ui.S(1), row.Y + Ui.S(5),
                        Ui.S(3), Math.Max(1, row.Height - Ui.S(10))), Ui.S(2), Theme.Accent);
                }

                Rectangle textRect = new Rectangle(row.X + padX, row.Y,
                    Math.Max(1, row.Width - padX * 2), row.Height);
                Ui.Text(g, items[i].Display, Theme.UiSmallBold, textRect,
                    i == selected ? Theme.Ink : Theme.InkSoft,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                if (items[i].Hint.Length > 0 && row.Width > Ui.S(150))
                {
                    Size hint = Ui.Measure(items[i].Hint, Theme.UiSmall);
                    Rectangle hintRect = new Rectangle(
                        Math.Max(textRect.X, row.Right - padX - hint.Width),
                        row.Y, hint.Width + Ui.S(2), row.Height);
                    Ui.Text(g, items[i].Hint, Theme.UiSmall, hintRect, Theme.Muted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine);
                }
            }

            Ui.DrawRounded(g, ClientRectangle, Ui.S(6), Theme.LineStrong, 1f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexAt(e.Y);
            if (index >= 0 && index != selected)
            {
                selected = index;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int index = IndexAt(e.Y);
            if (e.Button == MouseButtons.Left && index >= 0)
            {
                selected = index;
                if (Accepted != null) Accepted(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        private int IndexAt(int y)
        {
            int index = (y - Ui.S(1)) / Math.Max(1, rowHeight);
            return index >= 0 && index < items.Count ? index : -1;
        }
    }
}

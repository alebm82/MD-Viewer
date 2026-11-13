using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MdViewer;

namespace MdViewer
{
    internal sealed class MainForm : Form
    {
        private const string EmptyHtml =
            "<div class=\"empty\"><h2>Nothing to read yet</h2>" +
            "<p>Write Markdown in the left pane, drop a .md file on the window, " +
            "or press Ctrl+O to open one.</p></div>";

        private static readonly Regex QuotePrefix =
            new Regex("^([ \\t]*(?:>[ \\t]?)+)", RegexOptions.Compiled);

        private static readonly Regex ListMarker =
            new Regex("^( *)(?:([-*+])|(\\d+)([.)]))( +)(\\[[ xX]\\] +)?", RegexOptions.Compiled);

        private readonly Settings settings;
        private readonly DocumentSession session;

        private TopBar topBar;
        private SegmentedControl views;
        private FlatButton btnNew;
        private FlatButton btnOpen;
        private FlatButton btnSave;
        private FlatButton btnCopy;
        private FlatButton btnPrint;
        private FlatButton btnFont;
        private FlatButton btnSync;
        private FlatButton btnTheme;
        private FlatButton btnAssoc;
        private VerticalRule ruleA;
        private VerticalRule ruleB;
        private FormatBar formatBar;
        private PaneHeader headerEditor;
        private PaneHeader headerPreview;
        private Surface editorHost;
        private MarkdownBox editor;
        private Label placeholder;
        private Preview preview;
        private SplitHandle splitter;
        private StatusBar statusBar;
        private ToolTip tips;
        private SuggestionPopup suggestions;
        private Timer renderTimer;
        private Timer autosaveTimer;
        private Timer scrollTimer;
        private Timer flashTimer;

        private bool built;
        private bool loading;
        private bool dragging;
        private bool dirty;
        private bool dark;
        private bool sync;
        private bool wrap;
        private bool suggestEnabled;
        private bool pairsEnabled;
        private bool applyingEdit;
        private bool swallowNextChar;
        private int suggestStart;
        private int suggestLength;
        private string view;
        private double split;
        private string currentPath = string.Empty;
        private string currentName = "Untitled.md";
        private string baseline = string.Empty;
        private string newline = "\r\n";
        private string fontId = "system-serif";
        private string codeFontId = "system-mono";
        private int fontSize = 100;
        private double previewZoom = 1.0;
        private ContextMenuStrip fontMenu;

        private int lastFirstLine = -1;
        private double lastSentScroll = -1;

        public MainForm(string initialPath)
        {
            settings = Settings.Load();
            session = DocumentSession.Load();

            dark = settings.Theme == "dark";
            sync = settings.Sync;
            wrap = settings.Wrap;
            suggestEnabled = settings.Autocomplete;
            pairsEnabled = settings.AutoPairs;
            split = Math.Min(0.82, Math.Max(0.18, settings.Split));
            view = settings.View == "write" || settings.View == "read" ? settings.View : "split";
            fontId = Fonts.Find(settings.Font) != null ? settings.Font : "system-serif";
            codeFontId = Fonts.Find(settings.CodeFont) != null ? settings.CodeFont : "system-mono";
            fontSize = Math.Min(200, Math.Max(70, settings.FontSize));
            previewZoom = Math.Min(3.0, Math.Max(0.4, settings.Zoom));

            Theme.Set(dark);

            BuildUi();
            RestoreWindow();
            ApplyTheme();
            ApplyView();

            LoadStartup(initialPath);
        }

        /* ==================================================================
           CONSTRUCTION
           ================================================================== */

        private void BuildUi()
        {
            SuspendLayout();

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            AutoScaleMode = AutoScaleMode.None;
            RightToLeft = RightToLeft.No;
            RightToLeftLayout = false;
            Font = Theme.Ui;
            Text = "MD Viewer";
            BackColor = Theme.Chrome;
            MinimumSize = new Size(Ui.S(600), Ui.S(560));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;

            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // Without an extractable icon the default form icon is used.
            }

            topBar = new TopBar();
            statusBar = new StatusBar();
            formatBar = new FormatBar();
            formatBar.Command += ExecuteFormat;
            views = new SegmentedControl();
            views.Options = new string[] { "Write", "Split", "Read" };

            btnNew = MakeButton(Theme.IconNew, "New document  (Ctrl+N)");
            btnOpen = MakeButton(Theme.IconOpen, "Open file  (Ctrl+O)");
            btnSave = MakeButton(Theme.IconSave, "Save  (Ctrl+S)");
            btnSave.Primary = true;
            btnCopy = MakeButton(Theme.IconCopy, "Copy selection or all Markdown  (Ctrl+Shift+C)");
            btnPrint = MakeButton(Theme.IconPrint, "Print preview  (Ctrl+P)");
            btnFont = new FlatButton();
            btnFont.Label = "Aa";
            btnFont.LabelFont = Theme.UiBold;
            btnFont.Tag = "Reading and code fonts";
            btnFont.Size = new Size(Ui.S(38), Ui.S(34));
            btnSync = MakeButton(Theme.IconSync, "Follow the editor while scrolling");
            btnTheme = MakeButton(Theme.IconMoon, "Switch theme  (Ctrl+Shift+D)");
            btnAssoc = MakeButton(Theme.IconDefault, "Make MD Viewer the default app for .md files");
            btnAssoc.Active = FileAssoc.IsRegistered();

            ruleA = new VerticalRule();
            ruleB = new VerticalRule();

            headerEditor = new PaneHeader();
            headerEditor.Title = "Markdown";
            headerPreview = new PaneHeader();
            headerPreview.Title = "Preview";

            editorHost = new Surface();
            editor = new MarkdownBox();
            editor.Multiline = true;
            editor.BorderStyle = BorderStyle.None;
            editor.AcceptsTab = true;
            editor.MaxLength = 0;
            editor.RightToLeft = RightToLeft.No;
            editor.WordWrap = wrap;
            editor.ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both;
            editor.ShortcutsEnabled = true;
            editor.Font = Theme.Mono;
            editor.BackColor = Theme.Paper;
            editor.ForeColor = Theme.Ink;
            editor.TextChanged += OnEditorTextChanged;
            editor.KeyDown += OnEditorKeyDown;
            editor.KeyPress += OnEditorKeyPress;
            editor.KeyUp += delegate { UpdateCaret(); };
            editor.MouseUp += delegate { UpdateCaret(); UpdateSuggestions(); };
            editor.Enter += OnEditorFocusChanged;
            editor.Leave += OnEditorFocusChanged;
            editor.Scrolled += delegate { DismissSuggestions(); };
            editor.AllowDrop = true;
            editor.DragEnter += OnDragEnter;
            editor.DragDrop += OnDragDrop;

            placeholder = new Label();
            placeholder.AutoSize = true;
            placeholder.UseMnemonic = false;
            placeholder.Font = Theme.Doc;
            placeholder.BackColor = Theme.Paper;
            placeholder.ForeColor = Theme.Muted;
            placeholder.Cursor = Cursors.IBeam;
            placeholder.Text = "Write Markdown here. Drop a .md file, or press Ctrl+O.";
            placeholder.Click += delegate { editor.Focus(); };

            editorHost.Controls.Add(editor);
            editorHost.Controls.Add(placeholder);
            placeholder.BringToFront();
            editorHost.AllowDrop = true;
            editorHost.DragEnter += OnDragEnter;
            editorHost.DragDrop += OnDragDrop;

            preview = new Preview();
            preview.AllowDrop = true;
            preview.DragEnter += OnDragEnter;
            preview.DragDrop += OnDragDrop;
            preview.CopySourceRequested += delegate { CopyMarkdownAll(); };
            preview.SetTypography(fontId, codeFontId, fontSize);
            preview.SetZoom(previewZoom);
            preview.ZoomChanged += delegate
            {
                previewZoom = preview.Zoom;
                UpdateHeaders();
                SaveSettingsSoon();
            };

            splitter = new SplitHandle();
            splitter.MouseDown += OnSplitMouseDown;
            splitter.MouseMove += OnSplitMouseMove;
            splitter.MouseUp += OnSplitMouseUp;
            splitter.DoubleClick += delegate { split = 0.5; PerformLayout(); SaveSettingsSoon(); lastSentScroll = -1; };

            topBar.Controls.Add(views);
            topBar.Controls.Add(btnNew);
            topBar.Controls.Add(btnOpen);
            topBar.Controls.Add(btnSave);
            topBar.Controls.Add(ruleA);
            topBar.Controls.Add(btnCopy);
            topBar.Controls.Add(btnPrint);
            topBar.Controls.Add(ruleB);
            topBar.Controls.Add(btnFont);
            topBar.Controls.Add(btnSync);
            topBar.Controls.Add(btnTheme);
            topBar.Controls.Add(btnAssoc);

            Controls.Add(topBar);
            Controls.Add(statusBar);
            Controls.Add(formatBar);
            Controls.Add(headerEditor);
            Controls.Add(editorHost);
            Controls.Add(splitter);
            Controls.Add(headerPreview);
            Controls.Add(preview);

            views.SelectionChanged += delegate
            {
                SetView(views.SelectedIndex == 0 ? "write" : views.SelectedIndex == 2 ? "read" : "split");
            };

            btnNew.Click += delegate { NewDocument(); };
            btnOpen.Click += delegate { OpenDocument(); };
            btnSave.Click += delegate { Save(); };
            btnCopy.Click += delegate { CopyMarkdown(); };
            btnPrint.Click += delegate { PrintCurrent(); };
            btnFont.Click += delegate { ShowFontMenu(); };
            btnSync.Click += delegate { ToggleSync(); };
            btnTheme.Click += delegate { ToggleTheme(); };
            btnAssoc.Click += delegate { SetDefaultApp(); };

            editor.ContextMenuStrip = BuildEditorMenu();
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            tips = new ToolTip();
            tips.ShowAlways = false;
            tips.SetToolTip(btnNew, (string)btnNew.Tag);
            tips.SetToolTip(btnOpen, (string)btnOpen.Tag);
            tips.SetToolTip(btnSave, (string)btnSave.Tag);
            tips.SetToolTip(btnCopy, (string)btnCopy.Tag);
            tips.SetToolTip(btnPrint, (string)btnPrint.Tag);
            tips.SetToolTip(btnFont, (string)btnFont.Tag);
            tips.SetToolTip(btnSync, (string)btnSync.Tag);
            tips.SetToolTip(btnTheme, (string)btnTheme.Tag);
            tips.SetToolTip(btnAssoc, (string)btnAssoc.Tag);
            tips.SetToolTip(splitter, "Drag to resize \u00b7 double-click for 50/50");
            tips.SetToolTip(editor, "Markdown source");

            renderTimer = new Timer();
            renderTimer.Interval = 180;
            renderTimer.Tick += delegate
            {
                renderTimer.Stop();
                RenderNow();
            };

            autosaveTimer = new Timer();
            autosaveTimer.Interval = 1200;
            autosaveTimer.Tick += delegate
            {
                autosaveTimer.Stop();
                session.Save(editor.Text, baseline, currentName, currentPath);
            };

            scrollTimer = new Timer();
            scrollTimer.Interval = 90;
            scrollTimer.Tick += OnScrollTick;

            flashTimer = new Timer();
            flashTimer.Interval = 2400;
            flashTimer.Tick += delegate
            {
                flashTimer.Stop();
                statusBar.Message = string.Empty;
            };

            Shown += delegate
            {
                Native.DarkTitleBar(Handle, true);
                if (editor.IsHandleCreated) Native.ThemeControl(editor.Handle, dark);
                if (view != "read") editor.Focus();
                UpdateCaret();
            };

            Move += delegate { DismissSuggestions(); };
            Resize += delegate { DismissSuggestions(); };

            built = true;
            ResumeLayout(true);
        }

        private FlatButton MakeButton(string glyph, string tip)
        {
            FlatButton button = new FlatButton();
            button.Glyph = glyph;
            button.Tag = tip;
            return button;
        }

        private ContextMenuStrip BuildEditorMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Font = Theme.Ui;

            ToolStripMenuItem undo = Item(menu, "Undo", "Ctrl+Z", delegate
            {
                if (editor.CanUndo) editor.Undo();
            });
            Item(menu, "Redo", "Ctrl+Y", delegate { Native.EditRedo(editor.Handle); });
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "Cut", "Ctrl+X", delegate { editor.Cut(); });
            Item(menu, "Copy", "Ctrl+C", delegate { editor.Copy(); });
            Item(menu, "Paste", "Ctrl+V", delegate { editor.Paste(); });
            Item(menu, "Select all", "Ctrl+A", delegate { editor.SelectAll(); });
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "Copy all Markdown", "Ctrl+Shift+C", delegate { CopyMarkdownAll(); });
            ToolStripMenuItem wrapItem = new ToolStripMenuItem("Word wrap");
            wrapItem.Checked = wrap;
            wrapItem.Click += delegate { ToggleWrap(); };
            menu.Items.Add(wrapItem);

            ToolStripMenuItem aids = new ToolStripMenuItem("Typing aids");
            ToolStripMenuItem autoItem = new ToolStripMenuItem("Suggest Markdown as I type");
            autoItem.Checked = suggestEnabled;
            autoItem.Click += delegate { ToggleAutocomplete(); };
            ToolStripMenuItem pairItem = new ToolStripMenuItem("Close brackets and backticks");
            pairItem.Checked = pairsEnabled;
            pairItem.Click += delegate { ToggleAutoPairs(); };
            aids.DropDownItems.Add(autoItem);
            aids.DropDownItems.Add(pairItem);
            menu.Items.Add(aids);
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "Open\u2026", "Ctrl+O", delegate { OpenDocument(); });
            Item(menu, "Save", "Ctrl+S", delegate { Save(); });

            menu.Opening += delegate
            {
                undo.Enabled = editor.CanUndo;
                wrapItem.Checked = wrap;
                autoItem.Checked = suggestEnabled;
                pairItem.Checked = pairsEnabled;
            };

            return menu;
        }

        private static ToolStripMenuItem Item(ContextMenuStrip menu, string text, string shortcut, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ShortcutKeyDisplayString = shortcut;
            item.Click += handler;
            menu.Items.Add(item);
            return item;
        }

        private void RestoreWindow()
        {
            Rectangle bounds = settings.Bounds;
            bool visible = false;
            if (bounds != Rectangle.Empty)
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(bounds))
                    {
                        visible = true;
                        break;
                    }
                }
            }

            if (visible)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
            else
            {
                Size = new Size(Ui.S(1280), Ui.S(820));
            }

            if (settings.Maximized) WindowState = FormWindowState.Maximized;
        }

        /* ==================================================================
           STARTUP AND SESSION
           ================================================================== */

        private void LoadStartup(string initialPath)
        {
            if (!string.IsNullOrEmpty(initialPath) && File.Exists(initialPath))
            {
                LoadFile(initialPath, false);
            }
            else if (session.Exists)
            {
                currentName = string.IsNullOrEmpty(session.Name) ? "Untitled.md" : session.Name;
                currentPath = session.FilePath ?? string.Empty;

                newline = DetectNewline(session.Draft);
                SetEditorText(session.Draft);
                baseline = session.Baseline ?? string.Empty;
                dirty = !SameText(editor.Text, baseline);

                if (dirty) Flash("Draft restored");
            }
            else
            {
                SetEditorText(string.Empty);
                baseline = string.Empty;
                dirty = false;
            }


            UpdateStatus();
            UpdateHeaders();
            RenderNow();
            PerformLayout();
        }

        private void SetEditorText(string text)
        {
            loading = true;
            try
            {
                editor.Text = ToNewline(text, "\r\n");
            }
            finally
            {
                loading = false;
            }

            editor.SelectionStart = 0;
            editor.SelectionLength = 0;
            editor.ScrollToCaret();
            placeholder.Visible = editor.TextLength == 0;
            UpdateCaret();
        }

        private bool LoadFile(string path, bool announce)
        {
            string text;
            try
            {
                text = ReadTextFile(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The file could not be read.\n\n" + ex.Message,
                    "MD Viewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            currentPath = Path.GetFullPath(path);
            currentName = Path.GetFileName(currentPath);
            newline = DetectNewline(text);

            SetEditorText(text);
            baseline = editor.Text;
            dirty = false;

            session.Save(editor.Text, baseline, currentName, currentPath);
            preview.SetScrollFraction(0);
            lastSentScroll = -1;
            lastFirstLine = -1;

            UpdateStatus();
            UpdateHeaders();
            RenderNow();

            if (announce) Flash("Opened " + currentName);
            return true;
        }

        /* ==================================================================
           RENDERING
           ================================================================== */

        private void RenderNow()
        {
            string text = editor.Text;
            string html;
            try
            {
                /* Pictures that are written as a relative path belong to the
                   folder of the document. */
                html = text.Trim().Length == 0 ? EmptyHtml : Markdown.ToHtml(text, DocumentFolder());
            }
            catch
            {
                html = "<p>This document could not be rendered.</p>";
            }

            preview.Render(html);
            UpdateCounts();
            UpdateHeaders();

            headerEditor.Meta = "UTF-8 \u00b7 " + NewlineName();
            if (view == "split" && sync)
            {
                lastSentScroll = -1;
                lastFirstLine = -1;
                scrollTimer.Enabled = true;
            }
        }

        private void OnEditorTextChanged(object sender, EventArgs e)
        {
            placeholder.Visible = editor.TextLength == 0;
            UpdateCaret();
            if (loading) return;

            dirty = !SameText(editor.Text, baseline);
            statusBar.Dirty = dirty;
            Text = TitleText();
            UpdateSuggestions();

            renderTimer.Stop();
            renderTimer.Start();
            autosaveTimer.Stop();
            autosaveTimer.Start();
        }

        private void OnEditorFocusChanged(object sender, EventArgs e)
        {
            placeholder.Visible = editor.TextLength == 0;
        }

        private void UpdateCaret()
        {
            if (!editor.IsHandleCreated) return;
            int index = editor.SelectionStart;
            int line = editor.GetLineFromCharIndex(index);
            int start = editor.GetFirstCharIndexFromLine(line);
            if (start < 0) start = 0;
            statusBar.Line = line + 1;
            statusBar.Column = Math.Max(1, index - start + 1);
            FormatBarState();
        }

        private void UpdateCounts()
        {
            statusBar.Words = CountWords(editor.Text);
            statusBar.Chars = editor.TextLength;
        }

        private void UpdateStatus()
        {
            statusBar.FileName = currentName;
            statusBar.Dirty = dirty;
            statusBar.HasFile = currentPath.Length > 0;
            statusBar.Words = CountWords(editor.Text);
            statusBar.Chars = editor.TextLength;
            Text = TitleText();
            UpdateCaret();
            placeholder.Visible = editor.TextLength == 0;
        }

        private void UpdateHeaders()
        {
            headerEditor.Meta = "UTF-8 \u00b7 " + NewlineName();
            headerPreview.Meta = PreviewMeta();
            btnSync.Active = sync;
        }

        private string TitleText()
        {
            return (dirty ? "\u2022 " : string.Empty) + currentName + " \u2014 MD Viewer";
        }

        private string NewlineName()
        {
            return newline == "\n" ? "LF" : "CRLF";
        }

        private static int CountWords(string text)
        {
            int count = 0;
            bool inWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                bool space = char.IsWhiteSpace(text[i]);
                if (!space && !inWord)
                {
                    count++;
                    inWord = true;
                }
                else if (space)
                {
                    inWord = false;
                }
            }
            return count;
        }

        /* ==================================================================
           SCROLL SYNC
           ================================================================== */

        private void OnScrollTick(object sender, EventArgs e)
        {
            if (!sync || view != "split" || !editor.IsHandleCreated || !editor.Visible) return;

            int first = Native.FirstVisibleLine(editor.Handle);
            if (first == lastFirstLine) return;
            lastFirstLine = first;

            int lineHeight = Math.Max(1, editor.Font.Height);
            int visible = Math.Max(1, editor.ClientSize.Height / lineHeight);
            int total = editor.GetLineFromCharIndex(editor.TextLength) + 1;
            double fraction = total <= visible ? 0 : (double)first / (total - visible);
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;

            if (Math.Abs(fraction - lastSentScroll) < 0.003) return;
            lastSentScroll = fraction;
            preview.SetScrollFraction(fraction);
        }

        /* ==================================================================
           VIEW AND THEME
           ================================================================== */

        private void ApplyView()
        {
            views.SetSelected(ViewIndex(view));
            headerPreview.Meta = sync ? "Synced" : "Live";
            scrollTimer.Enabled = sync && view == "split";
            lastSentScroll = -1;
            lastFirstLine = -1;
            PerformLayout();
        }

        private int ViewIndex(string mode)
        {
            return mode == "write" ? 0 : mode == "read" ? 2 : 1;
        }

        private void SetView(string mode)
        {
            if (mode != "write" && mode != "read") mode = "split";
            view = mode;
            settings.View = mode;
            DismissSuggestions();
            ApplyView();
            if (mode == "write") editor.Focus();
        }

        private void ApplyTheme()
        {
            Theme.Set(dark);

            SuspendLayout();
            BackColor = Theme.Chrome;

            topBar.ApplyTheme();
            statusBar.ApplyTheme();
            formatBar.ApplyTheme();
            views.ApplyTheme();
            headerEditor.ApplyTheme();
            headerPreview.ApplyTheme();
            btnNew.ApplyTheme();
            btnOpen.ApplyTheme();
            btnSave.ApplyTheme();
            btnCopy.ApplyTheme();
            btnPrint.ApplyTheme();
            btnSync.ApplyTheme();
            btnTheme.ApplyTheme();
            btnAssoc.ApplyTheme();
            ruleA.ApplyTheme();
            ruleB.ApplyTheme();
            splitter.ApplyTheme();

            editorHost.BackColor = Theme.Paper;
            editor.BackColor = Theme.Paper;
            editor.ForeColor = Theme.Ink;
            placeholder.BackColor = Theme.Paper;
            placeholder.ForeColor = Theme.Muted;

            preview.SetTheme(dark);

            btnSync.Active = sync;
            btnTheme.Glyph = dark ? Theme.IconSun : Theme.IconMoon;
            DismissSuggestions();

            if (IsHandleCreated) Native.DarkTitleBar(Handle, true);
            if (editor.IsHandleCreated) Native.ThemeControl(editor.Handle, dark);

            ResumeLayout(true);
            Invalidate(true);
        }

        private void ToggleTheme()
        {
            dark = !dark;
            settings.Theme = dark ? "dark" : "light";
            ApplyTheme();
            Flash(dark ? "Dark theme" : "Light theme");
        }

        private void ToggleSync()
        {
            sync = !sync;
            settings.Sync = sync;
            UpdateHeaders();
            scrollTimer.Enabled = sync && view == "split";
            lastFirstLine = -1;
            lastSentScroll = -1;
            Flash(sync ? "Preview follows the editor" : "Scroll sync off");
        }

        private void SetDefaultApp()
        {
            string error = FileAssoc.Register();
            if (error != null)
            {
                MessageBox.Show(this,
                    "Windows could not register the .md file type.\n\n" + error,
                    "Default .md app", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnAssoc.Active = true;
            Flash("Registered for .md files");

            DialogResult answer = MessageBox.Show(this,
                "MD Viewer is now listed as a handler for .md files, so you can also " +
                "open documents with it from the right-click menu.\n\n" +
                "Windows 10 does not let programs choose the default silently. The " +
                "Default apps page opens next: pick MD Viewer under \".md\".\n\n" +
                "Open Windows Settings now?",
                "Default .md app", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

            if (answer != DialogResult.OK) return;

            if (!FileAssoc.OpenDefaultApps())
            {
                MessageBox.Show(this,
                    "Windows Settings could not be opened.\n\n" +
                    "Open Settings > Apps > Default apps and choose MD Viewer " +
                    "for .md files.",
                    "Default .md app", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ToggleWrap()
        {
            wrap = !wrap;
            settings.Wrap = wrap;
            editor.WordWrap = wrap;
            editor.ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both;
            if (editor.IsHandleCreated) Native.ThemeControl(editor.Handle, dark);
            PerformLayout();
            Flash(wrap ? "Word wrap on" : "Word wrap off");
        }

        /* ==================================================================
           TYPOGRAPHY
           ================================================================== */

        private void ShowFontMenu()
        {
            if (fontMenu == null)
            {
                fontMenu = new ContextMenuStrip();
                fontMenu.ShowImageMargin = false;
                fontMenu.Font = Theme.Ui;
                fontMenu.Opening += delegate { BuildFontMenu(); };
            }

            try
            {
                fontMenu.Show(btnFont, new Point(0, btnFont.Height));
            }
            catch
            {
                // If the strip cannot be shown the current face simply stays.
            }
        }

        private void BuildFontMenu()
        {
            if (fontMenu == null)
            {
                fontMenu = new ContextMenuStrip();
                fontMenu.ShowImageMargin = false;
                fontMenu.Font = Theme.Ui;
                fontMenu.Opening += delegate { BuildFontMenu(); };
            }

            fontMenu.Items.Clear();

            ToolStripMenuItem reading = new ToolStripMenuItem("Reading font");
            reading.DropDownItems.Add(MenuHeader("The face of the text"));
            AppendFamilies(reading.DropDownItems, false);
            fontMenu.Items.Add(reading);

            ToolStripMenuItem code = new ToolStripMenuItem("Code font");
            code.DropDownItems.Add(MenuHeader("The face of the code"));
            AppendFamilies(code.DropDownItems, true);
            fontMenu.Items.Add(code);

            fontMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem size = new ToolStripMenuItem("Text size");
            int[] sizes = new int[] { 85, 100, 115, 130, 150 };
            for (int i = 0; i < sizes.Length; i++)
            {
                int value = sizes[i];
                ToolStripMenuItem item = new ToolStripMenuItem(value.ToString(CultureInfo.InvariantCulture) + "%");
                item.Checked = fontSize == value;
                item.Click += delegate { ApplyFont(fontId, codeFontId, value); };
                size.DropDownItems.Add(item);
            }
            fontMenu.Items.Add(size);
        }

        private void AppendFamilies(ToolStripItemCollection items, bool mono)
        {
            string current = mono ? codeFontId : fontId;
            string category = null;

            foreach (FontChoice family in Fonts.All)
            {
                bool isMono = family.Category == "Mono";
                if (isMono != mono) continue;

                if (category != null && category != family.Category)
                    items.Add(new ToolStripSeparator());
                category = family.Category;

                string id = family.Id;
                string label = family.Name;
                if (family.Arabic) label += "  (Arabic)";

                ToolStripMenuItem item = new ToolStripMenuItem(label);
                item.Checked = string.Equals(id, current, StringComparison.OrdinalIgnoreCase);
                item.Click += delegate
                {
                    if (mono) ApplyFont(fontId, id, fontSize);
                    else ApplyFont(id, codeFontId, fontSize);
                };
                items.Add(item);
            }
        }

        private static ToolStripMenuItem MenuHeader(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            item.Font = Theme.UiSmallBold;
            return item;
        }

        private void ApplyFont(string reading, string code, int size)
        {
            fontId = reading;
            codeFontId = code;
            fontSize = size;

            preview.SetTypography(fontId, codeFontId, fontSize);
            UpdateHeaders();
            SaveSettingsSoon();
            Flash("Font: " + FontName(fontId));
        }

        private static string FontName(string id)
        {
            FontChoice choice = Fonts.Find(id);
            return choice == null ? "System Serif" : choice.Name;
        }

        private string PreviewMeta()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FontName(fontId));
            if (Math.Abs(previewZoom - 1.0) > 0.001)
                sb.Append(" \u00b7 ").Append((int)Math.Round(previewZoom * 100)).Append('%');
            sb.Append(" \u00b7 ").Append(sync ? "Synced" : "Live");
            return sb.ToString();
        }

        /* ==================================================================
           IMAGES
           ================================================================== */

        private static readonly string[] ImageExtensions = new string[]
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".tif", ".tiff", ".ico", ".avif"
        };

        internal static bool IsImageFile(string path)
        {
            string extension;
            try
            {
                extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < ImageExtensions.Length; i++)
            {
                if (extension == ImageExtensions[i]) return true;
            }
            return false;
        }

        private void InsertImage()
        {
            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Insert image";
                    dialog.Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.svg;*.tif;*.tiff|" +
                                   "All files|*.*";
                    dialog.InitialDirectory = StartFolder();
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    InsertImagePath(dialog.FileName);
                }
            }
            catch
            {
                // A cancelled dialog leaves the document alone.
            }
        }

        private void InsertImagePath(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string folder = DocumentFolder();
                string target = folder.Length > 0 ? RelativePath(folder, full) : full;
                target = target.Replace('\\', '/');

                string alt = Path.GetFileNameWithoutExtension(full);
                string markdown = "![" + alt + "](" + target + ")";

                editor.SelectedText = markdown;
                editor.SelectionStart = editor.SelectionStart + markdown.Length;
                editor.SelectionLength = 0;
                editor.Focus();

                UpdateCaret();
                Flash("Image inserted");
            }
            catch
            {
                // A picture that cannot be read is simply not inserted.
            }
        }

        /// <summary>The path of a file relative to a folder, using forward
        /// slashes so it can be dropped straight into the Markdown.</summary>
        internal static string RelativePath(string folder, string file)
        {
            try
            {
                string[] from = Path.GetFullPath(folder).Split(Path.DirectorySeparatorChar);
                string[] to = Path.GetFullPath(file).Split(Path.DirectorySeparatorChar);

                int common = 0;
                while (common < from.Length && common < to.Length &&
                       string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
                {
                    common++;
                }

                if (common == 0) return file;

                StringBuilder sb = new StringBuilder();
                for (int i = common; i < from.Length; i++)
                {
                    if (from[i].Length > 0) sb.Append("../");
                }
                for (int i = common; i < to.Length; i++)
                {
                    if (to[i].Length == 0) continue;
                    sb.Append(to[i]);
                    if (i < to.Length - 1) sb.Append('/');
                }

                string result = sb.ToString();
                return result.Length > 0 ? result : file;
            }
            catch
            {
                return file;
            }
        }

        private string DocumentFolder()
        {
            try
            {
                if (currentPath.Length == 0) return string.Empty;
                string folder = Path.GetDirectoryName(currentPath);
                return folder ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /* ==================================================================
           LAYOUT
           ================================================================== */

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (!built) return;

            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 0 || height <= 0) return;

            int topHeight = Ui.S(56);
            int statusHeight = Ui.S(31);
            int headerHeight = Ui.S(34);
            int divider = Ui.S(11);

            topBar.SetBounds(0, 0, width, topHeight);
            statusBar.SetBounds(0, height - statusHeight, width, statusHeight);
            LayoutTopBar(width, topHeight);

            int bodyTop = topHeight;
            int bodyHeight = height - topHeight - statusHeight;

            bool showEditor = view != "read";
            bool showPreview = view != "write";
            bool splitMode = view == "split";

            int formatHeight = showEditor ? Ui.S(40) : 0;
            formatBar.Visible = showEditor;
            if (showEditor) formatBar.SetBounds(0, bodyTop, width, formatHeight);
            bodyTop += formatHeight;
            bodyHeight -= formatHeight;
            if (bodyHeight < Ui.S(40)) bodyHeight = Ui.S(40);

            int editorWidth = width;
            if (splitMode) editorWidth = (int)Math.Round((width - divider) * split);
            if (splitMode)
            {
                int minPane = Ui.S(220);
                if (editorWidth < minPane) editorWidth = minPane;
                if (width - divider - editorWidth < minPane) editorWidth = width - divider - minPane;
                if (editorWidth < 1) editorWidth = 1;
            }

            headerEditor.Visible = showEditor;
            editorHost.Visible = showEditor;
            headerPreview.Visible = showPreview;
            preview.Visible = showPreview;
            splitter.Visible = splitMode;

            if (showEditor)
            {
                headerEditor.SetBounds(0, bodyTop, editorWidth, headerHeight);
                editorHost.SetBounds(0, bodyTop + headerHeight, editorWidth, bodyHeight - headerHeight);
                LayoutEditor();
            }

            if (showPreview)
            {
                int previewLeft = splitMode ? editorWidth + divider : 0;
                int previewWidth = width - previewLeft;
                if (previewWidth < 1) previewWidth = 1;
                headerPreview.SetBounds(previewLeft, bodyTop, previewWidth, headerHeight);
                preview.SetBounds(previewLeft, bodyTop + headerHeight, previewWidth, bodyHeight - headerHeight);
            }

            splitter.SetBounds(editorWidth, bodyTop, divider, bodyHeight);
        }

        private void LayoutEditor()
        {
            int padX = Ui.S(22);
            int padTop = Ui.S(15);
            int padBottom = Ui.S(16);
            int maxWidth = Ui.S(920);

            int available = editorHost.ClientSize.Width - padX * 2;
            if (available < Ui.S(60)) available = Ui.S(60);

            int editorWidth = Math.Min(available, maxWidth);
            int editorLeft = Math.Max(padX / 2, (editorHost.ClientSize.Width - editorWidth) / 2);
            int editorHeight = editorHost.ClientSize.Height - padTop - padBottom;
            if (editorHeight < Ui.S(40)) editorHeight = Ui.S(40);

            editor.SetBounds(editorLeft, padTop, editorWidth, editorHeight);

            placeholder.MaximumSize = new Size(editorWidth, 0);
            placeholder.Location = new Point(editorLeft + Ui.S(3), padTop + Ui.S(2));
        }

        private void LayoutTopBar(int width, int height)
        {
            int pad = Ui.S(16);
            int gap = Ui.S(4);
            int y = (height - Ui.S(34)) / 2;

            /* The order here is the order the buttons appear in the strip. */
            Control[] strip = new Control[]
            {
                btnNew, btnOpen, btnSave,
                ruleA,
                btnCopy, btnPrint, btnFont,
                ruleB,
                btnSync, btnTheme, btnAssoc
            };

            int groupWidth = 0;
            for (int i = 0; i < strip.Length; i++) groupWidth += strip[i].Width + gap;

            int x = Math.Max(pad, width - pad - groupWidth);

            for (int i = 0; i < strip.Length; i++)
            {
                Control control = strip[i];
                control.SetBounds(x, y, control.Width, control.Height);
                x += control.Width + gap;
            }

            int segmentWidth = Ui.S(216);
            int segmentHeight = Ui.S(30);
            int brandEnd = pad + Ui.S(28) + Ui.S(11) + Ui.Measure("MD Viewer", Theme.Brand).Width + Ui.S(18);
            int segmentLeft = (width - segmentWidth) / 2;

            if (segmentLeft < brandEnd) segmentLeft = brandEnd;
            if (segmentLeft + segmentWidth > x - Ui.S(14) || segmentLeft + segmentWidth > width - pad)
            {
                views.Visible = false;
            }
            else
            {
                views.Visible = true;
                views.SetBounds(segmentLeft, (height - segmentHeight) / 2, segmentWidth, segmentHeight);
            }
        }

        /* ==================================================================
           EDITOR BEHAVIOUR
           ================================================================== */

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab && !e.Control && !e.Alt)
            {
                IndentSelection(e.Shift);
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control && !e.Alt)
            {
                if (ContinueLine())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
        }

        private void OnEditorKeyPress(object sender, KeyPressEventArgs e)
        {
            /* Accepting a suggestion with Tab/Enter leaves the matching WM_CHAR in
               the queue (TranslateMessage runs before ProcessCmdKey). Swallow it so
               the document does not get a stray tab or newline. */
            if (swallowNextChar)
            {
                swallowNextChar = false;
                if (e.KeyChar == '\t' || e.KeyChar == '\r' || e.KeyChar == '\n')
                {
                    e.Handled = true;
                    return;
                }
            }

            if (!pairsEnabled || applyingEdit || loading) return;
            if (e.KeyChar < ' ' || e.KeyChar == (char)127) return;

            string text = editor.Text;
            int start = editor.SelectionStart;
            int length = editor.SelectionLength;

            if (Autocomplete.ShouldSkipOver(e.KeyChar, text, start, length))
            {
                editor.SelectionStart = Math.Min(text.Length, start + 1);
                editor.SelectionLength = 0;
                e.Handled = true;
                return;
            }

            string insert;
            int caretBack;
            if (Autocomplete.TryAutoPair(e.KeyChar, text, start, length, out insert, out caretBack))
            {
                e.Handled = true;
                applyingEdit = true;
                try
                {
                    editor.SelectedText = insert;
                    editor.SelectionStart = start + insert.Length - caretBack;
                    editor.SelectionLength = 0;
                }
                finally
                {
                    applyingEdit = false;
                }
            }
        }

        /* ==================================================================
           FORMATTING AND SUGGESTIONS
           ================================================================== */

        private void ExecuteFormat(string command)
        {
            if (command == null) return;

            if (command == "undo")
            {
                if (editor.CanUndo)
                {
                    editor.Undo();
                }
                return;
            }
            else if (command == "redo")
            {
                if (editor.CanRedo)
                {
                    editor.Redo();
                }
                return;
            }
            else if (command == "emoji")
            {
                editor.SelectedText = "😊";
                editor.SelectionStart = editor.SelectionStart + 2;
                return;
            }
            else if (command.StartsWith("notice-"))
            {
                string noticeType = command.Substring(7);
                string notice = "> [!" + noticeType + "]\n> " + "Content for " + noticeType + " goes here.";
                editor.SelectedText = notice;
                editor.SelectionStart = editor.SelectionStart + notice.Length;
                return;
            }

            if (view == "read") SetView("split");
            DismissSuggestions();

            if (command == "image")
            {
                InsertImage();
                return;
            }

            EditResult result;
            applyingEdit = true;
            try
            {
                result = EditorCommands.Apply(command, editor.Text,
                    editor.SelectionStart, editor.SelectionLength);
                if (result == null) return;

                editor.Select(result.ReplaceStart, result.ReplaceLength);
                editor.SelectedText = result.Insert;

                int start = Math.Min(result.SelectionStart, editor.TextLength);
                int length = Math.Min(result.SelectionLength, editor.TextLength - start);
                editor.Select(start, length);
            }
            finally
            {
                applyingEdit = false;
            }

            editor.Focus();
            UpdateCaret();
            FormatBarState();
            renderTimer.Stop();
            renderTimer.Start();
        }

        private void ApplySuggestion(Suggestion suggestion)
        {
            if (suggestion == null)
            {
                DismissSuggestions();
                return;
            }

            applyingEdit = true;
            try
            {
                int start = Math.Min(suggestStart, editor.TextLength);
                int length = Math.Min(suggestLength, editor.TextLength - start);
                editor.Select(start, length);
                editor.SelectedText = suggestion.Insert;

                int caret = start + suggestion.Insert.Length - suggestion.CaretBack;
                if (caret < 0) caret = 0;
                if (caret > editor.TextLength) caret = editor.TextLength;
                editor.SelectionStart = caret;
                editor.SelectionLength = 0;
            }
            finally
            {
                applyingEdit = false;
            }

            DismissSuggestions();
            UpdateCaret();
            FormatBarState();
        }

        private void UpdateSuggestions()
        {
            if (!suggestEnabled || applyingEdit || loading || view == "read" ||
                !editor.Focused || !editor.IsHandleCreated || editor.TextLength > 400000)
            {
                DismissSuggestions();
                editor.Focus(); // Ensure the editor remains focused
                return;
            }

            if (Native.ImeComposing(editor.Handle))
            {
                DismissSuggestions();
                return;
            }

            SuggestionSet set = Autocomplete.Get(editor.Text, editor.SelectionStart);
            if (set == null || set.Items.Count == 0)
            {
                DismissSuggestions();
                return;
            }

            suggestStart = set.Start;
            suggestLength = set.Length;

            Point caret = editor.GetPositionFromCharIndex(editor.SelectionStart);
            if (caret == Point.Empty && editor.SelectionStart > 0)
                caret = editor.GetPositionFromCharIndex(editor.SelectionStart - 1);

            Point anchor = editor.PointToScreen(
                new Point(caret.X, caret.Y + editor.Font.Height + Ui.S(2)));

            if (suggestions == null)
            {
                suggestions = new SuggestionPopup();
                suggestions.Accepted += delegate
                {
                    if (suggestions != null) ApplySuggestion(suggestions.Current);
                };
            }

            suggestions.ShowItems(set.Items, anchor);
        }

        private void DismissSuggestions()
        {
            if (suggestions != null)
            {
                suggestions.Dismiss();
                editor.Focus(); // تأكد من التركيز على مربع النص
            }
        }

        private void FormatBarState()
        {
            if (formatBar == null || editor == null) return;
            formatBar.SetState(EditorCommands.Describe(editor.Text, editor.SelectionStart));
        }

        private void ToggleAutocomplete()
        {
            suggestEnabled = !suggestEnabled;
            settings.Autocomplete = suggestEnabled;
            DismissSuggestions();
            Flash(suggestEnabled ? "Suggestions on" : "Suggestions off");
        }

        private void ToggleAutoPairs()
        {
            pairsEnabled = !pairsEnabled;
            settings.AutoPairs = pairsEnabled;
            Flash(pairsEnabled ? "Bracket closing on" : "Bracket closing off");
        }

        private void IndentSelection(bool outdent)
        {
            if (editor.SelectionLength == 0)
            {
                if (!outdent)
                {
                    string all = editor.Text;
                    int caret = editor.SelectionStart;
                    int lineStart = LineStartOf(all, caret);
                    int column = caret - lineStart;
                    int count = 2 - (column % 2);
                    if (count < 1) count = 2;
                    editor.SelectedText = new string(' ', count);
                    return;
                }
            }

            string text = editor.Text;
            int selStart = editor.SelectionStart;
            int selLength = editor.SelectionLength;

            int blockStart = LineStartOf(text, selStart);
            int blockEnd;
            if (selLength > 0)
            {
                int endPos = selStart + selLength;
                int nl = text.IndexOf('\n', endPos);
                blockEnd = nl < 0 ? text.Length : nl;
                if (endPos > 0 && endPos <= text.Length && text[endPos - 1] == '\n')
                    blockEnd = Math.Max(blockStart, endPos - 1);
            }
            else
            {
                int nl = text.IndexOf('\n', selStart);
                blockEnd = nl < 0 ? text.Length : nl;
            }

            if (blockEnd < blockStart) blockEnd = blockStart;
            string block = text.Substring(blockStart, blockEnd - blockStart);
            string[] lines = block.Split('\n');

            StringBuilder sb = new StringBuilder(block.Length + lines.Length * 2 + 8);
            int firstDelta = 0;
            int totalDelta = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!outdent)
                {
                    sb.Append("  ").Append(line);
                    totalDelta += 2;
                    if (i == 0) firstDelta += 2;
                }
                else
                {
                    int remove = 0;
                    if (line.Length > 0 && line[0] == '\t')
                    {
                        remove = 1;
                    }
                    else
                    {
                        while (remove < 2 && remove < line.Length && line[remove] == ' ') remove++;
                    }
                    sb.Append(line.Substring(remove));
                    totalDelta -= remove;
                    if (i == 0) firstDelta -= remove;
                }

                if (i < lines.Length - 1) sb.Append('\n');
            }

            string updated = sb.ToString();
            if (updated == block && outdent) return;

            editor.Select(blockStart, block.Length);
            editor.SelectedText = updated;

            if (selLength == 0)
            {
                int caret = selStart;
                if (outdent)
                {
                    int removed = -firstDelta;
                    int offset = selStart - blockStart;
                    caret = blockStart + Math.Max(0, offset - removed);
                }
                editor.SelectionStart = Math.Max(blockStart, Math.Min(blockStart + updated.Length, caret));
                editor.SelectionLength = 0;
            }
            else
            {
                editor.SelectionStart = blockStart;
                editor.SelectionLength = updated.Length;
            }
        }

        private bool ContinueLine()
        {
            if (editor.SelectionLength > 0) return false;

            string text = editor.Text;
            int caret = editor.SelectionStart;
            int lineStart = LineStartOf(text, caret);
            string line = text.Substring(lineStart, caret - lineStart);

            string quote = string.Empty;
            Match quoted = QuotePrefix.Match(line);
            if (quoted.Success) quote = quoted.Groups[1].Value;

            string rest = line.Substring(quote.Length);

            if (quote.Length > 0 && rest.Trim().Length == 0)
            {
                ReplaceRange(lineStart, caret - lineStart, string.Empty);
                return true;
            }

            Match marker = ListMarker.Match(rest);
            if (marker.Success)
            {
                string content = rest.Substring(marker.Length);
                if (content.Trim().Length == 0)
                {
                    ReplaceRange(lineStart, caret - lineStart, string.Empty);
                    return true;
                }

                string indent = marker.Groups[1].Value;
                string bullet;
                if (marker.Groups[2].Success)
                {
                    bullet = marker.Groups[2].Value + " ";
                }
                else
                {
                    int number = 1;
                    int.TryParse(marker.Groups[3].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out number);
                    bullet = (number + 1).ToString(CultureInfo.InvariantCulture) + marker.Groups[4].Value + " ";
                }

                string task = marker.Groups[6].Success ? "[ ] " : string.Empty;
                ReplaceRange(caret, 0, "\r\n" + quote + indent + bullet + task);
                return true;
            }

            if (quote.Length > 0)
            {
                ReplaceRange(caret, 0, "\r\n" + quote);
                return true;
            }

            return false;
        }

        private void ReplaceRange(int start, int length, string value)
        {
            editor.Select(start, length);
            editor.SelectedText = value;
            editor.SelectionStart = start + value.Length;
            editor.SelectionLength = 0;
        }

        private static int LineStartOf(string text, int index)
        {
            if (index <= 0) return 0;
            if (index > text.Length) index = text.Length;
            int nl = text.LastIndexOf('\n', index - 1);
            return nl < 0 ? 0 : nl + 1;
        }

        /* ==================================================================
           FILES
           ================================================================== */

        private void NewDocument()
        {
            if (!ConfirmDiscard()) return;

            currentPath = string.Empty;
            currentName = "Untitled.md";
            newline = "\r\n";
            SetEditorText(string.Empty);
            baseline = string.Empty;
            dirty = false;

            session.Save(string.Empty, string.Empty, currentName, string.Empty);
            SetView("write");
            UpdateStatus();
            UpdateHeaders();
            RenderNow();
            editor.Focus();
            Flash("New document");
        }

        private void OpenDocument()
        {
            if (!ConfirmDiscard()) return;

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Open Markdown";
                dialog.Filter = FileFilter;
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                dialog.InitialDirectory = StartFolder();

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadFile(dialog.FileName, true);
            }
        }

        private const string FileFilter =
            "Markdown (*.md;*.markdown;*.mdown)|*.md;*.markdown;*.mdown|" +
            "Text (*.txt)|*.txt|All files (*.*)|*.*";

        private void Save()
        {
            if (currentPath.Length == 0)
            {
                SaveAs();
                return;
            }
            WriteFile(currentPath);
        }

        private bool SaveAs()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save Markdown";
                dialog.Filter = FileFilter;
                dialog.FilterIndex = 1;
                dialog.AddExtension = true;
                dialog.DefaultExt = "md";
                dialog.FileName = currentName;
                dialog.InitialDirectory = StartFolder();

                if (dialog.ShowDialog(this) != DialogResult.OK) return false;

                currentPath = dialog.FileName;
                currentName = Path.GetFileName(currentPath);
                return WriteFile(currentPath);
            }
        }

        private bool WriteFile(string path)
        {
            try
            {
                string output = ToNewline(editor.Text, newline);
                File.WriteAllText(path, output, new UTF8Encoding(false));

                baseline = editor.Text;
                dirty = false;
                session.Save(editor.Text, baseline, currentName, currentPath);

                UpdateStatus();
                UpdateHeaders();
                Flash("Saved " + currentName);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The file could not be saved.\n\n" + ex.Message,
                    "MD Viewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private string StartFolder()
        {
            try
            {
                if (currentPath.Length > 0)
                {
                    string dir = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch
            {
                // Fall through to Documents.
            }

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(documents)) return documents;
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        private bool ConfirmDiscard()
        {
            if (!dirty) return true;
            DialogResult result = MessageBox.Show(this,
                "The current document has changes that are not saved to a file.\n\nReplace it?",
                "MD Viewer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return result == DialogResult.Yes;
        }

        private static string ReadTextFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                /* Older Arabic documents are often Windows-1256. Decode with
                   the system code page instead of showing replacement marks. */
                return Encoding.Default.GetString(bytes);
            }
        }

        private static string Uniform(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string ToNewline(string value, string style)
        {
            return Uniform(value).Replace("\n", style);
        }

        private static bool SameText(string a, string b)
        {
            return string.Equals(Uniform(a), Uniform(b), StringComparison.Ordinal);
        }

        private static string DetectNewline(string value)
        {
            if (value == null) return "\r\n";
            if (value.IndexOf("\r\n", StringComparison.Ordinal) >= 0) return "\r\n";
            if (value.IndexOf('\n') >= 0) return "\n";
            return "\r\n";
        }

        /* ==================================================================
           ACTIONS
           ================================================================== */

        private void CopyMarkdown()
        {
            string text = editor.SelectionLength > 0 ? editor.SelectedText : editor.Text;
            CopyToClipboard(text, editor.SelectionLength > 0 ? "Copied selection" : "Copied Markdown");
        }

        private void CopyMarkdownAll()
        {
            CopyToClipboard(editor.Text, "Copied Markdown");
        }

        private void CopyToClipboard(string text, string message)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                Flash(message);
            }
            catch
            {
                Flash("The clipboard is busy");
            }
        }

        private void PrintCurrent()
        {
            renderTimer.Stop();
            RenderNow();
            if (!preview.Print()) Flash("The preview is not ready yet");
        }

        private void Flash(string message)
        {
            statusBar.Message = message;
            flashTimer.Stop();
            flashTimer.Start();
        }

        private void SaveSettingsSoon()
        {
            settings.Split = split;
            settings.Font = fontId;
            settings.CodeFont = codeFontId;
            settings.FontSize = fontSize;
            settings.Zoom = previewZoom;
            settings.Save();
        }

        /* ==================================================================
           DRAG AND DROP
           ================================================================== */

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DroppedPath(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string path = DroppedPath(e);
            if (path == null) return;

            if (IsImageFile(path))
            {
                InsertImagePath(path);
                return;
            }

            if (!ConfirmDiscard()) return;
            LoadFile(path, true);
        }

        private static string DroppedPath(DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return null;
            if (!File.Exists(files[0])) return null;
            return files[0];
        }

        /* ==================================================================
           SPLITTER
           ================================================================== */

        private void OnSplitMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            DismissSuggestions();
            dragging = true;
            splitter.Dragging = true;
            splitter.Capture = true;
        }

        private void OnSplitMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;

            int width = ClientSize.Width - Ui.S(11);
            if (width <= 0) return;

            int x = PointToClient(Cursor.Position).X;
            double ratio = (double)x / width;
            if (ratio < 0.18) ratio = 0.18;
            if (ratio > 0.82) ratio = 0.82;
            split = ratio;

            PerformLayout();
            lastSentScroll = -1;
            lastFirstLine = -1;
        }

        private void OnSplitMouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            splitter.Dragging = false;
            splitter.Capture = false;
            SaveSettingsSoon();
        }

        /* ==================================================================
           KEYBOARD AND LIFECYCLE
           ================================================================== */

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            /* The suggestion list is handled here, before the key reaches the edit
               control. Returning true stops TranslateMessage, so Tab/Enter cannot
               also insert a tab or a newline after the suggestion is applied. */
            if (suggestions != null && suggestions.Visible)
            {
                if (keyData == Keys.Down) { suggestions.MoveSelection(1); return true; }
                if (keyData == Keys.Up) { suggestions.MoveSelection(-1); return true; }
                if (keyData == Keys.Tab || keyData == Keys.Enter)
                {
                    swallowNextChar = true;
                    ApplySuggestion(suggestions.Current);
                    return true;
                }
                if (keyData == Keys.Escape)
                {
                    DismissSuggestions();
                    return true;
                }
            }

            if (keyData == (Keys.Control | Keys.B)) { ExecuteFormat("bold"); return true; }
            if (keyData == (Keys.Control | Keys.I)) { ExecuteFormat("italic"); return true; }
            if (keyData == (Keys.Control | Keys.K)) { ExecuteFormat("link"); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.K)) { ExecuteFormat("code"); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.X)) { ExecuteFormat("strike"); return true; }
            if (keyData == (Keys.Control | Keys.Alt | Keys.D1)) { ExecuteFormat("h1"); return true; }
            if (keyData == (Keys.Control | Keys.Alt | Keys.D2)) { ExecuteFormat("h2"); return true; }
            if (keyData == (Keys.Control | Keys.Alt | Keys.D3)) { ExecuteFormat("h3"); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.D7)) { ExecuteFormat("ol"); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.D8)) { ExecuteFormat("ul"); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.D9)) { ExecuteFormat("quote"); return true; }

            if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveAs(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.C)) { CopyMarkdownAll(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenDocument(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { NewDocument(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { PrintCurrent(); return true; }
            if (keyData == (Keys.Control | Keys.D1)) { SetView("write"); return true; }
            if (keyData == (Keys.Control | Keys.D2)) { SetView("split"); return true; }
            if (keyData == (Keys.Control | Keys.D3)) { SetView("read"); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.D)) { ToggleTheme(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.W)) { ToggleWrap(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            renderTimer.Stop();
            autosaveTimer.Stop();
            scrollTimer.Stop();
            flashTimer.Stop();

            session.Save(editor.Text, baseline, currentName, currentPath);

            settings.Theme = dark ? "dark" : "light";
            settings.View = view;
            settings.Split = split;
            settings.Sync = sync;
            settings.Wrap = wrap;
            settings.Autocomplete = suggestEnabled;
            settings.AutoPairs = pairsEnabled;
            settings.Maximized = WindowState == FormWindowState.Maximized;
            settings.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.Save();

            if (suggestions != null)
            {
                suggestions.Dispose();
                suggestions = null;
            }
        }
    }
}

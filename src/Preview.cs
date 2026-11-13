using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MdViewer
{
    /// <summary>
    /// The reading pane. It hosts the IE11 engine that ships with Windows 10 and
    /// updates only the article's DOM, so typing never reloads or flickers the page.
    /// Nothing is fetched by the app itself: the HTML is generated locally and all
    /// remote-loading tags are limited to images the document itself references.
    ///
    /// The pane also owns two reader features:
    ///  - images: relative paths resolve against the folder of the open document,
    ///    a click opens the built-in viewer (zoom, pan, step through the document)
    ///    and the context menu can open, copy or save the picture;
    ///  - typography: the reading and code faces come from the Google Fonts that
    ///    ship inside the executable, or from the system stacks.
    /// </summary>
    internal sealed class Preview : UserControl
    {
        private readonly WebBrowser browser;
        private readonly ContextMenuStrip menu;
        private HtmlDocument hooked;
        private HtmlElement runner;
        private bool ready;
        private bool dark;
        private bool shellQueued;
        private int shellAttempts;
        private string pendingHtml = string.Empty;
        private double pendingScroll = -1;
        private string lastHtml = string.Empty;

        private string readingFont = "system-serif";
        private string codeFont = "system-mono";
        private int fontSize = 100;
        private double zoom = 1.0;

        private ToolStripMenuItem imageMenu;
        private ToolStripMenuItem imageView;
        private ToolStripMenuItem imageOpen;
        private ToolStripMenuItem imageCopyPath;
        private ToolStripMenuItem imageCopyBits;
        private ToolStripMenuItem imageSave;
        private readonly Dictionary<ToolStripMenuItem, double> zoomItems = new Dictionary<ToolStripMenuItem, double>();

        public event EventHandler CopySourceRequested;

        /// <summary>Raised when the reader changes the zoom of the page.</summary>
        public event EventHandler ZoomChanged;

        public Preview()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Paper;

            browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.ScriptErrorsSuppressed = true;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.WebBrowserShortcutsEnabled = true;
            browser.Navigating += OnNavigating;
            browser.DocumentCompleted += OnDocumentCompleted;

            menu = BuildMenu();

            Controls.Add(browser);
            LoadShell();
        }

        public IntPtr BrowserHandle
        {
            get { return browser.Handle; }
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.ShowImageMargin = false;
            m.Font = Theme.Ui;

            ToolStripMenuItem copy = new ToolStripMenuItem("Copy");
            copy.ShortcutKeyDisplayString = "Ctrl+C";
            copy.Click += delegate { CopySelection(); };

            ToolStripMenuItem selectAll = new ToolStripMenuItem("Select all");
            selectAll.ShortcutKeyDisplayString = "Ctrl+A";
            selectAll.Click += delegate { SelectAll(); };

            ToolStripMenuItem copySource = new ToolStripMenuItem("Copy Markdown source");
            copySource.Click += delegate
            {
                if (CopySourceRequested != null) CopySourceRequested(this, EventArgs.Empty);
            };

            imageMenu = new ToolStripMenuItem("Image");
            imageView = new ToolStripMenuItem("View image");
            imageView.Click += delegate { ViewCurrentImage(); };
            imageOpen = new ToolStripMenuItem("Open in default app");
            imageOpen.Click += delegate { OpenCurrentImage(); };
            imageCopyPath = new ToolStripMenuItem("Copy image path");
            imageCopyPath.Click += delegate { CopyCurrentImagePath(); };
            imageCopyBits = new ToolStripMenuItem("Copy image");
            imageCopyBits.Click += delegate { CopyCurrentImageBits(); };
            imageSave = new ToolStripMenuItem("Save image as\u2026");
            imageSave.Click += delegate { SaveCurrentImage(); };

            imageMenu.DropDownItems.Add(imageView);
            imageMenu.DropDownItems.Add(imageOpen);
            imageMenu.DropDownItems.Add(new ToolStripSeparator());
            imageMenu.DropDownItems.Add(imageCopyPath);
            imageMenu.DropDownItems.Add(imageCopyBits);
            imageMenu.DropDownItems.Add(imageSave);

            ToolStripMenuItem zoomMenu = new ToolStripMenuItem("Zoom");
            double[] levels = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
            for (int i = 0; i < levels.Length; i++)
            {
                double level = levels[i];
                ToolStripMenuItem item = new ToolStripMenuItem(Percent(level));
                item.Click += delegate { SetZoom(level); };
                zoomItems[item] = level;
                zoomMenu.DropDownItems.Add(item);
            }
            zoomMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem reset = new ToolStripMenuItem("Reset zoom  (Ctrl+0)");
            reset.Click += delegate { SetZoom(1.0); };
            zoomMenu.DropDownItems.Add(reset);

            m.Items.Add(copy);
            m.Items.Add(selectAll);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(imageMenu);
            m.Items.Add(zoomMenu);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(copySource);

            m.Opening += delegate { RefreshMenuState(); };
            return m;
        }

        private static string Percent(double level)
        {
            return ((int)Math.Round(level * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void RefreshMenuState()
        {
            ImageState image = CurrentImage();
            bool hasImage = image.Path.Length > 0;

            imageMenu.Enabled = hasImage;
            imageView.Enabled = hasImage;
            imageOpen.Enabled = hasImage;
            imageCopyPath.Enabled = hasImage;
            imageCopyBits.Enabled = hasImage && image.IsLocal;
            imageSave.Enabled = hasImage && image.IsLocal;

            foreach (KeyValuePair<ToolStripMenuItem, double> pair in zoomItems)
            {
                pair.Key.Checked = Math.Abs(pair.Value - zoom) < 0.001;
            }
        }

        private void OnContextMenuShowing(object sender, HtmlElementEventArgs e)
        {
            e.ReturnValue = false;
            BeginInvoke((MethodInvoker)delegate { menu.Show(Cursor.Position); });
        }

        private void HookDocument()
        {
            if (browser.Document == null || ReferenceEquals(hooked, browser.Document)) return;
            hooked = browser.Document;
            try
            {
                hooked.ContextMenuShowing += OnContextMenuShowing;
            }
            catch
            {
                // If the DOM event cannot be bound, the preview simply has no menu.
            }
        }

        /* ---------- document lifecycle ---------- */

        private void LoadShell()
        {
            ready = false;
            double keep = ScrollFraction();
            pendingHtml = lastHtml;
            pendingScroll = keep;

            if (!browser.IsHandleCreated)
            {
                /* The engine takes a document only once it is running. Before
                   that it can hold a single text, so the shell is handed over
                   once and picked up when the window appears. */
                if (shellQueued) return;
                shellQueued = true;
            }

            ApplyShell();
        }

        private void ApplyShell()
        {
            shellAttempts++;
            try
            {
                browser.DocumentText = ShellHtml(pendingHtml);
            }
            catch
            {
                // A failed navigation simply leaves the pane empty.
            }
        }

        private string ThemeClass
        {
            get { return dark ? "theme-dark" : "theme-light"; }
        }

        /// <summary>Chooses the reading face, the code face and the text size.</summary>
        public void SetTypography(string reading, string code, int sizePercent)
        {
            string newReading = string.IsNullOrEmpty(reading) ? "system-serif" : reading;
            string newCode = string.IsNullOrEmpty(code) ? "system-mono" : code;
            int newSize = sizePercent < 70 ? 70 : (sizePercent > 200 ? 200 : sizePercent);

            if (newReading == readingFont && newCode == codeFont && newSize == fontSize) return;

            readingFont = newReading;
            codeFont = newCode;
            fontSize = newSize;
            ApplyTypography();
        }

        /// <summary>Scales the page. 1.0 is the normal size.</summary>
        public void SetZoom(double level)
        {
            double clamped = level < 0.4 ? 0.4 : (level > 3.0 ? 3.0 : level);
            if (Math.Abs(clamped - zoom) < 0.0005) return;
            zoom = clamped;
            ApplyTypography();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        public double Zoom
        {
            get { return zoom; }
        }

        private void ApplyTypography()
        {
            if (!ready || browser.Document == null) return;
            SetStyleSheet("md-type", TypeCss());
        }

        private string TypeCss()
        {
            StringBuilder sb = new StringBuilder(2048);

            sb.Append(Fonts.FaceCss(readingFont));
            sb.Append(Fonts.FaceCss(codeFont));

            double size = 17.0 * fontSize / 100.0;
            sb.Append("body{font-family:").Append(Fonts.StackFor(readingFont)).Append(";");
            sb.Append("font-size:").Append(size.ToString("0.##", CultureInfo.InvariantCulture)).Append("px;}\r\n");
            sb.Append("h1,h2,h3,h4,h5,h6{font-family:inherit;}\r\n");
            sb.Append("code,pre,.code pre code,.code-lang{font-family:").Append(Fonts.StackFor(codeFont)).Append(";}\r\n");
            sb.Append("#page{zoom:").Append((Ui.Scale * zoom).ToString("0.###", CultureInfo.InvariantCulture)).Append(";}\r\n");
            return sb.ToString();
        }

        private string ShellHtml(string article)
        {
            StringBuilder sb = new StringBuilder(24000);
            sb.Append("<!DOCTYPE html>\r\n<html class=\"").Append(ThemeClass).Append("\"><head>");
            sb.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
            sb.Append("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">");
            sb.Append("<title>MD Viewer preview</title>");
            sb.Append("<style>").Append(Css()).Append("</style>");
            sb.Append("<style id=\"md-type\">").Append(TypeCss()).Append("</style>");
            sb.Append("</head>");
            sb.Append("<body class=\"").Append(ThemeClass).Append("\"><div id=\"page\"><article id=\"out\">");
            sb.Append(article ?? string.Empty);
            sb.Append("</article></div>");
            sb.Append("<div id=\"md-state\" style=\"display:none\"></div>");
            sb.Append("<div id=\"md-eval\" style=\"display:none\"></div>");
            sb.Append(LightboxHtml());
            sb.Append("<script>").Append(PreviewScript.Code).Append("</script>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string LightboxHtml()
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("<div id=\"md-lightbox\">");
            sb.Append("<div id=\"md-lb-frame\"><img id=\"md-lb-img\" alt=\"\"></div>");
            sb.Append("<div id=\"md-lb-caption\"></div>");
            sb.Append("<div id=\"md-lb-bar\">");
            sb.Append("<button id=\"md-lb-prev\" type=\"button\" title=\"Previous image\">&#8249;</button>");
            sb.Append("<button id=\"md-lb-zoomout\" type=\"button\" title=\"Zoom out\">&#8722;</button>");
            sb.Append("<button id=\"md-lb-zoom\" type=\"button\" title=\"Reset zoom\">100%</button>");
            sb.Append("<button id=\"md-lb-zoomin\" type=\"button\" title=\"Zoom in\">+</button>");
            sb.Append("<button id=\"md-lb-next\" type=\"button\" title=\"Next image\">&#8250;</button>");
            sb.Append("<span id=\"md-lb-count\"></span>");
            sb.Append("<button id=\"md-lb-close\" type=\"button\" title=\"Close (Esc)\">&#215;</button>");
            sb.Append("</div></div>");
            return sb.ToString();
        }

        private string Css()
        {
            StringBuilder css = new StringBuilder(14000);

            css.Append(@"
html, body { margin:0; padding:0; background:#fcfcfa; }
html { overflow-y:scroll; }
body {
  color:#1c1f24; font-family:Georgia, 'Times New Roman', serif;
  font-size:17px; line-height:1.7; -ms-text-size-adjust:100%; word-wrap:break-word;
}
#page { max-width:760px; margin:0 auto; padding:38px 44px 110px; }
h1,h2,h3,h4,h5,h6 {
  font-family:Georgia, 'Times New Roman', serif; font-weight:bold; line-height:1.22;
  letter-spacing:-0.01em; color:#1c1f24; margin:1.9em 0 0.55em;
}
h1 { margin-top:0; font-size:2.02em; padding-bottom:0.35em; border-bottom:1px solid #e2e2db; }
h2 { font-size:1.5em; } h3 { font-size:1.22em; } h4 { font-size:1.05em; }
h5 { font-size:1em; } h6 { font-size:0.92em; color:#6c727a; letter-spacing:0.02em; }
p { margin:0 0 1.05em; }
strong, b { font-weight:bold; } em, i { font-style:italic; }
del { color:#6c727a; }
a { color:#2e54c8; text-decoration:underline; }
img {
  max-width:100%; height:auto; border-radius:6px; vertical-align:middle; cursor:pointer;
}
img.md-broken {
  border:1px dashed #cdcfc6; border-radius:6px; padding:10px 16px; color:#6c727a;
  font-size:13px; font-family:'Segoe UI', Arial, sans-serif; background:#f1f2ec;
  min-width:70px; min-height:38px; cursor:default;
}
hr { height:1px; border:0; margin:2.4em 0; background:#e2e2db; }
ul, ol { margin:0 0 1.05em; padding-left:1.6em; }
li { margin:0.22em 0; }
li p { margin:0.4em 0; }
li ul, li ol { margin:0.35em 0 0.2em; }
blockquote {
  margin:1.4em 0; padding:0.15em 0 0.15em 1.15em;
  border-left:2px solid #2e54c8; color:#3c424a;
}
blockquote blockquote { margin:0.9em 0 0.4em; border-color:#cdcfc6; }
blockquote p:last-child { margin-bottom:0; }

code {
  font-family:'Cascadia Mono', Consolas, 'Courier New', monospace; font-size:0.82em;
  padding:0.14em 0.34em; border:1px solid #e2e2db; border-radius:4px;
  background:#f1f2ec; color:#1c1f24; word-wrap:break-word;
}
.code { margin:1.5em 0; border:1px solid #e2e2db; border-radius:8px; background:#f1f2ec; }
.code-head {
  min-height:30px; line-height:30px; padding:0 13px; background:#e9eae3;
  border-bottom:1px solid #e2e2db; border-radius:7px 7px 0 0;
}
.code-lang {
  font-family:'Cascadia Mono', Consolas, 'Courier New', monospace; font-size:10.5px;
  letter-spacing:0.06em; text-transform:uppercase; color:#6c727a;
}
.code pre { margin:0; padding:15px 17px; overflow-x:auto; }
.code pre code {
  padding:0; border:0; border-radius:0; background:none;
  font-size:13px; line-height:1.62; color:#1c1f24; word-wrap:normal;
}

.table-wrap {
  margin:1.5em 0; border:1px solid #e2e2db; border-radius:8px; overflow-x:auto;
}
table {
  width:100%; border-collapse:collapse; font-family:'Segoe UI', Arial, sans-serif;
  font-size:13.5px; line-height:1.5;
}
th, td { padding:9px 13px; text-align:left; vertical-align:top; border-bottom:1px solid #e2e2db; }
th {
  background:#f3f3ee; font-weight:600; font-size:12.5px; color:#3c424a; white-space:nowrap;
}
tr:last-child td { border-bottom:0; }
tbody tr:hover td { background:#f1f4ff; }
.ta-left { text-align:left; } .ta-center { text-align:center; } .ta-right { text-align:right; }
.rtl { direction:rtl; unicode-bidi:embed; }
table.rtl { direction:rtl; }
p.rtl, h1.rtl, h2.rtl, h3.rtl, h4.rtl, h5.rtl, h6.rtl, blockquote.rtl { text-align:right; }
ul.rtl, ol.rtl { direction:rtl; text-align:right; padding-left:0; padding-right:1.6em; }
ul.rtl li, ol.rtl li { text-align:right; }

li.task { list-style:none; position:relative; padding-left:22px; }
li.task.rtl { padding-left:0; padding-right:22px; }
.task-box {
  position:absolute; left:0; top:0.34em; width:13px; height:13px;
  border:1px solid #cdcfc6; border-radius:3px; background:#ffffff;
}
li.task.rtl .task-box { left:auto; right:0; }
.task-box.is-checked { background:#2e54c8; border-color:#2e54c8; }
.task-box.is-checked:after {
  content:'\2713'; display:block; color:#ffffff; font-family:'Segoe UI', Arial, sans-serif;
  font-size:11px; line-height:13px; text-align:center;
}
li.task-done { color:#6c727a; }
li.task-done p, li.task-done { text-decoration:line-through; }

.empty { margin-top:12vh; text-align:center; color:#6c727a; font-family:'Segoe UI', Arial, sans-serif; }
.empty h2 {
  margin:0 0 0.6em; padding:0; border:0; font-family:Georgia, serif; font-style:italic;
  font-weight:normal; font-size:22px; color:#3c424a; letter-spacing:0;
}
.empty p { max-width:340px; margin:0 auto; font-size:14px; line-height:1.6; }

/* ---------- the picture viewer ---------- */
#md-lightbox {
  position:fixed; left:0; top:0; width:100%; height:100%; z-index:2147483000;
  background:#0d0f13; display:none; font-family:'Segoe UI', Arial, sans-serif;
}
#md-lb-frame { position:absolute; left:0; top:0; right:0; bottom:78px; overflow:hidden; }
#md-lb-img {
  position:absolute; left:50%; top:50%; border:0; cursor:move; background:#171a1f;
  box-shadow:0 14px 52px rgba(0,0,0,0.7);
}
#md-lb-caption {
  position:absolute; left:24px; right:24px; bottom:58px; color:#aeb5bf; font-size:12px;
  text-align:center; overflow:hidden; white-space:nowrap; text-overflow:ellipsis;
}
#md-lb-bar {
  position:absolute; left:0; right:0; bottom:0; height:52px; text-align:center;
  background:#12151a; border-top:1px solid #262b33;
}
#md-lb-bar button {
  font-family:'Segoe UI', Arial, sans-serif; font-size:13px; color:#d8dce2;
  background:#1d222a; border:1px solid #2e343d; border-radius:5px; cursor:pointer;
  height:27px; min-width:34px; margin:12px 3px 0; padding:0 8px; line-height:25px;
}
#md-lb-bar button:hover { background:#272d36; border-color:#3a414b; }
#md-lb-count { color:#8f979f; font-size:12px; margin:0 12px; }

/* ---------- dark ---------- */
html.theme-dark, body.theme-dark { background:#15181d; color:#e7e7e2; }
body.theme-dark h1, body.theme-dark h2, body.theme-dark h3, body.theme-dark h4,
body.theme-dark h5, body.theme-dark h6 { color:#e7e7e2; }
body.theme-dark h1 { border-bottom-color:#292e36; }
body.theme-dark h6 { color:#949ba3; }
body.theme-dark a { color:#8fa6ff; }
body.theme-dark hr { background:#292e36; }
body.theme-dark blockquote { border-left-color:#8fa6ff; color:#c6c9cc; }
body.theme-dark blockquote blockquote { border-color:#3a404a; }
body.theme-dark code { border-color:#292e36; background:#1d2229; color:#e7e7e2; }
body.theme-dark .code { border-color:#292e36; background:#1d2229; }
body.theme-dark .code-head { background:#232931; border-bottom-color:#292e36; }
body.theme-dark .code-lang { color:#949ba3; }
body.theme-dark .code pre code { color:#e7e7e2; }
body.theme-dark .table-wrap { border-color:#292e36; }
body.theme-dark table { color:#e7e7e2; }
body.theme-dark th, body.theme-dark td { border-bottom-color:#292e36; }
body.theme-dark th { background:#1b1f25; color:#c6c9cc; }
body.theme-dark tbody tr:hover td { background:#1d2330; }
body.theme-dark li.task-done { color:#949ba3; }
body.theme-dark .task-box { border-color:#3a404a; background:#15181d; }
body.theme-dark .task-box.is-checked { background:#8fa6ff; border-color:#8fa6ff; }
body.theme-dark .task-box.is-checked:after { color:#10131a; }
body.theme-dark .empty { color:#949ba3; }
body.theme-dark .empty h2 { color:#c6c9cc; }
body.theme-dark img.md-broken { border-color:#3a404a; background:#1d2229; color:#949ba3; }
body.theme-dark {
  scrollbar-face-color:#2a2f37; scrollbar-track-color:#1b1f25; scrollbar-arrow-color:#8f979f;
  scrollbar-shadow-color:#2a2f37; scrollbar-highlight-color:#2a2f37;
  scrollbar-3dlight-color:#1b1f25; scrollbar-darkshadow-color:#1b1f25;
}
");

            css.Append(@"
@media print {
  html, html.theme-dark { background:#ffffff !important; }
  body, body.theme-dark {
    background:#ffffff !important; color:#111111 !important;
    font-size:11.5pt; line-height:1.55;
  }
  #page { max-width:none; padding:0; zoom:1 !important; }
  h1, h2, h3, h4, h5, h6, body.theme-dark h1, body.theme-dark h2, body.theme-dark h3,
  body.theme-dark h4, body.theme-dark h5, body.theme-dark h6 { color:#111111 !important; }
  h1 { font-size:20pt; border-bottom-color:#d0d0d0 !important; }
  h2 { font-size:15pt; } h3 { font-size:12.5pt; } h4 { font-size:11pt; }
  a, body.theme-dark a { color:#1c3f9e !important; text-decoration:none; }
  a[href^=""http""]:after {
    content:' (' attr(href) ')'; font-family:'Segoe UI', Arial, sans-serif;
    font-size:8.5pt; color:#555555; word-wrap:break-word;
  }
  body.theme-dark code, code {
    border-color:#dddddd !important; background:#f4f4f4 !important; color:#111111 !important;
  }
  .code, body.theme-dark .code {
    border-color:#cccccc !important; background:#f7f7f7 !important;
    page-break-inside:avoid;
  }
  .code-head, body.theme-dark .code-head {
    background:#efefef !important; border-bottom-color:#dddddd !important;
  }
  .code pre code, body.theme-dark .code pre code { color:#111111 !important; white-space:pre-wrap; }
  .code pre { white-space:pre-wrap; word-wrap:break-word; }
  blockquote, body.theme-dark blockquote {
    border-left-color:#999999 !important; color:#333333 !important;
  }
  .table-wrap, body.theme-dark .table-wrap { border-color:#cccccc !important; overflow:visible; }
  table { font-size:9.5pt; }
  th, td, body.theme-dark th, body.theme-dark td { border-color:#cccccc !important; }
  th, body.theme-dark th { background:#f3f3f3 !important; color:#222222 !important; }
  tr, blockquote, .code, .table-wrap, img { page-break-inside:avoid; }
  h1, h2, h3, h4, h5, h6 { page-break-after:avoid; }
  .empty { display:none; }
  #md-lightbox { display:none !important; }
}
");

            return css.ToString();
        }

        private void OnDocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            /* DocumentCompleted can fire for an intermediate about:blank before
               the generated shell exists. Only accept the shell document. */
            try
            {
                if (browser.Document == null) return;
                if (browser.Document.GetElementById("out") == null)
                {
                    /* The shell never made it into the engine. Now that the
                       window exists the document can be written again. */
                    if (shellAttempts < 4) ApplyShell();
                    return;
                }
            }
            catch
            {
                return;
            }

            ready = true;
            shellAttempts = 0;
            HookDocument();
            ApplyTheme();
            if (!string.IsNullOrEmpty(pendingHtml) || pendingScroll >= 0)
            {
                string html = pendingHtml;
                double scroll = pendingScroll;
                pendingHtml = string.Empty;
                pendingScroll = -1;
                SetArticleInternal(html, scroll >= 0 ? scroll : -1);
            }
            else
            {
                RunJs("if (window.md) { md.scan(); }");
            }
        }

        /* ---------- public surface ---------- */

        public void Render(string articleHtml)
        {
            string html = articleHtml ?? string.Empty;
            lastHtml = html;
            if (!ready)
            {
                pendingHtml = html;
                return;
            }
            SetArticleInternal(html, -1);
        }

        private void SetArticleInternal(string html, double restoreScroll)
        {
            double keep = restoreScroll >= 0 ? restoreScroll : ScrollFraction();
            try
            {
                HtmlElement host = browser.Document != null ? browser.Document.GetElementById("out") : null;
                if (host == null)
                {
                    pendingHtml = html;
                    pendingScroll = restoreScroll;
                    LoadShell();
                    return;
                }
                host.InnerHtml = html;
                if (keep > 0.0005) SetScrollFraction(keep);
                RunJs("if (window.md) { md.reset(); }");
            }
            catch
            {
                pendingHtml = html;
                pendingScroll = restoreScroll;
                LoadShell();
            }
        }

        public void SetTheme(bool isDark)
        {
            dark = isDark;
            BackColor = Theme.Paper;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (!ready || browser.Document == null) return;
            try
            {
                HtmlElement body = browser.Document.Body;
                if (body != null) body.SetAttribute("className", ThemeClass);
                if (body != null && body.Parent != null)
                    body.Parent.SetAttribute("className", ThemeClass);
            }
            catch
            {
                // Theme is cosmetic; leave the current class in place.
            }
        }

        public void SetScrollFraction(double fraction)
        {
            if (!ready) return;
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            string f = fraction.ToString("0.#####", CultureInfo.InvariantCulture);
            Eval("(function(){var d=document.documentElement,b=document.body;" +
                 "var max=Math.max(d.scrollHeight,d.offsetHeight,b?b.scrollHeight:0,b?b.offsetHeight:0)" +
                 "-Math.max(d.clientHeight,window.innerHeight||0);" +
                 "window.scrollTo(0,Math.round(" + f + "*Math.max(0,max)));return 1;})()");
        }

        public double ScrollFraction()
        {
            object result = Eval("(function(){var d=document.documentElement,b=document.body;" +
                                 "var top=window.pageYOffset||d.scrollTop||(b?b.scrollTop:0)||0;" +
                                 "var max=Math.max(d.scrollHeight,d.offsetHeight,b?b.scrollHeight:0,b?b.offsetHeight:0)" +
                                 "-Math.max(d.clientHeight,window.innerHeight||0);" +
                                 "return top+'|'+Math.max(0,max);})()");
            string text = result as string;
            if (text == null) return 0;
            string[] parts = text.Split('|');
            if (parts.Length != 2) return 0;
            double top, max;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out top)) return 0;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out max)) return 0;
            if (max <= 0) return 0;
            return Math.Max(0, Math.Min(1, top / max));
        }

        private object Eval(string script)
        {
            try
            {
                if (!ready || browser.Document == null) return null;

                RunJs("window.mdEval('" + JsString(script) + "');");

                HtmlElement box = browser.Document.GetElementById("md-eval");
                if (box == null) return null;

                string text = box.InnerText;
                if (text == null || text.Length == 0) return null;
                if (text.StartsWith("ERR:", StringComparison.Ordinal)) return null;
                if (text.StartsWith("OK:", StringComparison.Ordinal)) text = text.Substring(3);
                return text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Runs a line of script inside the preview. The engine only
        /// honours a script element that is added to the live document, so the
        /// old one is removed first and the list stays short.</summary>
        private void RunJs(string code)
        {
            if (code == null || code.Length == 0) return;
            try
            {
                if (!ready || browser.Document == null) return;
                HtmlDocument doc = browser.Document;

                if (runner != null)
                {
                    try { runner.InvokeMember("remove", new object[0]); } catch { }
                }

                HtmlElement script = doc.CreateElement("script");
                script.SetAttribute("text", code);
                doc.Body.AppendChild(script);
                runner = script;
            }
            catch
            {
                // Scripting is optional; the reader still shows the document.
            }
        }

        /// <summary>Replaces the style sheet that carries the typography.</summary>
        private void SetStyleSheet(string id, string css)
        {
            if (!ready) return;
            RunJs("window.mdSetStyle('" + JsString(id) + "', '" + JsString(css) + "');");
        }

        /* ---------- pictures ---------- */

        private struct ImageState
        {
            public string Url;
            public string Alt;
            public string Path;
            public bool IsLocal;
        }

        /// <summary>The picture the pointer was over, if any.</summary>
        public string CurrentImageUrl
        {
            get { return CurrentImage().Url; }
        }

        /// <summary>The file behind the picture under the pointer, if it is local.</summary>
        public string CurrentImagePath
        {
            get { return CurrentImage().Path; }
        }

        private ImageState CurrentImage()
        {
            ImageState state = new ImageState();
            state.Url = string.Empty;
            state.Alt = string.Empty;
            state.Path = string.Empty;
            state.IsLocal = false;

            try
            {
                if (!ready || browser.Document == null) return state;
                HtmlElement box = browser.Document.GetElementById("md-state");
                if (box == null) return state;

                string raw = box.InnerText;
                if (raw == null) return state;

                string[] parts = raw.Split('\u0001');
                if (parts.Length > 0) state.Url = parts[0];
                if (parts.Length > 1) state.Alt = parts[1];

                string local = LocalPathOf(state.Url);
                if (local != null)
                {
                    state.Path = local;
                    state.IsLocal = true;
                }
            }
            catch
            {
                // Without a bridge the picture actions stay disabled.
            }
            return state;
        }

        private static string LocalPathOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                Uri uri = new Uri(url, UriKind.Absolute);
                if (uri.IsFile) return uri.LocalPath;
            }
            catch
            {
                // Not a URI, so it cannot be a local file either.
            }
            return null;
        }

        private void ViewCurrentImage()
        {
            ImageState image = CurrentImage();
            if (image.Url.Length == 0) return;
            RunJs("if (window.md) { md.openBySrc('" + JsString(image.Url) + "'); }");
        }

        private void OpenCurrentImage()
        {
            ImageState image = CurrentImage();
            if (image.Url.Length == 0) return;

            try
            {
                if (image.IsLocal) Process.Start(new ProcessStartInfo(image.Path) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(image.Url) { UseShellExecute = true });
            }
            catch
            {
                // No application is registered for the file.
            }
        }

        private void CopyCurrentImagePath()
        {
            ImageState image = CurrentImage();
            if (image.Url.Length == 0) return;
            try
            {
                Clipboard.SetText(image.Path.Length > 0 ? image.Path : image.Url);
            }
            catch
            {
                // The clipboard may be locked by another program.
            }
        }

        private void CopyCurrentImageBits()
        {
            ImageState image = CurrentImage();
            if (!image.IsLocal) return;
            try
            {
                using (Image picture = Image.FromFile(image.Path))
                {
                    Clipboard.SetImage(picture);
                }
            }
            catch
            {
                // Formats that GDI+ cannot read are left alone.
            }
        }

        private void SaveCurrentImage()
        {
            ImageState image = CurrentImage();
            if (!image.IsLocal) return;

            try
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "Save image";
                    dialog.FileName = Path.GetFileName(image.Path);
                    dialog.Filter = "Images|" + Path.GetExtension(image.Path).TrimStart('.').ToUpperInvariant() + "|All files|*.*";
                    if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                    File.Copy(image.Path, dialog.FileName, true);
                }
            }
            catch
            {
                // A cancelled or failed copy leaves the picture where it was.
            }
        }

        private static string JsString(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", " ");
        }

        public void SelectAll()
        {
            try
            {
                if (ready) browser.Document.ExecCommand("SelectAll", false, null);
            }
            catch
            {
                // Selection commands are best-effort.
            }
        }

        public void CopySelection()
        {
            try
            {
                if (!ready) return;
                browser.Focus();
                browser.Document.ExecCommand("Copy", false, null);
            }
            catch
            {
                // If the engine refuses, the user can still drag-select and Ctrl+C.
            }
        }

        public bool Print()
        {
            try
            {
                if (!ready) return false;
                browser.ShowPrintDialog();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnNavigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            string url = e.Url != null ? e.Url.ToString() : string.Empty;
            if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;

            e.Cancel = true;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch
                {
                    // No association or blocked by policy; nothing else to do.
                }
            }
        }
    }
}

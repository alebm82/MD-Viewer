# MD Viewer — native Windows edition

A single-window Markdown editor with a live preview, written in C# for
**Windows Forms / .NET Framework 4.8** and compiled with the C# compiler that
ships inside Windows 10. No installer, no SDK, no NuGet packages, no server, no
telemetry: the whole application is one small `MDViewer.exe`.

The preview uses the IE11 engine that is part of Windows, driven through
`WebBrowser`; the HTML is generated locally by `src\Markdown.cs`.

---

## Requirements

- Windows 10 (64-bit) or later.
- .NET Framework 4.8 — included with current Windows 10 installations.
- Nothing else. Building needs only the in-box `csc.exe`.

## Build

```bat
build.bat          :: compile to bin\MDViewer.exe
build.bat run      :: compile, then launch
build.bat clean    :: remove bin\
build.bat msi      :: compile and build the installer, bin\MDViewer-<version>-x64.msi
```

The compiler is taken from
`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (falling back to the
32-bit Framework folder). The build is C# 5, `/platform:anycpu32bitpreferred`,
with `app.manifest` (Windows 10 GUID, per-monitor DPI awareness, long paths) and
`icon.ico` embedded. `README.md` is copied into `bin\` automatically.

`bin\MDViewer.exe` is portable: copy it anywhere, including a USB stick.

## Installing with the MSI

`installer\build-msi.ps1` builds `bin\MDViewer-<version>-x64.msi` with the
[WiX toolset](https://wixtoolset.org) (WiX 5, the last release under the
MPL-2.0 licence — it is installed automatically the first time the script
runs). `build.bat msi` does the same thing.

The package is **per user**: it installs into
`%LOCALAPPDATA%\Programs\MD Viewer` and needs no administrator rights. The
settings, the draft and the unpacked fonts stay in `%LOCALAPPDATA%\MD Viewer`,
so an upgrade leaves them alone.

```bat
build.bat msi
msiexec /i bin\MDViewer-1.0.0-x64.msi          :: silent install
msiexec /x bin\MDViewer-1.0.0-x64.msi          :: silent uninstall
msiexec /i bin\MDViewer-1.0.1-x64.msi          :: upgrade: no uninstall needed
```

**Upgrading is in place.** The `UpgradeCode` of the package never changes and
every build gets a higher version number, so the installer finds the previous
product, removes it and installs the new one. The user never has to uninstall
first, and reinstalling the same version is allowed as well.

Two things worth knowing:

- Windows Installer refuses to replace a program that is running, so it asks to
  close MD Viewer during an upgrade. Close it, or kill the process, and retry.
- The package also registers MD Viewer for `.md` files (the same per-user
  registration the star button in the toolbar writes). Windows 10 keeps the
  effective choice in a protected key, so confirm the default once in
  **Settings → Apps → Default apps** if needed.

The dialogs of the installer come from the `WixToolset.UI.wixext` extension,
which the script downloads into `installer\.wix\` the first time it builds.

## Using it

| Action | Shortcut |
| --- | --- |
| New document | `Ctrl+N` |
| Open | `Ctrl+O` (or drop a file on the window) |
| Save | `Ctrl+S` |
| Save as | `Ctrl+Shift+S` |
| Write / Split / Read view | `Ctrl+1` / `Ctrl+2` / `Ctrl+3` |
| Light / dark theme | `Ctrl+Shift+D` |
| Word wrap | `Ctrl+Shift+W` |
| Print the preview | `Ctrl+P` |
| Copy selection or whole document | `Ctrl+Shift+C` |
| Bold / italic / inline code | `Ctrl+B` / `Ctrl+I` / `Ctrl+Shift+K` |
| Strikethrough | `Ctrl+Shift+X` |
| Heading 1 / 2 / 3 | `Ctrl+Alt+1` / `Ctrl+Alt+2` / `Ctrl+Alt+3` |
| Bulleted / numbered list | `Ctrl+Shift+8` / `Ctrl+Shift+7` |
| Block quote | `Ctrl+Shift+9` |
| Insert link | `Ctrl+K` |
| Insert image | the picture button of the format bar |
| Fonts and text size | the `Aa` button in the top bar |

- **Views**: Write is a centred, comfortable-width editor; Split shows source and
  preview side by side; Read gives the preview the full window.
- **Splitter**: drag the gap to resize (18 %–82 %), double-click for 50/50.
- **Scroll sync**: the round button in the toolbar makes the preview follow the
  editor; the preview header shows `SYNCED` or `LIVE`.
- **Pictures**: every picture in the document is shown in the preview. A path
  that is written relative (`pictures/one.png`) is resolved against the folder
  of the document, so a file that sits next to the Markdown simply works; a web
  address and a `data:image/...` picture work as well. Clicking a picture opens
  the built-in viewer: zoom with the wheel or the `+`/`−` buttons, drag to move
  the picture, `←`/`→` step through the pictures of the document, `0` resets the
  zoom and `Esc` closes it. The context menu of the preview opens the picture in
  the default program, copies its path, copies the picture itself or saves it
  under another name.
- **Inserting pictures**: the picture button of the format bar asks for a file
  and writes `![name](path)`. The path is relative to the document when the
  picture lives in or under its folder, so the document stays portable.
  Dropping an image file on the window does the same.
- **Fonts**: the `Aa` button in the top bar opens the font picker. The reading
  face, the code face and the text size can be chosen separately, and the
  choice is remembered. The faces that ship with the viewer are the Google
  Fonts listed below; they are embedded in the executable and unpacked into
  `%LOCALAPPDATA%\MD Viewer\fonts` the first time they are used, so they work
  without a network connection.
- **Zoom**: right-click the preview for 50 %–200 % page zoom; the factor is
  remembered together with the fonts.
- **Editor**: 2-space Tab / Shift+Tab, automatic list, task-list, numbered-list
  and blockquote continuation, word wrap, undo/redo, context menu.
- **Format bar**: a full-width strip under the title bar with heading, emphasis,
  list, link/image/table and code-block buttons. It appears in Write and Split
  and hides in Read. Every button edits the Markdown source, so the preview
  updates immediately and `Ctrl+Z` still undoes the change one step at a time.
  Buttons light up when the caret is inside that kind of text. When the window
  is too narrow for all sixteen, the rest move into the `…` menu on the right.
- **Typing aids**: as you type a block starter (`-`, `1.`, `>`, `#`, ``` ``` ```)
  a small list offers the finished block; after ``` ``` ``` it offers language
  names; after `](` it offers addresses already used in the document. `↑`/`↓`
  and `Enter`/`Tab` pick a row, `Esc` closes the list. Typing `(`, `[` or a
  backtick closes the pair automatically, and typing the closing character just
  steps over it. Both aids can be switched off in the editor context menu under
  **Typing aids**.
- **Files**: UTF-8 (with or without BOM), UTF-16 LE/BE and the system ANSI code
  page are read; files are written as UTF-8 without BOM, keeping the original
  CRLF/LF style.
- **Session**: the working draft, the last opened file, window position, theme
  and view mode are restored on the next launch. A draft is cached about one
  second after you stop typing.
- **Printing**: the preview is printed as rendered (colours are flattened to
  white paper). "Microsoft Print to PDF" is a good way to export a PDF.

All state lives in `%LOCALAPPDATA%\MD Viewer\`:

| File | Purpose |
| --- | --- |
| `settings.ini` | theme, view, split ratio, sync, wrap, autocomplete, pairs, fonts, text size, zoom, window bounds |
| `draft.md` | autosaved working copy |
| `baseline.md` | text last saved to a real file, used for the dirty marker |
| `document.ini` | last file name and full path |
| `fonts\` | the embedded Google Fonts, unpacked on first use |

Delete that folder to reset the application completely.

## The fonts that ship with the viewer

Twelve families from Google Fonts are embedded in `MDViewer.exe` (about half a
megabyte) and unpacked on first use, so they are available offline and look the
same on every machine:

| Family | Kind | Family | Kind |
| --- | --- | --- | --- |
| EB Garamond | serif | Cairo | Arabic sans |
| Source Serif 4 | serif | Tajawal | Arabic sans |
| Noto Naskh Arabic | Arabic serif | Noto Sans Arabic | Arabic sans |
| Amiri | Arabic serif | JetBrains Mono | mono |
| Inter | sans | IBM Plex Mono | mono |
| IBM Plex Sans | sans | IBM Plex Sans Arabic | Arabic sans |

The picker also offers the system faces (`Georgia`, `Segoe UI`, `Consolas`),
which need no download at all.

The preview is drawn by IE11, which understands WOFF but not WOFF2, so
`tools\fetch-fonts.ps1` asks Google Fonts for WOFF files and keeps them in
`assets\fonts` together with `assets\fonts\manifest.txt`. Run the script again
to refresh or extend the collection:

```powershell
powershell -ExecutionPolicy Bypass -File tools\fetch-fonts.ps1
```

`build.bat` embeds every file in `assets\fonts` as a resource. The fonts are
written to `%LOCALAPPDATA%\MD Viewer\fonts` the first time a family is chosen
and reused from then on.

## Opening `.md` files with MD Viewer

The last button in the top bar (the star badge) registers the program as a
handler for `.md` files. It writes everything under `HKCU` — no installer and no
administrator rights:

- a ProgID `MDViewer.Markdown` with the icon and the `open` command;
- an `Open with` entry so the program appears in the right-click menu;
- a `Software\MDViewer\Capabilities` entry listed in `RegisteredApplications`;
- the `.md` default value, plus `OpenWithProgids`.

Windows 10 protects the per-user default choice with a hash, so a program may
not take over the file type silently. After registering, the app offers to open
**Settings → Apps → Default apps**, where you pick MD Viewer for `.md` once; the
button stays highlighted while the registration is present. The same page is
reachable by hand under "Choose a default app by file type".

## Markdown support

- ATX and Setext headings, paragraphs, thematic breaks
- `**bold**`, `*italic*`, `~~strike~~`, `` `inline code` ``
- Fenced code blocks (``` or ~~~, optional language label) and indented code
- Inline/reference links, autolinks, bare URLs, images (`file`, `http(s)` and
  `data:image/...` are allowed; `javascript:`, `vbscript:` and `data:text/html`
  links are rejected). A relative image path is resolved against the folder of
  the document.
- Ordered, unordered, nested and task lists; nested blockquotes
- Tables with left/centre/right column alignment and inline code in cells
- Backslash escapes, HTML entities, hard line breaks
- Raw HTML in the document is **escaped and shown as text**, never executed

Right-to-left text (Arabic, Hebrew) is detected per block: paragraphs,
headings, list items, quotes and table cells get the correct direction and
alignment. The Markdown source stays left-to-right so syntax remains
predictable.

Tables follow the language as well: a table that carries Arabic text is
mirrored, so the first column sits on the right and the header reads from
right to left. The alignment markers (`:---`, `:--:`, `---:`) follow the
reading direction, so `:---` in an Arabic table means the start side (the
right) and `---:` the end side. A table written in Latin keeps its own order.

## Known limits

- The preview engine is IE11: modern CSS such as flexbox, grid and custom
  properties is not available. The built-in stylesheet is IE-safe, but custom
  ideas that rely on those features will not render.
- The bundled fonts are WOFF files, because IE11 cannot read WOFF2. They are
  written to `%LOCALAPPDATA%\MD Viewer\fonts` on first use; a folder that
  cannot be written simply leaves the reader with the system faces.
- Code blocks are not syntax-highlighted (the language is shown as a label).
- There is no HTML/PDF export button; print to "Microsoft Print to PDF" instead.
- The document is re-rendered as a whole on every debounce. Multi-megabyte files
  work but feel less immediate.
- The preview may load **remote images** referenced by the document. Nothing
  else is fetched by the application itself.

## Privacy and security

- No network calls, analytics, crash upload or update check exist in the code.
- Settings and drafts never leave the machine.
- Opening an `http(s)`/`mailto` link hands it to the default browser; the app
  itself never becomes a browser.
- The app registers the IE11 document mode for itself under
  `HKCU\Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION`
  (per-user, no administrator rights).
- The `.md` handler registration (see above) is only written when you press the
  default-app button, and only in `HKCU\Software\Classes` and
  `HKCU\Software\MDViewer`.
- The preview shell no longer carries a Content-Security-Policy meta tag (it
  blocked the small script that drives the picture viewer and the zoom); safety
  comes from the parser escaping all raw HTML and sanitising link schemes.

## Replacing the icon

`icon.ico` is a multi-resolution icon (16–256 px). Replace the file or edit
`tools\make-icon.ps1` and run it, then rebuild — `build.bat` embeds whatever
`icon.ico` is present and warns if the file is missing.

## Source map

| File | Contents |
| --- | --- |
| `Program.cs` | entry point and crash reporting |
| `MainForm.cs` | window layout, views, editing behaviour, files, shortcuts |
| `FormatBar.cs` | formatting strip, button groups, overflow menu |
| `EditorCommands.cs` | the pure Markdown edits behind every format button |
| `AutocompleteEngine.cs` | suggestion and auto-pair rules (no WinForms) |
| `SuggestionPopup.cs` | focus-free suggestion list |
| `FileAssoc.cs` | per-user `.md` registration and Default apps page |
| `Markdown.cs` | self-contained Markdown → HTML parser |
| `Preview.cs` | IE11 host, stylesheet, themes, picture viewer, typography, zoom, print, context menu |
| `PreviewScript.cs` | the script inside the preview: picture viewer, zoom, the bridge to the native code |
| `Fonts.cs` | the embedded Google Fonts, their unpacking and their CSS |
| `Controls.cs` | painted toolbar, segmented view switch, headers, status bar |
| `Theme.cs` | palettes, fonts, icon glyphs |
| `Native.cs` | DWM dark title bar, scrollbar theming, edit-control helpers |
| `Session.cs` | settings/draft storage with atomic writes |
| `Ui.cs` | DPI scaling and GDI helpers |
| `tools\fetch-fonts.ps1` | downloads the bundled Google Fonts and writes the manifest |
| `installer\main.wxs` | the Windows Installer package (product, features, upgrade rule) |
| `installer\build-msi.ps1` | builds the MSI from the compiled program |

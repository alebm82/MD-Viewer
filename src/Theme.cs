using System;
using System.Drawing;
using System.Drawing.Text;

namespace MdViewer
{
    /// <summary>Palette and typography. Mirrors the web edition: graphite chrome,
    /// paper panes and a single "blue pencil" accent.</summary>
    internal static class Theme
    {
        public static bool Dark;

        public static Color Chrome;
        public static Color Chrome2;
        public static Color ChromeInk;
        public static Color ChromeMuted;
        public static Color ChromeLine;

        public static Color Paper;
        public static Color Paper2;
        public static Color Ink;
        public static Color InkSoft;
        public static Color Muted;
        public static Color Line;
        public static Color LineStrong;

        public static Color Accent;
        public static Color AccentInk;
        public static Color AccentBright;
        public static Color AccentSoft;

        public static Color CodeBg;
        public static Color CodeHead;
        public static Color Warn;
        public static Color Danger;
        public static Color Clean;

        public static Color ToastBg;
        public static Color ToastInk;

        public static readonly Font Ui;
        public static readonly Font UiBold;
        public static readonly Font UiSmall;
        public static readonly Font UiSmallBold;
        public static readonly Font UiSmallItalic;
        public static readonly Font UiSmallStrike;
        public static readonly Font Brand;
        public static readonly Font Mark;
        public static readonly Font Doc;
        public static readonly Font SerifItalic;
        public static readonly Font Mono;
        public static readonly Font MonoSmall;
        public static readonly Font Glyph;
        public static readonly Font GlyphSmall;

        public const string IconOpen = "\uE8E5";
        public const string IconNew = "\uE710";
        public const string IconSave = "\uE74E";
        public const string IconCopy = "\uE8C8";
        public const string IconPrint = "\uE749";
        public const string IconMoon = "\uE708";
        public const string IconSun = "\uE706";
        public const string IconSync = "\uE895";
        public const string IconLink = "\uE71B";
        public const string IconImage = "\uE8B9";
        public const string IconTable = "\uE80A";
        public const string IconDefault = "\uE73E";

        static Theme()
        {
            string mono = PickMono();
            Ui = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            UiBold = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
            UiSmall = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            UiSmallBold = new Font("Segoe UI", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
            UiSmallItalic = new Font("Segoe UI", 8.25f, FontStyle.Italic, GraphicsUnit.Point);
            UiSmallStrike = new Font("Segoe UI", 8.25f, FontStyle.Strikeout, GraphicsUnit.Point);
            Brand = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            Mark = new Font("Georgia", 12.5f, FontStyle.Bold, GraphicsUnit.Point);
            Doc = new Font("Georgia", 10.5f, FontStyle.Italic, GraphicsUnit.Point);
            SerifItalic = new Font("Georgia", 9f, FontStyle.Italic, GraphicsUnit.Point);
            Mono = new Font(mono, 10.25f, FontStyle.Regular, GraphicsUnit.Point);
            MonoSmall = new Font(mono, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            Glyph = new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Point);
            GlyphSmall = new Font("Segoe MDL2 Assets", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            Set(false);
        }

        private static string PickMono()
        {
            try
            {
                using (InstalledFontCollection fonts = new InstalledFontCollection())
                {
                    foreach (FontFamily family in fonts.Families)
                    {
                        if (string.Equals(family.Name, "Cascadia Mono", StringComparison.OrdinalIgnoreCase))
                            return family.Name;
                    }
                }
            }
            catch
            {
                // Fall through to Consolas, which ships with Windows 10.
            }
            return "Consolas";
        }

        public static void Set(bool dark)
        {
            Dark = dark;

            if (dark)
            {
                Chrome = Hex("#101216");
                Chrome2 = Hex("#191d23");
                ChromeInk = Hex("#e4e5e1");
                ChromeMuted = Hex("#8f979f");
                ChromeLine = Hex("#262b33");

                Paper = Hex("#15181d");
                Paper2 = Hex("#1b1f25");
                Ink = Hex("#e7e7e2");
                InkSoft = Hex("#c6c9cc");
                Muted = Hex("#949ba3");
                Line = Hex("#292e36");
                LineStrong = Hex("#3a404a");

                Accent = Hex("#8fa6ff");
                AccentInk = Hex("#10131a");
                AccentBright = Hex("#a9baff");
                AccentSoft = Color.FromArgb(30, 143, 166, 255);

                CodeBg = Hex("#1d2229");
                CodeHead = Hex("#232931");
                Warn = Hex("#e0b050");
                Danger = Hex("#ff8a80");
                Clean = Hex("#6fcf97");
            }
            else
            {
                Chrome = Hex("#191c21");
                Chrome2 = Hex("#21252c");
                ChromeInk = Hex("#e8e9e6");
                ChromeMuted = Hex("#98a0a9");
                ChromeLine = Hex("#2d333c");

                Paper = Hex("#fcfcfa");
                Paper2 = Hex("#f3f3ee");
                Ink = Hex("#1c1f24");
                InkSoft = Hex("#3c424a");
                Muted = Hex("#6c727a");
                Line = Hex("#e2e2db");
                LineStrong = Hex("#cdcfc6");

                Accent = Hex("#2e54c8");
                AccentInk = Hex("#ffffff");
                AccentBright = Hex("#9db2ff");
                AccentSoft = Color.FromArgb(24, 46, 84, 200);

                CodeBg = Hex("#f1f2ec");
                CodeHead = Hex("#e9eae3");
                Warn = Hex("#a96a00");
                Danger = Hex("#b3261e");
                Clean = Hex("#2e8b57");
            }

            ToastBg = Hex("#101216");
            ToastInk = Hex("#eceee9");
        }

        public static Color Hex(string value)
        {
            return ColorTranslator.FromHtml(value);
        }
    }
}

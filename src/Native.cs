using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MdViewer
{
    /// <summary>Thin P/Invoke layer: Windows 10 dark chrome, scrollbar theming,
    /// text-box scroll position and the IE11 rendering mode for WebBrowser.</summary>
    internal static class Native
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int EM_GETFIRSTVISIBLELINE = 0xCE;
        private const int EM_LINESCROLL = 0xB6;
        private const int EM_REDO = 0x454;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static void DarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            try
            {
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                if (hr != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));
            }
            catch
            {
                // Older Windows 10 builds ignore the attribute.
            }
        }

        public static void ThemeControl(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch
            {
                // Not fatal: the control simply keeps its classic scrollbars.
            }
        }

        public static int FirstVisibleLine(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 0;
            return (int)SendMessage(hwnd, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        }

        public static void ScrollLines(IntPtr hwnd, int lines)
        {
            if (hwnd == IntPtr.Zero) return;
            SendMessage(hwnd, EM_LINESCROLL, IntPtr.Zero, new IntPtr(lines));
        }

        public static void EditRedo(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            SendMessage(hwnd, EM_REDO, IntPtr.Zero, IntPtr.Zero);
        }

        private const int GCS_COMPSTR = 0x0008;

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hwnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr hImc);

        [DllImport("imm32.dll")]
        private static extern int ImmGetCompositionStringW(IntPtr hImc, int index, IntPtr buffer, int length);

        /// <summary>True while an IME is composing (Arabic, Japanese, Chinese...).
        /// Suggestion popups stay out of the way until the composition is committed.</summary>
        public static bool ImeComposing(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            IntPtr context = IntPtr.Zero;
            try
            {
                context = ImmGetContext(hwnd);
                if (context == IntPtr.Zero) return false;
                return ImmGetCompositionStringW(context, GCS_COMPSTR, IntPtr.Zero, 0) > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (context != IntPtr.Zero)
                {
                    try { ImmReleaseContext(hwnd, context); }
                    catch { }
                }
            }
        }

        /// <summary>Opts the process into IE11 document mode. The WebBrowser
        /// control otherwise defaults to IE7, which cannot render the preview.</summary>
        internal static class BrowserEmulation
        {
            public static void Register()
            {
                try
                {
                    string exe = Path.GetFileName(Application.ExecutablePath);
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                        @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                    {
                        if (key != null)
                            key.SetValue(exe, 11001, RegistryValueKind.DWord);
                    }
                }
                catch
                {
                    // The meta X-UA-Compatible tag in the generated page is the fallback.
                }
            }
        }
    }
}

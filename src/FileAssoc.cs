using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MdViewer
{
    /// <summary>Registers MD Viewer as a handler for .md files for the current user,
    /// with no installer and no administrator rights.
    ///
    /// Windows 10 keeps the effective choice in the UserChoice key and protects it
    /// with a hash, so an application may not set itself as the default silently.
    /// What we can do is register a ProgID, an "Open with" entry and the capability
    /// list, then send the user to the Default apps page to confirm.</summary>
    internal static class FileAssoc
    {
        public const string ProgId = "MDViewer.Markdown";

        private const string AppName = "MD Viewer";
        private const string CapabilitiesPath = "Software\\MDViewer\\Capabilities";
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

        public static string ExePath
        {
            get
            {
                try { return Application.ExecutablePath; }
                catch { return null; }
            }
        }

        private static string OpenCommand
        {
            get { return "\"" + ExePath + "\" \"%1\""; }
        }

        private static string IconPath
        {
            get { return "\"" + ExePath + "\",0"; }
        }

        /// <summary>True when the stored command already points at this executable.</summary>
        public static bool IsRegistered()
        {
            string exe = ExePath;
            if (string.IsNullOrEmpty(exe)) return false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    "Software\\Classes\\" + ProgId + "\\shell\\open\\command"))
                {
                    if (key == null) return false;
                    string value = key.GetValue(null) as string;
                    return value != null &&
                           value.StartsWith("\"" + exe + "\"", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Writes the per-user registration. Returns null on success, or a
        /// human-readable reason when Windows refused the change.</summary>
        public static string Register()
        {
            string exe = ExePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return "The program could not locate its own executable.";

            try
            {
                using (RegistryKey root = Registry.CurrentUser)
                {
                    using (RegistryKey progId = root.CreateSubKey("Software\\Classes\\" + ProgId))
                    {
                        progId.SetValue(null, "Markdown Document");
                        progId.SetValue("FriendlyTypeName", "Markdown Document");

                        using (RegistryKey icon = progId.CreateSubKey("DefaultIcon"))
                            icon.SetValue(null, IconPath);

                        using (RegistryKey command = progId.CreateSubKey("shell\\open\\command"))
                            command.SetValue(null, OpenCommand);
                    }

                    using (RegistryKey extension = root.CreateSubKey("Software\\Classes\\.md"))
                    {
                        /* Per-user legacy default. Windows 10 prefers the hashed
                           UserChoice value when one exists, so this is a best effort;
                           the Default apps page is the reliable confirmation. */
                        extension.SetValue(null, ProgId);

                        using (RegistryKey openWith = extension.CreateSubKey("OpenWithProgids"))
                            openWith.SetValue(ProgId, new byte[0], RegistryValueKind.Binary);
                    }

                    using (RegistryKey application = root.CreateSubKey(
                        "Software\\Classes\\Applications\\MDViewer.exe"))
                    {
                        application.SetValue("FriendlyAppName", AppName);

                        using (RegistryKey icon = application.CreateSubKey("DefaultIcon"))
                            icon.SetValue(null, IconPath);

                        using (RegistryKey command = application.CreateSubKey("shell\\open\\command"))
                            command.SetValue(null, OpenCommand);

                        using (RegistryKey types = application.CreateSubKey("SupportedTypes"))
                            types.SetValue(".md", string.Empty);
                    }

                    using (RegistryKey capabilities = root.CreateSubKey(CapabilitiesPath))
                    {
                        capabilities.SetValue("ApplicationName", AppName);
                        capabilities.SetValue("ApplicationDescription",
                            "Markdown editor with a live preview");

                        using (RegistryKey files = capabilities.CreateSubKey("FileAssociations"))
                            files.SetValue(".md", ProgId);
                    }

                    using (RegistryKey registered = root.CreateSubKey("Software\\RegisteredApplications"))
                        registered.SetValue(AppName, CapabilitiesPath);
                }

                SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Opens the Windows page where the user confirms the default app.</summary>
        public static bool OpenDefaultApps()
        {
            try
            {
                Process.Start("ms-settings:defaultapps");
                return true;
            }
            catch
            {
                // Older builds: fall back to the classic Default Programs panel.
            }

            try
            {
                Process.Start("control.exe", "/name Microsoft.DefaultPrograms");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

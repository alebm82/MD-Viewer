using System;
using System.IO;
using System.Windows.Forms;

namespace MdViewer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            Ui.Init();
            Native.BrowserEmulation.Register();

            string initial = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!string.IsNullOrEmpty(a) && File.Exists(a))
                {
                    initial = Path.GetFullPath(a);
                    break;
                }
            }

            try
            {
                Application.Run(new MainForm(initial));
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Report(e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Report(e.ExceptionObject as Exception);
        }

        private static void Report(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string dir = Settings.AppFolder;
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    ex + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                // Logging must never mask the original failure.
            }

            MessageBox.Show(
                "MD Viewer hit an unexpected problem.\n\n" + ex.Message,
                "MD Viewer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

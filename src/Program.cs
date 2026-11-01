using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GammaDeck
{
    internal static class Program
    {
        private static MainForm form;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Any(a => a.Equals("--unlock-gamma", StringComparison.OrdinalIgnoreCase)))
                return UnlockGamma();
            if (args.Any(a => a.Equals("--diag", StringComparison.OrdinalIgnoreCase)))
                return Diagnose();

            bool created;
            using (var mutex = new Mutex(true, "GammaDeck.SingleInstance.6E0B1D2A", out created))
            {
                if (!created)
                {
                    MessageWindow.SendToRunning(args.Length > 0 ? args : new[] { "--show" });
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);

                var cfg = AppConfig.Load();
                Loc.Set(cfg.Language);
                if (!cfg.ShortcutCreated && DesktopShortcut.Create())
                {
                    cfg.ShortcutCreated = true;
                    try { cfg.Save(); } catch { }
                }
                bool hidden = cfg.StartMinimized || args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
                form = new MainForm(cfg, hidden, args);
                Application.Run(form);
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static void Crash(Exception ex)
        {
            // Главное — не оставить пользователю «сломанные» цвета
            try { new ColorEngine().ResetAll(); } catch { }
            try
            {
                System.IO.Directory.CreateDirectory(AppConfig.Dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppConfig.Dir, "error.log"),
                    DateTime.Now + "\r\n" + ex + "\r\n\r\n");
            }
            catch { }
            MessageBox.Show(Loc.T("An error occurred; colors were reset to default.\n\n") + (ex != null ? ex.Message : ""),
                "GammaDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>--diag: ничего не меняет, пишет отчёт о мониторах и драйвере в diag.txt.</summary>
        private static int Diagnose()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("GammaDeck diag " + DateTime.Now);
            sb.AppendLine("64-bit: " + Environment.Is64BitProcess + ", OS: " + Environment.OSVersion);
            sb.AppendLine("NVAPI: " + (NvApi.Init() ? "OK" : "unavailable"));
            foreach (var s in Screen.AllScreens)
            {
                IntPtr dc = Native.CreateDC(s.DeviceName, null, null, IntPtr.Zero);
                bool dcOk = dc != IntPtr.Zero;
                if (dcOk) Native.DeleteDC(dc);
                sb.AppendLine(string.Format("{0} [{1}] {2}x{3}{4}: DC={5}; {6}", s.DeviceName, Native.MonitorName(s.DeviceName),
                    s.Bounds.Width, s.Bounds.Height, s.Primary ? " primary" : "", dcOk ? "OK" : "no",
                    NvApi.Describe(NvApi.GetHandle(s.DeviceName))));
            }
            var cfg = AppConfig.Load();
            sb.AppendLine("Selected: " + (string.IsNullOrEmpty(cfg.Monitor) ? "all monitors" : cfg.Monitor) +
                "; last profile: " + (string.IsNullOrEmpty(cfg.ActiveProfile) ? "—" : cfg.ActiveProfile));
            foreach (var kv in cfg.MonitorProfiles)
                sb.AppendLine("Profile " + kv.Key + ": " + (string.IsNullOrEmpty(kv.Value) ? "default" : kv.Value));
            System.IO.Directory.CreateDirectory(AppConfig.Dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppConfig.Dir, "diag.txt"), sb.ToString(), System.Text.Encoding.UTF8);
            return 0;
        }

        private static int UnlockGamma()
        {
            try { Loc.Set(AppConfig.Load().Language); } catch { }   // отдельный процесс с правами администратора — язык читаем из конфига сами
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hklm.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM"))
                    k.SetValue("GdiIcmGammaRange", 256, RegistryValueKind.DWord);
                MessageBox.Show(Loc.T("Gamma limit removed.\nRestart the computer for the change to take effect."),
                    "GammaDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.F("Could not change the registry: {0}", ex.Message), "GammaDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GammaDeck
{
    /// <summary>Ярлык на рабочем столе через WScript.Shell (late binding, без внешних зависимостей).</summary>
    internal static class DesktopShortcut
    {
        public const string FileName = "GammaDeck.lnk";

        public static string Path
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName); }
        }

        /// <summary>true — ярлык создан или уже лежит на месте; false — не вышло (попробуем при следующем старте).</summary>
        public static bool Create()
        {
            try
            {
                string lnk = Path;
                if (File.Exists(lnk)) return true;

                // иконку берём из программы и кладём рядом с конфигом: в exe она не вшита
                string ico = System.IO.Path.Combine(AppConfig.Dir, "GammaDeck.ico");
                Directory.CreateDirectory(AppConfig.Dir);
                using (var icon = Theme.MakeIcon())
                using (var fs = new FileStream(ico, FileMode.Create))
                    icon.Save(fs);

                string exe = Application.ExecutablePath;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return false;
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type st = sc.GetType();
                Set(st, sc, "TargetPath", exe);
                Set(st, sc, "WorkingDirectory", System.IO.Path.GetDirectoryName(exe));
                Set(st, sc, "Description", "GammaDeck");
                Set(st, sc, "IconLocation", ico + ",0");
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                return File.Exists(lnk);
            }
            catch { return false; }
        }

        private static void Set(Type t, object o, string prop, string value)
        {
            t.InvokeMember(prop, BindingFlags.SetProperty, null, o, new object[] { value });
        }
    }
}

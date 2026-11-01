using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace GammaDeck
{
    public class ChannelSettings
    {
        public double Brightness { get; set; }   // 0..100 %, по умолчанию 50
        public double Contrast { get; set; }     // 0..100 %, по умолчанию 50
        public double Gamma { get; set; }        // 0.30..2.80, по умолчанию 1.00

        public ChannelSettings() : this(50, 50, 1.0) { }

        public ChannelSettings(double brightness, double contrast, double gamma)
        {
            Brightness = brightness; Contrast = contrast; Gamma = gamma;
        }

        public ChannelSettings Clone() { return new ChannelSettings(Brightness, Contrast, Gamma); }

        public bool SameAs(ChannelSettings o)
        {
            return Math.Abs(Brightness - o.Brightness) < 0.01 && Math.Abs(Contrast - o.Contrast) < 0.01 && Math.Abs(Gamma - o.Gamma) < 0.001;
        }

        public void Clamp()
        {
            Brightness = Math.Max(0, Math.Min(100, Brightness));
            Contrast = Math.Max(0, Math.Min(100, Contrast));
            Gamma = Math.Max(0.3, Math.Min(2.8, Gamma));
        }
    }

    public class ColorSettings
    {
        public ChannelSettings Red { get; set; }
        public ChannelSettings Green { get; set; }
        public ChannelSettings Blue { get; set; }
        public int Vibrance { get; set; }   // Цифровая интенсивность 0..100 %, по умолчанию 50
        public int Hue { get; set; }        // Оттенок 0..359°, по умолчанию 0

        public ColorSettings()
        {
            Red = new ChannelSettings(); Green = new ChannelSettings(); Blue = new ChannelSettings();
            Vibrance = 50; Hue = 0;
        }

        public static ColorSettings Default() { return new ColorSettings(); }

        public static ColorSettings All(double brightness, double contrast, double gamma, int vibrance)
        {
            var s = new ColorSettings();
            s.Red = new ChannelSettings(brightness, contrast, gamma);
            s.Green = s.Red.Clone();
            s.Blue = s.Red.Clone();
            s.Vibrance = vibrance;
            return s;
        }

        public ColorSettings Clone()
        {
            var s = new ColorSettings();
            s.Red = Red.Clone(); s.Green = Green.Clone(); s.Blue = Blue.Clone();
            s.Vibrance = Vibrance; s.Hue = Hue;
            return s;
        }

        public bool SameAs(ColorSettings o)
        {
            return o != null && Red.SameAs(o.Red) && Green.SameAs(o.Green) && Blue.SameAs(o.Blue) && Vibrance == o.Vibrance && Hue == o.Hue;
        }

        public bool IsDefault { get { return SameAs(Default()); } }

        public void Normalize()
        {
            if (Red == null) Red = new ChannelSettings();
            if (Green == null) Green = new ChannelSettings();
            if (Blue == null) Blue = new ChannelSettings();
            Red.Clamp(); Green.Clamp(); Blue.Clamp();
            Vibrance = Math.Max(0, Math.Min(100, Vibrance));
            Hue = ((Hue % 360) + 360) % 360;
        }

        public static ColorSettings Lerp(ColorSettings a, ColorSettings b, double t)
        {
            var s = new ColorSettings();
            s.Red = LerpCh(a.Red, b.Red, t);
            s.Green = LerpCh(a.Green, b.Green, t);
            s.Blue = LerpCh(a.Blue, b.Blue, t);
            s.Vibrance = (int)Math.Round(a.Vibrance + (b.Vibrance - a.Vibrance) * t);
            int dh = ((b.Hue - a.Hue + 540) % 360) - 180; // кратчайший путь по кругу
            s.Hue = (((int)Math.Round(a.Hue + dh * t)) % 360 + 360) % 360;
            return s;
        }

        private static ChannelSettings LerpCh(ChannelSettings a, ChannelSettings b, double t)
        {
            return new ChannelSettings(
                a.Brightness + (b.Brightness - a.Brightness) * t,
                a.Contrast + (b.Contrast - a.Contrast) * t,
                a.Gamma + (b.Gamma - a.Gamma) * t);
        }
    }

    public class Profile
    {
        public string Name { get; set; }
        public string Color { get; set; }    // цвет кнопки, #RRGGBB
        public string Hotkey { get; set; }   // например "Ctrl+Alt+1"
        public ColorSettings Settings { get; set; }
        public List<MonitorHotkey> MonitorHotkeys { get; set; }   // клавиши «включить только на этом мониторе»

        public Profile() : this("Profile", "#76B900", new ColorSettings()) { }

        public Profile(string name, string color, ColorSettings s)
        {
            Name = name; Color = color; Hotkey = ""; Settings = s;
            MonitorHotkeys = new List<MonitorHotkey>();
        }

        public void Normalize()
        {
            if (Settings == null) Settings = new ColorSettings();
            Settings.Normalize();
            if (Hotkey == null) Hotkey = "";
            if (MonitorHotkeys == null) MonitorHotkeys = new List<MonitorHotkey>();
            MonitorHotkeys.RemoveAll(h => h == null || string.IsNullOrEmpty(h.Monitor) || string.IsNullOrEmpty(h.Hotkey));
        }
    }

    public class MonitorHotkey
    {
        public string Monitor { get; set; }  // \\.\DISPLAYn
        public string Hotkey { get; set; }
    }

    public class GameRule
    {
        public string Exe { get; set; }      // имя процесса без .exe
        public string Profile { get; set; }
    }

    public class AppConfig
    {
        public List<Profile> Profiles { get; set; }
        public List<GameRule> GameRules { get; set; }
        public string Monitor { get; set; }          // "" = все мониторы, иначе \\.\DISPLAYn
        public string ActiveProfile { get; set; }    // последний применённый (для старых версий и новых мониторов)
        public Dictionary<string, string> MonitorProfiles { get; set; }  // \\.\DISPLAYn → имя профиля

        public string HkReset { get; set; }
        public string HkNext { get; set; }
        public string HkPrev { get; set; }
        public string HkToggle { get; set; }
        public string HkShow { get; set; }

        public bool AutoSwitch { get; set; }
        public bool RevertOnLeave { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartMinimized { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool ResetOnExit { get; set; }
        public bool ApplyOnStart { get; set; }
        public bool ShowOsd { get; set; }
        public bool SmoothTransition { get; set; }
        public bool KeepApplied { get; set; }
        public bool HideFromCapture { get; set; }
        public bool ShortcutCreated { get; set; }    // ярлык на рабочем столе уже создавали (удалённый пользователем заново не создаём)
        public string Language { get; set; }         // "en" | "ru"; в конфиге всегда задан (по умолчанию — язык Windows)

        public AppConfig()
        {
            Language = "";
            Profiles = new List<Profile>();
            GameRules = new List<GameRule>();
            Monitor = "";
            ActiveProfile = "";
            MonitorProfiles = new Dictionary<string, string>();
            HkReset = "Ctrl+Alt+0";
            HkNext = "Ctrl+Alt+PageDown";
            HkPrev = "Ctrl+Alt+PageUp";
            HkToggle = "Ctrl+Alt+T";
            HkShow = "";
            AutoSwitch = true;
            RevertOnLeave = true;
            MinimizeToTray = true;
            ResetOnExit = true;
            ApplyOnStart = true;
            ShowOsd = true;
            SmoothTransition = true;
            KeepApplied = true;
            HideFromCapture = true;
        }

        public static string Dir
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("GAMMADECK_DIR");   // для тестов: отдельная папка настроек
                return !string.IsNullOrEmpty(over) ? over : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GammaDeck");
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

        public static AppConfig Load()
        {
            AppConfig cfg = null;
            try
            {
                if (File.Exists(FilePath))
                    cfg = Json.Deserialize<AppConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch
            {
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
            }
            if (cfg == null)
            {
                cfg = new AppConfig();
                cfg.Profiles = Presets.Create();
            }
            if (cfg.Profiles == null) cfg.Profiles = new List<Profile>();
            if (cfg.GameRules == null) cfg.GameRules = new List<GameRule>();
            if (cfg.MonitorProfiles == null) cfg.MonitorProfiles = new Dictionary<string, string>();
            if (cfg.ActiveProfile == null) cfg.ActiveProfile = "";
            if (cfg.Monitor == null) cfg.Monitor = "";
            cfg.Language = Loc.Normalize(cfg.Language);
            cfg.Profiles.RemoveAll(p => p == null);
            foreach (var p in cfg.Profiles)
            {
                p.Normalize();
                if (string.IsNullOrEmpty(p.Color)) p.Color = "#76B900";
            }
            MigrateRussianNames(cfg);
            return cfg;
        }

        /// <summary>Конфиг старых версий хранит имена встроенных профилей по-русски — переименовываем их на английские везде, где они упоминаются.</summary>
        private static void MigrateRussianNames(AppConfig cfg)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Стандарт", "Default" }, { "Яркий шутер", "Bright shooter" }, { "Тёмные игры", "Dark games" },
                { "Сочные цвета", "Vivid colors" }, { "Кино", "Cinema" }, { "Тёплый (вечер)", "Warm (evening)" },
                { "Холодный", "Cold" }, { "Чёрно-белый", "Black and white" }
            };
            foreach (var p in cfg.Profiles)
            {
                string to;
                if (p.Name == null || !map.TryGetValue(p.Name, out to)) continue;
                if (cfg.Profiles.Any(o => string.Equals(o.Name, to, StringComparison.OrdinalIgnoreCase))) continue;
                p.Name = to;
            }
            Func<string, string> fix = n => { string to; return n != null && map.TryGetValue(n, out to) && cfg.Profiles.Any(o => o.Name == to) ? to : n; };
            cfg.ActiveProfile = fix(cfg.ActiveProfile);
            foreach (var k in cfg.MonitorProfiles.Keys.ToList()) cfg.MonitorProfiles[k] = fix(cfg.MonitorProfiles[k]);
            foreach (var r in cfg.GameRules) r.Profile = fix(r.Profile);
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }

    public static class Presets
    {
        public static List<Profile> Create()
        {
            var list = new List<Profile>();
            list.Add(new Profile("Default", "#6B7280", ColorSettings.Default()));
            list[0].Hotkey = "Ctrl+Alt+1";

            var p = new Profile("Bright shooter", "#F97316", ColorSettings.All(52, 53, 1.15, 80));
            p.Hotkey = "Ctrl+Alt+2"; list.Add(p);

            p = new Profile("Dark games", "#22C55E", ColorSettings.All(57, 48, 1.45, 62));
            p.Hotkey = "Ctrl+Alt+3"; list.Add(p);

            p = new Profile("Vivid colors", "#EC4899", ColorSettings.All(50, 54, 1.0, 90));
            p.Hotkey = "Ctrl+Alt+4"; list.Add(p);

            p = new Profile("Cinema", "#8B5CF6", ColorSettings.All(49, 55, 0.92, 45));
            p.Hotkey = "Ctrl+Alt+5"; list.Add(p);

            var warm = ColorSettings.Default();
            warm.Green = new ChannelSettings(48, 50, 0.95);
            warm.Blue = new ChannelSettings(43, 50, 0.85);
            p = new Profile("Warm (evening)", "#F59E0B", warm);
            p.Hotkey = "Ctrl+Alt+6"; list.Add(p);

            var cold = ColorSettings.Default();
            cold.Red = new ChannelSettings(47, 50, 0.93);
            cold.Blue = new ChannelSettings(52, 50, 1.05);
            cold.Vibrance = 55;
            p = new Profile("Cold", "#3B82F6", cold);
            p.Hotkey = "Ctrl+Alt+7"; list.Add(p);

            p = new Profile("Black and white", "#E5E7EB", ColorSettings.All(50, 55, 1.0, 0));
            p.Hotkey = "Ctrl+Alt+8"; list.Add(p);

            return list;
        }
    }

    public static class Json
    {
        private static JavaScriptSerializer Ser()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = 16 * 1024 * 1024;
            return s;
        }

        public static T Deserialize<T>(string json) { return Ser().Deserialize<T>(json); }

        public static string Serialize(object o) { return Indent(Ser().Serialize(o)); }

        private static string Indent(string json)
        {
            var sb = new StringBuilder();
            int level = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        sb.Append('\n').Append(' ', ++level * 2);
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n').Append(' ', --level * 2).Append(c);
                        break;
                    case ',': sb.Append(c).Append('\n').Append(' ', level * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}

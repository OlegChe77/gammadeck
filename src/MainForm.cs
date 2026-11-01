using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GammaDeck
{
    internal sealed partial class MainForm : Form
    {
        /// <summary>Состояние одного монитора: свой профиль, свои настройки, свой переход и своя игра.</summary>
        private sealed class MonitorState
        {
            public string Device;
            public string Profile;                       // имя активного профиля или null
            public ColorSettings Settings = ColorSettings.Default();   // к чему стремится монитор
            public bool Dirty;
            public ColorSettings TransFrom, TransTo;
            public int TransStep = -1;                   // -1 — перехода нет
            public string GameExe, GameProfile, ProfileBeforeGame;
        }

        private const string HelpUrl = "https://dalink.to/olegche";

        private readonly AppConfig cfg;
        private readonly ColorEngine engine = new ColorEngine();
        private readonly MessageWindow msgWin = new MessageWindow();
        private readonly HotkeyManager hotkeys;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly OsdForm osd = new OsdForm();
        private readonly Timer transTimer = new Timer();
        private readonly Timer keepTimer = new Timer();
        private readonly Timer gameTimer = new Timer();
        private readonly Timer saveTimer = new Timer();
        private readonly Timer displayTimer = new Timer();
        private readonly string ownProcess = Process.GetCurrentProcess().ProcessName;
        private readonly Dictionary<string, MonitorState> mons = new Dictionary<string, MonitorState>(StringComparer.OrdinalIgnoreCase);

        private bool startHidden, reallyExit, suppress, trayHintShown, cleanedUp;
        private ColorSettings editing = ColorSettings.Default();   // то, что показывает редактор (выбранный монитор)
        private int channel;                             // 0 — все, 1 — R, 2 — G, 3 — B
        private const int TransSteps = 12;
        private string lastToggle;

        public MainForm(AppConfig cfg, bool startHidden, string[] args)
        {
            this.cfg = cfg;
            this.startHidden = startHidden;
            hotkeys = new HotkeyManager(msgWin.Handle);
            msgWin.Hotkey += id => hotkeys.Handle(id);
            msgWin.Command += HandleCommand;

            NvApi.Init();
            osd.ExcludeFromCapture = cfg.HideFromCapture;
            var added = SyncMonitors();

            Loc.Set(cfg.Language);
            Text = Loc.T("GammaDeck — monitor color for games and streams");
            Icon = Theme.MakeIcon();
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 800);
            MinimumSize = new Size(940, 700);
            BuildUi();

            transTimer.Interval = 16;
            transTimer.Tick += delegate { TransitionTick(); };
            keepTimer.Interval = 2000;
            keepTimer.Tick += delegate
            {
                if (!cfg.KeepApplied) return;
                foreach (var m in mons.Values) if (m.TransStep < 0) engine.ReapplyGamma(m.Device);
            };
            keepTimer.Start();
            gameTimer.Interval = 1000;
            gameTimer.Tick += delegate { GameTick(); };
            gameTimer.Start();
            saveTimer.Interval = 700;
            saveTimer.Tick += delegate { saveTimer.Stop(); cfg.Save(); };
            displayTimer.Interval = 1500;
            displayTimer.Tick += delegate { displayTimer.Stop(); OnDisplaysChanged(); };

            SetupTray();
            RegisterHotkeys();
            SystemEvents.DisplaySettingsChanged += OnSystemDisplayChanged;
            SystemEvents.PowerModeChanged += OnSystemPowerChanged;

            RestoreProfiles(added);
            RefreshAll();

            var rest = args.Where(a => !a.Equals("--tray", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (rest.Length > 0) HandleCommand(rest);
        }

        // ================= Мониторы =================

        private static string MonitorNum(string device)
        {
            return new string((device ?? "").Where(char.IsDigit).ToArray());
        }

        private List<MonitorState> SortedMons()
        {
            return mons.Values.OrderBy(m => m.Device.Length).ThenBy(m => m.Device, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Приводит список состояний к подключённым мониторам; возвращает новые.</summary>
        private List<MonitorState> SyncMonitors()
        {
            var all = ColorEngine.AllDevices();
            foreach (var d in mons.Keys.Where(k => !all.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) mons.Remove(d);
            var added = new List<MonitorState>();
            foreach (var d in all)
            {
                if (mons.ContainsKey(d)) continue;
                var m = new MonitorState { Device = d };
                mons[d] = m;
                added.Add(m);
            }
            return added;
        }

        /// <summary>Включает мониторам профили, сохранённые в config.json (у старого конфига — общий ActiveProfile).</summary>
        private void RestoreProfiles(List<MonitorState> list)
        {
            if (!cfg.ApplyOnStart) return;
            foreach (var m in list)
            {
                string name;
                if (!cfg.MonitorProfiles.TryGetValue(m.Device, out name)) name = cfg.ActiveProfile;
                var p = FindProfile(name);
                if (p != null) ApplyProfile(p, m.Device, false, false);
            }
        }

        /// <summary>Монитор, выбранный в списке; "" — все (и если выбранный сейчас не подключён).</summary>
        private string SelTarget
        {
            get { return !string.IsNullOrEmpty(cfg.Monitor) && mons.ContainsKey(cfg.Monitor) ? cfg.Monitor : ""; }
        }

        /// <summary>"" — все мониторы, иначе один; неподключённый монитор — пустой список.</summary>
        private List<MonitorState> Targets(string target)
        {
            if (string.IsNullOrEmpty(target)) return SortedMons();
            MonitorState m;
            return mons.TryGetValue(target, out m) ? new List<MonitorState> { m } : new List<MonitorState>();
        }

        /// <summary>Монитор, чьё состояние показывается для цели (для «всех» — основной).</summary>
        private MonitorState StateFor(string target)
        {
            MonitorState m;
            if (!string.IsNullOrEmpty(target) && mons.TryGetValue(target, out m)) return m;
            if (Screen.PrimaryScreen != null && mons.TryGetValue(Screen.PrimaryScreen.DeviceName, out m)) return m;
            if (mons.Count > 0) return SortedMons()[0];
            m = new MonitorState { Device = Screen.PrimaryScreen.DeviceName };
            mons[m.Device] = m;
            return m;
        }

        /// <summary>Общий профиль целевых мониторов или null, если у них разные.</summary>
        private string CommonProfile(string target)
        {
            var ts = Targets(target);
            if (ts.Count == 0) return null;
            string p = ts[0].Profile;
            return ts.All(m => m.Profile == p) ? p : null;
        }

        private void SetProfile(MonitorState m, string name)
        {
            m.Profile = name;
            cfg.MonitorProfiles[m.Device] = name ?? "";
            cfg.ActiveProfile = name ?? "";
        }

        private bool CalcDirty(MonitorState m)
        {
            var p = FindProfile(m.Profile);
            return p == null ? !m.Settings.IsDefault : !p.Settings.SameAs(m.Settings);
        }

        private void RecalcDirty()
        {
            foreach (var m in mons.Values) m.Dirty = CalcDirty(m);
        }

        private string ProfileLabel(MonitorState m)
        {
            return Loc.ProfileName(m.Profile ?? "Default") + (m.Dirty ? "*" : "");
        }

        // ================= Применение =================

        private Profile FindProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return cfg.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>target: "" — все мониторы, иначе \\.\DISPLAYn.</summary>
        private void ApplyProfile(Profile p, string target, bool showOsd, bool smooth = true)
        {
            var ts = Targets(target);
            if (ts.Count == 0)
            {
                Osd(Loc.F("Monitor {0} is not connected", MonitorNum(target)), Theme.Danger, null, "");
                return;
            }
            if (!p.Settings.IsDefault) lastToggle = p.Name;
            foreach (var m in ts)
            {
                SetProfile(m, p.Name);
                m.Dirty = false;
                m.Settings = p.Settings.Clone();
                ApplySettings(m, smooth && cfg.SmoothTransition);
            }
            RefreshAll();
            if (showOsd) Osd(Loc.ProfileName(p.Name), Theme.ParseColor(p.Color, Theme.Accent), p.Hotkey, target);
            ScheduleSave();
        }

        private void ApplySettings(MonitorState m, bool smooth)
        {
            if (!smooth)
            {
                m.TransStep = -1;
                engine.Apply(m.Device, m.Settings);
                UpdateStatus();
                return;
            }
            m.TransFrom = engine.Get(m.Device);
            m.TransTo = m.Settings.Clone();
            m.TransStep = 0;
            transTimer.Start();
        }

        private void TransitionTick()
        {
            bool any = false;
            foreach (var m in mons.Values)
            {
                if (m.TransStep < 0) continue;
                m.TransStep++;
                double t = m.TransStep / (double)TransSteps;
                if (t >= 1)
                {
                    m.TransStep = -1;
                    engine.Apply(m.Device, m.TransTo);
                    continue;
                }
                any = true;
                t = t * t * (3 - 2 * t);
                engine.Apply(m.Device, ColorSettings.Lerp(m.TransFrom, m.TransTo, t));
            }
            if (!any)
            {
                transTimer.Stop();
                UpdateStatus();
            }
        }

        private void ResetToDefault(string target, bool showOsd)
        {
            var def = cfg.Profiles.FirstOrDefault(p => p.Settings.IsDefault);
            if (def != null) { ApplyProfile(def, target, showOsd); return; }
            var ts = Targets(target);
            if (ts.Count == 0) return;
            foreach (var m in ts)
            {
                SetProfile(m, null);
                m.Dirty = false;
                m.Settings = ColorSettings.Default();
                ApplySettings(m, cfg.SmoothTransition);
            }
            RefreshAll();
            if (showOsd) Osd(Loc.ProfileName("Default"), Theme.Dim, cfg.HkReset, target);
            ScheduleSave();
        }

        private void Toggle(string target)
        {
            var ts = Targets(target);
            if (ts.Count == 0) return;
            if (ts.Any(m => !m.Settings.IsDefault))
            {
                var cur = FindProfile(StateFor(target).Profile);
                if (cur != null && !cur.Settings.IsDefault) lastToggle = cur.Name;
                ResetToDefault(target, true);
                return;
            }
            var p = FindProfile(lastToggle) ?? cfg.Profiles.FirstOrDefault(x => !x.Settings.IsDefault);
            if (p != null) ApplyProfile(p, target, true);
        }

        private void Cycle(string target, int dir)
        {
            int n = cfg.Profiles.Count;
            if (n == 0) return;
            string cur = StateFor(target).Profile;
            int idx = cfg.Profiles.FindIndex(p => p.Name == cur);
            int next = idx < 0 ? (dir > 0 ? 0 : n - 1) : (idx + dir + n) % n;
            ApplyProfile(cfg.Profiles[next], target, true);
        }

        private void Osd(string title, Color color, string hotkey, string target)
        {
            if (!cfg.ShowOsd) return;
            Screen scr = null;
            if (!string.IsNullOrEmpty(target))
                scr = Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, target, StringComparison.OrdinalIgnoreCase));
            if (scr == null)
            {
                IntPtr fg = Native.GetForegroundWindow();
                scr = fg != IntPtr.Zero ? Screen.FromHandle(fg) : Screen.PrimaryScreen;
            }
            string sub = "GammaDeck";
            if (mons.Count > 1) sub += "  ·  " + (string.IsNullOrEmpty(target) ? Loc.T("all monitors") : Loc.F("monitor {0}", MonitorNum(target)));
            if (!string.IsNullOrEmpty(hotkey)) sub += "  ·  " + hotkey;
            osd.ShowMessage(title, sub, color, scr);
        }

        // ================= Горячие клавиши и команды =================

        private void RegisterHotkeys()
        {
            hotkeys.Clear();
            hotkeys.Add(cfg.HkReset, () => ResetToDefault(SelTarget, true));
            hotkeys.Add(cfg.HkNext, () => Cycle(SelTarget, 1));
            hotkeys.Add(cfg.HkPrev, () => Cycle(SelTarget, -1));
            hotkeys.Add(cfg.HkToggle, () => Toggle(SelTarget));
            hotkeys.Add(cfg.HkShow, ToggleWindow);
            foreach (var p in cfg.Profiles)
            {
                var pp = p;
                hotkeys.Add(p.Hotkey, () => ApplyProfile(pp, SelTarget, true));
                foreach (var mh in p.MonitorHotkeys)
                {
                    string dev = mh.Monitor;
                    hotkeys.Add(mh.Hotkey, () => ApplyProfile(pp, dev, true));
                }
            }
            UpdateHotkeyStatus();
        }

        private static void SplitArg(string arg, out string key, out string val)
        {
            string s = arg.Trim();
            key = s.ToLowerInvariant();
            val = null;
            int eq = s.IndexOf('=');
            if (key.StartsWith("--") && eq > 0)
            {
                val = s.Substring(eq + 1).Trim('"');
                key = key.Substring(0, eq);
            }
        }

        private void HandleCommand(string[] a)
        {
            // --monitor N действует на все команды строки, где бы он ни стоял
            string target = SelTarget;
            for (int i = 0; i < a.Length; i++)
            {
                string key, val;
                SplitArg(a[i], out key, out val);
                if (key != "--monitor") continue;
                if (val == null && i + 1 < a.Length) val = a[i + 1].Trim().Trim('"');
                val = (val ?? "").Trim();
                if (val == "0" || val.Equals("all", StringComparison.OrdinalIgnoreCase) || val.Equals("все", StringComparison.OrdinalIgnoreCase))
                {
                    target = "";
                    continue;
                }
                string dev = @"\\.\DISPLAY" + MonitorNum(val);
                if (MonitorNum(val).Length == 0 || !mons.ContainsKey(dev))
                {
                    Osd(Loc.F("Monitor not found: {0}", val), Theme.Danger, null, "");
                    return;
                }
                target = dev;
            }

            for (int i = 0; i < a.Length; i++)
            {
                string key, val;
                SplitArg(a[i], out key, out val);
                switch (key)
                {
                    case "--show": ShowMain(); break;
                    case "--reset": ResetToDefault(target, true); break;
                    case "--next": Cycle(target, 1); break;
                    case "--prev": Cycle(target, -1); break;
                    case "--toggle": Toggle(target); break;
                    case "--exit": ExitApp(); return;
                    case "--monitor": if (val == null) i++; break;
                    case "--profile":
                        if (val == null && i + 1 < a.Length) val = a[++i].Trim('"');
                        var p = FindProfile(val) ?? cfg.Profiles.FirstOrDefault(x => string.Equals(Loc.ProfileName(x.Name), val, StringComparison.OrdinalIgnoreCase));
                        if (p != null) ApplyProfile(p, target, true);
                        else Osd(Loc.F("Profile not found: {0}", val), Theme.Danger, null, target);
                        break;
                }
            }
        }

        // ================= Автопереключение по играм =================

        private static string ProcessOf(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            if (pid == 0) return null;
            try
            {
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch { return null; }
        }

        private static string StripExe(string s)
        {
            s = (s ?? "").Trim();
            return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 4) : s;
        }

        private void GameTick()
        {
            IntPtr h = Native.GetForegroundWindow();
            string fg = ProcessOf(h);
            string dev = h != IntPtr.Zero ? Screen.FromHandle(h).DeviceName : null;
            if (lblActiveProc != null && pageGames.Visible && Visible)
                lblActiveProc.Text = Loc.F("Active window process:  {0}", fg == null ? "—" : fg + ".exe" + (mons.Count > 1 ? "  " + Loc.F("(monitor {0})", MonitorNum(dev)) : ""));
            if (!cfg.AutoSwitch || fg == null) return;
            if (fg.Equals(ownProcess, StringComparison.OrdinalIgnoreCase)) return;

            var rule = cfg.GameRules.FirstOrDefault(r => string.Equals(StripExe(r.Exe), fg, StringComparison.OrdinalIgnoreCase));
            // Игра ушла из фокуса или переехала на другой монитор — прежний профиль тем мониторам, где она была
            foreach (var m in SortedMons())
            {
                if (m.GameExe == null) continue;
                if (rule != null && string.Equals(m.Device, dev, StringComparison.OrdinalIgnoreCase)) continue;
                LeaveGame(m);
            }
            if (rule == null || dev == null) return;

            MonitorState s;
            if (!mons.TryGetValue(dev, out s)) return;
            if (s.GameExe != null && s.GameExe.Equals(fg, StringComparison.OrdinalIgnoreCase) && s.GameProfile == rule.Profile) return;
            if (s.GameExe == null) s.ProfileBeforeGame = s.Profile ?? "";
            s.GameExe = fg;
            s.GameProfile = rule.Profile;
            var p = FindProfile(rule.Profile);
            if (p != null && (p.Name != s.Profile || s.Dirty)) ApplyProfile(p, s.Device, true);
            else UpdateStatus();
        }

        private void LeaveGame(MonitorState m)
        {
            m.GameExe = null;
            m.GameProfile = null;
            UpdateStatus();
            if (!cfg.RevertOnLeave) return;
            var p = FindProfile(m.ProfileBeforeGame);
            if (p != null) { if (p.Name != m.Profile) ApplyProfile(p, m.Device, true); }
            else if (!m.Settings.IsDefault) ResetToDefault(m.Device, true);
        }

        // ================= Окно, трей, системные события =================

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1; // тёмный заголовок окна (Windows 10 20H1+ — атрибут 20, старее — 19)
            if (Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0)
                Native.DwmSetWindowAttribute(Handle, 19, ref dark, 4);
            ApplyAffinity();
        }

        private void ApplyAffinity()
        {
            osd.ExcludeFromCapture = cfg.HideFromCapture;
            osd.ApplyAffinity();
            if (IsHandleCreated)
                Native.SetWindowDisplayAffinity(Handle, cfg.HideFromCapture ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);
        }

        private void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            Native.SetForegroundWindow(Handle);
        }

        private void ToggleWindow()
        {
            if (Visible && WindowState != FormWindowState.Minimized && Form.ActiveForm == this) Hide();
            else ShowMain();
        }

        private void ExitApp()
        {
            reallyExit = true;
            Close();
        }

        private void SetupTray()
        {
            tray.Icon = Icon;
            tray.Text = "GammaDeck";
            tray.Visible = true;
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleWindow(); };
        }

        private void UpdateTray()
        {
            var list = SortedMons();
            string t = list.Count == 1 ? "GammaDeck — " + ProfileLabel(list[0])
                : "GammaDeck\n" + string.Join("\n", list.Select(m => MonitorNum(m.Device) + ": " + ProfileLabel(m)));
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            var menu = tray.ContextMenuStrip;
            menu.Items.Clear();
            if (list.Count > 1)
            {
                foreach (var m in list)
                {
                    var sub = new ToolStripMenuItem(Loc.F("Monitor {0}:  {1}", MonitorNum(m.Device), ProfileLabel(m)));
                    AddProfileItems(sub.DropDownItems, m.Device, m.Profile);
                    menu.Items.Add(sub);
                }
                var all = new ToolStripMenuItem(Loc.T("All monitors"));
                AddProfileItems(all.DropDownItems, "", CommonProfile(""));
                menu.Items.Add(all);
            }
            else AddProfileItems(menu.Items, "", list.Count > 0 ? list[0].Profile : null);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T(list.Count > 1 ? "Reset all monitors to default" : "Reset to default"), null, delegate { ResetToDefault("", true); });
            menu.Items.Add(Loc.T("Open GammaDeck"), null, delegate { ShowMain(); });
            menu.Items.Add(Loc.T("♥ Help the project"), null, delegate { OpenHelp(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T("Exit"), null, delegate { ExitApp(); });
        }

        private void AddProfileItems(ToolStripItemCollection items, string target, string current)
        {
            foreach (var p in cfg.Profiles)
            {
                var pp = p;
                // общая клавиша профиля действует на выбранный в списке монитор — подписываем её только при одном мониторе
                string hk = string.IsNullOrEmpty(target) ? (mons.Count == 1 ? p.Hotkey : "") : HotkeyFor(p, target);
                var item = new ToolStripMenuItem(Loc.ProfileName(p.Name) + (string.IsNullOrEmpty(hk) ? "" : "\t" + hk));
                item.Checked = p.Name == current;
                item.Click += delegate { ApplyProfile(pp, target, true); };
                items.Add(item);
            }
        }

        private static string HotkeyFor(Profile p, string device)
        {
            var mh = p.MonitorHotkeys.FirstOrDefault(h => string.Equals(h.Monitor, device, StringComparison.OrdinalIgnoreCase));
            return mh != null ? mh.Hotkey : "";
        }

        private void OnSystemDisplayChanged(object sender, EventArgs e)
        {
            if (IsHandleCreated) BeginInvoke(new Action(() => displayTimer.Start()));
        }

        private void OnSystemPowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume && IsHandleCreated) BeginInvoke(new Action(() => displayTimer.Start()));
        }

        private void OnDisplaysChanged()
        {
            NvApi.ClearCache();
            engine.ForgetNvState();
            var added = SyncMonitors();
            RefreshMonitors();
            engine.ReapplyAll();
            RestoreProfiles(added);
            RebuildTiles();
            RebuildHotkeyPage();
            RegisterHotkeys();
            RefreshAll();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && cfg.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!trayHintShown)
                {
                    trayHintShown = true;
                    tray.ShowBalloonTip(3000, Loc.T("GammaDeck is running in the tray"),
                        Loc.T("Hotkeys are active. To exit, right-click the tray icon."), ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Cleanup();
            base.OnFormClosed(e);
        }

        public void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;
            transTimer.Stop(); keepTimer.Stop(); gameTimer.Stop(); saveTimer.Stop(); displayTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChanged;
            SystemEvents.PowerModeChanged -= OnSystemPowerChanged;
            hotkeys.Clear();
            if (cfg.ResetOnExit && engine.Applied) engine.ResetAll();
            tray.Visible = false;
            tray.Dispose();
            msgWin.Dispose();
            cfg.Save();
        }

        private void ScheduleSave()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ================= Профили =================

        private string UniqueName(string name, Profile except = null)
        {
            string n = name;
            int i = 2;
            while (cfg.Profiles.Any(p => p != except && string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)))
                n = name + " (" + (i++) + ")";
            return n;
        }

        private bool HotkeyUsed(string hk)
        {
            if (string.IsNullOrEmpty(hk)) return false;
            var all = new[] { cfg.HkReset, cfg.HkNext, cfg.HkPrev, cfg.HkToggle, cfg.HkShow }
                .Concat(cfg.Profiles.Select(p => p.Hotkey))
                .Concat(cfg.Profiles.SelectMany(p => p.MonitorHotkeys).Select(h => h.Hotkey));
            return all.Any(h => string.Equals(h, hk, StringComparison.OrdinalIgnoreCase));
        }

        private void ProfilesChanged()
        {
            RebuildTiles();
            RebuildHotkeyPage();
            RefreshGameProfiles();
            RegisterHotkeys();
            RefreshAll();
            ScheduleSave();
        }

        private void NewProfile()
        {
            string name = InputDialog.Ask(this, Loc.T("New profile"), Loc.T("Profile name (current settings will be saved to it):"),
                Loc.F("My profile {0}", cfg.Profiles.Count + 1));
            if (name == null) return;
            var p = new Profile(UniqueName(name), Theme.Palette[cfg.Profiles.Count % Theme.Palette.Length], editing.Clone());
            cfg.Profiles.Add(p);
            foreach (var m in Targets(SelTarget)) SetProfile(m, p.Name);
            RecalcDirty();
            ProfilesChanged();
        }

        private void SaveToProfile(Profile p)
        {
            if (p == null) { NewProfile(); return; }
            p.Settings = editing.Clone();
            foreach (var m in Targets(SelTarget)) SetProfile(m, p.Name);
            RecalcDirty();
            ProfilesChanged();
        }

        private void RenameProfile(Profile p)
        {
            string shown = Loc.ProfileName(p.Name);
            string name = InputDialog.Ask(this, Loc.T("Rename"), Loc.T("New name:"), shown);
            if (name == null || name == shown || name == p.Name) return;
            string old = p.Name;
            p.Name = UniqueName(name, p);
            foreach (var r in cfg.GameRules.Where(r => r.Profile == old)) r.Profile = p.Name;
            foreach (var m in mons.Values)
            {
                if (m.Profile == old) m.Profile = p.Name;
                if (m.ProfileBeforeGame == old) m.ProfileBeforeGame = p.Name;
                if (m.GameProfile == old) m.GameProfile = p.Name;
            }
            foreach (var k in cfg.MonitorProfiles.Keys.ToList())
                if (cfg.MonitorProfiles[k] == old) cfg.MonitorProfiles[k] = p.Name;
            if (cfg.ActiveProfile == old) cfg.ActiveProfile = p.Name;
            if (lastToggle == old) lastToggle = p.Name;
            RefreshGameRules();
            ProfilesChanged();
        }

        private void ChangeColor(Profile p)
        {
            using (var d = new ColorDialog())
            {
                d.FullOpen = true;
                d.Color = Theme.ParseColor(p.Color, Theme.Accent);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                p.Color = Theme.ToHex(d.Color);
                ProfilesChanged();
            }
        }

        private void DuplicateProfile(Profile p)
        {
            var c = new Profile(UniqueName(Loc.F("{0} copy", Loc.ProfileName(p.Name))), p.Color, p.Settings.Clone());
            cfg.Profiles.Insert(cfg.Profiles.IndexOf(p) + 1, c);
            ProfilesChanged();
        }

        private void MoveProfile(Profile p, int dir)
        {
            int i = cfg.Profiles.IndexOf(p), j = i + dir;
            if (i < 0 || j < 0 || j >= cfg.Profiles.Count) return;
            cfg.Profiles[i] = cfg.Profiles[j];
            cfg.Profiles[j] = p;
            ProfilesChanged();
        }

        private void DeleteProfile(Profile p)
        {
            if (MessageBox.Show(this, Loc.F("Delete profile '{0}'?", Loc.ProfileName(p.Name)), "GammaDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            cfg.Profiles.Remove(p);
            cfg.GameRules.RemoveAll(r => r.Profile == p.Name);
            foreach (var m in mons.Values) if (m.Profile == p.Name) SetProfile(m, null);
            foreach (var k in cfg.MonitorProfiles.Keys.ToList())
                if (cfg.MonitorProfiles[k] == p.Name) cfg.MonitorProfiles[k] = "";
            if (cfg.ActiveProfile == p.Name) cfg.ActiveProfile = "";
            RecalcDirty();
            RefreshGameRules();
            ProfilesChanged();
        }

        private void RestorePresets()
        {
            int added = 0;
            foreach (var p in Presets.Create())
            {
                if (FindProfile(p.Name) != null) continue;
                if (HotkeyUsed(p.Hotkey)) p.Hotkey = "";
                cfg.Profiles.Add(p);
                added++;
            }
            ProfilesChanged();
            MessageBox.Show(this, added == 0 ? Loc.T("All built-in profiles are already present.") : Loc.F("Built-in profiles added: {0}", added),
                "GammaDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportProfiles()
        {
            using (var d = new SaveFileDialog())
            {
                d.Filter = Loc.T("GammaDeck profiles (*.json)|*.json");
                d.FileName = Loc.T("GammaDeck-profiles.json");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { System.IO.File.WriteAllText(d.FileName, Json.Serialize(cfg.Profiles), System.Text.Encoding.UTF8); }
                catch (Exception ex) { MessageBox.Show(this, Loc.F("Could not save: {0}", ex.Message), "GammaDeck"); }
            }
        }

        private void ImportProfiles()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = Loc.T("GammaDeck profiles (*.json)|*.json|All files|*.*");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var list = Json.Deserialize<List<Profile>>(System.IO.File.ReadAllText(d.FileName, System.Text.Encoding.UTF8));
                    int n = 0;
                    foreach (var p in list ?? new List<Profile>())
                    {
                        if (p == null) continue;
                        p.Normalize();
                        p.Name = UniqueName(string.IsNullOrWhiteSpace(p.Name) ? Loc.T("Import") : p.Name.Trim());
                        if (string.IsNullOrEmpty(p.Color)) p.Color = Theme.Palette[n % Theme.Palette.Length];
                        if (HotkeyUsed(p.Hotkey)) p.Hotkey = "";
                        p.MonitorHotkeys.RemoveAll(h => HotkeyUsed(h.Hotkey));
                        cfg.Profiles.Add(p);
                        n++;
                    }
                    ProfilesChanged();
                    MessageBox.Show(this, Loc.F("Profiles imported: {0}", n), "GammaDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, Loc.F("Could not read the file: {0}", ex.Message), "GammaDeck"); }
            }
        }

        private void SetAutostart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (on) k.SetValue("GammaDeck", "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue("GammaDeck", false);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, Loc.F("Could not change autostart: {0}", ex.Message), "GammaDeck"); }
        }

        private void UnlockGamma()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--unlock-gamma");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception) { } // пользователь отменил запрос UAC
        }
    }
}

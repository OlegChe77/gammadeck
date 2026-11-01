using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GammaDeck
{
    internal sealed partial class MainForm
    {
        private sealed class HkRow
        {
            public HotkeyBox Box;
            public Label Status;
            public Func<string> Get;
            public Action<string> Set;
        }

        private FlowLayoutPanel monStrip;
        private Label lblStatus;
        private readonly List<Button> navButtons = new List<Button>();
        private readonly List<Panel> pages = new List<Panel>();
        private Panel pageColor, pageHotkeys, pageGames, pageSettings;
        private int curPage;

        // Цвет
        private FlowLayoutPanel tiles;
        private Button btnSave;
        private readonly Button[] chanButtons = new Button[4];
        private SliderRow rowBright, rowContrast, rowGamma, rowVib, rowHue;
        private Label lblNvNote, lblEditTarget;
        private CurveView curve;

        // Горячие клавиши
        private readonly List<HkRow> hkRows = new List<HkRow>();
        private readonly Dictionary<Profile, HotkeyBox> profileBoxes = new Dictionary<Profile, HotkeyBox>();
        private readonly Dictionary<MonitorHotkey, HotkeyBox> monitorBoxes = new Dictionary<MonitorHotkey, HotkeyBox>();

        // Игры
        private ListView lvRules;
        private ComboBox cbProc, cbRuleProfile;
        private Label lblActiveProc;

        private void BuildUi()
        {
            SuspendLayout();

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(14, 10, 14, 10) };
            Controls.Add(content);

            // --- Шапка ---
            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Theme.Panel };
            var title = Theme.Lbl("GammaDeck", Theme.H1, Theme.Text);
            title.Location = new Point(16, 8);
            var sub = Theme.Lbl(Loc.T("Brightness, contrast, gamma, NVIDIA digital vibrance and hue — one click away"), Theme.Small, Theme.Dim);
            sub.Location = new Point(19, 40);
            var help = new LinkLabel
            {
                Text = Loc.T("♥ Help the project"), AutoSize = true, Font = Theme.UiBold, BackColor = Color.Transparent, Cursor = Cursors.Hand,
                LinkColor = Theme.Accent, VisitedLinkColor = Theme.Accent, ActiveLinkColor = Color.FromArgb(140, 210, 20),
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            help.LinkClicked += delegate { OpenHelp(); };
            header.Controls.AddRange(new Control[] { title, sub, help });
            header.Resize += delegate { help.Location = new Point(header.Width - help.Width - 18, 24); };

            // --- Навигация ---
            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg, Padding = new Padding(10, 8, 0, 0) };
            string[] names = { Loc.T("Color and profiles"), Loc.T("Hotkeys"), Loc.T("Games (auto-switch)"), Loc.T("Settings") };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Text = names[i], FlatStyle = FlatStyle.Flat, Height = 32, Font = Theme.UiBold, Cursor = Cursors.Hand,
                    ForeColor = Theme.Dim, BackColor = Theme.Bg, Margin = new Padding(0, 0, 6, 0), UseVisualStyleBackColor = false
                };
                b.Width = TextRenderer.MeasureText(names[i], Theme.UiBold).Width + 32;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Theme.Panel2;
                b.Click += delegate { ShowPage(idx); };
                navButtons.Add(b);
                nav.Controls.Add(b);
            }
            Controls.Add(nav);
            Controls.Add(header); // добавлена позже — значит, докуется выше навигации

            // --- Строка состояния ---
            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Panel };
            lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dim, Font = Theme.Small, Padding = new Padding(12, 0, 0, 0), AutoEllipsis = true };
            statusBar.Controls.Add(lblStatus);
            Controls.Add(statusBar);
            content.BringToFront();

            pageColor = BuildColorPage();
            pageHotkeys = NewPage();
            pageGames = BuildGamesPage();
            pageSettings = BuildSettingsPage();
            pages.AddRange(new[] { pageColor, pageHotkeys, pageGames, pageSettings });
            foreach (var p in pages) content.Controls.Add(p);

            RefreshMonitors();
            RebuildTiles();
            RebuildHotkeyPage();
            RefreshGameProfiles();
            RefreshGameRules();
            ShowPage(curPage);
            ResumeLayout();
        }

        /// <summary>Смена языка на лету: UI собирается заново, всё остальное (меню трея, статус) обновляется следом.</summary>
        private void ChangeLanguage(string lang)
        {
            lang = Loc.Normalize(lang);
            if (lang == cfg.Language && lang == Loc.Lang) return;
            cfg.Language = lang;
            Loc.Set(lang);
            ScheduleSave();
            SuspendLayout();
            var old = Controls.Cast<Control>().ToList();
            Controls.Clear();
            foreach (var c in old) c.Dispose();
            navButtons.Clear();
            pages.Clear();
            hkRows.Clear();
            profileBoxes.Clear();
            monitorBoxes.Clear();
            Text = Loc.T("GammaDeck — monitor color for games and streams");
            BuildUi();
            RefreshAll();
            RegisterHotkeys();
            ResumeLayout(true);
        }

        private static Panel NewPage()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, AutoScroll = true, Visible = false, Padding = new Padding(16) };
        }

        private void ShowPage(int idx)
        {
            curPage = idx;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == idx;
                navButtons[i].ForeColor = i == idx ? Theme.Accent : Theme.Dim;
                navButtons[i].BackColor = i == idx ? Theme.Panel : Theme.Bg;
            }
        }

        private void RefreshAll()
        {
            editing = StateFor(SelTarget).Settings.Clone();
            RefreshEditor();
            RefreshTiles();
            UpdateTray();
            UpdateStatus();
        }

        // ================= Страница «Цвет и профили» =================

        private Panel BuildColorPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, AutoScroll = true };
            page.Controls.Add(right);
            page.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Theme.Bg });

            // Левая колонка: профили
            var left = new Panel { Dock = DockStyle.Left, Width = 392, BackColor = Theme.Panel, Padding = new Padding(12) };
            tiles = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Panel, Padding = new Padding(0, 4, 0, 0) };
            left.Controls.Add(tiles);
            var lHead = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Panel };
            var lt = Theme.Lbl(Loc.T("Profiles"), Theme.H2, Theme.Text);
            lt.Location = new Point(0, 0);
            var lh = Theme.Lbl(Loc.T("Click to apply, right-click for the profile menu"), Theme.Small, Theme.Dim);
            lh.Location = new Point(1, 24);
            lHead.Controls.AddRange(new Control[] { lt, lh });
            left.Controls.Add(lHead);
            var lBottom = new Panel { Dock = DockStyle.Bottom, Height = 84, BackColor = Theme.Panel };
            btnSave = Theme.Btn(Loc.T("Save to profile"), delegate { SaveToProfile(FindProfile(CommonProfile(SelTarget))); }, true);
            btnSave.SetBounds(0, 8, 344, 32);
            btnSave.AutoEllipsis = true;
            var btnNew = Theme.Btn(Loc.T("+ New profile"), delegate { NewProfile(); });
            btnNew.SetBounds(0, 46, 168, 32);
            var btnPresets = Theme.Btn(Loc.T("Restore built-ins"), delegate { RestorePresets(); });
            btnPresets.SetBounds(176, 46, 168, 32);
            lBottom.Controls.AddRange(new Control[] { btnSave, btnNew, btnPresets });
            left.Controls.Add(lBottom);
            tiles.BringToFront();
            page.Controls.Add(left);

            // Полоса мониторов сверху; добавлена последней — значит, занимает всю ширину над колонками
            monStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 74, BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, 12),
                WrapContents = false, AutoScroll = false
            };
            page.Controls.Add(monStrip);
            right.BringToFront();

            // Правая колонка: настройки цвета, как в панели NVIDIA
            var stretch = new List<Control>();
            int y = 14;
            var h1 = Theme.Lbl(Loc.T("Desktop color settings"), Theme.H2, Theme.Text);
            h1.Location = new Point(18, y);
            right.Controls.Add(h1);
            lblEditTarget = Theme.Lbl("", Theme.UiBold, Theme.Accent);
            lblEditTarget.Location = new Point(h1.Right + 10, y + 5);
            right.Controls.Add(lblEditTarget);
            y += 34;

            var lc = Theme.Lbl(Loc.T("Channel:"), Theme.Ui, Theme.Dim);
            lc.Location = new Point(18, y + 6);
            right.Controls.Add(lc);
            string[] chans = { Loc.T("All channels"), Loc.T("Red"), Loc.T("Green"), Loc.T("Blue") };
            Color[] chanColors = { Theme.Text, Color.FromArgb(255, 95, 95), Color.FromArgb(100, 215, 100), Color.FromArgb(95, 150, 255) };
            int x = 188;
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = Theme.Btn(chans[i], delegate { channel = idx; RefreshEditor(); });
                b.Width = 96;
                b.Location = new Point(x, y);
                b.Tag = chanColors[i];
                x += 101;
                chanButtons[i] = b;
                right.Controls.Add(b);
            }
            y += 44;

            rowBright = AddRow(right, stretch, ref y, Loc.T("Brightness"), 0, 100, 50, v => v + "%");
            rowContrast = AddRow(right, stretch, ref y, Loc.T("Contrast"), 0, 100, 50, v => v + "%");
            rowGamma = AddRow(right, stretch, ref y, Loc.T("Gamma"), 30, 280, 100, v => (v / 100.0).ToString("0.00"));
            rowBright.ValueChanged += delegate { EditChannel(c => c.Brightness = rowBright.Value); };
            rowContrast.ValueChanged += delegate { EditChannel(c => c.Contrast = rowContrast.Value); };
            rowGamma.ValueChanged += delegate { EditChannel(c => c.Gamma = rowGamma.Value / 100.0); };

            y += 14;
            var h2 = Theme.Lbl(Loc.T("NVIDIA color enhancements"), Theme.H2, Theme.Text);
            h2.Location = new Point(18, y);
            right.Controls.Add(h2);
            y += 34;
            rowVib = AddRow(right, stretch, ref y, Loc.T("Digital vibrance"), 0, 100, 50, v => v + "%");
            rowHue = AddRow(right, stretch, ref y, Loc.T("Hue"), 0, 359, 0, v => v + "°");
            rowVib.ValueChanged += delegate { if (suppress) return; editing.Vibrance = rowVib.Value; EditorChanged(); };
            rowHue.ValueChanged += delegate { if (suppress) return; editing.Hue = rowHue.Value; EditorChanged(); };
            lblNvNote = Theme.Lbl("", Theme.Small, Theme.Warn);
            lblNvNote.Location = new Point(18, y);
            right.Controls.Add(lblNvNote);
            y += 24;

            curve = new CurveView();
            curve.SetBounds(18, y, 400, 130);
            right.Controls.Add(curve);
            stretch.Add(curve);
            y += 140;

            var bResetCh = Theme.Btn(Loc.T("Reset channel"), delegate { ResetChannel(); });
            bResetCh.Location = new Point(18, y);
            var bResetAll = Theme.Btn(Loc.T("Reset all"), delegate { editing = ColorSettings.Default(); EditorChanged(); RefreshEditor(); });
            bResetAll.Location = new Point(bResetCh.Right + 8, y);
            right.Controls.AddRange(new Control[] { bResetCh, bResetAll });
            y += 44;
            var hint = Theme.Lbl(Loc.T("Double-click a slider to reset it. Mouse wheel and arrow keys fine-tune (hold Shift for steps of 10).\n") +
                                 Loc.T("Only you see these changes: OBS and recordings keep the normal picture."), Theme.Small, Theme.Dim);
            hint.Location = new Point(18, y);
            right.Controls.Add(hint);

            right.Resize += delegate
            {
                int w = Math.Max(400, right.ClientSize.Width - 40);
                foreach (var c in stretch) c.Width = c is CurveView ? Math.Min(w, 760) : Math.Min(w, 900);
            };
            return page;
        }

        private static SliderRow AddRow(Panel parent, List<Control> stretch, ref int y, string text, int min, int max, int def, Func<int, string> fmt)
        {
            var r = new SliderRow(text, min, max, def, fmt);
            r.SetBounds(18, y, 600, 34);
            parent.Controls.Add(r);
            stretch.Add(r);
            y += 38;
            return r;
        }

        private ChannelSettings ShownChannel()
        {
            return channel == 2 ? editing.Green : channel == 3 ? editing.Blue : editing.Red;
        }

        private void EditChannel(Action<ChannelSettings> f)
        {
            if (suppress) return;
            if (channel == 0 || channel == 1) f(editing.Red);
            if (channel == 0 || channel == 2) f(editing.Green);
            if (channel == 0 || channel == 3) f(editing.Blue);
            EditorChanged();
        }

        private void ResetChannel()
        {
            EditChannel(c => { c.Brightness = 50; c.Contrast = 50; c.Gamma = 1.0; });
            RefreshEditor();
        }

        private void EditorChanged()
        {
            foreach (var m in Targets(SelTarget))
            {
                m.TransStep = -1;
                m.Settings = editing.Clone();
                m.Dirty = CalcDirty(m);
                engine.Apply(m.Device, m.Settings);
            }
            curve.Settings = editing;
            curve.Invalidate();
            RefreshTiles();
            UpdateStatus();
        }

        private void RefreshEditor()
        {
            if (rowBright == null) return;
            suppress = true;
            var c = ShownChannel();
            rowBright.Value = (int)Math.Round(c.Brightness);
            rowContrast.Value = (int)Math.Round(c.Contrast);
            rowGamma.Value = (int)Math.Round(c.Gamma * 100);
            rowVib.Value = editing.Vibrance;
            rowHue.Value = editing.Hue;
            suppress = false;

            for (int i = 0; i < 4; i++)
            {
                bool sel = i == channel;
                chanButtons[i].BackColor = sel ? Theme.Hover : Theme.Panel2;
                chanButtons[i].ForeColor = sel ? (Color)chanButtons[i].Tag : Theme.Dim;
                chanButtons[i].FlatAppearance.BorderColor = sel ? (Color)chanButtons[i].Tag : Theme.Border;
            }

            bool nv = NvApi.Available;
            rowVib.Enabled = nv;
            rowHue.Enabled = nv;
            lblNvNote.Text = !nv ? Loc.T("NVIDIA driver (nvapi64.dll) not found — vibrance and hue are unavailable.")
                : NvApi.LegacyVibrance ? Loc.T("The driver supports vibrance only from 50% and up.") : "";
            curve.Settings = editing;
            curve.Invalidate();
            string t = SelTarget;
            lblEditTarget.Text = mons.Count < 2 ? "" : string.IsNullOrEmpty(t) ? Loc.T("— all monitors") : Loc.F("— monitor {0}", MonitorNum(t));
        }

        private void RebuildTiles()
        {
            var old = tiles.Controls.Cast<Control>().ToList();
            tiles.SuspendLayout();
            tiles.Controls.Clear();
            foreach (var c in old)
            {
                if (c.ContextMenuStrip != null) c.ContextMenuStrip.Dispose();
                c.Dispose();
            }
            foreach (var p in cfg.Profiles)
            {
                var pp = p;
                var t = new ProfileTile(p);
                t.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ApplyProfile(pp, SelTarget, false); };
                t.ContextMenuStrip = TileMenu(p);
                tiles.Controls.Add(t);
            }
            tiles.ResumeLayout();
            RefreshTiles();
        }

        private ContextMenuStrip TileMenu(Profile p)
        {
            var m = new ContextMenuStrip();
            m.Items.Add(Loc.T("Apply"), null, delegate { ApplyProfile(p, SelTarget, false); });
            if (mons.Count > 1)
            {
                var on = new ToolStripMenuItem(Loc.T("Apply to monitor"));
                foreach (var ms in SortedMons())
                {
                    string dev = ms.Device;
                    on.DropDownItems.Add(MonitorTitle(dev), null, delegate { ApplyProfile(p, dev, false); });
                }
                on.DropDownItems.Add(Loc.T("All monitors"), null, delegate { ApplyProfile(p, "", false); });
                m.Items.Add(on);
            }
            m.Items.Add(Loc.T("Save current settings here"), null, delegate { SaveToProfile(p); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Loc.T("Rename…"), null, delegate { RenameProfile(p); });
            m.Items.Add(Loc.T("Button color…"), null, delegate { ChangeColor(p); });
            m.Items.Add(Loc.T("Hotkey…"), null, delegate
            {
                ShowPage(1);
                HotkeyBox b;
                if (profileBoxes.TryGetValue(p, out b)) { pageHotkeys.ScrollControlIntoView(b); b.Focus(); }
            });
            var hkMon = new ToolStripMenuItem(Loc.T("Hotkey for a specific monitor"));
            foreach (var ms in SortedMons())
            {
                string dev = ms.Device;
                string hk = HotkeyFor(p, dev);
                hkMon.DropDownItems.Add(MonitorTitle(dev) + (hk.Length > 0 ? "\t" + hk : ""), null, delegate { EditMonitorHotkey(p, dev); });
            }
            m.Items.Add(hkMon);
            m.Items.Add(Loc.T("Duplicate"), null, delegate { DuplicateProfile(p); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Loc.T("Move earlier"), null, delegate { MoveProfile(p, -1); });
            m.Items.Add(Loc.T("Move later"), null, delegate { MoveProfile(p, 1); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Loc.T("Delete"), null, delegate { DeleteProfile(p); });
            return m;
        }

        private static string MonitorTitle(string device)
        {
            string name = Native.MonitorName(device);
            return Loc.F("Monitor {0}", MonitorNum(device)) + (string.IsNullOrEmpty(name) ? "" : ": " + name);
        }

        private void RefreshTiles()
        {
            if (tiles == null) return;
            string target = SelTarget;
            string active = CommonProfile(target);
            bool dirty = Targets(target).Any(m => m.Dirty);
            var list = SortedMons();
            foreach (ProfileTile t in tiles.Controls)
            {
                t.Active = t.Profile.Name == active;
                t.Modified = dirty;
                // на каких мониторах сейчас стоит профиль
                t.Monitors = list.Count < 2 ? "" : string.Join(" ", list.Where(m => m.Profile == t.Profile.Name).Select(m => MonitorNum(m.Device)));
                t.Invalidate();
            }
            UpdateMonitorCards();
            btnSave.Text = active != null ? Loc.F("Save to '{0}'", Loc.ProfileName(active)) + (dirty ? " *" : "") : Loc.T("Save as new profile");
        }

        private void RefreshMonitors()
        {
            if (monStrip == null) return;
            var old = monStrip.Controls.Cast<Control>().ToList();
            monStrip.SuspendLayout();
            monStrip.Controls.Clear();
            foreach (var c in old) c.Dispose();
            var list = SortedMons();
            if (list.Count > 1)
            {
                var all = new MonitorCard(MonitorCard.Kind.All, "") { Badge = "★", Title = Loc.T("All monitors") };
                all.Click += delegate { SelectMonitor(""); };
                monStrip.Controls.Add(all);
            }
            foreach (var m in list)
            {
                string dev = m.Device;
                var scr = Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, dev, StringComparison.OrdinalIgnoreCase));
                var card = new MonitorCard(MonitorCard.Kind.Monitor, dev)
                {
                    Badge = MonitorNum(dev),
                    Title = Native.MonitorName(dev),
                    Detail = scr == null ? "" : scr.Bounds.Width + "×" + scr.Bounds.Height
                };
                card.Click += delegate { SelectMonitor(dev); };
                monStrip.Controls.Add(card);
            }
            var add = new MonitorCard(MonitorCard.Kind.Add, "");
            add.Click += delegate { AddMonitor(); };
            monStrip.Controls.Add(add);
            monStrip.ResumeLayout();
            UpdateMonitorCards();
        }

        /// <summary>Подписи карточек: какой профиль сейчас на мониторе и какая карточка выбрана.</summary>
        private void UpdateMonitorCards()
        {
            if (monStrip == null) return;
            string sel = SelTarget;
            foreach (var c in monStrip.Controls.OfType<MonitorCard>())
            {
                if (c.Type == MonitorCard.Kind.Add) continue;
                if (c.Type == MonitorCard.Kind.All)
                {
                    string common = CommonProfile("");
                    c.Selected = sel.Length == 0;
                    c.Profile = common != null ? Loc.ProfileName(common) : Loc.T("Different profiles");
                    c.ProfileColor = ProfileColor(common);
                }
                else
                {
                    MonitorState m;
                    if (!mons.TryGetValue(c.Device, out m)) continue;
                    c.Selected = sel.Length == 0 ? mons.Count == 1 : string.Equals(sel, c.Device, StringComparison.OrdinalIgnoreCase);
                    c.Profile = ProfileLabel(m);
                    c.ProfileColor = ProfileColor(m.Profile);
                }
                c.Invalidate();
            }
        }

        private Color ProfileColor(string name)
        {
            var p = FindProfile(name);
            return p == null ? Theme.Dim : Theme.ParseColor(p.Color, Theme.Accent);
        }

        private void SelectMonitor(string device)
        {
            if (string.Equals(device, cfg.Monitor ?? "", StringComparison.OrdinalIgnoreCase)) return;
            cfg.Monitor = device;   // только выбор, что настраивать; цвета мониторов не меняются
            RefreshAll();
            ScheduleSave();
        }

        /// <summary>Кнопка «+»: заново ищет подключённые мониторы; если новых нет — подсказывает, как включить.</summary>
        private void AddMonitor()
        {
            var before = new HashSet<string>(mons.Keys, StringComparer.OrdinalIgnoreCase);
            OnDisplaysChanged();
            if (mons.Keys.Any(k => !before.Contains(k))) return;
            if (MessageBox.Show(this,
                Loc.T("No new monitors found.\n\nConnect the monitor and enable it in Windows (Win+P → Extend), then click + again.\n\nOpen Windows display settings?"),
                "GammaDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start("ms-settings:display"); } catch { }
            }
        }

        private static void OpenHelp()
        {
            try { Process.Start(HelpUrl); } catch { }
        }

        private void UpdateStatus()
        {
            if (lblStatus == null) return;
            var parts = new List<string>();
            bool warn = false;
            bool multi = mons.Count > 1;
            var gf = engine.GammaFailed;
            if (gf.Length > 0)
            {
                parts.Add(Loc.F("Gamma{0}: Windows rejected such strong values — Settings → 'Remove Windows gamma limit'",
                    multi ? " " + Loc.F("(monitor {0})", string.Join(", ", gf.Select(MonitorNum))) : ""));
                warn = true;
            }
            else parts.Add(Loc.T("Gamma: OK"));
            var nf = engine.NvFailed;
            if (!NvApi.Available) parts.Add(Loc.T("NVIDIA API not found"));
            else if (nf.Length > 0)
            {
                parts.Add(Loc.F("NVIDIA{0}: monitor is not on an NVIDIA GPU or the driver denied access",
                    multi ? " " + Loc.F("(monitor {0})", string.Join(", ", nf.Select(MonitorNum))) : ""));
                warn = true;
            }
            else parts.Add("NVIDIA: OK");
            var list = SortedMons();
            if (!multi)
            {
                var m0 = list.FirstOrDefault();
                parts.Add(Loc.T("Profile: ") + (m0 != null && m0.Profile != null ? Loc.ProfileName(m0.Profile) : "—") + (m0 != null && m0.Dirty ? Loc.T(" (modified)") : ""));
            }
            else parts.Add(Loc.T("Profiles: ") + string.Join(",  ", list.Select(m => MonitorNum(m.Device) + " — " + ProfileLabel(m))));
            if (cfg.AutoSwitch)
            {
                var games = list.Where(m => m.GameExe != null).Select(m => m.GameExe + (multi ? " (" + MonitorNum(m.Device) + ")" : "")).ToList();
                if (games.Count > 0) parts.Add(Loc.T("Game: ") + string.Join(", ", games));
            }
            lblStatus.Text = string.Join("     •     ", parts);
            lblStatus.ForeColor = warn ? Theme.Warn : Theme.Dim;
        }

        // ================= Страница «Горячие клавиши» =================

        private void RebuildHotkeyPage()
        {
            var page = pageHotkeys;
            page.SuspendLayout();
            var old = page.Controls.Cast<Control>().ToList();
            page.Controls.Clear();
            foreach (var c in old) c.Dispose();
            hkRows.Clear();
            profileBoxes.Clear();
            monitorBoxes.Clear();

            int y = 14;
            var h = Theme.Lbl(Loc.T("Hotkeys"), Theme.H2, Theme.Text);
            h.Location = new Point(18, y);
            page.Controls.Add(h);
            y += 30;
            var hint = Theme.Lbl(Loc.T("Click a field and press a key combination. Backspace / Delete clears it.\n") +
                                 Loc.T("Hotkeys work globally — in games, in OBS, and while the app is minimized to the tray. Stream Deck buttons work too (the 'Hotkey' action)."),
                                 Theme.Small, Theme.Dim);
            hint.Location = new Point(18, y);
            page.Controls.Add(hint);
            y += 46;

            y = HkSection(page, y, Loc.T("General actions"));
            y = AddHkRow(page, y, Loc.T("Reset to default"), () => cfg.HkReset, v => cfg.HkReset = v, null);
            y = AddHkRow(page, y, Loc.T("Next profile"), () => cfg.HkNext, v => cfg.HkNext = v, null);
            y = AddHkRow(page, y, Loc.T("Previous profile"), () => cfg.HkPrev, v => cfg.HkPrev = v, null);
            y = AddHkRow(page, y, Loc.T("Toggle: profile ⇄ default"), () => cfg.HkToggle, v => cfg.HkToggle = v, null);
            y = AddHkRow(page, y, Loc.T("Show / hide the app window"), () => cfg.HkShow, v => cfg.HkShow = v, null);
            y += 10;
            y = HkSection(page, y, Loc.T("Profiles"));
            foreach (var p in cfg.Profiles)
            {
                var pp = p;
                y = AddHkRow(page, y, Loc.ProfileName(p.Name), () => pp.Hotkey, v => pp.Hotkey = v, pp);
                foreach (var hm in p.MonitorHotkeys)
                {
                    var mh = hm;
                    y = AddHkRow(page, y, "      ↳ " + Loc.F("monitor {0}", MonitorNum(mh.Monitor)) + (mons.ContainsKey(mh.Monitor) ? "" : " " + Loc.T("(not connected)")),
                        () => mh.Hotkey, v => mh.Hotkey = v, null, delegate
                        {
                            pp.MonitorHotkeys.Remove(mh);
                            BeginInvoke(new Action(RebuildHotkeyPage));
                        });
                    monitorBoxes[mh] = hkRows[hkRows.Count - 1].Box;
                }
            }
            y += 16;
            y = HkSection(page, y, Loc.T("Command line (Stream Deck 'Open', shortcuts, OBS scripts)"));
            var cmd = Theme.Lbl(
                "GammaDeck.exe --profile \"Dark games\"               — " + Loc.T("on the monitor selected in the monitor strip") + "\n" +
                "GammaDeck.exe --profile \"Dark games\" --monitor 2   — " + Loc.T("on DISPLAY2 (--monitor all — on all)") + "\n" +
                "GammaDeck.exe --reset | --next | --prev | --toggle | --show | --exit   (" + Loc.T("also accept --monitor N") + ")\n" +
                Loc.T("If the app is already running, the command is passed to it instantly (a second copy does not open)."),
                new Font("Consolas", 9f), Theme.Dim);
            cmd.Location = new Point(18, y);
            page.Controls.Add(cmd);
            page.ResumeLayout();
            UpdateHotkeyStatus();
        }

        private static int HkSection(Panel page, int y, string text)
        {
            var l = Theme.Lbl(text, Theme.UiBold, Theme.Accent);
            l.Location = new Point(18, y);
            page.Controls.Add(l);
            return y + 28;
        }

        private int AddHkRow(Panel page, int y, string text, Func<string> get, Action<string> set, Profile p, Action remove = null)
        {
            var l = Theme.Lbl(text, Theme.Ui, Theme.Text);
            l.AutoSize = false;
            l.AutoEllipsis = true;
            l.SetBounds(18, y + 3, 290, 22);
            var box = new HotkeyBox();
            box.Location = new Point(316, y);
            box.Hotkey = get();
            var clear = Theme.Btn("✕", null);
            clear.SetBounds(532, y - 2, 34, 28);
            clear.MinimumSize = Size.Empty;
            clear.Width = 34;
            var status = Theme.Lbl("", Theme.Small, Theme.Danger);
            status.Location = new Point(576, y + 5);
            var row = new HkRow { Box = box, Status = status, Get = get, Set = set };
            hkRows.Add(row);
            if (p != null) profileBoxes[p] = box;

            box.Enter += delegate { hotkeys.Clear(); };
            box.Leave += delegate { RegisterHotkeys(); };
            box.HotkeyChanged += delegate { HotkeyEdited(row); };
            clear.Click += delegate
            {
                if (remove != null)   // строка «только монитор N» — крестик удаляет её целиком
                {
                    remove();
                    RegisterHotkeys();
                    UpdateTray();
                    ScheduleSave();
                    return;
                }
                box.Hotkey = "";
                HotkeyEdited(row);
                RegisterHotkeys();
            };
            page.Controls.AddRange(new Control[] { l, box, clear, status });
            return y + 34;
        }

        private void HotkeyEdited(HkRow row)
        {
            string hk = row.Box.Hotkey;
            row.Set(hk);
            if (hk.Length > 0)
            {
                foreach (var other in hkRows)
                {
                    if (other == row || !string.Equals(other.Get(), hk, StringComparison.OrdinalIgnoreCase)) continue;
                    other.Set("");
                    other.Box.Hotkey = "";
                }
            }
            if (tiles != null) foreach (Control t in tiles.Controls) t.Invalidate();
            UpdateTray();
            ScheduleSave();
        }

        private void UpdateHotkeyStatus()
        {
            foreach (var r in hkRows)
            {
                string hk = r.Box.Hotkey;
                r.Status.Text = hk.Length > 0 && hotkeys.Failed.Contains(hk) ? Loc.T("⚠ taken by another program or invalid") : "";
            }
        }

        /// <summary>Пункт меню профиля «Горячая клавиша для конкретного монитора».</summary>
        private void EditMonitorHotkey(Profile p, string device)
        {
            var mh = p.MonitorHotkeys.FirstOrDefault(h => string.Equals(h.Monitor, device, StringComparison.OrdinalIgnoreCase));
            if (mh == null)
            {
                mh = new MonitorHotkey { Monitor = device, Hotkey = "" };
                p.MonitorHotkeys.Add(mh);
                RebuildHotkeyPage();
            }
            ShowPage(1);
            HotkeyBox b;
            if (monitorBoxes.TryGetValue(mh, out b)) { pageHotkeys.ScrollControlIntoView(b); b.Focus(); }
        }

        // ================= Страница «Игры» =================

        private Panel BuildGamesPage()
        {
            var page = NewPage();
            page.AutoScroll = false;
            int y = 14;
            var h = Theme.Lbl(Loc.T("Auto-switch profile by game"), Theme.H2, Theme.Text);
            h.Location = new Point(18, y);
            page.Controls.Add(h);
            y += 34;
            var c1 = Theme.Check(Loc.T("Switch profile when the game window is active"), cfg.AutoSwitch, v => { cfg.AutoSwitch = v; ScheduleSave(); });
            c1.Location = new Point(18, y);
            y += 28;
            var c2 = Theme.Check(Loc.T("Restore the previous profile when you leave the game (Alt+Tab to OBS, browser, Discord)"), cfg.RevertOnLeave, v => { cfg.RevertOnLeave = v; ScheduleSave(); });
            c2.Location = new Point(18, y);
            y += 34;
            lblActiveProc = Theme.Lbl(Loc.F("Active window process:  {0}", "—"), Theme.Small, Theme.Dim);
            lblActiveProc.Location = new Point(18, y);
            y += 28;
            page.Controls.AddRange(new Control[] { c1, c2, lblActiveProc });

            cbProc = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel2, ForeColor = Theme.Text, Font = Theme.Ui, Width = 250 };
            cbProc.Location = new Point(18, y + 4);
            cbProc.DropDown += delegate { FillProcesses(); };
            var bBrowse = Theme.Btn(Loc.T("Browse…"), delegate
            {
                using (var d = new OpenFileDialog())
                {
                    d.Filter = Loc.T("Programs (*.exe)|*.exe");
                    if (d.ShowDialog(this) == DialogResult.OK) cbProc.Text = System.IO.Path.GetFileName(d.FileName);
                }
            });
            bBrowse.Location = new Point(276, y + 2);
            var arrow = Theme.Lbl("→", Theme.H2, Theme.Dim);
            arrow.Location = new Point(bBrowse.Right + 10, y + 2);
            cbRuleProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel2, ForeColor = Theme.Text, Font = Theme.Ui, Width = 220 };
            cbRuleProfile.Location = new Point(bBrowse.Right + 40, y + 4);
            var bAdd = Theme.Btn(Loc.T("Add"), delegate { AddRule(); }, true);
            bAdd.Location = new Point(cbRuleProfile.Right + 12, y + 2);
            page.Controls.AddRange(new Control[] { cbProc, bBrowse, arrow, cbRuleProfile, bAdd });
            y += 46;

            lvRules = new ListView
            {
                View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = Theme.Panel2,
                ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Ui, OwnerDraw = true, HideSelection = false
            };
            lvRules.Columns.Add(Loc.T("Game (process)"), 300);
            lvRules.Columns.Add(Loc.T("Profile"), 300);
            lvRules.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Hover)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.UiBold, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            lvRules.DrawItem += (s, e) => { e.DrawDefault = true; };
            lvRules.DrawSubItem += (s, e) => { e.DrawDefault = true; };
            lvRules.Location = new Point(18, y);
            page.Controls.Add(lvRules);

            var bRemove = Theme.Btn(Loc.T("Delete selected"), delegate
            {
                foreach (ListViewItem it in lvRules.SelectedItems) cfg.GameRules.Remove((GameRule)it.Tag);
                RefreshGameRules();
                ScheduleSave();
            });
            var note = Theme.Lbl(Loc.T("Works best in windowed / borderless mode.\nIf a fullscreen game resets the gamma, enable 'Keep settings applied'."),
                Theme.Small, Theme.Dim);
            page.Controls.AddRange(new Control[] { bRemove, note });

            int top = y;
            page.Resize += delegate
            {
                int w = page.ClientSize.Width - 36;
                lvRules.SetBounds(18, top, w, Math.Max(80, page.ClientSize.Height - top - 76));
                lvRules.Columns[0].Width = w / 2;
                lvRules.Columns[1].Width = w - w / 2 - 4;
                bRemove.Location = new Point(18, lvRules.Bottom + 10);
                note.Location = new Point(bRemove.Right + 14, lvRules.Bottom + 11);
            };
            return page;
        }

        private void FillProcesses()
        {
            string text = cbProc.Text;
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.ProcessName != ownProcess) names.Add(p.ProcessName + ".exe");
                }
                catch { }
                finally { p.Dispose(); }
            }
            cbProc.Items.Clear();
            foreach (var n in names) cbProc.Items.Add(n);
            cbProc.Text = text;
        }

        private void AddRule()
        {
            string exe = StripExe(cbProc.Text);
            var prof = cbRuleProfile.SelectedItem as ProfileItem;
            if (exe.Length == 0 || prof == null)
            {
                MessageBox.Show(this, Loc.T("Choose a game process and a profile."), "GammaDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            cfg.GameRules.RemoveAll(r => string.Equals(StripExe(r.Exe), exe, StringComparison.OrdinalIgnoreCase));
            cfg.GameRules.Add(new GameRule { Exe = exe + ".exe", Profile = prof.Name });
            cbProc.Text = "";
            RefreshGameRules();
            ScheduleSave();
        }

        private void RefreshGameRules()
        {
            if (lvRules == null) return;
            lvRules.Items.Clear();
            foreach (var r in cfg.GameRules)
            {
                var it = new ListViewItem(r.Exe);
                it.SubItems.Add(FindProfile(r.Profile) != null ? Loc.ProfileName(r.Profile) : Loc.F("{0}  (profile deleted)", Loc.ProfileName(r.Profile)));
                it.Tag = r;
                lvRules.Items.Add(it);
            }
        }

        private void RefreshGameProfiles()
        {
            if (cbRuleProfile == null) return;
            var sel = cbRuleProfile.SelectedItem as ProfileItem;
            cbRuleProfile.Items.Clear();
            foreach (var p in cfg.Profiles) cbRuleProfile.Items.Add(new ProfileItem(p.Name));
            int idx = -1;
            if (sel != null)
                for (int i = 0; i < cbRuleProfile.Items.Count; i++)
                    if (((ProfileItem)cbRuleProfile.Items[i]).Name == sel.Name) idx = i;
            if (idx >= 0) cbRuleProfile.SelectedIndex = idx;
            else if (cbRuleProfile.Items.Count > 0) cbRuleProfile.SelectedIndex = 0;
        }

        /// <summary>Пункт списка профилей: в правилах хранится английское имя, на экране — переведённое.</summary>
        private sealed class ProfileItem
        {
            public readonly string Name;
            public ProfileItem(string name) { Name = name; }
            public override string ToString() { return Loc.ProfileName(Name); }
        }

        // ================= Страница «Настройки» =================

        private Panel BuildSettingsPage()
        {
            var page = NewPage();
            int y = 14;

            y = HkSection(page, y, Loc.T("Language"));
            var cbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel2, ForeColor = Theme.Text, Font = Theme.Ui, Width = 200 };
            cbLang.Items.AddRange(new object[] { "English", "Русский" });
            cbLang.SelectedIndex = Loc.IsRu ? 1 : 0;
            cbLang.Location = new Point(18, y);
            cbLang.SelectedIndexChanged += delegate
            {
                string lang = cbLang.SelectedIndex == 1 ? Loc.Ru : Loc.En;
                if (lang != Loc.Lang) BeginInvoke(new Action(() => ChangeLanguage(lang)));   // UI пересобирается — не из обработчика самого списка
            };
            page.Controls.Add(cbLang);
            y += 44;

            y = HkSection(page, y, Loc.T("Startup"));
            y = AddCheck(page, y, Loc.T("Start with Windows"), cfg.StartWithWindows, v => { cfg.StartWithWindows = v; SetAutostart(v); });
            y = AddCheck(page, y, Loc.T("Start minimized to tray"), cfg.StartMinimized, v => cfg.StartMinimized = v);
            y = AddCheck(page, y, Loc.T("Apply the last profile on startup"), cfg.ApplyOnStart, v => cfg.ApplyOnStart = v);
            y = AddCheck(page, y, Loc.T("Close button minimizes to tray (hotkeys keep working)"), cfg.MinimizeToTray, v => cfg.MinimizeToTray = v);
            y += 12;

            y = HkSection(page, y, Loc.T("Behavior"));
            y = AddCheck(page, y, Loc.T("Show an on-screen notification when the profile changes"), cfg.ShowOsd, v => cfg.ShowOsd = v);
            y = AddCheck(page, y, Loc.T("Smooth transition between profiles"), cfg.SmoothTransition, v => cfg.SmoothTransition = v);
            y = AddCheck(page, y, Loc.T("Keep settings applied (re-apply if a game resets the gamma)"), cfg.KeepApplied, v => cfg.KeepApplied = v);
            y = AddCheck(page, y, Loc.T("Reset color to default when the app exits"), cfg.ResetOnExit, v => cfg.ResetOnExit = v);
            y = AddCheck(page, y, Loc.T("Hide the app window and notifications from screen capture (OBS, Discord)"), cfg.HideFromCapture, v => { cfg.HideFromCapture = v; ApplyAffinity(); });
            y += 12;

            y = HkSection(page, y, Loc.T("Profiles"));
            var bExp = Theme.Btn(Loc.T("Export profiles…"), delegate { ExportProfiles(); });
            bExp.Location = new Point(18, y);
            var bImp = Theme.Btn(Loc.T("Import profiles…"), delegate { ImportProfiles(); });
            bImp.Location = new Point(bExp.Right + 8, y);
            var bDir = Theme.Btn(Loc.T("Open settings folder"), delegate
            {
                System.IO.Directory.CreateDirectory(AppConfig.Dir);
                Process.Start("explorer.exe", "\"" + AppConfig.Dir + "\"");
            });
            bDir.Location = new Point(bImp.Right + 8, y);
            page.Controls.AddRange(new Control[] { bExp, bImp, bDir });
            y += 46;

            y = HkSection(page, y, "Windows");
            var bUnlock = Theme.Btn(Loc.T("Remove Windows gamma limit…"), delegate { UnlockGamma(); });
            bUnlock.Location = new Point(18, y);
            page.Controls.Add(bUnlock);
            y += 38;
            var info = Theme.Lbl(
                Loc.T("By default Windows refuses very strong gamma/brightness. This button writes GdiIcmGammaRange = 256 to the registry\n") +
                Loc.T("(requires administrator rights, takes effect after a restart). Needed only if the status bar says the gamma was not accepted."),
                Theme.Small, Theme.Dim);
            info.Location = new Point(18, y);
            page.Controls.Add(info);
            y += 48;

            y = HkSection(page, y, Loc.T("Support"));
            var bHelp = Theme.Btn(Loc.T("♥ Help the project"), delegate { OpenHelp(); }, true);
            bHelp.Location = new Point(18, y);
            page.Controls.Add(bHelp);
            y += 46;

            y = HkSection(page, y, Loc.T("How it works"));
            var how = Theme.Lbl(
                Loc.T("• Brightness, contrast and gamma use the GPU gamma ramp (like 'Adjust desktop color settings' in the NVIDIA control panel).\n") +
                Loc.T("• Digital vibrance and hue go straight through the NVIDIA driver (NVAPI).\n") +
                Loc.T("• All of it is applied at the GPU output, so stream viewers and recordings see the normal picture — only your screen changes.\n") +
                Loc.T("• Settings and profiles are stored in ") + AppConfig.FilePath,
                Theme.Small, Theme.Dim);
            how.Location = new Point(18, y);
            page.Controls.Add(how);
            return page;
        }

        private int AddCheck(Panel page, int y, string text, bool value, Action<bool> set)
        {
            var c = Theme.Check(text, value, v => { set(v); ScheduleSave(); });
            c.Location = new Point(18, y);
            page.Controls.Add(c);
            return y + 28;
        }
    }
}

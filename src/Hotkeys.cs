using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GammaDeck
{
    internal static class Hotkeys
    {
        public static bool IsWinDown()
        {
            return (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
        }

        public static string Format(Keys mods, bool win, Keys key)
        {
            var parts = new List<string>();
            if ((mods & Keys.Control) != 0) parts.Add("Ctrl");
            if ((mods & Keys.Alt) != 0) parts.Add("Alt");
            if ((mods & Keys.Shift) != 0) parts.Add("Shift");
            if (win) parts.Add("Win");
            parts.Add(KeyName(key));
            return string.Join("+", parts);
        }

        private static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            switch (k)
            {
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                case Keys.Return: return "Enter";
                default: return k.ToString();
            }
        }

        /// <summary>Клавиша, которую нельзя назначать без модификатора (сломает обычный набор текста).</summary>
        public static bool NeedsModifier(Keys k)
        {
            return (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || k == Keys.Space || k == Keys.Enter || k == Keys.Tab;
        }

        public static bool TryParse(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var raw in s.Split('+'))
            {
                string t = raw.Trim();
                if (t.Length == 0) return false;
                switch (t.ToLowerInvariant())
                {
                    case "ctrl": mods |= Native.MOD_CONTROL; continue;
                    case "alt": mods |= Native.MOD_ALT; continue;
                    case "shift": mods |= Native.MOD_SHIFT; continue;
                    case "win": mods |= Native.MOD_WIN; continue;
                }
                if (vk != 0) return false;
                if (t.Length == 1 && char.IsDigit(t[0])) { vk = (uint)(Keys.D0 + (t[0] - '0')); continue; }
                if (t.Equals("PageDown", StringComparison.OrdinalIgnoreCase)) { vk = (uint)Keys.Next; continue; }
                if (t.Equals("PageUp", StringComparison.OrdinalIgnoreCase)) { vk = (uint)Keys.Prior; continue; }
                Keys k;
                if (!Enum.TryParse(t, true, out k)) return false;
                vk = (uint)(k & Keys.KeyCode);
            }
            return vk != 0;
        }
    }

    /// <summary>Регистрирует глобальные горячие клавиши на окне сообщений.</summary>
    internal sealed class HotkeyManager
    {
        private readonly IntPtr hwnd;
        private readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();
        private int nextId = 1;

        public readonly HashSet<string> Failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HotkeyManager(IntPtr hwnd) { this.hwnd = hwnd; }

        public void Clear()
        {
            foreach (var id in actions.Keys) Native.UnregisterHotKey(hwnd, id);
            actions.Clear();
            Failed.Clear();
        }

        public bool Add(string hotkey, Action action)
        {
            if (string.IsNullOrWhiteSpace(hotkey)) return true;
            uint mods, vk;
            if (!Hotkeys.TryParse(hotkey, out mods, out vk)) { Failed.Add(hotkey); return false; }
            int id = nextId++;
            if (Native.RegisterHotKey(hwnd, id, mods | Native.MOD_NOREPEAT, vk))
            {
                actions[id] = action;
                return true;
            }
            Failed.Add(hotkey);
            return false;
        }

        public void Handle(int id)
        {
            Action a;
            if (actions.TryGetValue(id, out a)) a();
        }
    }

    /// <summary>
    /// Скрытое окно: принимает горячие клавиши и команды от второй копии программы
    /// (GammaDeck.exe --profile "Имя" — удобно для Stream Deck).
    /// </summary>
    internal sealed class MessageWindow : NativeWindow, IDisposable
    {
        private const string Caption = "GammaDeck.IPC.6E0B1D2A";

        public event Action<int> Hotkey;
        public event Action<string[]> Command;

        public MessageWindow()
        {
            var cp = new CreateParams();
            cp.Caption = Caption;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                if (Hotkey != null) Hotkey(m.WParam.ToInt32());
                return;
            }
            if (m.Msg == Native.WM_COPYDATA)
            {
                var cds = (Native.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Native.COPYDATASTRUCT));
                string s = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2);
                if (Command != null) Command(s.Split('\n'));
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        public static bool SendToRunning(string[] args)
        {
            IntPtr target = Native.FindWindow(null, Caption);
            if (target == IntPtr.Zero) return false;
            Native.AllowSetForegroundWindow(-1);
            string s = string.Join("\n", args);
            IntPtr mem = Marshal.StringToHGlobalUni(s);
            try
            {
                var cds = new Native.COPYDATASTRUCT { dwData = IntPtr.Zero, cbData = s.Length * 2, lpData = mem };
                Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, ref cds);
            }
            finally { Marshal.FreeHGlobal(mem); }
            return true;
        }

        public void Dispose() { DestroyHandle(); }
    }
}

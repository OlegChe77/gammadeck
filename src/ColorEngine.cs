using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace GammaDeck
{
    internal static class GammaMath
    {
        public static ushort[] BuildRamp(ColorSettings s)
        {
            var ramp = new ushort[768];
            Fill(ramp, 0, s.Red);
            Fill(ramp, 256, s.Green);
            Fill(ramp, 512, s.Blue);
            return ramp;
        }

        /// <summary>Значение кривой (0..1) для входа x (0..1) — та же формула, что и в рампе.</summary>
        public static double Eval(ChannelSettings c, double x)
        {
            double b = (c.Brightness - 50) / 100.0;
            double k = (c.Contrast - 50) / 50.0;
            k = k >= 0 ? 1 + k : 1 + k * 0.75;
            double g = Math.Max(0.3, Math.Min(2.8, c.Gamma));
            double v = Math.Pow(x, 1.0 / g);
            v = (v - 0.5) * k + 0.5 + b;
            return Math.Max(0, Math.Min(1, v));
        }

        private static void Fill(ushort[] ramp, int offset, ChannelSettings c)
        {
            for (int i = 0; i < 256; i++)
                ramp[offset + i] = (ushort)Math.Round(Eval(c, i / 255.0) * 65535);
        }

        public static bool SetRamp(string device, ushort[] ramp)
        {
            IntPtr dc = Native.CreateDC(device, null, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try { return Native.SetDeviceGammaRamp(dc, ramp); }
            finally { Native.DeleteDC(dc); }
        }
    }

    /// <summary>Применяет настройки цвета к мониторам; для каждого устройства хранит своё состояние.</summary>
    internal sealed class ColorEngine
    {
        private readonly Dictionary<string, ColorSettings> current = new Dictionary<string, ColorSettings>();
        private readonly Dictionary<string, int> lastVibrance = new Dictionary<string, int>();
        private readonly Dictionary<string, int> lastHue = new Dictionary<string, int>();
        private readonly HashSet<string> gammaFailed = new HashSet<string>();
        private readonly HashSet<string> nvFailed = new HashSet<string>();

        public bool Applied { get { return current.Count > 0; } }
        public string[] GammaFailed { get { return gammaFailed.OrderBy(d => d).ToArray(); } }
        public string[] NvFailed { get { return nvFailed.OrderBy(d => d).ToArray(); } }

        public static string[] AllDevices()
        {
            return Screen.AllScreens.Select(s => s.DeviceName).ToArray();
        }

        /// <summary>Что сейчас стоит на мониторе (во время плавного перехода — промежуточное значение).</summary>
        public ColorSettings Get(string device)
        {
            ColorSettings s;
            return current.TryGetValue(device, out s) ? s.Clone() : ColorSettings.Default();
        }

        public void Apply(string device, ColorSettings s, bool force = false)
        {
            current[device] = s.Clone();
            if (GammaMath.SetRamp(device, GammaMath.BuildRamp(s))) gammaFailed.Remove(device);
            else gammaFailed.Add(device);

            if (!NvApi.Available) return;
            IntPtr h = NvApi.GetHandle(device);
            if (h == IntPtr.Zero) { nvFailed.Add(device); return; }
            bool ok = true;
            int v;
            if (force || !lastVibrance.TryGetValue(device, out v) || v != s.Vibrance)
            {
                if (NvApi.SetVibrance(h, s.Vibrance)) lastVibrance[device] = s.Vibrance;
                else ok = false;
            }
            if (force || !lastHue.TryGetValue(device, out v) || v != s.Hue)
            {
                if (NvApi.SetHue(h, s.Hue)) lastHue[device] = s.Hue;
            }
            if (ok) nvFailed.Remove(device);
            else nvFailed.Add(device);
        }

        /// <summary>Повторно ставит гамму монитора (игры в полноэкранном режиме иногда её сбрасывают).</summary>
        public void ReapplyGamma(string device)
        {
            ColorSettings s;
            if (!current.TryGetValue(device, out s) || s.IsDefault) return;
            GammaMath.SetRamp(device, GammaMath.BuildRamp(s));
        }

        /// <summary>После смены конфигурации дисплеев заново применяет всё к подключённым мониторам.</summary>
        public void ReapplyAll()
        {
            var all = AllDevices();
            foreach (var d in current.Keys.ToList())
            {
                if (all.Contains(d)) Apply(d, current[d], true);
                else { current.Remove(d); gammaFailed.Remove(d); nvFailed.Remove(d); }
            }
        }

        public void ForgetNvState()
        {
            lastVibrance.Clear();
            lastHue.Clear();
        }

        /// <summary>Сбрасывает все мониторы к стандартным значениям.</summary>
        public void ResetAll()
        {
            var def = ColorSettings.Default();
            var ramp = GammaMath.BuildRamp(def);
            foreach (var d in AllDevices())
            {
                GammaMath.SetRamp(d, ramp);
                IntPtr h = NvApi.GetHandle(d);
                if (h != IntPtr.Zero)
                {
                    NvApi.SetVibrance(h, def.Vibrance);
                    NvApi.SetHue(h, def.Hue);
                }
            }
            ForgetNvState();
            current.Clear();
            gammaFailed.Clear();
            nvFailed.Clear();
        }
    }
}

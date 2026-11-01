using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GammaDeck
{
    /// <summary>
    /// Обёртка над nvapi64.dll (идёт вместе с драйвером NVIDIA):
    /// «Цифровая интенсивность» (Digital Vibrance) и «Оттенок» (Hue).
    /// </summary>
    internal static class NvApi
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface64(uint id);

        [DllImport("nvapi.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface32(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetAssociatedHandleFn([MarshalAs(UnmanagedType.LPStr)] string displayName, out IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDvcInfoFn(IntPtr h, uint outputId, ref DvcInfo info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetDvcLevelFn(IntPtr h, uint outputId, int level);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDvcInfoExFn(IntPtr h, uint outputId, ref DvcInfoEx info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetDvcLevelExFn(IntPtr h, uint outputId, ref DvcInfoEx info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetHueInfoFn(IntPtr h, uint outputId, ref HueInfo info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetHueAngleFn(IntPtr h, uint outputId, uint angle);

        [StructLayout(LayoutKind.Sequential)]
        private struct DvcInfo { public uint Version; public uint Current; public uint Min; public uint Max; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DvcInfoEx { public uint Version; public int Current; public int Min; public int Max; public int Default; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HueInfo { public uint Version; public uint Current; public uint Default; }

        private const uint DvcInfoVer = 16 | (1 << 16);
        private const uint DvcInfoExVer = 20 | (1 << 16);
        private const uint HueInfoVer = 12 | (1 << 16);

        private struct Range { public int Min, Max, Default; public bool Ex; }

        private static GetAssociatedHandleFn getHandle;
        private static GetDvcInfoFn getDvc;
        private static SetDvcLevelFn setDvc;
        private static GetDvcInfoExFn getDvcEx;
        private static SetDvcLevelExFn setDvcEx;
        private static GetHueInfoFn getHue;
        private static SetHueAngleFn setHue;

        private static readonly Dictionary<string, IntPtr> handles = new Dictionary<string, IntPtr>();
        private static readonly Dictionary<IntPtr, Range> ranges = new Dictionary<IntPtr, Range>();

        public static bool Available { get; private set; }

        /// <summary>true, если драйвер поддерживает только старый режим (интенсивность 50–100%).</summary>
        public static bool LegacyVibrance { get; private set; }

        public static bool Init()
        {
            try
            {
                var init = Get<InitializeFn>(0x0150E828);
                if (init == null || init() != 0) return false;
                getHandle = Get<GetAssociatedHandleFn>(0x35C29134);
                getDvc = Get<GetDvcInfoFn>(0x4085DE45);
                setDvc = Get<SetDvcLevelFn>(0x172409B4);
                getDvcEx = Get<GetDvcInfoExFn>(0x0E45002D);
                setDvcEx = Get<SetDvcLevelExFn>(0x4A82C2B1);
                getHue = Get<GetHueInfoFn>(0x95B64341);
                setHue = Get<SetHueAngleFn>(0xF5A0F22C);
                Available = getHandle != null && (setDvc != null || setDvcEx != null);
            }
            catch
            {
                Available = false;
            }
            return Available;
        }

        private static T Get<T>(uint id) where T : class
        {
            IntPtr p = Environment.Is64BitProcess ? QueryInterface64(id) : QueryInterface32(id);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        public static void ClearCache()
        {
            handles.Clear();
            ranges.Clear();
        }

        public static IntPtr GetHandle(string device)
        {
            if (!Available) return IntPtr.Zero;
            IntPtr h;
            if (handles.TryGetValue(device, out h)) return h;
            try
            {
                if (getHandle(device, out h) != 0) h = IntPtr.Zero;
            }
            catch { h = IntPtr.Zero; }
            handles[device] = h;
            return h;
        }

        private static bool TryGetRange(IntPtr h, out Range r)
        {
            if (ranges.TryGetValue(h, out r)) return true;
            r = new Range();
            try
            {
                if (getDvcEx != null && setDvcEx != null)
                {
                    var ex = new DvcInfoEx { Version = DvcInfoExVer };
                    if (getDvcEx(h, 0, ref ex) == 0 && ex.Max > ex.Min)
                    {
                        r.Min = ex.Min; r.Max = ex.Max; r.Default = Math.Max(ex.Min, Math.Min(ex.Max, ex.Default)); r.Ex = true;
                        ranges[h] = r;
                        return true;
                    }
                }
                if (getDvc != null && setDvc != null)
                {
                    var info = new DvcInfo { Version = DvcInfoVer };
                    if (getDvc(h, 0, ref info) == 0 && info.Max > info.Min)
                    {
                        // В старом API 0 = 50% (по умолчанию), максимум = 100%
                        r.Min = (int)info.Min; r.Max = (int)info.Max; r.Default = (int)info.Min; r.Ex = false;
                        LegacyVibrance = true;
                        ranges[h] = r;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Только чтение: описание диапазона интенсивности и оттенка для диагностики.</summary>
        public static string Describe(IntPtr h)
        {
            if (h == IntPtr.Zero) return "no NVIDIA handle";
            Range r;
            string s = TryGetRange(h, out r)
                ? string.Format("vibrance: {0} API, min={1} default={2} max={3} current={4}", r.Ex ? "Ex" : "legacy", r.Min, r.Default, r.Max, CurrentLevel(h, r))
                : "vibrance: unavailable";
            try
            {
                var hue = new HueInfo { Version = HueInfoVer };
                if (getHue != null && getHue(h, 0, ref hue) == 0) s += string.Format("; hue: {0}° (default {1}°)", hue.Current, hue.Default);
            }
            catch { }
            return s;
        }

        private static string CurrentLevel(IntPtr h, Range r)
        {
            try
            {
                if (r.Ex)
                {
                    var ex = new DvcInfoEx { Version = DvcInfoExVer };
                    return getDvcEx(h, 0, ref ex) == 0 ? ex.Current.ToString() : "?";
                }
                var info = new DvcInfo { Version = DvcInfoVer };
                return getDvc(h, 0, ref info) == 0 ? info.Current.ToString() : "?";
            }
            catch { return "?"; }
        }

        /// <summary>percent — как в панели NVIDIA: 0..100, по умолчанию 50.</summary>
        public static bool SetVibrance(IntPtr h, int percent)
        {
            if (h == IntPtr.Zero) return false;
            Range r;
            if (!TryGetRange(h, out r)) return false;
            percent = Math.Max(0, Math.Min(100, percent));
            int level = percent <= 50
                ? r.Min + (int)Math.Round((r.Default - r.Min) * percent / 50.0)
                : r.Default + (int)Math.Round((r.Max - r.Default) * (percent - 50) / 50.0);
            try
            {
                if (r.Ex)
                {
                    var ex = new DvcInfoEx { Version = DvcInfoExVer };
                    if (getDvcEx(h, 0, ref ex) != 0) return false;
                    ex.Version = DvcInfoExVer;
                    ex.Current = level;
                    return setDvcEx(h, 0, ref ex) == 0;
                }
                return setDvc(h, 0, level) == 0;
            }
            catch { return false; }
        }

        /// <summary>angle — 0..359 градусов, по умолчанию 0.</summary>
        public static bool SetHue(IntPtr h, int angle)
        {
            if (h == IntPtr.Zero || setHue == null) return false;
            angle = ((angle % 360) + 360) % 360;
            try { return setHue(h, 0, (uint)angle) == 0; }
            catch { return false; }
        }
    }
}

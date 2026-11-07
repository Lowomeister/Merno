// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Runtime.InteropServices;

namespace Merno.Core;

/// <summary>Монитор: имя, разрешение, текущая частота и все частоты, которые Windows разрешает при этом разрешении.</summary>
public sealed class MonitorInfo
{
    public string Device;      // \\.\DISPLAY1
    public string Name;
    public int Width, Height, Now;
    public bool Primary;       // основной монитор Windows (не обязательно первый по номеру)
    public List<int> Rates = new();
    public int Max => Rates.Count > 0 ? Rates.Max() : Now;
    /// <summary>59 и 60 Гц, 119 и 120 Гц — одно и то же: считаем отличием разницу от 5 Гц.</summary>
    public bool CanFaster => Max - Now >= 5;
}

/// <summary>
/// Частота обновления мониторов. Берём только режимы, которые сама Windows считает допустимыми
/// для текущего разрешения (EnumDisplaySettings), и перед сменой спрашиваем у Windows, примет ли она режим (CDS_TEST).
/// </summary>
public static class Display
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, int index, ref DISPLAY_DEVICE dd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);

    const int ENUM_CURRENT_SETTINGS = -1, ATTACHED_TO_DESKTOP = 1, PRIMARY_DEVICE = 4;
    const int DM_DISPLAYFREQUENCY = 0x400000, CDS_UPDATEREGISTRY = 1, CDS_TEST = 2, DISP_CHANGE_SUCCESSFUL = 0;

    static DEVMODE NewMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    /// <summary>Все включённые мониторы.</summary>
    public static List<MonitorInfo> Monitors()
    {
        var res = new List<MonitorInfo>();
        try
        {
            for (int i = 0; i < 16; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & ATTACHED_TO_DESKTOP) == 0) continue;
                var cur = NewMode();
                if (!EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref cur) || cur.dmDisplayFrequency <= 1) continue;
                var m = new MonitorInfo { Device = dd.DeviceName, Width = cur.dmPelsWidth, Height = cur.dmPelsHeight, Now = cur.dmDisplayFrequency, Primary = (dd.StateFlags & PRIMARY_DEVICE) != 0 };
                var mode = NewMode();
                for (int k = 0; k < 5000 && EnumDisplaySettings(dd.DeviceName, k, ref mode); k++, mode = NewMode())
                    if (mode.dmPelsWidth == cur.dmPelsWidth && mode.dmPelsHeight == cur.dmPelsHeight && mode.dmDisplayFrequency > 1 && !m.Rates.Contains(mode.dmDisplayFrequency))
                        m.Rates.Add(mode.dmDisplayFrequency);
                m.Rates.Sort();
                // имя монитора — второй вызов с именем адаптера; «Универсальный монитор PnP» не информативен
                var mon = new DISPLAY_DEVICE { cb = dd.cb };
                bool named = EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0) && !string.IsNullOrWhiteSpace(mon.DeviceString)
                             && !mon.DeviceString.StartsWith("Generic", StringComparison.OrdinalIgnoreCase) && !mon.DeviceString.StartsWith("Универсальный", StringComparison.OrdinalIgnoreCase);
                m.Name = named ? mon.DeviceString : $"Монитор {res.Count + 1}";
                res.Add(m);
            }
        }
        catch (Exception ex) { Log.Skip("Мониторы: " + ex.Message); }
        // основной — первым: о нём человек и спрашивает
        return res.OrderByDescending(m => m.Primary).ToList();
    }

    /// <summary>Ставит частоту. Сначала спрашивает Windows, примет ли она такой режим. null — успех, иначе текст ошибки.</summary>
    public static string SetRate(string device, int hz)
    {
        try
        {
            var dm = NewMode();
            if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm)) return "Не удалось прочитать настройки монитора.";
            dm.dmDisplayFrequency = hz;
            dm.dmFields = DM_DISPLAYFREQUENCY;
            int test = ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (test != DISP_CHANGE_SUCCESSFUL) return "Windows не может включить эту частоту на этом мониторе.";
            int r = ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (r != DISP_CHANGE_SUCCESSFUL) { Log.Warn($"Частота монитора {device}: Windows вернула код {r}"); return "Не удалось сменить частоту."; }
            Log.Ok($"Частота монитора {device}: {hz} Гц");
            return null;
        }
        catch (Exception ex) { Log.Warn("Частота монитора: " + ex.Message); return "Не удалось сменить частоту."; }
    }
}

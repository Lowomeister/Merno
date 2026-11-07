// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Runtime.InteropServices;

namespace Merno.Core;

/// <summary>Вход или выход звука — то, что видно в окне «Звук» Windows: «Динамики», «Микрофон».</summary>
public class AudioEndpoint
{
    public string Id, Name, Adapter;
    /// <summary>InstanceId звукового устройства, к которому относится вход/выход (как в Диспетчере устройств).</summary>
    public string Instance;
    /// <summary>1 — работает, 2 — отключено, 4 — устройства нет, 8 — не подключено (штекер вынут, монитор без звука).</summary>
    public int State;
    public bool Capture;
    public bool Off => State == 2;
}

/// <summary>
/// Входы и выходы звука через Windows Core Audio. Включение и отключение — тем же способом, что кнопка
/// «Отключить» в окне «Звук» (IPolicyConfig.SetEndpointVisibility): устройство остаётся в Диспетчере устройств,
/// в окне «Звук» видно как «Отключено», вернуть можно оттуда же. Перезагрузка не нужна.
/// Устройство целиком (Диспетчер устройств) так не выключаем: тогда оно пропадает из окна «Звук» совсем.
/// </summary>
public static class AudioEndpoints
{
    public static List<AudioEndpoint> List()
    {
        var res = new List<AudioEndpoint>();
        IMMDeviceEnumerator en = null;
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            foreach (int flow in new[] { 0, 1 })   // 0 — вывод, 1 — запись
            {
                if (en.EnumAudioEndpoints(flow, 0xF, out var col) != 0 || col == null) continue;
                try
                {
                    col.GetCount(out int n);
                    for (int i = 0; i < n; i++)
                    {
                        if (col.Item(i, out var dev) != 0 || dev == null) continue;
                        try
                        {
                            dev.GetId(out string id);
                            dev.GetState(out int state);
                            string name = null, adapter = null, inst = null;
                            if (dev.OpenPropertyStore(0, out var ps) == 0 && ps != null)
                            {
                                try { name = Prop(ps, FriendlyName); adapter = Prop(ps, InterfaceName); inst = Prop(ps, DeviceInstance); }
                                finally { Marshal.ReleaseComObject(ps); }
                            }
                            if (!string.IsNullOrEmpty(id))
                                res.Add(new AudioEndpoint { Id = id, State = state, Instance = StripPrefix(inst), Name = name ?? "", Adapter = adapter ?? "", Capture = flow == 1 });
                        }
                        finally { Marshal.ReleaseComObject(dev); }
                    }
                }
                finally { Marshal.ReleaseComObject(col); }
            }
        }
        catch (Exception ex) { Log.Skip("Звук: не удалось получить список входов и выходов: " + ex.Message); }
        finally { if (en != null) Marshal.ReleaseComObject(en); }
        return res;
    }

    /// <summary>Включить или отключить вход/выход, как в окне «Звук».</summary>
    public static bool SetVisible(string id, bool on)
    {
        object o = null;
        try
        {
            o = new PolicyConfigClient();
            int hr = ((IPolicyConfig)o).SetEndpointVisibility(id, on ? 1 : 0);
            if (hr != 0) Log.Skip($"Звук: Windows не дала {(on ? "включить" : "отключить")} вход или выход (код 0x{hr:X8})");
            return hr == 0;
        }
        catch (Exception ex) { Log.Skip("Звук: " + ex.Message); return false; }
        finally { if (o != null) Marshal.ReleaseComObject(o); }
    }

    // ---------------------------------------------------------------- COM
    static PROPERTYKEY FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
    static PROPERTYKEY InterfaceName = new() { fmtid = new Guid("026e516e-b814-414b-83cd-856d6fef4822"), pid = 2 };
    // «{1}.HDAUDIO\FUNC_01&VEN_10DE…» — код устройства; имя устройства у входа английское и с русским списком не совпадает
    static PROPERTYKEY DeviceInstance = new() { fmtid = new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), pid = 2 };

    static string StripPrefix(string s) => s == null ? "" : s.StartsWith("{") && s.IndexOf("}.") is int i and > 0 ? s[(i + 2)..] : s;

    static string Prop(IPropertyStore ps, PROPERTYKEY key)
    {
        var pv = new PROPVARIANT();
        try
        {
            if (ps.GetValue(ref key, out pv) != 0) return null;
            return pv.vt == 31 && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : null;   // VT_LPWSTR
        }
        finally { PropVariantClear(ref pv); }
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore props);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    // Недокументированный интерфейс окна «Звук» (Windows 7–11). Важен только порядок методов: вызываем последний.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}

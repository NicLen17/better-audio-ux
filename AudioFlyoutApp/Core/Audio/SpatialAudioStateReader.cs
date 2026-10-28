using System;
using System.Runtime.InteropServices;

namespace BetterAudio.Core.Audio;

// Read-only documented Core Audio / Windows Sonic interfaces. Never edit registry state.
internal static class SpatialAudioStateReader
{
    public static bool? Read(string id)
    {
        object? enumerator = null;
        IMMDevice? device = null;
        object? client = null;
        try
        {
            enumerator = new DeviceEnumerator();
            Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDevice(id, out device));
            var iid = typeof(ISpatialAudioClient).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 1, IntPtr.Zero, out client));
            Marshal.ThrowExceptionForHR(((ISpatialAudioClient)client).GetMaxDynamicObjectCount(out var count));
            return count > 0;
        }
        catch (COMException) { return null; }
        finally
        {
            if (client is not null) Marshal.ReleaseComObject(client);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IntPtr device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("BBF8E066-AAAA-49BE-9A4D-FD2A858EA27F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpatialAudioClient
    {
        [PreserveSig] int GetStaticObjectPosition(int type, out float x, out float y, out float z);
        [PreserveSig] int GetNativeStaticObjectTypeMask(out uint mask);
        [PreserveSig] int GetMaxDynamicObjectCount(out uint count);
    }
}

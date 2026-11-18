// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SonyHeadphonesFlyout;

internal enum AmbientMode { NoiseCancelling, Ambient, Off }

internal sealed record Headphone(ulong Address, string Name, bool IsConnected)
{
    public string Connection => IsConnected ? "蓝牙已连接" : "已配对 · 蓝牙未连接";
}

internal interface IHeadphones
{
    Task<IReadOnlyList<Headphone>> ListAsync();
    Task<IHeadphoneSession> ConnectAsync(Headphone headphone);
}

internal interface IHeadphoneSession : IDisposable
{
    Task SetAmbientAsync(AmbientMode mode, int level, bool voiceFocus);
    Task SetVptAsync(int preset);
}

internal sealed class HeadphoneException(string message) : Exception(message);

internal sealed class BluetoothHeadphones : IHeadphones
{
    public Task<IReadOnlyList<Headphone>> ListAsync() => Task.Run<IReadOnlyList<Headphone>>(() =>
    {
        var devices = new NativeDevice[32];
        while (true)
        {
            var result = NativeMethods.SonyEnumerate(devices, devices.Length, out int count);
            if (result is 1168 or 259)
                throw new HeadphoneException("找不到蓝牙适配器。请在 Windows 中开启蓝牙后重试。");
            ThrowIfFailed(result, "读取蓝牙设备失败");
            if (count <= devices.Length)
            {
                return devices.Take(count)
                    .Where(device => IsSonyName(device.Name))
                    .Select(device => new Headphone(device.Address, device.Name.Trim(), device.Connected != 0))
                    .OrderByDescending(device => device.IsConnected)
                    .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            devices = new NativeDevice[count];
        }
    });

    public Task<IHeadphoneSession> ConnectAsync(Headphone headphone) => Task.Run<IHeadphoneSession>(() =>
    {
        int result = NativeMethods.SonyConnect(headphone.Address, out var session);
        if (result != 0)
        {
            session.Dispose();
            throw new HeadphoneException(result switch
            {
                10064 or 10108 => $"Sony 设置通道暂不可用。请在其他设备上断开这副耳机，将它重新连到电脑后重试。 ({result})",
                10060 => "连接超时。请确认耳机已开机并靠近电脑。 (10060)",
                _ => $"无法连接耳机控制通道。请检查耳机的蓝牙连接、设备兼容性及其他控制应用。 ({result})"
            });
        }
        return new BluetoothSession(session);
    });

    private static bool IsSonyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var model = name.TrimStart();
        if (model.StartsWith("LE_", StringComparison.OrdinalIgnoreCase)) return false;
        return model.StartsWith("WH-", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("WF-", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("WI-", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("MDR-", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("LinkBuds", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("INZONE", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("Sony ", StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result == 0) return;
        throw new HeadphoneException(result == 10060
            ? "耳机应答超时。请重新连接后再试。"
            : $"{operation}。 ({result})");
    }

    private sealed class BluetoothSession(SessionHandle handle) : IHeadphoneSession
    {
        public Task SetAmbientAsync(AmbientMode mode, int level, bool voiceFocus) => Task.Run(() =>
            ThrowIfFailed(NativeMethods.SonySetAmbient(handle, (int)mode, level, voiceFocus ? 1 : 0), "环境声命令发送失败"));

        public Task SetVptAsync(int preset) => Task.Run(() =>
            ThrowIfFailed(NativeMethods.SonySetVpt(handle, preset), "VPT 命令发送失败"));

        public void Dispose() => handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeDevice
    {
        public ulong Address;
        public int Connected;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    private sealed class SessionHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SessionHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle()
        {
            NativeMethods.SonyClose(handle);
            return true;
        }
    }

    private static class NativeMethods
    {
        private const string Library = "SonyTransport.dll";

        [DllImport(Library, ExactSpelling = true)]
        internal static extern int SonyEnumerate([Out] NativeDevice[] devices, int capacity, out int total);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern int SonyConnect(ulong address, out SessionHandle session);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void SonyClose(IntPtr session);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern int SonySetAmbient(SessionHandle session, int mode, int level, int voice);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern int SonySetVpt(SessionHandle session, int preset);
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace SonyHeadphonesFlyout;

internal partial class DevicesWindow : FlyoutWindow
{
    internal sealed record DeviceList(IReadOnlyList<Headphone> Devices, string? Error = null);

    private readonly Func<Task<DeviceList>> _discover;
    private readonly bool _autoSelect;
    private readonly Action<Headphone> _select;
    private bool _closed;

    public DevicesWindow(Func<Task<DeviceList>> discover, bool autoSelect, Action<Headphone> select)
    {
        _discover = discover;
        _autoSelect = autoSelect;
        _select = select;
        InitializeComponent();
        Closed += (_, _) => _closed = true;
    }

    public async Task DiscoverAsync()
    {
        RefreshButton.IsEnabled = false;
        Subtitle.Text = "正在查找设备…";
        var result = await _discover();
        if (!_closed) ShowDevices(result);
    }

    internal static async Task<DeviceList> ReadDevicesAsync(IHeadphones headphones)
    {
        try { return new(await headphones.ListAsync()); }
        catch (Exception ex) when (ex is HeadphoneException or DllNotFoundException or BadImageFormatException)
        {
            return new([], ex is HeadphoneException ? ex.Message : "蓝牙组件无法加载，请保留完整的应用文件夹。");
        }
    }

    internal void ShowDevices(DeviceList result)
    {
        var devices = result.Devices;
        if (_autoSelect && devices is [{ IsConnected: true } connected])
        {
            _select(connected);
            return;
        }

        Subtitle.Text = devices.Count > 0 ? $"{devices.Count} 台设备" : "蓝牙设备";
        Devices.ItemsSource = devices;
        Devices.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyCard.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = true;
        if (result.Error is { } error)
        {
            ShowEmpty("无法读取设备", error);
        }
        else if (devices.Count == 0)
        {
            ShowEmpty("未找到 Sony 耳机", "请在 Windows 蓝牙设置中配对耳机，完成后点「刷新」。");
        }
    }

    private void ShowEmpty(string title, string description)
    {
        EmptyTitle.Text = title;
        EmptyDescription.Text = description;
        EmptyCard.Visibility = Visibility.Visible;
    }

    private void SelectClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Headphone device }) _select(device);
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) => await DiscoverAsync();

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SonyHeadphoneControl;

internal sealed class FlyoutController : IDisposable
{
    private readonly IHeadphones _headphones;
    private readonly HeadphoneSettingsStore _settings = new();
    private readonly Icon _icon = CreateIcon();
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Font _menuFont = new("Microsoft YaHei UI", 9f);
    private readonly Forms.NotifyIcon _tray;
    private FlyoutWindow? _window;
    private Task<DevicesWindow.DeviceList>? _discovery;
    private Rectangle _workArea;
    private long _lastDeactivation;
    private System.Drawing.Point _deactivationCursor;
    private int _openRequest;
    private bool _opening;

    public FlyoutController(IHeadphones headphones)
    {
        _headphones = headphones;
        _menu.Font = _menuFont;
        _menu.Items.Add("打开 Sony Headphone Control", null, (_, _) => ShowFromTray());
        _menu.Items.Add("退出", null, (_, _) => Application.Current.Shutdown());
        _tray = new Forms.NotifyIcon
        {
            Text = "Sony Headphone Control",
            Icon = _icon,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            if (_window is not null || _opening) CloseCurrent();
            else if (Environment.TickCount64 - _lastDeactivation > 350 || Forms.Cursor.Position != _deactivationCursor) ShowFromTray();
        };
        // Load the native Bluetooth dependency before the first click. Only share an in-progress scan.
        _ = DiscoverDevicesAsync();
    }

    public async void ShowFromTray()
    {
        _workArea = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        int request = ++_openRequest;
        _opening = true;
        var result = await DiscoverDevicesAsync();
        if (request != _openRequest) return;
        _opening = false;

        if (result.Devices is [{ IsConnected: true } connected])
        {
            ShowControls(connected);
            return;
        }
        var window = new DevicesWindow(DiscoverDevicesAsync, autoSelect: true, ShowControls);
        window.ShowDevices(result);
        Open(window);
    }

    private Task<DevicesWindow.DeviceList> DiscoverDevicesAsync() => _discovery is { IsCompleted: false } pending
        ? pending
        : _discovery = DevicesWindow.ReadDevicesAsync(_headphones);

    private void ShowDevicePicker()
    {
        var window = new DevicesWindow(DiscoverDevicesAsync, autoSelect: false, ShowControls);
        Open(window);
        _ = window.DiscoverAsync();
    }

    private void ShowControls(Headphone device)
    {
        var window = new ControlsWindow(_headphones, device, _settings, ShowDevicePicker);
        Open(window);
        window.StartConnection();
    }

    private void Open(FlyoutWindow window)
    {
        CloseCurrent();
        _window = window;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(window, _window)) return;
            _window = null;
            ++_openRequest;
            _opening = false;
        };
        window.Deactivated += DismissOnDeactivation;
        window.ShowNearTray(_workArea);
    }

    private void DismissOnDeactivation(object? sender, EventArgs e)
    {
        if (sender is not FlyoutWindow window || !ReferenceEquals(window, _window) || window.IsClosing) return;
        _lastDeactivation = Environment.TickCount64;
        _deactivationCursor = Forms.Cursor.Position;
        CloseCurrent();
    }

    private void CloseCurrent()
    {
        ++_openRequest;
        _opening = false;
        var previous = _window;
        _window = null;
        if (previous is null) return;
        previous.Deactivated -= DismissOnDeactivation;
        if (!previous.IsClosing) previous.Close();
    }

    public void Dispose()
    {
        CloseCurrent();
        _tray.Dispose();
        _menu.Dispose();
        _menuFont.Dispose();
        _icon.Dispose();
    }

    private static Icon CreateIcon()
    {
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/Headphones.ico")).Stream;
        using var icon = new Icon(resource, Forms.SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}

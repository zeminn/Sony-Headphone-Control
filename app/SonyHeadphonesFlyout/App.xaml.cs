// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Win32;
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Wpf.Ui.Appearance;

namespace SonyHeadphonesFlyout;

public partial class App : Application
{
    private FlyoutController? _controller;
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, "Local\\SonyHeadphonesFlyout.Bluetooth", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        ApplicationThemeManager.ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        _controller = new FlyoutController(new BluetoothHeadphones());
        if (e.Args.Contains("--show")) _controller.ShowFromTray();
    }

    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            Dispatcher.BeginInvoke(() => ApplicationThemeManager.ApplySystemTheme());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

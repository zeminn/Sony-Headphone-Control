// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SonyHeadphoneControl;

internal partial class ControlsWindow : FlyoutWindow
{
    private readonly IHeadphones _headphones;
    private readonly Headphone _device;
    private readonly HeadphoneSettingsStore _settings;
    private readonly Action _back;
    private readonly double _defaultLevel;
    private readonly DispatcherTimer _levelTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly List<SettingChange> _pending = [];
    private IHeadphoneSession? _session;
    private AmbientMode? _ambient;
    private bool _connecting;
    private bool _sending;
    private bool _closed;
    private bool _restoring;

    private const int ArenaPreset = 2;

    private enum SettingGroup { Ambient, Vpt }

    private sealed record SettingChange(SettingGroup Group, Func<IHeadphoneSession, Task> Send, Action Confirm);

    public ControlsWindow(IHeadphones headphones, Headphone device, HeadphoneSettingsStore settings, Action back)
    {
        _headphones = headphones;
        _device = device;
        _settings = settings;
        _back = back;
        InitializeComponent();
        _defaultLevel = Level.Value;
        DeviceName.Text = device.Name;
        DeviceName.ToolTip = device.Name;
        _levelTimer.Tick += (_, _) => SendAmbient();
        _settings.Changed += SettingsChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _settings.Changed -= SettingsChanged;
            _levelTimer.Stop();
            _pending.Clear();
            _session?.Dispose();
            _session = null;
        };
        RestoreSettings();
    }

    public async void StartConnection()
    {
        if (_connecting || _closed || _session is not null) return;
        _connecting = true;
        ConnectionText.Text = "正在连接控制通道…";
        ConnectionCard.Visibility = Visibility.Collapsed;
        try
        {
            var session = await _headphones.ConnectAsync(_device);
            if (_closed) { session.Dispose(); return; }
            _session = session;
            ConnectionText.Text = "控制通道已连接";
        }
        catch (Exception e) when (e is HeadphoneException or DllNotFoundException or BadImageFormatException)
        {
            if (!_closed) HandleConnectionFailure(e);
        }
        finally
        {
            _connecting = false;
            if (!_closed) UpdateEnabled();
        }
    }

    private async void QueueChange(SettingGroup group, Func<IHeadphoneSession, Task> send, Action confirm)
    {
        if (_closed || _session is not { } session) return;

        // Serialize RFCOMM commands and keep the latest pending value for each control group.
        _pending.RemoveAll(change => change.Group == group);
        _pending.Add(new SettingChange(group, send, confirm));
        if (_sending) return;

        _sending = true;
        try
        {
            while (!_closed && _pending.Count > 0)
            {
                var change = _pending[0];
                _pending.RemoveAt(0);
                try
                {
                    await change.Send(session);
                }
                catch (Exception e) when (e is HeadphoneException or ObjectDisposedException)
                {
                    if (!_closed) HandleConnectionFailure(e);
                    break;
                }

                change.Confirm();
            }
        }
        finally
        {
            _sending = false;
        }
    }

    private void HandleConnectionFailure(Exception error)
    {
        _session?.Dispose();
        _session = null;
        _levelTimer.Stop();
        _pending.Clear();
        RestoreSettings();
        ConnectionText.Text = "控制通道未连接";
        ConnectionDetails.Text = error is HeadphoneException ? error.Message : "蓝牙组件无法使用，请检查连接与应用文件。";
        ConnectionCard.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void SettingsChanged(ulong address)
    {
        if (address == _device.Address && !_sending && !_levelTimer.IsEnabled) RestoreSettings();
    }

    private void RestoreSettings()
    {
        var saved = _settings.Get(_device.Address);
        _restoring = true;
        try
        {
            _ambient = saved.Ambient?.Mode;
            foreach (RadioButton choice in AmbientModes.Children)
                choice.IsChecked = _ambient.HasValue && Equals(choice.Tag, ((int)_ambient.Value).ToString());

            AmbientDetails.Visibility = _ambient == AmbientMode.Ambient ? Visibility.Visible : Visibility.Collapsed;
            Level.Value = saved.Ambient?.Level ?? _defaultLevel;
            VoiceFocus.IsChecked = saved.Ambient?.VoiceFocus ?? false;
            DisplayVpt(saved.Vpt);
        }
        finally
        {
            _restoring = false;
        }
        UpdateLevel();
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        AmbientInput.IsEnabled = VptToggle.IsEnabled = _session is not null;
        VptInput.IsEnabled = _session is not null && VptToggle.IsChecked == true;
    }

    private void AmbientSelected(object sender, RoutedEventArgs e)
    {
        if (_restoring || sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int mode)) return;
        _ambient = (AmbientMode)mode;
        AmbientDetails.Visibility = _ambient == AmbientMode.Ambient ? Visibility.Visible : Visibility.Collapsed;
        SendAmbient();
    }

    private void VptSelected(object sender, RoutedEventArgs e)
    {
        if (_restoring || VptToggle.IsChecked != true || sender is not RadioButton { Tag: string tag }
            || !int.TryParse(tag, out int preset)) return;
        SendVpt(preset);
    }

    private void VptActivationChanged(object sender, RoutedEventArgs e)
    {
        if (_restoring || _session is null) return;
        int preset = VptToggle.IsChecked == true ? ArenaPreset : 0;
        DisplayVpt(preset);
        UpdateEnabled();
        SendVpt(preset);
    }

    private void DisplayVpt(int? preset)
    {
        bool wasRestoring = _restoring;
        _restoring = true;
        try
        {
            VptToggle.IsChecked = preset is > 0;
            foreach (RadioButton choice in VptInput.Children)
                choice.IsChecked = preset.HasValue && Equals(choice.Tag, preset.Value.ToString());
        }
        finally
        {
            _restoring = wasRestoring;
        }
    }

    private void SendVpt(int preset) => QueueChange(SettingGroup.Vpt,
        session => session.SetVptAsync(preset), () => _settings.ConfirmVpt(_device.Address, preset));

    private void UpdateLevel()
    {
        if (LevelText is null || VoiceFocus is null) return;
        LevelText.Text = $"{Level.Value:0} / 19";
        VoiceFocus.IsEnabled = Level.Value > 2;
    }

    private void LevelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateLevel();
        if (_restoring || _session is null || _ambient != AmbientMode.Ambient) return;
        _pending.RemoveAll(change => change.Group == SettingGroup.Ambient);
        if (!_levelTimer.IsEnabled) _levelTimer.Start();
    }

    private void VoiceFocusChanged(object sender, RoutedEventArgs e)
    {
        if (!_restoring && _ambient == AmbientMode.Ambient) SendAmbient();
    }

    private void SendAmbient()
    {
        _levelTimer.Stop();
        if (_session is null || _ambient is not { } mode) return;
        int level = (int)Level.Value;
        bool focus = level > 2 && VoiceFocus.IsChecked == true;
        var setting = new AmbientSetting(mode, level, focus);
        QueueChange(SettingGroup.Ambient, session => session.SetAmbientAsync(mode, level, focus),
            () => _settings.ConfirmAmbient(_device.Address, setting));
    }

    private void RetryClick(object sender, RoutedEventArgs e) => StartConnection();

    private void BackClick(object sender, RoutedEventArgs e) => _back();

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

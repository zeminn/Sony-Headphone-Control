using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace SonyHeadphoneControl;

internal static class UiCheck
{
    private sealed class TestApp : App
    {
        protected override void OnStartup(StartupEventArgs e) { }
    }

    [STAThread]
    private static int Main()
    {
        var app = new TestApp();
        app.InitializeComponent();
        ApplicationThemeManager.ApplySystemTheme();
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        int status = 1;
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await CloseDuringDeactivation();
                SettingsWriteRecovery();
                await CloseDuringDiscovery();
                await DeviceList();
                await Commands();
                await CommandBurst();
                await ClosedConnectionCleanup();
                await FirstAmbientFailure();
                await LateAcknowledgement();
                await CheckStablePresentation();
                CheckBackdrop();
                await CheckTypographyAndGlow();
                status = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { dispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run();
        return status;
    }

    private static async Task CloseDuringDeactivation()
    {
        var headset = new Headphone(7, "Closing lifecycle headset", false);
        var backend = new RecordingHeadphones(headset);
        using var controller = new FlyoutController(backend);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var currentWindow = typeof(FlyoutController).GetField("_window", flags)!;
        var open = typeof(FlyoutController).GetMethod("Open", flags)!;
        var deactivate = typeof(Window).GetMethod("OnDeactivated", flags)!;
        typeof(FlyoutController).GetField("_workArea", flags)!.SetValue(controller,
            new System.Drawing.Rectangle(-20000, -15000, 1080, 800));

        for (int index = 0; index < 30; ++index)
        {
            FlyoutWindow window = index % 2 == 0
                ? new DevicesWindow(() => Task.FromResult(new DevicesWindow.DeviceList([])), false, _ => { })
                : new ControlsWindow(backend, headset, new HeadphoneSettingsStore(TestSettingsPath()), () => { });
            window.ShowActivated = false;
            open.Invoke(controller, [window]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CancelEventHandler loseFocusWhileClosing = (_, _) => deactivate.Invoke(window, [EventArgs.Empty]);
            window.Closing += loseFocusWhileClosing;
            try
            {
                switch (index % 3)
                {
                    case 0:
                        window.Close();
                        break;
                    case 1:
                        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent
                        });
                        break;
                    default:
                        deactivate.Invoke(window, [EventArgs.Empty]);
                        break;
                }
                Expect(!window.IsVisible && currentWindow.GetValue(controller) is null,
                    "Closing a popup left a stale controller window");
                Expect(!Dispatcher.CurrentDispatcher.HasShutdownStarted && Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown,
                    "Closing a popup shut down the resident tray application");
            }
            finally
            {
                window.Closing -= loseFocusWhileClosing;
                if (window.IsVisible) window.Close();
            }
        }
        var canceled = new DevicesWindow(() => Task.FromResult(new DevicesWindow.DeviceList([])), false, _ => { })
        {
            ShowActivated = false
        };
        int attempts = 0;
        CancelEventHandler cancelFirstClose = (_, e) => e.Cancel = ++attempts == 1;
        canceled.Closing += cancelFirstClose;
        open.Invoke(controller, [canceled]);
        try
        {
            canceled.Close();
            Expect(canceled.IsVisible && !canceled.IsClosing, "A canceled close left the popup in its closing state");
            deactivate.Invoke(canceled, [EventArgs.Empty]);
            Expect(!canceled.IsVisible && currentWindow.GetValue(controller) is null,
                "The popup could not dismiss after its first close was canceled");
        }
        finally
        {
            canceled.Closing -= cancelFirstClose;
            if (canceled.IsVisible) canceled.Close();
        }
        Console.WriteLine("Verified 30 Close/Escape/deactivation cycles and canceled-close recovery preserve the resident tray application.");
    }

    private static void SettingsWriteRecovery()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sony-write-recovery-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        string temporary = path + ".tmp";
        Directory.CreateDirectory(temporary);
        try
        {
            var settings = new HeadphoneSettingsStore(path);
            int changes = 0;
            settings.Changed += _ => ++changes;
            var choice = new AmbientSetting(AmbientMode.Ambient, 17, true);
            settings.ConfirmAmbient(1, choice);
            Expect(settings.Get(1).Ambient == choice, "A disk error discarded a confirmed setting from memory");
            Directory.Delete(temporary);
            settings.ConfirmAmbient(1, choice);
            Expect(File.Exists(path) && new HeadphoneSettingsStore(path).Get(1).Ambient == choice,
                "Repeating a confirmed setting did not retry a failed disk write");
            Expect(changes == 1, "Retrying persistence repeated a settings-change notification");
            Console.WriteLine("Verified settings persist after a temporary disk-write failure recovers.");
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(temporary)) Directory.Delete(temporary);
            else File.Delete(temporary);
            Directory.Delete(directory);
        }
    }

    private static async Task CloseDuringDiscovery()
    {
        var backend = new DelayedDiscoveryHeadphones();
        using var controller = new FlyoutController(backend);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var windowField = typeof(FlyoutController).GetField("_window", flags)!;
        typeof(FlyoutController).GetField("_workArea", flags)!.SetValue(controller,
            new System.Drawing.Rectangle(-20000, -15000, 1080, 800));
        var window = new DevicesWindow(() => Task.FromResult(new DevicesWindow.DeviceList([])), false, _ => { })
        {
            ShowActivated = false
        };
        typeof(FlyoutController).GetMethod("Open", flags)!.Invoke(controller, [window]);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        controller.ShowFromTray();
        typeof(FlyoutController).GetField("_workArea", flags)!.SetValue(controller,
            new System.Drawing.Rectangle(-20000, -15000, 1080, 800));
        window.Close();
        backend.Discovery.SetResult([]);
        await Task.Delay(80);
        Expect(windowField.GetValue(controller) is null, "A delayed scan reopened a window that the user closed");
        Expect(backend.ListCalls == 1, "Concurrent opening requests repeated device discovery");
        Console.WriteLine("Verified closing the popup cancels a pending reopen and shares its in-progress scan.");
    }

    private static async Task DeviceList()
    {
        var bluetooth = new BluetoothHeadphones();
        var devices = await bluetooth.ListAsync();
        Console.WriteLine("Windows Sony devices: " + string.Join(", ", devices.Select(device => $"{device.Name} [{device.Connection}]")));
        Headphone? selected = null;
        var window = new DevicesWindow(() => DevicesWindow.ReadDevicesAsync(bluetooth), true, device => selected = device);
        await window.DiscoverAsync();
        if (selected is null)
        {
            var rows = (ItemsControl)window.FindName("Devices");
            Expect(rows.Items.Count == devices.Count, "Actual window list does not match known Sony devices");
        }
        else Expect(devices.Count == 1 && selected.IsConnected, "Normal list skipped an offline or multi-device list");
        window.Close();

        var headset = new Headphone(1, "Test headset", false);
        var backend = new RecordingHeadphones(headset);
        var offline = new DevicesWindow(() => DevicesWindow.ReadDevicesAsync(backend), true,
            _ => throw new Exception("Paired offline device was opened automatically"));
        await offline.DiscoverAsync();
        var listed = (ItemsControl)offline.FindName("Devices");
        Expect(listed.Items.Count == 1 && Equals(listed.Items[0], headset), "Paired offline device is missing from the window");
        var refresh = (FrameworkElement)offline.FindName("RefreshButton");
        Expect(refresh.Parent is Grid && Grid.GetColumn(refresh) == 2 && Grid.GetRow(refresh) == 0,
            "Refresh is not in the title row");
        offline.Close();
        var onlineBackend = new RecordingHeadphones(headset with { IsConnected = true });
        var online = new DevicesWindow(() => DevicesWindow.ReadDevicesAsync(onlineBackend), true, device => selected = device);
        selected = null;
        await online.DiscoverAsync();
        Expect(selected?.IsConnected == true, "Only connected Sony device did not open automatically");
        online.Close();
        Console.WriteLine("Verified real list binding and offline/online single-device navigation.");
    }

    private static async Task Commands()
    {
        var headset = new Headphone(1, "Test headset", true);
        var backend = new RecordingHeadphones(headset);
        string path = TestSettingsPath();
        var settings = new HeadphoneSettingsStore(path);
        var window = new ControlsWindow(backend, headset, settings, () => { });
        window.StartConnection();
        var modes = (UniformGrid)window.FindName("AmbientModes");
        var vpt = (UniformGrid)window.FindName("VptInput");
        var level = (Slider)window.FindName("Level");
        var voice = (CheckBox)window.FindName("VoiceFocus");
        var toggle = (ToggleButton)window.FindName("VptToggle");
        Expect(level.Minimum == 0 && level.Maximum == 19, "Incorrect slider range");
        Expect(backend.Session.Calls.Count == 0, "Connecting wrote an initial setting");
        ((RadioButton)modes.Children[1]).IsChecked = true;
        Expect(backend.Session.Calls.SequenceEqual(["Ambient:12:False"]), "Choosing the ambient mode did not send immediately");
        level.Value = 0;
        level.Value = 1;
        level.Value = 19;
        Expect(!vpt.IsEnabled, "The VPT presets are active before switching VPT on");
        toggle.IsChecked = true;
        Expect(((RadioButton)vpt.Children[1]).IsChecked == true, "Activation did not select Arena");
        ((RadioButton)vpt.Children[2]).IsChecked = true;
        await Task.Delay(240);
        Expect(backend.Session.Calls.Count == 1, "Commands ran concurrently");
        backend.Session.FirstAck.SetResult();
        await Until(() => backend.Session.Calls.Count >= 3);
        Expect(backend.Session.Calls.SequenceEqual(["Ambient:12:False", "vpt:3", "Ambient:19:False"]), "Stale pending settings were sent");
        voice.IsChecked = true;
        Expect(backend.Session.Calls.Last() == "Ambient:19:True", "Voice selection did not send immediately");
        ((RadioButton)vpt.Children[3]).IsChecked = true;
        Expect(backend.Session.Calls.Last() == "vpt:4", "Selecting a VPT preset did not send immediately");
        level.Value = 0;
        await Until(() => backend.Session.Calls.Last() == "Ambient:0:False");
        Expect(!voice.IsEnabled, "Voice focus stayed enabled at level 0");
        toggle.IsChecked = false;
        Expect(backend.Session.Calls.Last() == "vpt:0" && !vpt.IsEnabled, "VPT switch did not send Off");
        toggle.IsChecked = true;
        Expect(backend.Session.Calls.Last() == "vpt:2", "VPT switch did not activate Arena exactly once");
        ((RadioButton)modes.Children[0]).IsChecked = true;
        Expect(backend.Session.MaxInFlight == 1, "Command send overlap");
        window.Close();

        int calls = backend.Session.Calls.Count;
        var restarted = new HeadphoneSettingsStore(path);
        var reopened = new ControlsWindow(backend, headset, restarted, () => { });
        reopened.StartConnection();
        modes = (UniformGrid)reopened.FindName("AmbientModes");
        vpt = (UniformGrid)reopened.FindName("VptInput");
        toggle = (ToggleButton)reopened.FindName("VptToggle");
        Expect(((RadioButton)modes.Children[0]).IsChecked == true && toggle.IsChecked == true &&
            ((RadioButton)vpt.Children[1]).IsChecked == true, "Reopening did not restore Noise cancelling and Arena");
        Expect(backend.Session.Calls.Count == calls, "Restoring settings sent an unsolicited command");
        Expect(restarted.Get(99) == new HeadphoneSettings(), "Confirmed settings leaked to another Bluetooth device");

        backend.Session.FailNext = true;
        ((RadioButton)vpt.Children[3]).IsChecked = true;
        Expect(restarted.Get(headset.Address).Vpt == 2 && ((RadioButton)vpt.Children[1]).IsChecked == true,
            "A failed command displaced the last confirmed preset");
        reopened.Close();
        File.Delete(path);
        Console.WriteLine("Verified commands and coalescing, VPT Arena/Off, persisted highlights after restart, and rollback on send failure.");
    }

    private static async Task FirstAmbientFailure()
    {
        var headset = new Headphone(4, "Failed ambient test headset", true);
        var backend = new RecordingHeadphones(headset);
        string path = TestSettingsPath();
        var settings = new HeadphoneSettingsStore(path);
        var window = new ControlsWindow(backend, headset, settings, () => { });
        window.StartConnection();
        var input = (UIElement)window.FindName("AmbientInput");
        var mode = (RadioButton)((UniformGrid)window.FindName("AmbientModes")).Children[1];
        var level = (Slider)window.FindName("Level");
        var voice = (CheckBox)window.FindName("VoiceFocus");
        double defaultLevel = level.Value;

        mode.IsChecked = true;
        level.Value = 17;
        voice.IsChecked = true;
        backend.Session.FailNext = true;
        backend.Session.FirstAck.SetResult();
        await Until(() => !input.IsEnabled);

        Expect(settings.Get(headset.Address).Ambient is null && mode.IsChecked == false &&
            level.Value == defaultLevel && voice.IsChecked == false, "First ambient failure did not reset unsaved inputs");
        window.StartConnection();
        mode.IsChecked = true;
        Expect(backend.Session.Calls.SequenceEqual([$"Ambient:{defaultLevel}:False", $"Ambient:{defaultLevel}:False"]),
            "Retry sent a stale ambient value");
        window.Close();
        File.Delete(path);
        Console.WriteLine("Verified a failed first ambient command restores defaults before retry.");
    }

    private static async Task CommandBurst()
    {
        var headset = new Headphone(5, "Burst test headset", true);
        var backend = new RecordingHeadphones(headset);
        string path = TestSettingsPath();
        var settings = new HeadphoneSettingsStore(path);
        var window = new ControlsWindow(backend, headset, settings, () => { });
        try
        {
            window.StartConnection();
            var modes = (UniformGrid)window.FindName("AmbientModes");
            var level = (Slider)window.FindName("Level");
            var toggle = (ToggleButton)window.FindName("VptToggle");
            ((RadioButton)modes.Children[1]).IsChecked = true;
            var stopwatch = Stopwatch.StartNew();
            for (int index = 0; index < 500; ++index)
            {
                level.Value = index % 20;
                toggle.IsChecked = index % 2 == 0;
            }
            stopwatch.Stop();
            await Task.Delay(180);
            var pending = (System.Collections.ICollection)typeof(ControlsWindow)
                .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Expect(pending.Count == 2 && backend.Session.Calls.Count == 1,
                "A burst grew the pending queue or sent commands before the first ACK");
            backend.Session.FirstAck.SetResult();
            await Until(() => settings.Get(headset.Address) is { Ambient.Level: 19, Vpt: 0 });
            Expect(backend.Session.Calls.SequenceEqual(["Ambient:12:False", "vpt:0", "Ambient:19:False"]) &&
                backend.Session.MaxInFlight == 1, "The burst sent stale values or overlapping commands");
            Console.WriteLine($"Burst check: 1,000 input changes in {stopwatch.ElapsedMilliseconds}ms, three commands, at most two pending changes.");
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }

    private static async Task ClosedConnectionCleanup()
    {
        var headset = new Headphone(6, "Delayed connection cleanup headset", true);
        var backend = new RecordingHeadphones(headset) { DelayConnection = true };
        var settings = new HeadphoneSettingsStore(TestSettingsPath());
        for (int index = 0; index < 25; ++index)
        {
            var window = new ControlsWindow(backend, headset, settings, () => { });
            window.StartConnection();
            window.Close();
        }
        backend.Connection.SetResult(backend.Session);
        await Until(() => backend.Session.DisposeCount == 25);
        Expect(backend.Session.Calls.Count == 0, "A closed connection attempt changed a headphone setting");
        Console.WriteLine("Verified 25 late connection results are released after their windows close.");
    }

    private static async Task LateAcknowledgement()
    {
        var headset = new Headphone(2, "Late ACK headset", true);
        var backend = new RecordingHeadphones(headset);
        var path = TestSettingsPath();
        var settings = new HeadphoneSettingsStore(path);
        var window = new ControlsWindow(backend, headset, settings, () => { });
        window.StartConnection();
        ((RadioButton)((UniformGrid)window.FindName("AmbientModes")).Children[0]).IsChecked = true;
        Expect(settings.Get(headset.Address).Ambient is null, "Unacknowledged setting was saved");
        window.Close();
        var reopened = new ControlsWindow(backend, headset, settings, () => { });
        reopened.StartConnection();
        backend.Session.FirstAck.SetResult();
        await Until(() => settings.Get(headset.Address).Ambient?.Mode == AmbientMode.NoiseCancelling);
        Expect(((RadioButton)((UniformGrid)reopened.FindName("AmbientModes")).Children[0]).IsChecked == true,
            "The new window missed the old window's late acknowledgement");
        reopened.Close();
        File.Delete(path);
        Console.WriteLine("Verified an acknowledgement arriving after the original window closed.");
    }

    private static string TestSettingsPath() => Path.Combine(Path.GetTempPath(), $"sony-ui-check-{Guid.NewGuid()}.json");

    private static async Task CheckStablePresentation()
    {
        var headset = new Headphone(3, "Presentation test headset", true);
        var backend = new RecordingHeadphones(headset) { DelayConnection = true };
        var window = new ControlsWindow(backend, headset, new HeadphoneSettingsStore(TestSettingsPath()), () => { })
        {
            ShowActivated = false
        };
        var offscreen = new System.Drawing.Rectangle(-20000, -15000, 1080, 800);
        bool wasCloaked = false;
        window.IsVisibleChanged += (_, _) => { if (window.IsVisible) wasCloaked = IsCloaked(window); };
        window.ShowNearTray(offscreen);
        window.StartConnection();
        await Until(() => !IsCloaked(window));
        Expect(wasCloaked, "The controls window began showing before DWM hid it for layout");
        int connectingHeight = WindowHeight(window);
        backend.Connection.SetResult(backend.Session);
        await Until(() => ((TextBlock)window.FindName("ConnectionText")).Text == "控制通道已连接");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        int connectedHeight = WindowHeight(window);
        Expect(connectingHeight > 0 && connectingHeight < 650 && connectingHeight == connectedHeight,
            $"The popup height is incorrect or changed between connecting and connected: {connectingHeight}, {connectedHeight}");
        Expect(window.FindName("AmbientStatus") is null && window.FindName("VptStatus") is null,
            "Routine status text still occupies the controls layout");
        window.Close();

        var list = new DevicesWindow(() => DevicesWindow.ReadDevicesAsync(backend), false, _ => { }) { ShowActivated = false };
        list.ShowDevices(new DevicesWindow.DeviceList([headset with { IsConnected = false }]));
        bool listWasCloaked = false;
        list.IsVisibleChanged += (_, _) => { if (list.IsVisible) listWasCloaked = IsCloaked(list); };
        list.ShowNearTray(offscreen);
        await Until(() => !IsCloaked(list));
        int listHeight = WindowHeight(list);
        Expect(listWasCloaked && listHeight > 0 && listHeight < 300,
            $"Device list presentation was invalid: cloaked={listWasCloaked}, height={listHeight}");
        list.Close();
        Console.WriteLine($"Verified stable height at first visibility and after layout/connection: controls {connectedHeight}px, devices {listHeight}px.");
    }

    private static int WindowHeight(Window window)
    {
        Expect(GetWindowRect(new WindowInteropHelper(window).Handle, out var rect), "Could not measure popup rectangle");
        return rect.Bottom - rect.Top;
    }

    private static bool IsCloaked(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Expect(DwmGetWindowAttribute(hwnd, 14, out int flags, sizeof(int)) == 0, "Could not read DWM cloak state");
        return (flags & 1) != 0;
    }

    private static async Task CheckTypographyAndGlow()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/Fonts/NotoSansSC-Regular.otf"));
        Expect(resource is not null, "Simplified Chinese font was not packaged as a WPF resource");
        resource?.Stream.Dispose();
        var family = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/#Noto Sans SC");
        var faces = family.GetTypefaces().ToList();
        Expect(faces.Count >= 2, "Regular and Medium Simplified Chinese font faces were not resolved");
        foreach (var character in "控制环境声连接耳机刷新竞技场")
            Expect(faces.Any(face => face.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey(character)),
                $"Missing packaged glyph for {character}");

        var headset = new Headphone(3, "Typography test headset", true);
        var backend = new RecordingHeadphones(headset);
        var window = new ControlsWindow(backend, headset, new HeadphoneSettingsStore(TestSettingsPath()), () => { })
        {
            ShowActivated = false,
            Left = -20000,
            Top = -15000
        };
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var text = (TextBlock)window.FindName("ConnectionText");
        var retry = (Control)window.FindName("RetryButton");
        Expect(text.Language.IetfLanguageTag.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) && text.FontFamily.Source.Contains("Noto Sans SC"),
            $"Text style did not inherit zh-CN/Noto Sans SC: {text.Language}, {text.FontFamily.Source}");
        Expect(retry.Language.IetfLanguageTag.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) && retry.FontFamily.Source.Contains("Noto Sans SC"),
            $"Button font family did not inherit: {retry.Language}, {retry.FontFamily.Source}");
        var resolvedFace = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        Expect(resolvedFace.TryGetGlyphTypeface(out var resolvedGlyphs) && resolvedGlyphs.FontUri.ToString().Contains("NotoSansSC", StringComparison.OrdinalIgnoreCase),
            $"The flyout font resolves to {resolvedGlyphs?.FontUri}");

        var surface = (ContentControl)window.Content;
        var fallback = (Border)surface.Template.FindName("AccentGlow", surface);
        var artwork = (Border)surface.Template.FindName("ArtworkGlow", surface);
        Expect(fallback.Visibility == Visibility.Visible && artwork.Visibility == Visibility.Collapsed, "Missing-cover fallback did not activate");
        int before = WindowHeight(window);
        byte[] pixels = Enumerable.Repeat(new byte[] { 140, 70, 210, 255 }, 16 * 16).SelectMany(pixel => pixel).ToArray();
        var fixture = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
        var key = (DependencyPropertyKey)typeof(FlyoutWindow).GetField("CoverArtworkPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        window.SetValue(key, fixture);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Expect(fallback.Visibility == Visibility.Collapsed && artwork.Visibility == Visibility.Visible &&
            ReferenceEquals(((ImageBrush)artwork.Background).ImageSource, fixture), "Artwork did not replace the theme-color fallback");
        Expect(before == WindowHeight(window), "Loading a cover changed floating-window geometry");
        window.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var current = await Task.Run(() => MediaCover.ReadAsync(timeout.Token), timeout.Token);
            Console.WriteLine(current is not null ? "Windows media session provided a valid cover." : "Windows media session had no cover; verified fallback.");
        }
        catch (OperationCanceledException) { Console.WriteLine("Media cover request timed out; verified fallback."); }
        Console.WriteLine("Verified Simplified Chinese font faces, zh-CN inheritance, artwork and fallback layers, and stable geometry.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WinRect rect);

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Expect(condition(), "Timed out waiting for update");
    }

    private static void CheckBackdrop()
    {
        var headset = new Headphone(1, "Test headset", true);
        var backend = new RecordingHeadphones(headset);
        foreach (var window in new FlyoutWindow[] {
            new DevicesWindow(() => DevicesWindow.ReadDevicesAsync(backend), false, _ => { }),
            new ControlsWindow(backend, headset, new HeadphoneSettingsStore(TestSettingsPath()), () => { })
        })
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var status = DwmGetWindowAttribute(hwnd, 38, out int backdrop, sizeof(int));
            Expect(status == 0 && backdrop == 3, "Windows did not apply the acrylic backdrop");
            Expect(window.CustomWindowChrome.GlassFrameThickness.Left == -1, "Acrylic does not extend across the client area");
            window.Close();
        }
        Console.WriteLine("Both flyouts use the Windows desktop acrylic backdrop.");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, uint size);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class DelayedDiscoveryHeadphones : IHeadphones
    {
        public int ListCalls { get; private set; }
        public TaskCompletionSource<IReadOnlyList<Headphone>> Discovery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Headphone>> ListAsync()
        {
            ++ListCalls;
            return Discovery.Task;
        }

        public Task<IHeadphoneSession> ConnectAsync(Headphone device) =>
            throw new InvalidOperationException("A discovery-only test attempted to connect a device");
    }

    private sealed class RecordingHeadphones(Headphone headset) : IHeadphones
    {
        public RecordingSession Session { get; } = new();
        public bool DelayConnection { get; init; }
        public TaskCompletionSource<IHeadphoneSession> Connection { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<Headphone>> ListAsync() => Task.FromResult<IReadOnlyList<Headphone>>([headset]);
        public Task<IHeadphoneSession> ConnectAsync(Headphone device) => DelayConnection ? Connection.Task : Task.FromResult<IHeadphoneSession>(Session);
    }

    private sealed class RecordingSession : IHeadphoneSession
    {
        private int _inFlight;
        public int MaxInFlight { get; private set; }
        public List<string> Calls { get; } = [];
        public TaskCompletionSource FirstAck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailNext { get; set; }
        public int DisposeCount { get; private set; }

        public Task SetAmbientAsync(AmbientMode mode, int level, bool voiceFocus) => Record($"{mode}:{level}:{voiceFocus}");
        public Task SetVptAsync(int preset) => Record($"vpt:{preset}");
        public void Dispose() => ++DisposeCount;

        private async Task Record(string command)
        {
            Calls.Add(command);
            MaxInFlight = Math.Max(++_inFlight, MaxInFlight);
            if (Calls.Count == 1) await FirstAck.Task;
            --_inFlight;
            if (FailNext)
            {
                FailNext = false;
                throw new HeadphoneException("Simulated command failure");
            }
        }
    }
}

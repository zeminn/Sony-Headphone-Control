// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using MicaWPF.Controls;
using MicaWPF.Core.Enums;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace SonyHeadphonesFlyout;

public class FlyoutWindow : MicaWindow
{
    private static readonly DependencyPropertyKey CoverArtworkPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CoverArtwork), typeof(ImageSource), typeof(FlyoutWindow), new PropertyMetadata(null));
    public static readonly DependencyProperty CoverArtworkProperty = CoverArtworkPropertyKey.DependencyProperty;
    public ImageSource? CoverArtwork => (ImageSource?)GetValue(CoverArtworkProperty);

    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint DwmCloak = 13;
    private Rectangle? _workArea;
    private IntPtr _hwnd;
    private CancellationTokenSource? _coverLoad;
    private bool _cloaked;

    private double DpiScale => GetDpiForWindow(_hwnd) is > 0 and var dpi ? dpi / 96.0 : 1;

    public FlyoutWindow()
    {
        Width = 372;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SystemBackdropType = BackdropType.Acrylic;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Language = XmlLanguage.GetLanguage("zh-CN");
        SetResourceReference(FontFamilyProperty, "FlyoutFont");
        UseLayoutRounding = true;
        SizeChanged += (_, _) =>
        {
            if (IsVisible) Position();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CustomWindowChrome.CaptionHeight = 0;
        CustomWindowChrome.UseAeroCaptionButtons = false;
        _hwnd = new WindowInteropHelper(this).Handle;
        int rounded = 2;
        DwmSetWindowAttribute(_hwnd, 33, ref rounded, sizeof(int));
    }

    internal void ShowNearTray(Rectangle workArea)
    {
        _workArea = workArea;
        // Place the HWND on its target monitor before sizing in device-independent units.
        new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(_hwnd, IntPtr.Zero, workArea.Right - 100, workArea.Bottom - 100, 0, 0, NoSize | NoZOrder | NoActivate);
        FitWorkingArea();
        // Acrylic is drawn by DWM outside WPF's opacity. Hide the whole native window until layout is rendered.
        _cloaked = SetCloaked(true);
        ContentRendered += PresentFirstFrame;
        Show();
    }

    private void PresentFirstFrame(object? sender, EventArgs e)
    {
        ContentRendered -= PresentFirstFrame;
        Position();
        if (_cloaked) SetCloaked(false);
        if (ShowActivated && !IsActive) Activate();
        if (!SystemParameters.HighContrast) _ = LoadCoverAsync();
    }

    private async Task LoadCoverAsync()
    {
        using var load = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        _coverLoad = load;
        try
        {
            var cover = await Task.Run(() => MediaCover.ReadAsync(load.Token), load.Token);
            if (!load.IsCancellationRequested && IsVisible && cover is not null)
                SetValue(CoverArtworkPropertyKey, cover);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        finally { _coverLoad = null; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _coverLoad?.Cancel();
        base.OnClosed(e);
    }

    private bool SetCloaked(bool cloak)
    {
        int value = cloak ? 1 : 0;
        return DwmSetWindowAttribute(_hwnd, DwmCloak, ref value, sizeof(int)) == 0;
    }

    private void FitWorkingArea()
    {
        if (_workArea is not Rectangle area) return;
        var scale = DpiScale;
        Width = Math.Min(372, Math.Max(1, area.Width / scale - 24));
        MaxHeight = Math.Min(760, Math.Max(1, area.Height / scale - 24));
    }

    private void Position()
    {
        if (_hwnd == IntPtr.Zero || _workArea is not Rectangle area || ActualWidth <= 0 || ActualHeight <= 0) return;
        var scale = DpiScale;
        int gap = (int)Math.Round(12 * scale);
        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);
        int left = Math.Max(area.Left, area.Right - width - gap);
        int top = Math.Max(area.Top, area.Bottom - height - gap);
        SetWindowPos(_hwnd, IntPtr.Zero, left, top, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, uint size);
}

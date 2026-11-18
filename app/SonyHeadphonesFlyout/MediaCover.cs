// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace SonyHeadphonesFlyout;

internal static class MediaCover
{
    public static async Task<BitmapSource?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                .AsTask(cancellationToken).ConfigureAwait(false);
            var session = media.GetCurrentSession();
            if (session is null) return null;

            var properties = await session.TryGetMediaPropertiesAsync()
                .AsTask(cancellationToken).ConfigureAwait(false);
            if (properties?.Thumbnail is not { } thumbnail) return null;

            using var source = await thumbnail.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (source.Size == 0) return null;
            using var stream = source.AsStreamForRead();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 256;
            image.StreamSource = stream;
            image.EndInit();

            int side = Math.Min(image.PixelWidth, image.PixelHeight);
            var cover = new CroppedBitmap(image, new Int32Rect((image.PixelWidth - side) / 2, (image.PixelHeight - side) / 2, side, side));
            cover.Freeze();
            return cover;
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException
            or InvalidOperationException or NotSupportedException or ArgumentException or FormatException)
        {
            // Media sessions can change or disappear while their artwork stream is opening.
            return null;
        }
    }
}

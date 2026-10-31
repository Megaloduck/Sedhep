using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using Sedhep.Infrastructure;

namespace Sedhep.Services;

/// <summary>
/// Reads a QR code out of a picture. The pixels are copied straight out of the platform bitmap as
/// BGRA, so no image format is decoded twice and nothing has to be scaled up first.
/// </summary>
public sealed class QrImageReader
{
    private readonly ILogger<QrImageReader> _logger;

    public QrImageReader(ILogger<QrImageReader> logger)
    {
        _logger = logger;
    }

    /// <summary>Returns the text of the first QR code in the stream, or null when there is none.</summary>
    public string? Read(Stream stream)
    {
        try
        {
            using var bitmap = new Bitmap(stream);
            var size = bitmap.PixelSize;

            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }

            var stride = size.Width * 4;
            var pixels = new byte[stride * size.Height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

            try
            {
                bitmap.CopyPixels(
                    new PixelRect(0, 0, size.Width, size.Height),
                    handle.AddrOfPinnedObject(),
                    pixels.Length,
                    stride);
            }
            finally
            {
                handle.Free();
            }

            return QrCodes.Decode(pixels, size.Width, size.Height);
        }
        catch (Exception ex)
        {
            // A file that is not an image, or a truncated one, is a user mistake, not a crash.
            _logger.LogInformation(ex, "Could not read a picture as a QR code");
            return null;
        }
    }
}

// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using ZXing;
using ZXing.Common;

namespace Sedhep.Infrastructure;

/// <summary>
/// The two halves of a QR code: drawing one from a short text, and reading one back out of a
/// picture. Only QR codes are written, and only QR codes are read, so a code the app produced is
/// the only thing it will accept.
/// </summary>
public static class QrCodes
{
    private static readonly BarcodeFormat Format = BarcodeFormat.QR_CODE;

    /// <summary>
    /// Encodes <paramref name="text"/> into a grid of modules. The grid includes the quiet border
    /// scanners need, and each entry is one module, so the caller can pick its own module size.
    /// </summary>
    public static bool[,] Encode(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var hints = new Dictionary<EncodeHintType, object>
        {
            // The code carries a device name, which may not be Latin.
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
            // A screenshot is usually clean; the extra redundancy costs nothing at this size.
            [EncodeHintType.ERROR_CORRECTION] = "M"
        };

        // Asking for no particular size returns the bare grid plus the four-module quiet zone.
        var matrix = new MultiFormatWriter().encode(text, Format, 0, 0, hints);
        var modules = new bool[matrix.Width, matrix.Height];

        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                modules[x, y] = matrix[x, y];
            }
        }

        return modules;
    }

    /// <summary>
    /// Reads the first QR code in a picture. The pixels must be 32-bit BGRA, which is what the
    /// platform bitmap hands back. Returns null when there is no code to read.
    /// </summary>
    public static string? Decode(byte[] bgraPixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);

        if (width <= 0 || height <= 0 || bgraPixels.Length < width * height * 4)
        {
            return null;
        }

        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                // A code photographed off a screen is rarely square on; try hard before giving up.
                TryHarder = true,
                PossibleFormats = new[] { Format }
            }
        };

        var result = reader.Decode(bgraPixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        return string.IsNullOrWhiteSpace(result?.Text) ? null : result!.Text.Trim();
    }
}

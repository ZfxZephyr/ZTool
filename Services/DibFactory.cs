using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace ZTool.Services;

/// <summary>
/// Builds the device-independent bitmap the clipboard wants for an image.
/// Screenshots go out as 32-bit BGRA with the alpha byte forced opaque: every
/// target reads that, and an image on the clipboard is being pasted somewhere,
/// not composited.
/// </summary>
internal static class DibFactory
{
    private const int HeaderSize = 40; // BITMAPINFOHEADER

    public static byte[]? FromBitmap(Bitmap bitmap)
    {
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        try
        {
            var stride = width * 4;
            var pixels = new byte[stride * height];
            var buffer = Marshal.AllocHGlobal(pixels.Length);

            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), buffer, pixels.Length, stride);
                Marshal.Copy(buffer, pixels, 0, pixels.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;
            }

            var dib = new byte[HeaderSize + pixels.Length];
            WriteHeader(dib, width, height);

            // A DIB stores rows bottom-up; CopyPixels hands them top-down.
            for (var row = 0; row < height; row++)
            {
                var from = row * stride;
                var to = HeaderSize + (height - 1 - row) * stride;
                Buffer.BlockCopy(pixels, from, dib, to, stride);
            }

            return dib;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static void WriteHeader(byte[] dib, int width, int height)
    {
        // biSize, biWidth, biHeight, biPlanes, biBitCount, biCompression, …
        BitConverter.TryWriteBytes(dib.AsSpan(0, 4), HeaderSize);
        BitConverter.TryWriteBytes(dib.AsSpan(4, 4), width);
        BitConverter.TryWriteBytes(dib.AsSpan(8, 4), height); // positive = bottom-up
        BitConverter.TryWriteBytes(dib.AsSpan(12, 2), (ushort)1);
        BitConverter.TryWriteBytes(dib.AsSpan(14, 2), (ushort)32);
        BitConverter.TryWriteBytes(dib.AsSpan(16, 4), 0); // BI_RGB
    }
}

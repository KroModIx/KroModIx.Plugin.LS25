using System;
using System.IO;
using System.Runtime.InteropServices;
using NLog;
using Pfim;
using SkiaSharp;
using PfimImageFormat = Pfim.ImageFormat;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Konvertiert DDS-Bytes (LS/FS-Mod-Icons) in PNG-Bytes für den Preview-Cache.
/// 1:1 aus LS-ModManager übernommen. Pfim dekodiert BC1/BC2/BC3-komprimierte
/// sowie unkomprimierte DDS-Formate; SkiaSharp encodet zu PNG.
/// Stride-Falle: Pfim gibt <see cref="IImage.Stride"/> zurück, das je nach
/// Format vom naiven <c>Width * BytesPerPixel</c> abweichen kann (Padding auf
/// Alignment-Grenzen) — echten Stride an SkiaSharp geben, sonst sheared Bild.
/// </summary>
public static class DdsToPngConverter
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static byte[]? Convert(byte[] ddsBytes)
    {
        if (ddsBytes.Length < 128) return null;
        try
        {
            using var stream = new MemoryStream(ddsBytes);
            using var image = Pfimage.FromStream(stream);
            return ToPng(image);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "DDS-Dekodierung fehlgeschlagen (Format nicht unterstützt).");
            return null;
        }
    }

    private static byte[]? ToPng(IImage image)
    {
        var colorType = image.Format switch
        {
            PfimImageFormat.Rgba32 => SKColorType.Bgra8888,
            PfimImageFormat.Rgb24 => SKColorType.Bgra8888,
            _ => (SKColorType?)null,
        };
        if (colorType is null)
        {
            Log.Debug("Pfim-Format nicht unterstützt: {Format}", image.Format);
            return null;
        }

        var (pixelBytes, stride) = image.Format == PfimImageFormat.Rgb24
            ? ExpandRgbToBgra(image)
            : (image.Data, image.Stride);

        var info = new SKImageInfo(image.Width, image.Height, colorType.Value, SKAlphaType.Premul);
        using var bitmap = new SKBitmap();
        var handle = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
        try
        {
            var pinned = handle.AddrOfPinnedObject();
            if (!bitmap.InstallPixels(info, pinned, stride))
                return null;
            using var img = SKImage.FromBitmap(bitmap);
            using var data = img.Encode(SKEncodedImageFormat.Png, quality: 90);
            return data.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    private static (byte[] Bytes, int Stride) ExpandRgbToBgra(IImage image)
    {
        var newStride = image.Width * 4;
        var result = new byte[newStride * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            var srcRow = y * image.Stride;
            var dstRow = y * newStride;
            for (var x = 0; x < image.Width; x++)
            {
                var srcPx = srcRow + x * 3;
                var dstPx = dstRow + x * 4;
                result[dstPx + 0] = image.Data[srcPx + 0];
                result[dstPx + 1] = image.Data[srcPx + 1];
                result[dstPx + 2] = image.Data[srcPx + 2];
                result[dstPx + 3] = 0xFF;
            }
        }
        return (result, newStride);
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        byte[]? primary = null;
        try
        {
            using var stream = new MemoryStream(ddsBytes);
            using var image = Pfimage.FromStream(stream);
            primary = ToPng(image);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Pfim-DDS-Dekodierung fehlgeschlagen — versuche ffmpeg-Fallback.");
        }
        if (primary is not null) return primary;

        // v1.14.0 Fallback: ffmpeg. Pfim scheitert bei BC7 + einigen exotischen
        // DXT-Varianten. ffmpeg (falls installiert) deckt praktisch alle DDS-
        // Kompressionen ab. Kein Fehler wenn ffmpeg fehlt — Convert liefert
        // null, Caller zeigt Icon-Fallback.
        try { return ConvertViaFfmpeg(ddsBytes); }
        catch (Exception ex)
        {
            Log.Debug(ex, "ffmpeg-DDS-Fallback fehlgeschlagen — kein Preview verfuegbar.");
            return null;
        }
    }

    /// <summary>v1.14.0: ruft <c>ffmpeg -f dds -i pipe:0 -f image2pipe -c:v png pipe:1</c>
    /// und streamt Ein-/Ausgabe via Stdin/Stdout. Timeout: 5 s (grosszuegig
    /// fuer 4K-Textures). Liefert null wenn ffmpeg fehlt oder exit-code != 0.</summary>
    private static byte[]? ConvertViaFfmpeg(byte[] ddsBytes)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error",
            "-f", "dds", "-i", "pipe:0",
            "-frames:v", "1", "-f", "image2pipe", "-c:v", "png", "pipe:1" })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi);
        if (proc is null) return null;

        // Stdin schreiben + schliessen (ffmpeg braucht EOF um zu decoden).
        var writeTask = Task.Run(() =>
        {
            proc.StandardInput.BaseStream.Write(ddsBytes, 0, ddsBytes.Length);
            proc.StandardInput.BaseStream.Flush();
            proc.StandardInput.Close();
        });
        using var output = new MemoryStream();
        proc.StandardOutput.BaseStream.CopyTo(output);
        writeTask.Wait(TimeSpan.FromSeconds(5));
        if (!proc.WaitForExit(5000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return null;
        }
        if (proc.ExitCode != 0) return null;
        var bytes = output.ToArray();
        return bytes.Length >= 8 ? bytes : null;
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

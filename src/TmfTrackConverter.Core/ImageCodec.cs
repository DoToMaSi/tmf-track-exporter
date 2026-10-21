using Pfim;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using GBX.NET;

namespace TmfTrackConverter.Core;

public static class ImageCodec
{
    public static string? ExportTexture(
        string? sourcePath,
        string destinationDirectory,
        string materialName,
        Vec3? tint = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        Directory.CreateDirectory(destinationDirectory);
        var safeName = string.Join("_", materialName.Split(Path.GetInvalidFileNameChars()));
        var pngPath = Path.Combine(destinationDirectory, safeName + ".png");

        var ext = Path.GetExtension(sourcePath);
        try
        {
            if (TryLoad(sourcePath, ext, out var image) && image is not null)
            {
                using (image)
                {
                    if (tint is { } color)
                    {
                        ApplyTint(image, color);
                    }

                    image.SaveAsPng(pngPath);
                    return pngPath;
                }
            }
        }
        catch
        {
            // Fall back to copying the original file.
        }

        var fallback = Path.Combine(destinationDirectory, safeName + ext);
        File.Copy(sourcePath, fallback, overwrite: true);
        return fallback;
    }

    private static bool TryLoad(string sourcePath, string ext, out Image<Rgba32>? image)
    {
        image = null;
        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            using var loaded = Image.Load<Rgba32>(sourcePath);
            image = loaded.Clone();
            return true;
        }

        if (ext.Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            image = LoadDds(sourcePath);
            return image is not null;
        }

        return false;
    }

    private static Image<Rgba32>? LoadDds(string sourcePath)
    {
        using var image = Pfimage.FromFile(sourcePath);
        image.Decompress();

        return image.Format switch
        {
            ImageFormat.Rgba32 => CopyBgra32(image),
            ImageFormat.Rgb24 => CopyRgb24(image, bgr: true),
            _ => null
        };
    }

    private static Image<Rgba32> CopyBgra32(IImage image)
    {
        var result = new Image<Rgba32>(image.Width, image.Height);
        var data = image.Data;
        var stride = image.Stride;
        result.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var src = y * stride;
                for (var x = 0; x < image.Width; x++)
                {
                    row[x] = new Rgba32(data[src + 2], data[src + 1], data[src], data[src + 3]);
                    src += 4;
                }
            }
        });
        return result;
    }

    private static Image<Rgba32> CopyRgb24(IImage image, bool bgr)
    {
        var result = new Image<Rgba32>(image.Width, image.Height);
        var data = image.Data;
        var stride = image.Stride;
        result.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var src = y * stride;
                for (var x = 0; x < image.Width; x++)
                {
                    var r = bgr ? data[src + 2] : data[src];
                    var g = data[src + 1];
                    var b = bgr ? data[src] : data[src + 2];
                    row[x] = new Rgba32(r, g, b);
                    src += 3;
                }
            }
        });
        return result;
    }

    private static void ApplyTint(Image<Rgba32> image, Vec3 tint)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    row[x] = new Rgba32(
                        ClampByte(pixel.R * tint.X),
                        ClampByte(pixel.G * tint.Y),
                        ClampByte(pixel.B * tint.Z),
                        pixel.A);
                }
            }
        });
    }

    private static byte ClampByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}

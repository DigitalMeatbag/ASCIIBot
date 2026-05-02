using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace ASCIIBot.Services;

internal static class FormatExtensionHelper
{
    public static string GetExtension(IImageFormat format) => format switch
    {
        PngFormat  => ".png",
        JpegFormat => ".jpg",
        BmpFormat  => ".bmp",
        GifFormat  => ".gif",
        WebpFormat => ".webp",
        _          => ".bin",
    };
}

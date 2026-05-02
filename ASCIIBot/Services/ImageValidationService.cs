using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Services;

public sealed class ImageValidationService
{
    private const int MaxDimension = 4096;

    private readonly ILogger<ImageValidationService> _logger;

    public ImageValidationService(ILogger<ImageValidationService> logger)
    {
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(Stream imageStream)
    {
        // Step 1: Content-based format detection
        IImageFormat? format;
        try
        {
            format = await Image.DetectFormatAsync(imageStream);
        }
        catch
        {
            return Error("The submitted file type is not supported. Processing has been rejected.");
        }

        imageStream.Position = 0;

        if (format is null ||
            format is not (PngFormat or JpegFormat or BmpFormat or GifFormat or WebpFormat))
        {
            return Error("The submitted file type is not supported. Processing has been rejected.");
        }

        // Step 2: Decode and check frames + dimensions
        Image<Rgba32> image;
        try
        {
            image = await Image.LoadAsync<Rgba32>(imageStream);
        }
        catch (UnknownImageFormatException)
        {
            return Error("The submitted image could not be decoded. Processing has been rejected.");
        }
        catch
        {
            return Error("The submitted image could not be decoded. Processing has been rejected.");
        }

        // Animation check
        if (format is GifFormat && image.Frames.Count > 1)
        {
            image.Dispose();
            return Error("Animated images are not supported in this version. Processing has been rejected.");
        }

        if (format is WebpFormat)
        {
            try
            {
                if (image.Frames.Count > 1)
                {
                    image.Dispose();
                    return Error("Animated images are not supported in this version. Processing has been rejected.");
                }
            }
            catch
            {
                // Cannot determine frame count — reject conservatively
                image.Dispose();
                return Error("Animated images are not supported in this version. Processing has been rejected.");
            }
        }

        // Dimension check
        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            image.Dispose();
            return Error("The submitted image exceeds the maximum supported dimensions. Processing has been rejected.");
        }

        _logger.LogDebug("Validated image: {Format} {Width}x{Height} frames={Frames}",
            format.Name, image.Width, image.Height, image.Frames.Count);

        return new ValidationResult.Ok { Image = image };
    }

    private static ValidationResult.Error Error(string message) =>
        new() { Message = message };
}

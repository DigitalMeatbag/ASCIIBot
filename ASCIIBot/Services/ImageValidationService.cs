using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly BotOptions _options;
    private readonly ILogger<ImageValidationService> _logger;

    public ImageValidationService(IOptions<BotOptions> options, ILogger<ImageValidationService> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public async Task<ValidationResult> ValidateAsync(Stream imageStream)
    {
        // Capture original bytes for reattach before any decoding
        byte[] originalBytes;
        if (imageStream is MemoryStream ms && ms.TryGetBuffer(out var seg))
        {
            originalBytes = seg.Array![seg.Offset..(seg.Offset + seg.Count)];
        }
        else
        {
            using var capture = new MemoryStream();
            await imageStream.CopyToAsync(capture);
            originalBytes = capture.ToArray();
            imageStream   = new MemoryStream(originalBytes);
        }

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

        // APNG detection: PNG with animation control chunk is rejected as unsupported animated content
        if (format is PngFormat && ContainsApngChunk(originalBytes))
        {
            return Error("The submitted file type is not supported. Processing has been rejected.");
        }

        // Step 2: Decode and check dimensions + frames
        Image<Rgba32> image;
        try
        {
            image = await Image.LoadAsync<Rgba32>(imageStream);
        }
        catch
        {
            return Error("The submitted image could not be decoded. Processing has been rejected.");
        }

        // Dimension check
        if (image.Width > _options.MaxDecodedImageWidth || image.Height > _options.MaxDecodedImageHeight)
        {
            image.Dispose();
            return Error("The submitted image exceeds the maximum supported dimensions. Processing has been rejected.");
        }

        // Animated GIF routing
        if (format is GifFormat && image.Frames.Count > 1)
        {
            _logger.LogDebug("Routing animated GIF: {Width}x{Height} frames={Frames}",
                image.Width, image.Height, image.Frames.Count);
            return new ValidationResult.AnimatedOk
            {
                Image         = image,
                OriginalBytes = originalBytes,
                Format        = format,
            };
        }

        // Animated WebP routing
        if (format is WebpFormat)
        {
            bool isAnimated;
            try
            {
                isAnimated = image.Frames.Count > 1;
            }
            catch
            {
                image.Dispose();
                return Error("The submitted file type is not supported. Processing has been rejected.");
            }

            if (isAnimated)
            {
                _logger.LogDebug("Routing animated WebP: {Width}x{Height} frames={Frames}",
                    image.Width, image.Height, image.Frames.Count);
                return new ValidationResult.AnimatedOk
                {
                    Image         = image,
                    OriginalBytes = originalBytes,
                    Format        = format,
                };
            }
        }

        _logger.LogDebug("Validated image: {Format} {Width}x{Height} frames={Frames}",
            format.Name, image.Width, image.Height, image.Frames.Count);

        return new ValidationResult.Ok
        {
            Image         = image,
            OriginalBytes = originalBytes,
            Format        = format,
        };
    }

    // Walk PNG chunk structure to detect APNG animation control chunk (acTL).
    // Stops at IDAT to avoid scanning pixel data for false positives.
    private static bool ContainsApngChunk(byte[] bytes)
    {
        if (bytes.Length < 8) return false;
        int pos = 8; // skip 8-byte PNG signature
        while (pos + 8 <= bytes.Length)
        {
            int chunkLen = (bytes[pos] << 24) | (bytes[pos + 1] << 16) |
                           (bytes[pos + 2] << 8) | bytes[pos + 3];
            if (chunkLen < 0 || (long)pos + 8 + chunkLen + 4 > bytes.Length) break;

            // acTL chunk type: 0x61 0x63 0x54 0x4C
            if (bytes[pos + 4] == 0x61 && bytes[pos + 5] == 0x63 &&
                bytes[pos + 6] == 0x54 && bytes[pos + 7] == 0x4C)
                return true;

            // IDAT chunk: stop here — acTL must precede IDAT in a valid APNG
            if (bytes[pos + 4] == 0x49 && bytes[pos + 5] == 0x44 &&
                bytes[pos + 6] == 0x41 && bytes[pos + 7] == 0x54)
                break;

            pos += 8 + chunkLen + 4;
        }
        return false;
    }

    private static ValidationResult.Error Error(string message) =>
        new() { Message = message };
}

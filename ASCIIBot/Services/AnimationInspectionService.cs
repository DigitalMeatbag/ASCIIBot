using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Services;

public sealed class AnimationInspectionService
{
    private readonly BotOptions _options;
    private readonly ILogger<AnimationInspectionService> _logger;

    public AnimationInspectionService(
        IOptions<BotOptions>                  options,
        ILogger<AnimationInspectionService>   logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public AnimationInspectionResult Inspect(Image<Rgba32> image, IImageFormat format)
    {
        int frameCount = image.Frames.Count;

        // Source-frame safety fuse
        if (frameCount > _options.AnimationMaxSourceFrames)
        {
            _logger.LogDebug("Source frame count {Count} exceeds fuse {Limit}",
                frameCount, _options.AnimationMaxSourceFrames);
            return Rejected("The submitted animation exceeds processing limits. Processing has been rejected.");
        }

        // Extract per-frame delays
        long[] frameDelaysMs;
        try
        {
            frameDelaysMs = ExtractFrameDelays(image, format, frameCount);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract animation frame delays");
            return Rejected("The submitted animation could not be inspected. Processing has been rejected.");
        }

        // Compute frame start times and total duration
        long[] frameStartTimesMs = new long[frameCount];
        long   runningMs         = 0;
        for (int i = 0; i < frameCount; i++)
        {
            frameStartTimesMs[i] = runningMs;
            runningMs += frameDelaysMs[i];
        }
        long sourceDurationMs = runningMs;

        // Duration must be positive
        if (sourceDurationMs <= 0)
        {
            _logger.LogDebug("Animation duration is non-positive: {DurationMs}ms", sourceDurationMs);
            return Rejected("The submitted animation could not be inspected. Processing has been rejected.");
        }

        // Duration must not exceed limit (equal to limit is accepted)
        if (sourceDurationMs > _options.AnimationMaxDurationMs)
        {
            _logger.LogDebug("Animation duration {DurationMs}ms exceeds limit {LimitMs}ms",
                sourceDurationMs, _options.AnimationMaxDurationMs);
            return Rejected("The submitted animation exceeds the maximum supported duration. Processing has been rejected.");
        }

        _logger.LogDebug("Animation inspected: {Format} {W}x{H} {Frames} frames {DurationMs}ms",
            format.Name, image.Width, image.Height, frameCount, sourceDurationMs);

        return new AnimationInspectionResult.Ok
        {
            CanvasWidth       = image.Width,
            CanvasHeight      = image.Height,
            SourceFrameCount  = frameCount,
            SourceDurationMs  = sourceDurationMs,
            FrameStartTimesMs = frameStartTimesMs,
        };
    }

    private static long[] ExtractFrameDelays(Image<Rgba32> image, IImageFormat format, int frameCount)
    {
        long[] delays = new long[frameCount];

        if (format is GifFormat)
        {
            for (int i = 0; i < frameCount; i++)
            {
                var meta = image.Frames[i].Metadata.GetGifMetadata();
                // GIF frame delay is in centiseconds (1/100 s); convert to milliseconds
                delays[i] = meta.FrameDelay * 10L;
            }
        }
        else if (format is WebpFormat)
        {
            for (int i = 0; i < frameCount; i++)
            {
                var meta = image.Frames[i].Metadata.GetWebpMetadata();
                // WebP frame delay is already in milliseconds
                delays[i] = meta.FrameDelay;
            }
        }
        else
        {
            throw new InvalidOperationException($"Unsupported animated format for inspection: {format.Name}");
        }

        return delays;
    }

    private static AnimationInspectionResult.Rejected Rejected(string message) =>
        new() { Message = message };
}

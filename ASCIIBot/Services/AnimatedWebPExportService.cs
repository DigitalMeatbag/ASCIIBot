using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Services;

public sealed class AnimatedWebPExportService
{
    private readonly BotOptions _options;
    private readonly PngRenderService _png;
    private readonly ILogger<AnimatedWebPExportService> _logger;

    public AnimatedWebPExportService(
        IOptions<BotOptions>                options,
        PngRenderService                    png,
        ILogger<AnimatedWebPExportService>  logger)
    {
        _options = options.Value;
        _png     = png;
        _logger  = logger;
    }

    /// <summary>
    /// Exports the animated render to animated WebP bytes.
    /// Returns null if frame pixel dimensions exceed configured limits (rejection).
    /// Throws on encoder failure (failure).
    /// </summary>
    public byte[]? Export(AnimatedAsciiRender render, bool colorEnabled)
    {
        if (render.Frames.Length == 0)
            throw new InvalidOperationException("AnimatedAsciiRender has no frames.");

        var firstRender  = render.Frames[0].Render;
        var (imgW, imgH) = _png.ComputePixelDimensions(firstRender.Width, firstRender.Height);

        if (imgW > _options.RenderPngMaxWidth || imgH > _options.RenderPngMaxHeight)
        {
            _logger.LogDebug("Animated WebP frame dimensions {W}x{H} exceed limits, rejecting", imgW, imgH);
            return null;
        }

        _logger.LogDebug("Exporting animated WebP: {Frames} frames at {W}x{H}px",
            render.Frames.Length, imgW, imgH);

        using var mainImage = BuildAnimatedImage(render, colorEnabled, imgW, imgH);

        using var ms = new MemoryStream();
        mainImage.SaveAsWebp(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        var bytes = ms.ToArray();

        _logger.LogDebug("Animated WebP encoded: {Bytes} bytes", bytes.Length);
        return bytes;
    }

    private Image<Rgba32> BuildAnimatedImage(
        AnimatedAsciiRender render,
        bool                colorEnabled,
        int                 imgW,
        int                 imgH)
    {
        var mainImage = new Image<Rgba32>(imgW, imgH);
        try
        {
            // Rasterize root frame (index 0)
            _png.RasterizeFrame(mainImage, render.Frames[0].Render, colorEnabled);
            SetWebPFrameDelay(mainImage.Frames.RootFrame, render.Frames[0].Duration);

            // Add subsequent frames
            for (int i = 1; i < render.Frames.Length; i++)
            {
                using var frameImage = new Image<Rgba32>(imgW, imgH);
                _png.RasterizeFrame(frameImage, render.Frames[i].Render, colorEnabled);
                mainImage.Frames.AddFrame(frameImage.Frames.RootFrame);
                SetWebPFrameDelay(mainImage.Frames[i], render.Frames[i].Duration);
            }

            // Infinite looping
            mainImage.Metadata.GetWebpMetadata().RepeatCount = 0;

            return mainImage;
        }
        catch
        {
            mainImage.Dispose();
            throw;
        }
    }

    private static void SetWebPFrameDelay(ImageFrame<Rgba32> frame, TimeSpan duration)
    {
        var meta = frame.Metadata.GetWebpMetadata();
        meta.FrameDelay = (uint)Math.Max(1, (long)duration.TotalMilliseconds);
    }
}

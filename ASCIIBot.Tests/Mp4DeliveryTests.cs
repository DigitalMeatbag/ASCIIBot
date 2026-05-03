using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Tests;

/// <summary>§27.5 MP4 delivery: animated WebP delivery rules applied to MP4-sourced output.</summary>
public sealed class Mp4DeliveryTests
{
    private static OutputDeliveryService MakeService(
        long totalUploadLimit   = 10_000_000,
        int  animWebPByteLimit  = 8_388_608,
        int  pngByteLimit       = 8_388_608) =>
        new(Options.Create(new BotOptions
        {
            TotalUploadByteLimit   = totalUploadLimit,
            AnimationWebPByteLimit = animWebPByteLimit,
            RenderPngByteLimit     = pngByteLimit,
            RenderPngMaxWidth      = 4096,
            RenderPngMaxHeight     = 4096,
            InlineCharacterLimit   = 2000,
            AttachmentByteLimit    = 1_000_000,
        }),
        new AnsiColorService(),
        new PlainTextExportService(),
        new PngRenderService(
            Options.Create(new BotOptions
            {
                RenderPngByteLimit = pngByteLimit,
                RenderPngMaxWidth  = 4096,
                RenderPngMaxHeight = 4096,
            }),
            NullLogger<PngRenderService>.Instance),
        NullLogger<OutputDeliveryService>.Instance);

    private static RenderFile Mp4OriginalFile(int size = 500) =>
        new() { Content = new byte[size], Filename = "asciibot-original.mp4" };

    // --- Delivery result type ---

    [Fact]
    public void Mp4Delivery_ReturnsAnimatedResult()
    {
        var svc    = MakeService();
        var webp   = new byte[1_000];
        var result = svc.DecideAnimated(webp, showOriginal: false, originalImage: null);
        Assert.IsType<DeliveryResult.Animated>(result);
    }

    // --- Original file filename convention ---

    [Fact]
    public void Mp4Delivery_OriginalFile_HasMp4Extension()
    {
        var svc    = MakeService();
        var webp   = new byte[1_000];
        var orig   = Mp4OriginalFile();
        var result = svc.DecideAnimated(webp, showOriginal: true, originalImage: orig);
        var animated = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.NotNull(animated.OriginalImage);
        Assert.EndsWith(".mp4", animated.OriginalImage!.Filename, StringComparison.OrdinalIgnoreCase);
    }

    // --- WebP output size limit ---

    [Fact]
    public void Mp4Delivery_WebPExceedsAnimLimit_ReturnsRejected()
    {
        var svc  = MakeService(animWebPByteLimit: 100);
        var webp = new byte[101];
        var result = svc.DecideAnimated(webp, showOriginal: false, originalImage: null);
        Assert.IsType<DeliveryResult.Rejected>(result);
    }

    // --- Original omitted when total upload would exceed limit ---

    [Fact]
    public void Mp4Delivery_TotalExceedsLimit_OriginalOmitted_WhenRequested()
    {
        // Total limit set smaller than webp + original combined
        var svc  = MakeService(totalUploadLimit: 1500);
        var webp = new byte[1_000];
        var orig = Mp4OriginalFile(size: 600); // total = 1600, over limit
        var result = svc.DecideAnimated(webp, showOriginal: true, originalImage: orig);
        var animated = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.Null(animated.OriginalImage);
    }

    // --- show_original=false ---

    [Fact]
    public void Mp4Delivery_ShowOriginalFalse_OriginalImageIsNull()
    {
        var svc    = MakeService();
        var webp   = new byte[1_000];
        var result = svc.DecideAnimated(webp, showOriginal: false, originalImage: null);
        var animated = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.Null(animated.OriginalImage);
    }

    // --- WebP render filename ---

    [Fact]
    public void Mp4Delivery_WebPRenderFilename_IsWebP()
    {
        var svc    = MakeService();
        var webp   = new byte[1_000];
        var result = svc.DecideAnimated(webp, showOriginal: false, originalImage: null);
        var animated = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.EndsWith(".webp", animated.WebPRender.Filename, StringComparison.OrdinalIgnoreCase);
    }
}

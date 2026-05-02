using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

/// <summary>§25.9 Reject vs fail classification: message wording correctness.</summary>
public sealed class RejectVsFailTests
{
    private static AnimationInspectionService MakeInspector(
        int maxSourceFrames = 1000,
        int maxDurationMs   = 12_000) =>
        new(Options.Create(new BotOptions
        {
            AnimationMaxSourceFrames = maxSourceFrames,
            AnimationMaxDurationMs   = maxDurationMs,
        }), NullLogger<AnimationInspectionService>.Instance);

    private static OutputDeliveryService MakeDelivery(
        long totalUploadLimit  = 10_000_000,
        int  animWebPByteLimit = 8_388_608) =>
        new(Options.Create(new BotOptions
        {
            TotalUploadByteLimit   = totalUploadLimit,
            AnimationWebPByteLimit = animWebPByteLimit,
            RenderPngByteLimit     = 8_388_608,
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
                RenderPngByteLimit = 8_388_608,
                RenderPngMaxWidth  = 4096,
                RenderPngMaxHeight = 4096,
            }),
            NullLogger<PngRenderService>.Instance),
        NullLogger<OutputDeliveryService>.Instance);

    private static readonly IImageFormat GifFmt;

    static RejectVsFailTests()
    {
        using var g = new Image<Rgba32>(1, 1);
        var ms = new MemoryStream();
        g.SaveAsGif(ms);
        ms.Position = 0;
        GifFmt = Image.DetectFormat(ms)!;
    }

    // ─── Inspection rejections ────────────────────────────────────────────────

    [Fact]
    public async Task DurationTooLong_MessageContainsRejected()
    {
        var svc = MakeInspector(maxDurationMs: 100);
        using var img = await MakeGifAsync(2, frameDelayCs: 10); // 200ms > 100ms
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceFrameFuseExceeded_MessageContainsRejected()
    {
        var svc = MakeInspector(maxSourceFrames: 2);
        using var img = MakeInMemoryGif(3);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZeroDuration_MessageContainsRejected()
    {
        var svc = MakeInspector();
        using var img = await MakeGifAsync(2, frameDelayCs: 0); // total=0ms
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Delivery rejections ──────────────────────────────────────────────────

    [Fact]
    public void GeneratedWebPTooLarge_MessageContainsRejected()
    {
        var svc    = MakeDelivery(animWebPByteLimit: 100);
        var result = svc.DecideAnimated(new byte[200], showOriginal: false, originalImage: null);
        var rej    = Assert.IsType<DeliveryResult.Rejected>(result);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebPAloneExceedsTotalBudget_MessageContainsRejected()
    {
        var svc    = MakeDelivery(totalUploadLimit: 100, animWebPByteLimit: 8_388_608);
        var result = svc.DecideAnimated(new byte[200], showOriginal: false, originalImage: null);
        var rej    = Assert.IsType<DeliveryResult.Rejected>(result);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Encoder failure classification (§20.2) ───────────────────────────────

    [Fact]
    public void ExportService_EmptyFrames_ThrowsNotReturnsNull()
    {
        // Encoder failure is classified as a failure (throws), not a rejection (null).
        var exporter = new AnimatedWebPExportService(
            Options.Create(new BotOptions
            {
                RenderPngMaxWidth  = 4096,
                RenderPngMaxHeight = 4096,
                AnimationWebPByteLimit = 8_388_608,
            }),
            new PngRenderService(
                Options.Create(new BotOptions
                {
                    RenderPngMaxWidth  = 4096,
                    RenderPngMaxHeight = 4096,
                    RenderPngByteLimit = 8_388_608,
                }),
                NullLogger<PngRenderService>.Instance),
            NullLogger<AnimatedWebPExportService>.Instance);

        var emptyRender = new AnimatedAsciiRender
        {
            Width = 0, Height = 0, LoopCount = 0,
            Frames = Array.Empty<AnimatedAsciiFrame>(),
        };

        // Should throw, not return null (failure, not rejection)
        Assert.ThrowsAny<Exception>(() => exporter.Export(emptyRender, colorEnabled: true));
    }

    // ─── Message wording verification ─────────────────────────────────────────

    [Fact]
    public async Task DurationLimitMessage_UsesDurationKeyword()
    {
        var svc = MakeInspector(maxDurationMs: 100);
        using var img = await MakeGifAsync(2, frameDelayCs: 10);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("duration", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FrameFuseMessage_UsesProcessingLimitsKeyword()
    {
        var svc = MakeInspector(maxSourceFrames: 1);
        using var img = MakeInMemoryGif(2);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnimationWebPTooLargeMessage_UsesDeliveryLimitsKeyword()
    {
        var svc    = MakeDelivery(animWebPByteLimit: 100);
        var result = svc.DecideAnimated(new byte[200], showOriginal: false, originalImage: null);
        var rej    = Assert.IsType<DeliveryResult.Rejected>(result);
        Assert.Contains("delivery limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Image<Rgba32> MakeInMemoryGif(int frameCount)
    {
        var img = new Image<Rgba32>(4, 4);
        for (int i = 1; i < frameCount; i++)
        {
            var f = new Image<Rgba32>(4, 4);
            img.Frames.AddFrame(f.Frames.RootFrame);
        }
        return img;
    }

    private static async Task<Image<Rgba32>> MakeGifAsync(int frameCount, int frameDelayCs)
    {
        using var temp = new Image<Rgba32>(4, 4);
        temp.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
        for (int i = 1; i < frameCount; i++)
        {
            var f = new Image<Rgba32>(4, 4);
            f.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
            temp.Frames.AddFrame(f.Frames.RootFrame);
        }
        var ms = new MemoryStream();
        temp.SaveAsGif(ms, new GifEncoder());
        ms.Position = 0;
        return await Image.LoadAsync<Rgba32>(ms);
    }
}

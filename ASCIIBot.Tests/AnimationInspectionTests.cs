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

/// <summary>§25.2 Animated inspection: metadata, duration, frame fuse, timing.</summary>
public sealed class AnimationInspectionTests
{
    // GIF and WebP format instances obtained from minimal round-trips
    private static readonly IImageFormat GifFmt;
    private static readonly IImageFormat WebPFmt;

    static AnimationInspectionTests()
    {
        using var g = new Image<Rgba32>(1, 1);
        var ms1 = new MemoryStream();
        g.SaveAsGif(ms1);
        ms1.Position = 0;
        GifFmt = Image.DetectFormat(ms1)!;

        using var w = new Image<Rgba32>(1, 1);
        var ms2 = new MemoryStream();
        w.SaveAsWebp(ms2, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        ms2.Position = 0;
        WebPFmt = Image.DetectFormat(ms2)!;
    }

    private static AnimationInspectionService MakeService(
        int maxSourceFrames = 1000,
        int maxDurationMs   = 12_000) =>
        new(Options.Create(new BotOptions
        {
            AnimationMaxSourceFrames = maxSourceFrames,
            AnimationMaxDurationMs   = maxDurationMs,
        }), NullLogger<AnimationInspectionService>.Instance);

    // ─── Source-frame safety fuse ─────────────────────────────────────────────

    [Fact]
    public void Inspect_FrameCountAtLimit_ReturnsOk()
    {
        var svc = MakeService(maxSourceFrames: 3);
        using var img = MakeGifImageInMemory(3, 10);
        var result = svc.Inspect(img, GifFmt);
        Assert.IsType<AnimationInspectionResult.Ok>(result);
    }

    [Fact]
    public void Inspect_FrameCountAboveLimit_ReturnsRejected()
    {
        var svc = MakeService(maxSourceFrames: 3);
        using var img = MakeGifImageInMemory(4, 10);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_FrameCountEqualToDefaultFuse_NotRejectedByFuse()
    {
        var svc = MakeService(maxSourceFrames: 1000);
        // Build 1000-frame image in memory (no round-trip) — fuse check is count-only
        using var img = MakeGifImageInMemory(1000, 10);
        var result = svc.Inspect(img, GifFmt);
        // May fail duration check but NOT frame fuse (should not see "processing limits" message)
        if (result is AnimationInspectionResult.Rejected rej)
            Assert.DoesNotContain("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_FrameCountAboveDefaultFuse_ReturnsRejected()
    {
        var svc = MakeService(maxSourceFrames: 1000);
        using var img = MakeGifImageInMemory(1001, 0);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Duration checks ──────────────────────────────────────────────────────

    [Fact]
    public async Task Inspect_DurationExceedsLimit_ReturnsRejected()
    {
        var svc = MakeService(maxDurationMs: 1000);
        // 3 frames × 400ms = 1200ms > 1000ms
        using var img = await MakeGifImageRoundTripAsync(3, frameDelayCs: 40);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("duration", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_DurationExactlyAtLimit_ReturnsOk()
    {
        var svc = MakeService(maxDurationMs: 1200);
        // 3 frames × 400ms = 1200ms == 1200ms (at limit, allowed)
        using var img = await MakeGifImageRoundTripAsync(3, frameDelayCs: 40);
        var result = svc.Inspect(img, GifFmt);
        Assert.IsType<AnimationInspectionResult.Ok>(result);
    }

    [Fact]
    public async Task Inspect_ZeroDuration_ReturnsRejected()
    {
        var svc = MakeService();
        // All frame delays = 0 → total duration = 0
        using var img = await MakeGifImageRoundTripAsync(2, frameDelayCs: 0);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("inspected", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_ValidGif_ReturnsOkWithCorrectMetadata()
    {
        var svc = MakeService();
        // 2 frames × 100ms = 200ms total
        using var img = await MakeGifImageRoundTripAsync(2, frameDelayCs: 10);
        var result = svc.Inspect(img, GifFmt);
        var ok = Assert.IsType<AnimationInspectionResult.Ok>(result);
        Assert.Equal(2,   ok.SourceFrameCount);
        Assert.Equal(200, ok.SourceDurationMs);
        Assert.Equal(2,   ok.FrameStartTimesMs.Length);
        Assert.Equal(0,   ok.FrameStartTimesMs[0]);
        Assert.Equal(100, ok.FrameStartTimesMs[1]);
    }

    [Fact]
    public async Task Inspect_ValidWebP_ReturnsOkWithCorrectMetadata()
    {
        var svc = MakeService();
        using var img = await MakeWebPImageRoundTripAsync(3, frameDelayMs: 200);
        var result = svc.Inspect(img, WebPFmt);
        var ok = Assert.IsType<AnimationInspectionResult.Ok>(result);
        Assert.Equal(3,   ok.SourceFrameCount);
        Assert.Equal(600, ok.SourceDurationMs);
        Assert.Equal(0,   ok.FrameStartTimesMs[0]);
        Assert.Equal(200, ok.FrameStartTimesMs[1]);
        Assert.Equal(400, ok.FrameStartTimesMs[2]);
    }

    [Fact]
    public async Task Inspect_CanvasWidthAndHeight_ReturnedInOk()
    {
        var svc = MakeService();
        using var img = await MakeGifImageRoundTripAsync(2, frameDelayCs: 10);
        var result = svc.Inspect(img, GifFmt);
        var ok = Assert.IsType<AnimationInspectionResult.Ok>(result);
        Assert.Equal(img.Width,  ok.CanvasWidth);
        Assert.Equal(img.Height, ok.CanvasHeight);
    }

    // ─── Default 12-second limit (§25.2) ─────────────────────────────────────

    [Fact]
    public async Task Inspect_12SecondAnimation_Accepted()
    {
        var svc = MakeService(maxDurationMs: 12_000);
        // 12 frames × 1000ms = 12,000ms exactly → accepted
        using var img = await MakeGifImageRoundTripAsync(12, frameDelayCs: 100);
        var result = svc.Inspect(img, GifFmt);
        Assert.IsType<AnimationInspectionResult.Ok>(result);
    }

    [Fact]
    public async Task Inspect_AnimationOver12Seconds_Rejected()
    {
        var svc = MakeService(maxDurationMs: 12_000);
        // 13 frames × 1000ms = 13,000ms > 12,000ms
        using var img = await MakeGifImageRoundTripAsync(13, frameDelayCs: 100);
        var result = svc.Inspect(img, GifFmt);
        Assert.IsType<AnimationInspectionResult.Rejected>(result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    // In-memory only; no round-trip — frame delays may not be properly stored.
    // Use only for frame-count fuse tests.
    private static Image<Rgba32> MakeGifImageInMemory(int frameCount, int frameDelayCs)
    {
        var img = new Image<Rgba32>(8, 8);
        img.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
        for (int i = 1; i < frameCount; i++)
        {
            var frame = new Image<Rgba32>(8, 8);
            frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
            img.Frames.AddFrame(frame.Frames.RootFrame);
        }
        return img;
    }

    // Round-trip through GIF encoding ensures frame delays are preserved in metadata.
    private static async Task<Image<Rgba32>> MakeGifImageRoundTripAsync(int frameCount, int frameDelayCs)
    {
        using var temp = new Image<Rgba32>(8, 8);
        temp.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
        for (int i = 1; i < frameCount; i++)
        {
            var frame = new Image<Rgba32>(8, 8);
            frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
            temp.Frames.AddFrame(frame.Frames.RootFrame);
        }
        var ms = new MemoryStream();
        temp.SaveAsGif(ms, new GifEncoder());
        ms.Position = 0;
        return await Image.LoadAsync<Rgba32>(ms);
    }

    // Round-trip through WebP encoding ensures frame delays are preserved.
    private static async Task<Image<Rgba32>> MakeWebPImageRoundTripAsync(int frameCount, uint frameDelayMs)
    {
        using var temp = new Image<Rgba32>(8, 8);
        temp.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = frameDelayMs;
        for (int i = 1; i < frameCount; i++)
        {
            using var frame = new Image<Rgba32>(8, 8);
            frame.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = frameDelayMs;
            temp.Frames.AddFrame(frame.Frames.RootFrame);
        }
        temp.Metadata.GetWebpMetadata().RepeatCount = 0;
        var ms = new MemoryStream();
        temp.SaveAsWebp(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        ms.Position = 0;
        return await Image.LoadAsync<Rgba32>(ms);
    }
}

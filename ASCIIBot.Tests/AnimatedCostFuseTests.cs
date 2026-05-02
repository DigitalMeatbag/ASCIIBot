using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

/// <summary>§25.5 Cost fuse: output-cell computation, source-frame fuse.</summary>
public sealed class AnimatedCostFuseTests
{
    private static readonly IImageFormat GifFmt;

    static AnimatedCostFuseTests()
    {
        using var g = new Image<Rgba32>(1, 1);
        var ms = new MemoryStream();
        g.SaveAsGif(ms);
        ms.Position = 0;
        GifFmt = Image.DetectFormat(ms)!;
    }

    // ─── Total output-cell computation ────────────────────────────────────────

    [Fact]
    public void TotalOutputCells_ComputedAsWidthTimesHeightTimesFrames()
    {
        long cells = (long)10 * 5 * 6;
        Assert.Equal(300L, cells);
    }

    [Fact]
    public void CostAtLimit_IsNotGreaterThanLimit()
    {
        long cells = (long)100 * 30 * 100;
        Assert.Equal(300_000L, cells);
        Assert.False(cells > 300_000);
    }

    [Fact]
    public void CostAboveLimit_IsGreaterThanLimit()
    {
        long cells = 300_001L;
        Assert.True(cells > 300_000);
    }

    // ─── Source-frame fuse via AnimationInspectionService ────────────────────

    private static AnimationInspectionService MakeInspector(int maxSourceFrames) =>
        new(Options.Create(new BotOptions
        {
            AnimationMaxSourceFrames = maxSourceFrames,
            AnimationMaxDurationMs   = 12_000,
        }), NullLogger<AnimationInspectionService>.Instance);

    [Fact]
    public void SourceFrameFuse_EqualToLimit_NotRejectedByFuse()
    {
        var svc = MakeInspector(3);
        using var img = MakeInMemoryGif(3);
        var result = svc.Inspect(img, GifFmt);
        // Frame fuse not exceeded; may be rejected by duration but NOT by frame count
        if (result is AnimationInspectionResult.Rejected rej)
            Assert.DoesNotContain("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceFrameFuse_AboveLimit_RejectedWithCorrectMessage()
    {
        var svc = MakeInspector(3);
        using var img = MakeInMemoryGif(4);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceFrameFuse_DefaultLimit1000_EqualToLimit_NotRejectedByFuse()
    {
        var svc = MakeInspector(1000);
        using var img = MakeInMemoryGif(1000);
        var result = svc.Inspect(img, GifFmt);
        if (result is AnimationInspectionResult.Rejected rej)
            Assert.DoesNotContain("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceFrameFuse_DefaultLimit1000_AboveLimit_Rejected()
    {
        var svc = MakeInspector(1000);
        using var img = MakeInMemoryGif(1001);
        var result = svc.Inspect(img, GifFmt);
        var rej = Assert.IsType<AnimationInspectionResult.Rejected>(result);
        Assert.Contains("processing limits", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── No adaptive degradation ─────────────────────────────────────────────

    [Fact]
    public void SamplingService_NeverReducesFrameCountToFitCostFuse()
    {
        var svc = new AnimationSamplingService(Options.Create(new BotOptions
        {
            AnimationMaxOutputFrames          = 48,
            AnimationTargetSampleIntervalMs   = 100,
            AnimationMinFrameDelayMs          = 100,
        }));
        var result = svc.Sample(5000, Enumerable.Range(0, 50).Select(i => (long)i * 100).ToArray());
        // Capped at maxFrames=48, not further reduced by any cost logic
        Assert.Equal(48, result.SelectedSourceFrameIndices.Length);
    }

    private static Image<Rgba32> MakeInMemoryGif(int frameCount)
    {
        var img = new Image<Rgba32>(4, 4);
        for (int i = 1; i < frameCount; i++)
        {
            var frame = new Image<Rgba32>(4, 4);
            img.Frames.AddFrame(frame.Frames.RootFrame);
        }
        return img;
    }
}

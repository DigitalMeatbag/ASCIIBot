using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

/// <summary>§25.6 Animated render model: frame dimensions, loop count, color/detail consistency.</summary>
public sealed class AnimatedRenderModelTests
{
    private static AnimatedAsciiRenderService MakeRenderService() =>
        new(new AsciiRenderService());

    private static AnimationSamplingService MakeSampler(int maxFrames = 48) =>
        new(Options.Create(new BotOptions
        {
            AnimationMaxOutputFrames          = maxFrames,
            AnimationTargetSampleIntervalMs   = 100,
            AnimationMinFrameDelayMs          = 100,
        }));

    private static SizePreset   Small  => SizePreset.FromString("small");
    private static DetailPreset Normal => DetailPreset.FromString("normal");

    // ─── Frame count and dimensions ───────────────────────────────────────────

    [Fact]
    public async Task FrameCount_MatchesSampledFrameCount()
    {
        using var img = await MakeAnimGif(3, 10);
        var sampler  = MakeSampler(maxFrames: 3);
        var sampling = sampler.Sample(300, new long[] { 0, 100, 200 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: true);
        Assert.Equal(sampling.SelectedSourceFrameIndices.Length, result.Frames.Length);
    }

    [Fact]
    public async Task AllFrames_HaveIdenticalWidthAndHeight()
    {
        using var img = await MakeAnimGif(4, 10);
        var sampler  = MakeSampler(maxFrames: 4);
        var sampling = sampler.Sample(400, new long[] { 0, 100, 200, 300 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: true);
        foreach (var frame in result.Frames)
        {
            Assert.Equal(result.Width,  frame.Render.Width);
            Assert.Equal(result.Height, frame.Render.Height);
        }
    }

    [Fact]
    public async Task EachFrame_ContainsRichAsciiRender()
    {
        using var img = await MakeAnimGif(2, 10);
        var sampler  = MakeSampler(maxFrames: 2);
        var sampling = sampler.Sample(200, new long[] { 0, 100 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: true);
        foreach (var frame in result.Frames)
            Assert.NotNull(frame.Render);
    }

    [Fact]
    public async Task AnimatedRender_LoopCountIsZero()
    {
        using var img = await MakeAnimGif(2, 10);
        var sampler  = MakeSampler(maxFrames: 2);
        var sampling = sampler.Sample(200, new long[] { 0, 100 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: true);
        Assert.Equal(0, result.LoopCount);
    }

    // ─── Color/detail consistency ─────────────────────────────────────────────

    [Fact]
    public async Task ColorOn_AllFrames_HaveRgbForegrounds()
    {
        using var img = await MakeColoredAnimGif(2, 10, r: 200, g: 50, b: 50);
        var sampler  = MakeSampler(maxFrames: 2);
        var sampling = sampler.Sample(200, new long[] { 0, 100 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: true);
        // All cells should have the sampled foreground (not pure monochrome)
        // Just verify the foreground is not all-white (monochrome default #E6EDF3 = 230,237,243)
        foreach (var frame in result.Frames)
        {
            var cell = frame.Render.Cells[0][0];
            // Red-heavy source → R should be meaningfully higher than B
            Assert.True(cell.Foreground.R >= cell.Foreground.B);
        }
    }

    [Fact]
    public async Task ColorOff_AllFrames_RenderSuccessfully()
    {
        using var img = await MakeColoredAnimGif(2, 10, r: 200, g: 50, b: 50);
        var sampler  = MakeSampler(maxFrames: 2);
        var sampling = sampler.Sample(200, new long[] { 0, 100 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: false);
        // When color=off, AsciiRenderService still samples RGB but the module would use monochrome.
        // The RenderFrame method stores sampled colors regardless — color mode applies at export time.
        // So we just verify all frames were rendered (no exception, no nulls)
        Assert.All(result.Frames, f => Assert.NotNull(f.Render));
    }

    // ─── §25.4 Sample times and durations preserved ───────────────────────────

    [Fact]
    public async Task SampleTimesAndDurations_PreservedInFrameMetadata()
    {
        using var img = await MakeAnimGif(2, 10);
        var sampler  = MakeSampler(maxFrames: 2);
        var sampling = sampler.Sample(200, new long[] { 0, 100 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: true);
        for (int i = 0; i < result.Frames.Length; i++)
        {
            Assert.Equal(sampling.SampleTimes[i],    result.Frames[i].SampleTime);
            Assert.Equal(sampling.OutputDurations[i], result.Frames[i].Duration);
        }
    }

    [Fact]
    public async Task FrameIndices_AreSequential()
    {
        using var img = await MakeAnimGif(3, 10);
        var sampler  = MakeSampler(maxFrames: 3);
        var sampling = sampler.Sample(300, new long[] { 0, 100, 200 });
        var (cols, rows) = AsciiRenderService.ComputeDimensions(img.Width, img.Height, Small);

        var result = MakeRenderService().Render(img, cols, rows, sampling, Normal, colorEnabled: true);
        for (int i = 0; i < result.Frames.Length; i++)
            Assert.Equal(i, result.Frames[i].Index);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<Image<Rgba32>> MakeAnimGif(int frameCount, int frameDelayCs)
    {
        using var temp = new Image<Rgba32>(32, 32);
        temp.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
        for (int i = 1; i < frameCount; i++)
        {
            var f = new Image<Rgba32>(32, 32);
            f.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
            temp.Frames.AddFrame(f.Frames.RootFrame);
        }
        var ms = new MemoryStream();
        temp.SaveAsGif(ms, new GifEncoder());
        ms.Position = 0;
        return await Image.LoadAsync<Rgba32>(ms);
    }

    private static async Task<Image<Rgba32>> MakeColoredAnimGif(
        int frameCount, int frameDelayCs, byte r, byte g, byte b)
    {
        using var temp = new Image<Rgba32>(32, 32);
        // Fill all pixels with the target color
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                temp[x, y] = new Rgba32(r, g, b, 255);
        temp.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
        for (int i = 1; i < frameCount; i++)
        {
            var f = new Image<Rgba32>(32, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    f[x, y] = new Rgba32(r, g, b, 255);
            f.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayCs;
            temp.Frames.AddFrame(f.Frames.RootFrame);
        }
        var ms = new MemoryStream();
        temp.SaveAsGif(ms, new GifEncoder());
        ms.Position = 0;
        return await Image.LoadAsync<Rgba32>(ms);
    }
}

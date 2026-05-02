using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

/// <summary>§25.7 Animated WebP export: frame order, durations, loop, limits, failure classification.</summary>
public sealed class AnimatedWebPExportTests
{
    private static AnimatedWebPExportService MakeExporter(
        int pngMaxWidth       = 4096,
        int pngMaxHeight      = 4096,
        int animWebPByteLimit = 8_388_608) =>
        new(Options.Create(new BotOptions
        {
            RenderPngMaxWidth    = pngMaxWidth,
            RenderPngMaxHeight   = pngMaxHeight,
            AnimationWebPByteLimit = animWebPByteLimit,
        }),
        new PngRenderService(
            Options.Create(new BotOptions
            {
                RenderPngMaxWidth  = pngMaxWidth,
                RenderPngMaxHeight = pngMaxHeight,
                RenderPngByteLimit = 8_388_608,
            }),
            NullLogger<PngRenderService>.Instance),
        NullLogger<AnimatedWebPExportService>.Instance);

    // ─── Frame order and durations ────────────────────────────────────────────

    [Fact]
    public void Export_ProducesAnimatedWebPBytes()
    {
        var svc    = MakeExporter();
        var render = MakeRender(cols: 10, rows: 5, frameCount: 3, durationMs: 200);
        var bytes  = svc.Export(render, colorEnabled: true);
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0);
    }

    [Fact]
    public void Export_PreservesFrameCount()
    {
        var svc    = MakeExporter();
        var render = MakeRender(cols: 10, rows: 5, frameCount: 3, durationMs: 200);
        var bytes  = svc.Export(render, colorEnabled: true);
        Assert.NotNull(bytes);

        // Reload and verify frame count
        using var loaded = Image.Load<Rgba32>(bytes!);
        Assert.Equal(3, loaded.Frames.Count);
    }

    [Fact]
    public void Export_AppliesPerFrameDurations()
    {
        var svc    = MakeExporter();
        var render = MakeRenderWithVaryingDurations(cols: 8, rows: 4,
            durations: new[] { 100, 200, 300 });
        var bytes  = svc.Export(render, colorEnabled: true);
        Assert.NotNull(bytes);

        using var loaded = Image.Load<Rgba32>(bytes!);
        // Verify frame delays (WebP metadata) match the render durations
        for (int i = 0; i < render.Frames.Length; i++)
        {
            var meta = loaded.Frames[i].Metadata.GetWebpMetadata();
            Assert.Equal((uint)render.Frames[i].Duration.TotalMilliseconds, meta.FrameDelay);
        }
    }

    [Fact]
    public void Export_SetsInfiniteLoopMetadata()
    {
        var svc    = MakeExporter();
        var render = MakeRender(cols: 8, rows: 4, frameCount: 2, durationMs: 100);
        var bytes  = svc.Export(render, colorEnabled: true);
        Assert.NotNull(bytes);

        using var loaded = Image.Load<Rgba32>(bytes!);
        var webpMeta = loaded.Metadata.GetWebpMetadata();
        Assert.Equal((ushort)0, webpMeta.RepeatCount);
    }

    // ─── Dimension limit enforcement ──────────────────────────────────────────

    [Fact]
    public void Export_FrameDimensionsExceedLimit_ReturnsNull()
    {
        // Max 10x10 pixels — the rasterized render will exceed this
        var svc    = MakeExporter(pngMaxWidth: 10, pngMaxHeight: 10);
        var render = MakeRender(cols: 5, rows: 5, frameCount: 2, durationMs: 100);
        // 5 cols × ~8px cell + 24px padding = ~64px > 10px limit
        var bytes  = svc.Export(render, colorEnabled: true);
        Assert.Null(bytes); // dimension exceeded → rejection
    }

    [Fact]
    public void Export_FrameDimensionsWithinLimit_ReturnsBytes()
    {
        var svc    = MakeExporter(pngMaxWidth: 4096, pngMaxHeight: 4096);
        var render = MakeRender(cols: 5, rows: 5, frameCount: 2, durationMs: 100);
        var bytes  = svc.Export(render, colorEnabled: true);
        Assert.NotNull(bytes);
    }

    // ─── Byte limit enforcement (done in DeliveryService, not here) ───────────

    // §25.7: The export service returns bytes; the byte-limit check is applied in
    // OutputDeliveryService.DecideAnimated. The export service does not enforce it.

    // ─── Encoder failure classification ───────────────────────────────────────

    [Fact]
    public void Export_EmptyFrameList_ThrowsException()
    {
        var svc = MakeExporter();
        var render = new AnimatedAsciiRender
        {
            Width     = 0,
            Height    = 0,
            LoopCount = 0,
            Frames    = Array.Empty<AnimatedAsciiFrame>(),
        };
        // Empty frame list → exception (encoder failure, not rejection)
        Assert.Throws<InvalidOperationException>(() => svc.Export(render, colorEnabled: true));
    }

    // ─── No fallback ──────────────────────────────────────────────────────────

    [Fact]
    public void Export_OnDimensionExceedance_ReturnsNullNotGif()
    {
        // Verify we return null (rejection), not a different format
        var svc    = MakeExporter(pngMaxWidth: 10, pngMaxHeight: 10);
        var render = MakeRender(cols: 5, rows: 5, frameCount: 2, durationMs: 100);
        var bytes  = svc.Export(render, colorEnabled: true);
        Assert.Null(bytes); // no GIF or static fallback
    }

    // ─── Fixed encoder mode ───────────────────────────────────────────────────

    [Fact]
    public void Export_OutputIsDeterministic()
    {
        var svc    = MakeExporter();
        var render = MakeRender(cols: 8, rows: 4, frameCount: 2, durationMs: 100);
        var b1 = svc.Export(render, colorEnabled: true);
        var b2 = svc.Export(render, colorEnabled: true);
        // Lossless encoder should produce identical output for identical input
        Assert.Equal(b1, b2);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AnimatedAsciiRender MakeRender(int cols, int rows, int frameCount, int durationMs)
    {
        var frames = new AnimatedAsciiFrame[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            frames[i] = new AnimatedAsciiFrame
            {
                Index      = i,
                SampleTime = TimeSpan.FromMilliseconds(i * durationMs),
                Duration   = TimeSpan.FromMilliseconds(durationMs),
                Render     = MakeRichRender(cols, rows),
            };
        }
        return new AnimatedAsciiRender
        {
            Width     = cols,
            Height    = rows,
            LoopCount = 0,
            Frames    = frames,
        };
    }

    private static AnimatedAsciiRender MakeRenderWithVaryingDurations(int cols, int rows, int[] durations)
    {
        var frames = new AnimatedAsciiFrame[durations.Length];
        long t = 0;
        for (int i = 0; i < durations.Length; i++)
        {
            frames[i] = new AnimatedAsciiFrame
            {
                Index      = i,
                SampleTime = TimeSpan.FromMilliseconds(t),
                Duration   = TimeSpan.FromMilliseconds(durations[i]),
                Render     = MakeRichRender(cols, rows),
            };
            t += durations[i];
        }
        return new AnimatedAsciiRender
        {
            Width     = cols,
            Height    = rows,
            LoopCount = 0,
            Frames    = frames,
        };
    }

    private static RichAsciiRender MakeRichRender(int cols, int rows)
    {
        var cells = new RichAsciiCell[rows][];
        for (int r = 0; r < rows; r++)
        {
            cells[r] = new RichAsciiCell[cols];
            for (int c = 0; c < cols; c++)
            {
                cells[r][c] = new RichAsciiCell
                {
                    Row        = r,
                    Column     = c,
                    Character  = '@',
                    Foreground = new RgbColor(200, 200, 200),
                };
            }
        }
        return new RichAsciiRender { Width = cols, Height = rows, Cells = cells };
    }
}

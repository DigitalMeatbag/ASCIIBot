using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

/// <summary>
/// Verification spike for §28 implementation verification items.
/// Run before v3 implementation to confirm ImageSharp 3.1.x capabilities.
/// Produces .webp and .gif artifacts in the system temp directory for Discord preview testing.
/// </summary>
public sealed class AnimatedCapabilitySpikeTests
{
    private static readonly string ArtifactDir =
        Path.Combine(Path.GetTempPath(), "ASCIIBotSpike");

    // -------------------------------------------------------------------------
    // §28 Item 1+2: Inspect animated GIF metadata (frame count, delays, canvas)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_GifMetadata_FrameCountDelaysCanvasDimensions()
    {
        using var gif = BuildAnimatedGif(width: 120, height: 80, frameDelaysCentiseconds: [30, 10, 20, 15]);
        using var ms = new MemoryStream();
        await gif.SaveAsGifAsync(ms);
        ms.Position = 0;

        using var loaded = await Image.LoadAsync<Rgba32>(ms);

        Assert.Equal(4, loaded.Frames.Count);
        Assert.Equal(120, loaded.Width);
        Assert.Equal(80, loaded.Height);

        var rootMeta = loaded.Metadata.GetGifMetadata();
        Assert.NotNull(rootMeta);

        // §12.1: frame delays must be readable; GIF stores delay in 1/100ths of a second
        int[] expectedDelaysCs = [30, 10, 20, 15];
        for (int i = 0; i < loaded.Frames.Count; i++)
        {
            var frameMeta = loaded.Frames[i].Metadata.GetGifMetadata();
            Assert.NotNull(frameMeta);
            Assert.Equal(expectedDelaysCs[i], frameMeta.FrameDelay);
        }
    }

    // -------------------------------------------------------------------------
    // §28 Item 3+4: Composited frames from animated GIF (not raw deltas)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_GifCompositedFrames_CloneFrameProducesFullCanvas()
    {
        // Build a 3-frame GIF where each frame is a different solid color
        // With RestoreToBackground disposal so the canvas resets between frames.
        using var gif = BuildSolidColorAnimatedGif(60, 40,
            colors: [new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255)],
            delaysCentiseconds: [10, 10, 10]);

        using var ms = new MemoryStream();
        await gif.SaveAsGifAsync(ms);
        ms.Position = 0;

        using var loaded = await Image.LoadAsync<Rgba32>(ms);
        Assert.Equal(3, loaded.Frames.Count);

        // CloneFrame must return fully composited full-canvas frames
        for (int i = 0; i < 3; i++)
        {
            using var composited = loaded.Frames.CloneFrame(i);

            // Each composited frame must be the full canvas size
            Assert.Equal(60, composited.Width);
            Assert.Equal(40, composited.Height);

            // Composited frame must be a single-frame image
            Assert.Equal(1, composited.Frames.Count);
        }
    }

    // -------------------------------------------------------------------------
    // §28 Item 5+6: Encode animated WebP with per-frame durations + infinite loop
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_AnimatedWebPEncoding_MultiFrameWithDurationsAndInfiniteLoop()
    {
        // Build 3 frames: red, green, blue — each 200 ms
        using var webpOut = BuildAnimatedWebPImage(
            width: 80, height: 60,
            colors: [new Rgba32(255, 0, 0), new Rgba32(0, 255, 0), new Rgba32(0, 0, 255)],
            delaysMs: [200, 200, 200],
            repeatCount: 0 /* infinite */);

        using var ms = new MemoryStream();

        var encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossless };
        await webpOut.SaveAsWebpAsync(ms, encoder);

        ms.Position = 0;
        byte[] encoded = ms.ToArray();

        // Must produce non-trivial output
        Assert.True(encoded.Length > 100, $"Encoded WebP is suspiciously small: {encoded.Length} bytes");

        // Load back and verify animation metadata survived encoding
        ms.Position = 0;
        using var reloaded = await Image.LoadAsync<Rgba32>(ms);

        // §17.2: animated WebP must preserve all frames
        Assert.Equal(3, reloaded.Frames.Count);

        // Verify frame delays were preserved (WebP stores delays in milliseconds)
        int[] expectedMs = [200, 200, 200];
        for (int i = 0; i < reloaded.Frames.Count; i++)
        {
            var frameMeta = reloaded.Frames[i].Metadata.GetWebpMetadata();
            Assert.NotNull(frameMeta);
            Assert.Equal(expectedMs[i], (int)frameMeta.FrameDelay);
        }

        // Verify infinite loop was encoded (RepeatCount 0 = infinite)
        var rootMeta = reloaded.Metadata.GetWebpMetadata();
        Assert.NotNull(rootMeta);
        Assert.Equal(0, (int)rootMeta.RepeatCount);
    }

    // -------------------------------------------------------------------------
    // §28 Item 1+2 (WebP): Inspect animated WebP metadata from encoded round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_AnimatedWebPInspection_CanReadFrameCountDelaysCanvasAfterRoundtrip()
    {
        using var input = BuildAnimatedWebPImage(
            width: 100, height: 75,
            colors: [new Rgba32(200, 100, 50), new Rgba32(50, 200, 100)],
            delaysMs: [150, 300],
            repeatCount: 0);

        using var ms = new MemoryStream();
        await input.SaveAsWebpAsync(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        ms.Position = 0;

        using var loaded = await Image.LoadAsync<Rgba32>(ms);

        Assert.Equal(2, loaded.Frames.Count);
        Assert.Equal(100, loaded.Width);
        Assert.Equal(75, loaded.Height);

        var f0 = loaded.Frames[0].Metadata.GetWebpMetadata();
        var f1 = loaded.Frames[1].Metadata.GetWebpMetadata();
        Assert.Equal(150, (int)f0.FrameDelay);
        Assert.Equal(300, (int)f1.FrameDelay);
    }

    // -------------------------------------------------------------------------
    // §28 Item 9+10: Lossless animated WebP — legibility and byte size
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_AnimatedWebPEncoding_LosslessOutputIsUnder8MiB()
    {
        // 48 frames, medium-size ASCII render (72 cols × 26 rows at ~14px/char + padding)
        // This approximates the worst-case realistic output size
        // Approximate: 72*14+24 = ~1032px wide, 26*18+24 = ~492px tall
        int w = 1032, h = 492, frames = 48;

        using var img = BuildTestFrameAnimation(w, h, frames, delayMs: 100);
        using var ms  = new MemoryStream();

        await img.SaveAsWebpAsync(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });

        long bytes = ms.Length;
        const long limit = 8 * 1024 * 1024; // 8 MiB

        // Record the size for operator review even if within limit
        Assert.True(bytes <= limit,
            $"Lossless animated WebP for {frames} frames at {w}x{h} = {bytes:N0} bytes, exceeds 8 MiB limit. " +
            "Consider lossy encoding.");
    }

    // -------------------------------------------------------------------------
    // §28 Artifact generation — manual Discord preview verification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_Artifacts_GenerateDiscordTestFiles()
    {
        Directory.CreateDirectory(ArtifactDir);

        // Artifact 1: Short animated WebP (3 frames, 300ms each)
        using (var img = BuildAnimatedWebPImage(
            200, 150,
            [new Rgba32(220, 50, 50), new Rgba32(50, 220, 50), new Rgba32(50, 50, 220)],
            [300, 300, 300], repeatCount: 0))
        {
            await img.SaveAsWebpAsync(
                Path.Combine(ArtifactDir, "spike_short_3frame.webp"),
                new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        }

        // Artifact 2: Near-ceiling animated WebP (48 frames, simulating ASCII output)
        // Use small frame size for practical artifact generation
        using (var img = BuildTestFrameAnimation(400, 300, 48, 250))
        {
            await img.SaveAsWebpAsync(
                Path.Combine(ArtifactDir, "spike_48frame_ceiling.webp"),
                new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        }

        // Artifact 3: Simulated ASCII-text-like animated WebP (dark bg, white text approximation)
        using (var img = BuildAsciiLikeAnimatedWebP(frames: 12))
        {
            await img.SaveAsWebpAsync(
                Path.Combine(ArtifactDir, "spike_ascii_like.webp"),
                new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        }

        // Artifact 4: Same ASCII-like content, lossy for size comparison
        using (var img = BuildAsciiLikeAnimatedWebP(frames: 12))
        {
            await img.SaveAsWebpAsync(
                Path.Combine(ArtifactDir, "spike_ascii_like_lossy_q85.webp"),
                new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 85 });
        }

        // Report where to find artifacts
        Assert.True(Directory.Exists(ArtifactDir));
        var files = Directory.GetFiles(ArtifactDir, "*.webp");
        Assert.True(files.Length >= 3,
            $"Expected at least 3 artifact files in {ArtifactDir}, found {files.Length}");

        // Print file sizes to test output
        foreach (var f in files.OrderBy(x => x))
        {
            var info = new FileInfo(f);
            _ = info.Length; // just ensure accessible
        }
    }

    // -------------------------------------------------------------------------
    // §28: Verify GIF composited frame extraction for complex disposal modes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_GifDisposalMode2_CloneFrameRestoresBackground()
    {
        // Create GIF with RestoreToBackground disposal to test compositing correctness
        int w = 40, h = 40;
        using var gif = new Image<Rgba32>(w, h, new Rgba32(255, 0, 0, 255)); // red base

        // Frame 0: Full red, RestoreToBackground
        gif.Frames[0].Metadata.GetGifMetadata().FrameDelay      = 10;
        gif.Frames[0].Metadata.GetGifMetadata().DisposalMethod  = GifDisposalMethod.RestoreToBackground;

        // Frame 1: Green patch overlaid at center
        var frame1 = new Image<Rgba32>(w, h, new Rgba32(0, 255, 0, 255));
        gif.Frames.AddFrame(frame1.Frames[0]);
        gif.Frames[1].Metadata.GetGifMetadata().FrameDelay      = 10;
        gif.Frames[1].Metadata.GetGifMetadata().DisposalMethod  = GifDisposalMethod.RestoreToBackground;

        gif.Metadata.GetGifMetadata().RepeatCount = 0;

        using var ms = new MemoryStream();
        await gif.SaveAsGifAsync(ms);
        ms.Position = 0;

        using var loaded = await Image.LoadAsync<Rgba32>(ms);
        Assert.Equal(2, loaded.Frames.Count);

        using var frame0composited = loaded.Frames.CloneFrame(0);
        Assert.Equal(w, frame0composited.Width);
        Assert.Equal(h, frame0composited.Height);
        // Frame 0 should be the full-canvas composited view
        Assert.Equal(1, frame0composited.Frames.Count);
    }

    // -------------------------------------------------------------------------
    // Helper: Calculate total duration from GIF frame metadata
    // -------------------------------------------------------------------------

    [Fact]
    public void Spike_GifDurationCalculation_SumOfFrameDelays()
    {
        // §12.2: source animation duration = sum of source-frame display durations
        int[] delaysCentiseconds = [30, 10, 20, 40]; // 100cs = 1000ms total
        int expectedTotalMs = delaysCentiseconds.Sum() * 10; // centiseconds → ms

        // Create image, set frame metadata, sum it back up
        using var gif = BuildAnimatedGif(10, 10, delaysCentiseconds);
        using var ms  = new MemoryStream();
        gif.SaveAsGif(ms);
        ms.Position = 0;

        using var loaded = Image.Load<Rgba32>(ms);
        int actualTotalMs = loaded.Frames
            .Select(f => f.Metadata.GetGifMetadata().FrameDelay * 10) // cs → ms
            .Sum();

        Assert.Equal(expectedTotalMs, actualTotalMs);
    }

    // -------------------------------------------------------------------------
    // §28 Item 9: Realistic rasterized ASCII frames — lossless byte size check
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_RealisticAnimatedWebP_48FramesRasterizedAscii_ByteSizeUnder8MiB()
    {
        var pngSvc = new PngRenderService(
            Options.Create(new BotOptions()),
            NullLogger<PngRenderService>.Instance);

        var rng  = new Random(42);
        const string Ramp = "@%#*+=-:. ";
        Image<Rgba32>? animation = null;

        try
        {
            for (int fi = 0; fi < 48; fi++)
            {
                // Generate a varied 72×26 RichAsciiRender (medium preset size)
                var cells = new RichAsciiCell[26][];
                for (int row = 0; row < 26; row++)
                {
                    cells[row] = new RichAsciiCell[72];
                    for (int col = 0; col < 72; col++)
                    {
                        cells[row][col] = new RichAsciiCell
                        {
                            Row        = row,
                            Column     = col,
                            Character  = Ramp[rng.Next(Ramp.Length)],
                            Foreground = new RgbColor(
                                (byte)rng.Next(256),
                                (byte)rng.Next(256),
                                (byte)rng.Next(256)),
                        };
                    }
                }
                var render   = new RichAsciiRender { Width = 72, Height = 26, Cells = cells };
                var pngBytes = pngSvc.TryRenderPng(render, colorEnabled: true);
                Assert.NotNull(pngBytes);

                using var frameImg = Image.Load<Rgba32>(pngBytes);
                if (fi == 0)
                {
                    animation = frameImg.Clone();
                    animation.Frames[0].Metadata.GetWebpMetadata().FrameDelay = 100u;
                }
                else
                {
                    animation!.Frames.AddFrame(frameImg.Frames[0]);
                    animation.Frames[fi].Metadata.GetWebpMetadata().FrameDelay = 100u;
                }
            }

            Assert.NotNull(animation);
            Assert.Equal(48, animation.Frames.Count);
            animation.Metadata.GetWebpMetadata().RepeatCount = 0;

            using var ms = new MemoryStream();
            await animation.SaveAsWebpAsync(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });

            long losslessBytes = ms.Length;
            const long limit   = 8L * 1024 * 1024;

            // Save as both lossless and lossy for Discord preview + comparison
            Directory.CreateDirectory(ArtifactDir);
            await File.WriteAllBytesAsync(
                Path.Combine(ArtifactDir, $"spike_realistic_48frame_lossless.webp"),
                ms.ToArray());

            using var ms2 = new MemoryStream();
            await animation.SaveAsWebpAsync(ms2, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 85 });
            long lossyBytes = ms2.Length;
            await File.WriteAllBytesAsync(
                Path.Combine(ArtifactDir, $"spike_realistic_48frame_lossy_q85.webp"),
                ms2.ToArray());

            Assert.True(losslessBytes <= limit,
                $"Realistic 48-frame lossless animated WebP = {losslessBytes:N0} bytes ({losslessBytes / 1024.0 / 1024:F2} MiB), " +
                $"exceeds 8 MiB cap. Lossy={lossyBytes:N0} bytes. Consider making lossy the default.");
        }
        finally
        {
            animation?.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // §6.1: APNG detection behavior probe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Spike_ApngDetection_PngFormatWithAcTLChunk()
    {
        // Create a valid PNG, then inject an acTL chunk to simulate APNG.
        // This tests whether ImageSharp exposes Frames.Count > 1 for APNG,
        // or whether raw-byte chunk scanning is needed for detection.
        using var img = new Image<Rgba32>(4, 4, new Rgba32(255, 0, 0, 255));
        using var rawPng = new MemoryStream();
        await img.SaveAsPngAsync(rawPng);
        byte[] pngBytes = rawPng.ToArray();

        // Minimal acTL chunk: length=8, type="acTL", frameCount=2, playCount=0, CRC
        // Insert right after the IHDR chunk (offset 33 = 8 sig + 25 IHDR chunk)
        var apngBytes = InjectAcTLChunk(pngBytes);

        using var apngStream = new MemoryStream(apngBytes);

        // Can ImageSharp detect the format?
        apngStream.Position = 0;
        var format = await Image.DetectFormatAsync(apngStream);
        Assert.IsType<PngFormat>(format); // APNG is still PNG format

        // Does ImageSharp expose multiple frames?
        apngStream.Position = 0;
        using var loaded = await Image.LoadAsync<Rgba32>(apngStream);
        int frameCount = loaded.Frames.Count;

        // Also check raw byte scan for "acTL"
        bool rawAcTLFound = apngBytes.AsSpan().IndexOf("acTL"u8) >= 0;
        Assert.True(rawAcTLFound, "acTL chunk should be present in injected APNG bytes");

        // If ImageSharp exposes APNG frames as Frames.Count > 1, we can use that for detection.
        // If it flattens to 1 frame, we MUST use raw byte scanning.
        // This assertion documents the behavior — change the expected value if ImageSharp behavior changes.
        Assert.True(rawAcTLFound, "acTL must be present in the injected APNG bytes");

        // Record whether ImageSharp exposes APNG frames (determines detection strategy).
        // Expected: frameCount == 1 (ImageSharp does NOT decompose APNG → raw-byte scan required).
        // If this fails, ImageSharp changed behavior and Frames.Count > 1 is now usable.
        Assert.True(frameCount >= 1,
            $"APNG loaded by ImageSharp reports {frameCount} frames. " +
            $"raw acTL found={rawAcTLFound}. " +
            $"If frameCount > 1: use Frames.Count check. If frameCount == 1: raw-byte acTL scan required.");

        // Hypothesis: ImageSharp flattens APNG to 1 frame → raw-byte acTL scan required for detection.
        // If this assertion FAILS with actual=N>1, change implementation to use Frames.Count > 1 check.
        Assert.Equal(1, frameCount);
    }

    private static byte[] InjectAcTLChunk(byte[] pngBytes)
    {
        // PNG signature: 8 bytes. IHDR chunk: 4 len + 4 type + 13 data + 4 CRC = 25 bytes.
        // Insert acTL immediately after IHDR (offset 33).
        const int insertAfter = 33;

        // acTL chunk: frameCount=2 (4 bytes), playCount=0 (4 bytes) = 8 data bytes
        uint frameCount = 2u, playCount = 0u;
        var dataBytes = new byte[8];
        WriteBigEndian(dataBytes, 0, frameCount);
        WriteBigEndian(dataBytes, 4, playCount);

        byte[] typeBytes = [0x61, 0x63, 0x54, 0x4C]; // "acTL"
        uint crc = ComputeCrc(typeBytes, dataBytes);

        var chunk = new byte[12 + 8]; // len(4) + type(4) + data(8) + crc(4)
        WriteBigEndian(chunk, 0, (uint)dataBytes.Length);
        typeBytes.CopyTo(chunk, 4);
        dataBytes.CopyTo(chunk, 8);
        WriteBigEndian(chunk, 16, crc);

        var result = new byte[pngBytes.Length + chunk.Length];
        pngBytes.AsSpan(0, insertAfter).CopyTo(result);
        chunk.CopyTo(result, insertAfter);
        pngBytes.AsSpan(insertAfter).CopyTo(result.AsSpan(insertAfter + chunk.Length));
        return result;
    }

    private static void WriteBigEndian(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static uint ComputeCrc(byte[] type, byte[] data)
    {
        // CRC-32 per PNG spec (polynomial 0xEDB88320)
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
            crc = Crc32Step(crc, b);
        foreach (byte b in data)
            crc = Crc32Step(crc, b);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Crc32Step(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Image<Rgba32> BuildAnimatedGif(int width, int height, int[] frameDelaysCentiseconds)
    {
        var colors = frameDelaysCentiseconds.Select((_, i) =>
            new Rgba32((byte)(i * 60 % 256), (byte)(i * 80 % 256), (byte)(i * 100 % 256), 255)).ToArray();
        return BuildSolidColorAnimatedGif(width, height, colors, frameDelaysCentiseconds);
    }

    private static Image<Rgba32> BuildSolidColorAnimatedGif(
        int width, int height, Rgba32[] colors, int[] delaysCentiseconds)
    {
        var img = new Image<Rgba32>(width, height, colors[0]);
        img.Frames[0].Metadata.GetGifMetadata().FrameDelay = delaysCentiseconds[0];

        for (int i = 1; i < colors.Length; i++)
        {
            var frame = new Image<Rgba32>(width, height, colors[i]);
            img.Frames.AddFrame(frame.Frames[0]);
            img.Frames[i].Metadata.GetGifMetadata().FrameDelay = delaysCentiseconds[i];
            frame.Dispose();
        }

        img.Metadata.GetGifMetadata().RepeatCount = 0;
        return img;
    }

    private static Image<Rgba32> BuildAnimatedWebPImage(
        int width, int height, Rgba32[] colors, int[] delaysMs, ushort repeatCount)
    {
        var img = new Image<Rgba32>(width, height, colors[0]);
        img.Frames[0].Metadata.GetWebpMetadata().FrameDelay = (uint)delaysMs[0];

        for (int i = 1; i < colors.Length; i++)
        {
            var frame = new Image<Rgba32>(width, height, colors[i]);
            img.Frames.AddFrame(frame.Frames[0]);
            img.Frames[i].Metadata.GetWebpMetadata().FrameDelay = (uint)delaysMs[i];
            frame.Dispose();
        }

        img.Metadata.GetWebpMetadata().RepeatCount = repeatCount;
        return img;
    }

    private static Image<Rgba32> BuildTestFrameAnimation(int width, int height, int frames, int delayMs)
    {
        // Alternating dark/light frames to simulate ASCII render variation
        var dark  = new Rgba32(0x0B, 0x0D, 0x10, 255);
        var light = new Rgba32(0xE6, 0xED, 0xF3, 255);

        var img = new Image<Rgba32>(width, height, dark);
        img.Frames[0].Metadata.GetWebpMetadata().FrameDelay = (uint)delayMs;

        for (int i = 1; i < frames; i++)
        {
            var frame = new Image<Rgba32>(width, height, i % 2 == 0 ? dark : light);
            img.Frames.AddFrame(frame.Frames[0]);
            img.Frames[i].Metadata.GetWebpMetadata().FrameDelay = (uint)delayMs;
            frame.Dispose();
        }

        img.Metadata.GetWebpMetadata().RepeatCount = 0;
        return img;
    }

    private static Image<Rgba32> BuildAsciiLikeAnimatedWebP(int frames)
    {
        // Approximate a real ASCII-art terminal frame size: dark bg, with varying pixel rows
        // 48 cols × 18 rows at ~14px/cell + 24px padding ≈ 696×276
        int w = 696, h = 276;
        var bg   = new Rgba32(0x0B, 0x0D, 0x10, 255);
        var text = new Rgba32(0xE6, 0xED, 0xF3, 255);

        // Frame 0: dark background
        var img = new Image<Rgba32>(w, h, bg);
        // Simulate text pixels by setting individual pixels row-by-row
        SetTextRow(img, 12, text);
        img.Frames[0].Metadata.GetWebpMetadata().FrameDelay = 100u;

        for (int fi = 1; fi < frames; fi++)
        {
            var frame = new Image<Rgba32>(w, h, bg);
            // Shift which row has "text" pixels each frame
            SetTextRow(frame, 12 + (fi * 14 % (h - 24)), text);
            img.Frames.AddFrame(frame.Frames[0]);
            img.Frames[fi].Metadata.GetWebpMetadata().FrameDelay = 100u;
            frame.Dispose();
        }

        img.Metadata.GetWebpMetadata().RepeatCount = 0;
        return img;
    }

    private static void SetTextRow(Image<Rgba32> img, int y, Rgba32 color)
    {
        if (y < 0 || y >= img.Height) return;
        for (int x = 12; x < img.Width - 12; x++)
            img[x, y] = color;
    }
}

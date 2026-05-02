using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

/// <summary>§25.2 Animated validation: format routing, APNG detection, canvas dimensions.</summary>
public sealed class AnimationValidationTests
{
    private static ImageValidationService MakeService(int maxWidth = 4096, int maxHeight = 4096) =>
        new(Options.Create(new BotOptions
        {
            MaxDecodedImageWidth  = maxWidth,
            MaxDecodedImageHeight = maxHeight,
        }), NullLogger<ImageValidationService>.Instance);

    // ─── Format routing ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnimatedGif_RoutesToAnimatedOk()
    {
        var svc    = MakeService();
        using var stream = CreateAnimatedGif(4, 4, 3);
        var result = await svc.ValidateAsync(stream);
        var ok = Assert.IsType<ValidationResult.AnimatedOk>(result);
        ok.Image.Dispose();
        Assert.IsType<GifFormat>(ok.Format);
    }

    [Fact]
    public async Task AnimatedWebP_RoutesToAnimatedOk()
    {
        var svc    = MakeService();
        using var stream = CreateAnimatedWebP(4, 4, 3, 100);
        var result = await svc.ValidateAsync(stream);
        var ok = Assert.IsType<ValidationResult.AnimatedOk>(result);
        ok.Image.Dispose();
        Assert.IsType<WebpFormat>(ok.Format);
    }

    [Fact]
    public async Task StaticGif_RoutesToOk()
    {
        var svc    = MakeService();
        using var stream = CreateStaticGif(4, 4);
        var result = await svc.ValidateAsync(stream);
        var ok = Assert.IsType<ValidationResult.Ok>(result);
        ok.Image.Dispose();
    }

    [Fact]
    public async Task StaticWebP_RoutesToOk()
    {
        var svc    = MakeService();
        using var stream = CreateStaticWebP(4, 4);
        var result = await svc.ValidateAsync(stream);
        var ok = Assert.IsType<ValidationResult.Ok>(result);
        ok.Image.Dispose();
    }

    // ─── APNG rejection ──────────────────────────────────────────────────────

    [Fact]
    public async Task Png_WithApngChunk_ReturnsError()
    {
        var svc      = MakeService();
        var pngBytes = CreateApngBytes();
        using var stream = new MemoryStream(pngBytes);
        var result   = await svc.ValidateAsync(stream);
        var error    = Assert.IsType<ValidationResult.Error>(result);
        Assert.Contains("not supported", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Png_WithoutApngChunk_RoutesToOk()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(4, 4);
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        var result = await svc.ValidateAsync(ms);
        var ok = Assert.IsType<ValidationResult.Ok>(result);
        ok.Image.Dispose();
    }

    // ─── Canvas dimension limits for animated ────────────────────────────────

    [Fact]
    public async Task AnimatedGif_ExceedingMaxWidth_ReturnsError()
    {
        var svc    = MakeService(maxWidth: 10);
        using var stream = CreateAnimatedGif(11, 4, 2);
        var result = await svc.ValidateAsync(stream);
        var error  = Assert.IsType<ValidationResult.Error>(result);
        Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnimatedGif_ExceedingMaxHeight_ReturnsError()
    {
        var svc    = MakeService(maxHeight: 10);
        using var stream = CreateAnimatedGif(4, 11, 2);
        var result = await svc.ValidateAsync(stream);
        var error  = Assert.IsType<ValidationResult.Error>(result);
        Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnimatedGif_AtExactMaxDimension_RoutesToAnimatedOk()
    {
        var svc    = MakeService(maxWidth: 10, maxHeight: 10);
        using var stream = CreateAnimatedGif(10, 10, 2);
        var result = await svc.ValidateAsync(stream);
        var ok = Assert.IsType<ValidationResult.AnimatedOk>(result);
        ok.Image.Dispose();
    }

    // ─── OriginalBytes preserved ──────────────────────────────────────────────

    [Fact]
    public async Task AnimatedGif_RetainsOriginalBytes()
    {
        var svc    = MakeService();
        using var stream = CreateAnimatedGif(4, 4, 2);
        var expected = stream.ToArray();
        stream.Position = 0;
        var result = await svc.ValidateAsync(stream);
        var ok = Assert.IsType<ValidationResult.AnimatedOk>(result);
        ok.Image.Dispose();
        Assert.Equal(expected, ok.OriginalBytes);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static MemoryStream CreateAnimatedGif(int width, int height, int frameCount)
    {
        using var img = new Image<Rgba32>(width, height);
        for (int i = 1; i < frameCount; i++)
            img.Frames.AddFrame(new Image<Rgba32>(width, height).Frames.RootFrame);
        var ms = new MemoryStream();
        img.SaveAsGif(ms, new GifEncoder());
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateAnimatedWebP(int width, int height, int frameCount, uint frameDelayMs)
    {
        using var img = new Image<Rgba32>(width, height);
        img.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = frameDelayMs;
        for (int i = 1; i < frameCount; i++)
        {
            using var frame = new Image<Rgba32>(width, height);
            frame.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = frameDelayMs;
            img.Frames.AddFrame(frame.Frames.RootFrame);
        }
        img.Metadata.GetWebpMetadata().RepeatCount = 0;
        var ms = new MemoryStream();
        img.SaveAsWebp(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateStaticGif(int width, int height)
    {
        using var img = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        img.SaveAsGif(ms);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateStaticWebP(int width, int height)
    {
        using var img = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        img.SaveAsWebp(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        ms.Position = 0;
        return ms;
    }

    private static byte[] CreateApngBytes()
    {
        using var img = new Image<Rgba32>(4, 4);
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return InjectAcTLChunk(ms.ToArray());
    }

    private static byte[] InjectAcTLChunk(byte[] pngBytes)
    {
        int pos = 8;
        int idatPos = -1;
        while (pos + 8 <= pngBytes.Length)
        {
            int len = (pngBytes[pos] << 24) | (pngBytes[pos + 1] << 16) |
                      (pngBytes[pos + 2] << 8)  |  pngBytes[pos + 3];
            if (len < 0) break;
            if (pngBytes[pos + 4] == 0x49 && pngBytes[pos + 5] == 0x44 &&
                pngBytes[pos + 6] == 0x41 && pngBytes[pos + 7] == 0x54)
            {
                idatPos = pos;
                break;
            }
            pos += 8 + len + 4;
        }
        if (idatPos < 0) throw new InvalidOperationException("IDAT not found");

        byte[] type = { 0x61, 0x63, 0x54, 0x4C }; // acTL
        byte[] data = { 0, 0, 0, 1, 0, 0, 0, 0 }; // numFrames=1, numPlays=0
        uint crc = ComputeCrc(type, data);

        byte[] chunk = new byte[20];
        chunk[0] = 0; chunk[1] = 0; chunk[2] = 0; chunk[3] = 8;
        chunk[4] = type[0]; chunk[5] = type[1]; chunk[6] = type[2]; chunk[7] = type[3];
        for (int i = 0; i < 8; i++) chunk[8 + i] = data[i];
        chunk[16] = (byte)(crc >> 24); chunk[17] = (byte)(crc >> 16);
        chunk[18] = (byte)(crc >> 8);  chunk[19] = (byte)crc;

        var result = new byte[pngBytes.Length + 20];
        pngBytes.AsSpan(0, idatPos).CopyTo(result);
        chunk.AsSpan().CopyTo(result.AsSpan(idatPos));
        pngBytes.AsSpan(idatPos).CopyTo(result.AsSpan(idatPos + 20));
        return result;
    }

    private static uint ComputeCrc(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1; }
        foreach (byte b in data) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1; }
        return crc ^ 0xFFFFFFFF;
    }
}

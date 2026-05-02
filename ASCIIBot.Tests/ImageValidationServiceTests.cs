using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

public sealed class ImageValidationServiceTests
{
    private static ImageValidationService MakeService() =>
        new(NullLogger<ImageValidationService>.Instance);

    // --- Valid static images ---

    [Fact]
    public async Task ValidateAsync_ValidPng_ReturnsOk()
    {
        var svc    = MakeService();
        using var stream = CreatePng(1, 1);
        var result = await svc.ValidateAsync(stream);
        Assert.IsType<ValidationResult.Ok>(result);
        ((ValidationResult.Ok)result).Image.Dispose();
    }

    [Fact]
    public async Task ValidateAsync_ValidJpeg_ReturnsOk()
    {
        var svc    = MakeService();
        using var stream = CreateJpeg(1, 1);
        var result = await svc.ValidateAsync(stream);
        Assert.IsType<ValidationResult.Ok>(result);
        ((ValidationResult.Ok)result).Image.Dispose();
    }

    [Fact]
    public async Task ValidateAsync_StaticGif_ReturnsOk()
    {
        var svc    = MakeService();
        using var stream = CreateStaticGif(1, 1);
        var result = await svc.ValidateAsync(stream);
        Assert.IsType<ValidationResult.Ok>(result);
        ((ValidationResult.Ok)result).Image.Dispose();
    }

    // --- Invalid / rejected inputs ---

    [Fact]
    public async Task ValidateAsync_RandomBytes_ReturnsError()
    {
        var svc = MakeService();
        using var stream = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });
        var result = await svc.ValidateAsync(stream);
        Assert.IsType<ValidationResult.Error>(result);
    }

    [Fact]
    public async Task ValidateAsync_AnimatedGif_ReturnsError()
    {
        var svc    = MakeService();
        using var stream = CreateAnimatedGif(2, 2, frameCount: 2);
        var result = await svc.ValidateAsync(stream);
        var error  = Assert.IsType<ValidationResult.Error>(result);
        Assert.Contains("Animated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_ImageWidthExceedsMax_ReturnsError()
    {
        var svc    = MakeService();
        using var stream = CreatePng(4097, 1);
        var result = await svc.ValidateAsync(stream);
        var error  = Assert.IsType<ValidationResult.Error>(result);
        Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_ImageHeightExceedsMax_ReturnsError()
    {
        var svc    = MakeService();
        using var stream = CreatePng(1, 4097);
        var result = await svc.ValidateAsync(stream);
        var error  = Assert.IsType<ValidationResult.Error>(result);
        Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_ImageWithinMaxDimensions_ReturnsOk()
    {
        var svc    = MakeService();
        using var stream = CreatePng(4096, 4096);
        var result = await svc.ValidateAsync(stream);
        Assert.IsType<ValidationResult.Ok>(result);
        ((ValidationResult.Ok)result).Image.Dispose();
    }

    // --- Helpers ---

    private static MemoryStream CreatePng(int width, int height)
    {
        using var img = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream CreateJpeg(int width, int height)
    {
        using var img = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
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

    private static MemoryStream CreateAnimatedGif(int width, int height, int frameCount)
    {
        using var img = new Image<Rgba32>(width, height);

        // Add extra frames to make it animated
        for (var i = 1; i < frameCount; i++)
        {
            var frame = new Image<Rgba32>(width, height).Frames.RootFrame;
            img.Frames.AddFrame(frame);
        }

        var ms = new MemoryStream();
        var encoder = new GifEncoder();
        img.SaveAsGif(ms, encoder);
        ms.Position = 0;
        return ms;
    }
}

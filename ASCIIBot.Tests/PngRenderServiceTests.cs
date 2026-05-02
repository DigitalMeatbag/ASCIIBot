using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

public sealed class PngRenderServiceTests
{
    private static PngRenderService MakeService(
        int pngByteLimit  = 8_388_608,
        int pngMaxWidth   = 4096,
        int pngMaxHeight  = 4096)
    {
        var opts = Options.Create(new BotOptions
        {
            RenderPngByteLimit   = pngByteLimit,
            RenderPngMaxWidth    = pngMaxWidth,
            RenderPngMaxHeight   = pngMaxHeight,
            TotalUploadByteLimit = 12_582_912,
        });
        return new PngRenderService(opts, NullLogger<PngRenderService>.Instance);
    }

    private static RichAsciiRender MakeRender(int cols, int rows, byte r = 200, byte g = 200, byte b = 200)
    {
        var cells = new RichAsciiCell[rows][];
        for (var row = 0; row < rows; row++)
        {
            cells[row] = new RichAsciiCell[cols];
            for (var col = 0; col < cols; col++)
            {
                cells[row][col] = new RichAsciiCell
                {
                    Row        = row,
                    Column     = col,
                    Character  = 'X',
                    Foreground = new RgbColor(r, g, b),
                };
            }
        }
        return new RichAsciiRender { Width = cols, Height = rows, Cells = cells };
    }

    // ─── PNG is produced ─────────────────────────────────────────────────────

    [Fact]
    public void TryRenderPng_SmallRender_ReturnsBytes()
    {
        var svc    = MakeService();
        var render = MakeRender(10, 5);
        var bytes  = svc.TryRenderPng(render, colorEnabled: true);
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0);
    }

    [Fact]
    public void TryRenderPng_OutputIsValidPng()
    {
        var svc    = MakeService();
        var render = MakeRender(5, 3);
        var bytes  = svc.TryRenderPng(render, colorEnabled: true);
        Assert.NotNull(bytes);

        using var ms  = new MemoryStream(bytes!);
        using var img = Image.Load<Rgba32>(ms);
        Assert.True(img.Width  > 0);
        Assert.True(img.Height > 0);
    }

    // ─── Dimension limits ────────────────────────────────────────────────────

    [Fact]
    public void TryRenderPng_ExceedsPngMaxWidth_ReturnsNull()
    {
        var svc    = MakeService(pngMaxWidth: 1);
        var render = MakeRender(10, 5);
        var bytes  = svc.TryRenderPng(render, colorEnabled: false);
        Assert.Null(bytes);
    }

    [Fact]
    public void TryRenderPng_ExceedsPngMaxHeight_ReturnsNull()
    {
        var svc    = MakeService(pngMaxHeight: 1);
        var render = MakeRender(5, 10);
        var bytes  = svc.TryRenderPng(render, colorEnabled: false);
        Assert.Null(bytes);
    }

    // ─── Byte limit ──────────────────────────────────────────────────────────

    [Fact]
    public void TryRenderPng_ExceedsByteLimit_ReturnsNull()
    {
        var svc    = MakeService(pngByteLimit: 1);
        var render = MakeRender(5, 3);
        var bytes  = svc.TryRenderPng(render, colorEnabled: false);
        Assert.Null(bytes);
    }

    [Fact]
    public void TryRenderPng_WithinByteLimit_ReturnsBytes()
    {
        var svc    = MakeService(pngByteLimit: 8_388_608);
        var render = MakeRender(5, 3);
        var bytes  = svc.TryRenderPng(render, colorEnabled: true);
        Assert.NotNull(bytes);
    }

    // ─── Color vs monochrome ──────────────────────────────────────────────────

    [Fact]
    public void TryRenderPng_ColorEnabled_ReturnsNonNullBytes()
    {
        var svc    = MakeService();
        var render = MakeRender(5, 3, r: 255, g: 0, b: 0); // bright red
        var bytes  = svc.TryRenderPng(render, colorEnabled: true);
        Assert.NotNull(bytes);
    }

    [Fact]
    public void TryRenderPng_ColorDisabled_ReturnsNonNullBytes()
    {
        var svc    = MakeService();
        var render = MakeRender(5, 3, r: 255, g: 0, b: 0);
        var bytes  = svc.TryRenderPng(render, colorEnabled: false);
        Assert.NotNull(bytes);
    }

    // ─── PNG foreground contrast floor ───────────────────────────────────────

    [Fact]
    public void TryRenderPng_VeryDarkForeground_StillProducesValidPng()
    {
        // dark foreground (luma < 96) should be blended toward monochrome fg — no exception
        var svc    = MakeService();
        var render = MakeRender(3, 2, r: 5, g: 5, b: 5); // very dark
        var bytes  = svc.TryRenderPng(render, colorEnabled: true);
        Assert.NotNull(bytes);
    }

    // ─── Large preset dimensions ──────────────────────────────────────────────

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    public void TryRenderPng_EachSizePreset_ProducesValidPng(string sizeName)
    {
        using var img = new SixLabors.ImageSharp.Image<Rgba32>(200, 200);
        var svc       = MakeService();
        var preset    = SizePreset.FromString(sizeName);
        var renderer  = new AsciiRenderService();
        var render    = renderer.Render(img, preset, DetailPreset.Normal);

        var bytes = svc.TryRenderPng(render, colorEnabled: true);
        Assert.NotNull(bytes);

        using var ms    = new MemoryStream(bytes!);
        using var pngImg = Image.Load<Rgba32>(ms);
        Assert.True(pngImg.Width  <= 4096);
        Assert.True(pngImg.Height <= 4096);
    }
}

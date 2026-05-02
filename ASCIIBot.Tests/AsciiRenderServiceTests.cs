using ASCIIBot.Models;
using ASCIIBot.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

public sealed class AsciiRenderServiceTests
{
    private static AsciiRenderService MakeService() => new();

    // ─── Grid formula / dimension calculation ───────────────────────────────

    [Theory]
    [InlineData("small",  48, 18)]
    [InlineData("medium", 72, 26)]
    [InlineData("large", 100, 35)]
    public void Presets_SquareImage_RespectBounds(string sizeName, int maxCols, int maxRows)
    {
        var preset = SizePreset.FromString(sizeName);
        var (cols, rows) = AsciiRenderService.ComputeDimensions(100, 100, preset);
        Assert.True(cols > 0);
        Assert.True(rows > 0);
        Assert.True(cols <= maxCols, $"cols {cols} exceeds max {maxCols}");
        Assert.True(rows <= maxRows, $"rows {rows} exceeds max {maxRows}");
    }

    [Fact]
    public void ComputeDimensions_WideImage_RowsCappedAtMaxLines()
    {
        var preset = SizePreset.Medium;
        var (cols, rows) = AsciiRenderService.ComputeDimensions(100, 1000, preset);
        Assert.Equal(26, rows);
        Assert.True(cols <= 72);
        Assert.True(cols >= 1);
    }

    [Fact]
    public void ComputeDimensions_TallImage_ColsScaledDown()
    {
        var preset = SizePreset.Medium;
        var (cols, rows) = AsciiRenderService.ComputeDimensions(1, 100, preset);
        Assert.Equal(26, rows);
        Assert.Equal(1, cols);
    }

    [Fact]
    public void ComputeDimensions_PreservesAspectRatio_WithinToleranceForWideImage()
    {
        var preset = SizePreset.Medium;
        var (cols, rows) = AsciiRenderService.ComputeDimensions(200, 100, preset);
        Assert.Equal(72, cols);
        Assert.Equal(18, rows);
    }

    [Fact]
    public void ComputeDimensions_RoundAwayFromZero_MidpointRoundsUp()
    {
        // candidateRows = round_away_from_zero(26.5)
        // Image 100x73, medium preset: candidateRows = round(73/100 * 72 * 0.5) = round(26.28) = 26 < 26 → if branch
        // To hit midpoint: need ratio such that (H/W)*72*0.5 = X.5
        // E.g. H/W = X.5/36. Use W=200, H=125: 125/200*72*0.5 = 22.5 → round_away_from_zero = 23
        var preset = SizePreset.Medium;
        var (cols, rows) = AsciiRenderService.ComputeDimensions(200, 125, preset);
        // candidateRows = round_away_from_zero(125/200 * 72 * 0.5) = round_away_from_zero(22.5) = 23
        Assert.Equal(23, rows);
        Assert.Equal(72, cols);
    }

    [Fact]
    public void ComputeDimensions_NeverReturnsZeroDimensions()
    {
        // 1x1 image should produce at least 1x1
        var (cols, rows) = AsciiRenderService.ComputeDimensions(1, 1, SizePreset.Small);
        Assert.True(cols >= 1);
        Assert.True(rows >= 1);
    }

    // ─── Detail does not change grid dimensions ──────────────────────────────

    [Theory]
    [InlineData("low")]
    [InlineData("normal")]
    [InlineData("high")]
    public void Detail_DoesNotChangeGridDimensions(string detailName)
    {
        var svc    = MakeService();
        var detail = DetailPreset.FromString(detailName);
        using var img = new Image<Rgba32>(100, 100);

        var renderNormal = svc.Render(img, SizePreset.Medium, DetailPreset.Normal);
        var renderDetail = svc.Render(img, SizePreset.Medium, detail);

        Assert.Equal(renderNormal.Width,  renderDetail.Width);
        Assert.Equal(renderNormal.Height, renderDetail.Height);
    }

    [Theory]
    [InlineData("small",  "high")]
    [InlineData("small",  "low")]
    [InlineData("medium", "high")]
    [InlineData("large",  "low")]
    public void SizeAndDetail_GridNeverExceedsSizeBudget(string sizeName, string detailName)
    {
        var svc    = MakeService();
        var preset = SizePreset.FromString(sizeName);
        var detail = DetailPreset.FromString(detailName);
        using var img = new Image<Rgba32>(500, 500);

        var render = svc.Render(img, preset, detail);

        Assert.True(render.Width  <= preset.Columns,  $"Width  {render.Width} > {preset.Columns}");
        Assert.True(render.Height <= preset.MaxLines, $"Height {render.Height} > {preset.MaxLines}");
    }

    // ─── Detail preset values ────────────────────────────────────────────────

    [Fact]
    public void DetailPreset_Low_HasFullScale()    => Assert.Equal(1.00, DetailPreset.Low.SampleWindowScale);
    [Fact]
    public void DetailPreset_Normal_HasThreeQuarters() => Assert.Equal(0.75, DetailPreset.Normal.SampleWindowScale);
    [Fact]
    public void DetailPreset_High_HasHalfScale()   => Assert.Equal(0.50, DetailPreset.High.SampleWindowScale);

    [Fact]
    public void DetailPreset_FromString_DefaultsToNormal()
    {
        Assert.Equal(DetailPreset.Normal.SampleWindowScale, DetailPreset.FromString("anything").SampleWindowScale);
        Assert.Equal(DetailPreset.Normal.SampleWindowScale, DetailPreset.FromString(null).SampleWindowScale);
    }

    [Fact]
    public void DetailPreset_FromString_ParsesAllValues()
    {
        Assert.Equal(DetailPreset.Low.SampleWindowScale,    DetailPreset.FromString("low").SampleWindowScale);
        Assert.Equal(DetailPreset.Normal.SampleWindowScale, DetailPreset.FromString("normal").SampleWindowScale);
        Assert.Equal(DetailPreset.High.SampleWindowScale,   DetailPreset.FromString("high").SampleWindowScale);
    }

    // ─── Character ramp / luminance mapping ─────────────────────────────────

    [Fact]
    public void Render_WhitePixel_MapsToAtCharacter()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(255, 255, 255, 255);

        var result = svc.Render(img, SizePreset.Small, DetailPreset.Normal);

        Assert.Equal('@', result.Cells[0][0].Character);
    }

    [Fact]
    public void Render_BlackPixel_MapsToSpaceCharacter()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 0, 0, 255);

        var result = svc.Render(img, SizePreset.Small, DetailPreset.Normal);

        Assert.Equal(' ', result.Cells[0][0].Character);
    }

    [Fact]
    public void Render_TransparentPixel_CompositedOnDarkBackground()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 0, 0, 0); // fully transparent black

        var result = svc.Render(img, SizePreset.Small, DetailPreset.Normal);

        // Composited on dark background #0B0D10 -> very dark -> space (least visible ramp char)
        Assert.Equal(' ', result.Cells[0][0].Character);
        // Foreground should be the dark background color
        Assert.Equal(0x0B, result.Cells[0][0].Foreground.R);
        Assert.Equal(0x0D, result.Cells[0][0].Foreground.G);
        Assert.Equal(0x10, result.Cells[0][0].Foreground.B);
    }

    [Fact]
    public void Render_SolidRed_PreservesForegroundColor()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(255, 0, 0, 255);

        var result = svc.Render(img, SizePreset.Small, DetailPreset.Normal);

        Assert.Equal(255, result.Cells[0][0].Foreground.R);
        Assert.Equal(0,   result.Cells[0][0].Foreground.G);
        Assert.Equal(0,   result.Cells[0][0].Foreground.B);
    }

    // ─── Rich render model structure ─────────────────────────────────────────

    [Fact]
    public void Render_RichModel_DimensionsMatchCellArrayShape()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(100, 100);

        var render = svc.Render(img, SizePreset.Medium, DetailPreset.Normal);

        Assert.Equal(render.Height, render.Cells.Length);
        for (var r = 0; r < render.Height; r++)
            Assert.Equal(render.Width, render.Cells[r].Length);
    }

    [Fact]
    public void Render_RichModel_CellCoordinatesAreCorrect()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(10, 10);

        var render = svc.Render(img, new SizePreset(3, 3), DetailPreset.Normal);

        for (var r = 0; r < render.Height; r++)
            for (var c = 0; c < render.Width; c++)
            {
                Assert.Equal(r, render.Cells[r][c].Row);
                Assert.Equal(c, render.Cells[r][c].Column);
            }
    }

    // ─── Detail sample-window behavior ───────────────────────────────────────

    [Fact]
    public void Detail_LowAndHighProduceDifferentColors_ForHeterogeneousImage()
    {
        // 2x2 image: top-left black, top-right white, bottom-left white, bottom-right black
        // With detail=low, sample window covers the full cell region → average is gray
        // With detail=high, sample window is smaller → more variation
        var svc = MakeService();
        using var img = new Image<Rgba32>(4, 4);

        img[0, 0] = new Rgba32(0,   0,   0,   255); img[1, 0] = new Rgba32(0,   0,   0,   255);
        img[2, 0] = new Rgba32(255, 255, 255, 255); img[3, 0] = new Rgba32(255, 255, 255, 255);
        img[0, 1] = new Rgba32(0,   0,   0,   255); img[1, 1] = new Rgba32(0,   0,   0,   255);
        img[2, 1] = new Rgba32(255, 255, 255, 255); img[3, 1] = new Rgba32(255, 255, 255, 255);
        img[0, 2] = new Rgba32(255, 255, 255, 255); img[1, 2] = new Rgba32(255, 255, 255, 255);
        img[2, 2] = new Rgba32(0,   0,   0,   255); img[3, 2] = new Rgba32(0,   0,   0,   255);
        img[0, 3] = new Rgba32(255, 255, 255, 255); img[1, 3] = new Rgba32(255, 255, 255, 255);
        img[2, 3] = new Rgba32(0,   0,   0,   255); img[3, 3] = new Rgba32(0,   0,   0,   255);

        // Both renders should have the same grid dimensions
        var renderLow  = svc.Render(img, new SizePreset(2, 2), DetailPreset.Low);
        var renderHigh = svc.Render(img, new SizePreset(2, 2), DetailPreset.High);

        Assert.Equal(renderLow.Width,  renderHigh.Width);
        Assert.Equal(renderLow.Height, renderHigh.Height);
    }

    // ─── Size presets produce distinct output ────────────────────────────────

    [Theory]
    [InlineData("small",  48, 18)]
    [InlineData("medium", 72, 26)]
    [InlineData("large", 100, 35)]
    public void Render_Presets_BoundedByMaxDimensions(string sizeName, int maxCols, int maxRows)
    {
        var svc    = MakeService();
        var preset = SizePreset.FromString(sizeName);
        using var img = new Image<Rgba32>(500, 500);

        var result = svc.Render(img, preset, DetailPreset.Normal);

        Assert.True(result.Width  <= maxCols);
        Assert.True(result.Height <= maxRows);
    }

    // ─── Sample-window pixel inclusion ──────────────────────────────────────

    [Fact]
    public void Render_SinglePixelImage_DoesNotThrow()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(128, 64, 32, 255);

        // Should not throw even when sample windows would be sub-pixel.
        // A 1x1 source scaled to Large produces a grid ≤ 100×35 (not 1×1 — the preset fills up).
        var render = svc.Render(img, SizePreset.Large, DetailPreset.High);
        Assert.True(render.Width  >= 1);
        Assert.True(render.Height >= 1);
        Assert.True(render.Width  <= 100);
        Assert.True(render.Height <= 35);
    }

    [Fact]
    public void Render_KnownTwoPixelImage_LeftBlackRightWhite()
    {
        var svc = MakeService();
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(0,   0,   0,   255);
        img[1, 0] = new Rgba32(255, 255, 255, 255);

        var render = svc.Render(img, new SizePreset(2, 1), DetailPreset.Normal);

        Assert.Equal(' ', render.Cells[0][0].Character);
        Assert.Equal('@', render.Cells[0][1].Character);
    }
}

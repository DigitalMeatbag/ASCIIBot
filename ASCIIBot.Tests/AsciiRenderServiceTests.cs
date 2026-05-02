using ASCIIBot.Models;
using ASCIIBot.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Tests;

public sealed class AsciiRenderServiceTests
{
    private static AsciiRenderService MakeService() => new();

    // --- Dimension calculation ---

    [Theory]
    [InlineData("small",  48, 18)]
    [InlineData("medium", 72, 26)]
    [InlineData("large", 100, 35)]
    public void Presets_SquareImage_RespectBounds(string sizeName, int maxCols, int maxRows)
    {
        var preset = SizePreset.FromString(sizeName);
        // Square image: rows = targetCols * 0.5, which exceeds maxLines for all presets,
        // so cols is recalculated. Both must stay within the preset's limits.
        var (cols, rows) = AsciiRenderService.ComputeDimensions(100, 100, preset);
        Assert.True(cols > 0);
        Assert.True(rows > 0);
        Assert.True(cols <= maxCols, $"cols {cols} exceeds max {maxCols}");
        Assert.True(rows <= maxRows, $"rows {rows} exceeds max {maxRows}");
    }

    [Fact]
    public void ComputeDimensions_WideImage_RowsCappedAtMaxLines()
    {
        var preset = SizePreset.Medium; // 72 cols, 26 max rows
        // Very wide: 1000x100 → raw rows = 72 * (100/1000) * 0.5 = 3.6 → 3 rows, well within limit
        // Tall: 100x1000 → raw rows = 72 * (1000/100) * 0.5 = 360 → must cap at 26
        var (cols, rows) = AsciiRenderService.ComputeDimensions(100, 1000, preset);
        Assert.Equal(26, rows);
        Assert.True(cols <= 72);
        Assert.True(cols >= 1);
    }

    [Fact]
    public void ComputeDimensions_TallImage_ColsScaledDown()
    {
        var preset = SizePreset.Medium; // 72 cols, 26 max rows
        // 1x100 image (extreme portrait): rows would be huge, cap at 26, cols should be tiny
        var (cols, rows) = AsciiRenderService.ComputeDimensions(1, 100, preset);
        Assert.Equal(26, rows);
        // cols = 26 * (1/100) * 2 = 0.52 → clamped to 1
        Assert.Equal(1, cols);
    }

    [Fact]
    public void ComputeDimensions_PreservesAspectRatio_WithinToleranceForWideImage()
    {
        var preset = SizePreset.Medium;
        // 200x100 wide image: rows = 72 * (100/200) * 0.5 = 18 (within 26)
        var (cols, rows) = AsciiRenderService.ComputeDimensions(200, 100, preset);
        Assert.Equal(72, cols);
        Assert.Equal(18, rows);
    }

    // --- Luminance and character mapping ---

    [Fact]
    public void Render_WhitePixel_MapsToSpaceCharacter()
    {
        var svc   = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(255, 255, 255, 255);

        var result = svc.Render(img, SizePreset.Small);

        Assert.Equal(' ', result.Chars[0][0]);
    }

    [Fact]
    public void Render_BlackPixel_MapsToAtCharacter()
    {
        var svc   = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 0, 0, 255);

        var result = svc.Render(img, SizePreset.Small);

        Assert.Equal('@', result.Chars[0][0]);
    }

    [Fact]
    public void Render_TransparentPixel_TreatedAsWhite()
    {
        var svc   = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(0, 0, 0, 0); // black but fully transparent

        var result = svc.Render(img, SizePreset.Small);

        // After compositing onto white: r=g=b=255 → space
        Assert.Equal(' ', result.Chars[0][0]);
    }

    [Fact]
    public void Render_KnownPattern_ProducesExpectedCharacters()
    {
        var svc   = MakeService();
        // 2-pixel wide, 1-pixel tall: left=black, right=white
        using var img = new Image<Rgba32>(2, 1);
        img[0, 0] = new Rgba32(0,   0,   0,   255);
        img[1, 0] = new Rgba32(255, 255, 255, 255);

        var result = svc.Render(img, new SizePreset(2, 1));

        Assert.Equal('@', result.Chars[0][0]);
        Assert.Equal(' ', result.Chars[0][1]);
    }

    [Fact]
    public void Render_ColorGrid_MatchesComposited()
    {
        var svc   = MakeService();
        using var img = new Image<Rgba32>(1, 1);
        img[0, 0] = new Rgba32(255, 0, 0, 255); // solid red

        var result = svc.Render(img, SizePreset.Small);

        Assert.Equal(255, result.Colors[0][0].R);
        Assert.Equal(0,   result.Colors[0][0].G);
        Assert.Equal(0,   result.Colors[0][0].B);
    }

    // --- Size presets produce distinct output ---

    [Theory]
    [InlineData("small",  48, 18)]
    [InlineData("medium", 72, 26)]
    [InlineData("large", 100, 35)]
    public void Render_Presets_BoundedByMaxDimensions(string sizeName, int maxCols, int maxRows)
    {
        var svc    = MakeService();
        var preset = SizePreset.FromString(sizeName);
        using var img = new Image<Rgba32>(500, 500);

        var result = svc.Render(img, preset);

        Assert.True(result.Columns <= maxCols);
        Assert.True(result.Rows    <= maxRows);
    }
}

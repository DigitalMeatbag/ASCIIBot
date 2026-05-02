using ASCIIBot.Models;
using ASCIIBot.Services;

namespace ASCIIBot.Tests;

public sealed class AnsiColorServiceTests
{
    private static AnsiColorService MakeService() => new();

    // --- Nearest color mapping ---

    [Fact]
    public void NearestColor_PureRed_MapsToStandardRed()
    {
        var svc   = MakeService();
        var color = svc.NearestColor(255, 0, 0);
        // Standard Red (31) RGB=(170,0,0): dist=7225. Bright Red (91) RGB=(255,85,85): dist=14450.
        Assert.Equal(31, color.ForegroundCode);
    }

    [Fact]
    public void NearestColor_PureWhite_MapsToBrightWhite()
    {
        var svc   = MakeService();
        var color = svc.NearestColor(255, 255, 255);
        Assert.Equal(97, color.ForegroundCode); // Bright White
    }

    [Fact]
    public void NearestColor_PureBlack_MapsToBlack()
    {
        var svc   = MakeService();
        var color = svc.NearestColor(0, 0, 0);
        Assert.Equal(30, color.ForegroundCode); // Black
    }

    [Fact]
    public void NearestColor_PureGreen_MapsToStandardGreen()
    {
        var svc   = MakeService();
        var color = svc.NearestColor(0, 255, 0);
        // Standard Green (32) RGB=(0,170,0): dist=7225. Bright Green (92) RGB=(85,255,85): dist=14450.
        Assert.Equal(32, color.ForegroundCode);
    }

    [Fact]
    public void NearestColor_PureBlue_MapsToStandardBlue()
    {
        var svc   = MakeService();
        var color = svc.NearestColor(0, 0, 255);
        // Standard Blue (34) RGB=(0,0,170): dist=7225. Bright Blue (94) RGB=(85,85,255): dist=14450.
        Assert.Equal(34, color.ForegroundCode);
    }

    // --- ANSI render building ---

    [Fact]
    public void BuildAnsiRender_SingleRow_EndsWithResetSequence()
    {
        var svc    = MakeService();
        var result = MakeRenderResult(new[] { (0, 0, 0) }); // 1 black pixel

        var output = svc.BuildAnsiRender(result);

        Assert.Contains("\x1b[0m", output);
    }

    [Fact]
    public void BuildAnsiRender_AllSameColor_OnlyOneEscapeSequencePerRow()
    {
        var svc    = MakeService();
        // 5 identical black pixels in a row → should produce one open escape + one reset
        var result = MakeRenderResult(new[] { (0,0,0), (0,0,0), (0,0,0), (0,0,0), (0,0,0) });

        var output = svc.BuildAnsiRender(result);

        // One opening escape (\x1b[Xm) + chars + reset (\x1b[0m)
        var openingEscapes = System.Text.RegularExpressions.Regex.Matches(output, @"\x1b\[\d+m").Count;
        // Opening escape for the run + the reset = 2 total
        Assert.Equal(2, openingEscapes);
    }

    [Fact]
    public void BuildAnsiRender_TwoColors_GroupsRuns()
    {
        var svc    = MakeService();
        // 3 black + 3 white pixels → 2 color groups
        var result = MakeRenderResult(new[]
        {
            (0,0,0), (0,0,0), (0,0,0),
            (255,255,255), (255,255,255), (255,255,255),
        });

        var output = svc.BuildAnsiRender(result);

        // Two opening escapes (black group + white group) + one reset = 3
        var escapes = System.Text.RegularExpressions.Regex.Matches(output, @"\x1b\[\d+m").Count;
        Assert.Equal(3, escapes);
    }

    [Fact]
    public void BuildAnsiRender_ContainsAllChars()
    {
        var svc    = MakeService();
        var result = MakeRenderResult(new[] { (0,0,0), (255,255,255) });
        var chars  = result.Chars.SelectMany(row => row).ToArray();

        var output = svc.BuildAnsiRender(result);

        // Strip ANSI escapes and newlines, check chars are present
        var stripped = System.Text.RegularExpressions.Regex.Replace(output, @"\x1b\[\d+m", "").Replace("\n", "");
        Assert.Equal(new string(chars), stripped);
    }

    private static AsciiRenderResult MakeRenderResult(IList<(int R, int G, int B)> pixels)
    {
        var cols   = pixels.Count;
        var chars  = new char[1][] { new char[cols] };
        var colors = new (byte R, byte G, byte B)[1][] { new (byte, byte, byte)[cols] };

        for (var i = 0; i < cols; i++)
        {
            chars[0][i]  = 'X';
            colors[0][i] = ((byte)pixels[i].R, (byte)pixels[i].G, (byte)pixels[i].B);
        }

        return new AsciiRenderResult
        {
            Chars   = chars,
            Colors  = colors,
            Columns = cols,
            Rows    = 1,
        };
    }
}

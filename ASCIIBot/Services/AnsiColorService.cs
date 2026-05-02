using ASCIIBot.Models;
using System.Text;

namespace ASCIIBot.Services;

public sealed class AnsiColorService
{
    private static readonly AnsiColor[] Palette =
    [
        new(  0,   0,   0, 30),   // black
        new(170,   0,   0, 31),   // red
        new(  0, 170,   0, 32),   // green
        new(170, 170,   0, 33),   // yellow
        new(  0,   0, 170, 34),   // blue
        new(170,   0, 170, 35),   // magenta
        new(  0, 170, 170, 36),   // cyan
        new(170, 170, 170, 37),   // white
        new( 85,  85,  85, 90),   // bright black
        new(255,  85,  85, 91),   // bright red
        new( 85, 255,  85, 92),   // bright green
        new(255, 255,  85, 93),   // bright yellow
        new( 85,  85, 255, 94),   // bright blue
        new(255,  85, 255, 95),   // bright magenta
        new( 85, 255, 255, 96),   // bright cyan
        new(255, 255, 255, 97),   // bright white
    ];

    public AnsiColor NearestColor(byte r, byte g, byte b)
    {
        AnsiColor best     = Palette[0];
        long      bestDist = long.MaxValue;

        foreach (var color in Palette)
        {
            var dr   = (long)r - color.R;
            var dg   = (long)g - color.G;
            var db   = (long)b - color.B;
            var dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = color;
            }
        }

        return best;
    }

    public string BuildAnsiRender(RichAsciiRender render)
    {
        var sb = new StringBuilder(render.Height * (render.Width * 8 + 8));

        for (var row = 0; row < render.Height; row++)
        {
            var runStart = 0;
            var firstFg  = render.Cells[row][0].Foreground;
            var runCode  = NearestColor(firstFg.R, firstFg.G, firstFg.B).ForegroundCode;

            for (var col = 1; col <= render.Width; col++)
            {
                int nextCode;
                if (col < render.Width)
                {
                    var fg = render.Cells[row][col].Foreground;
                    nextCode = NearestColor(fg.R, fg.G, fg.B).ForegroundCode;
                }
                else
                {
                    nextCode = -1; // flush sentinel
                }

                if (nextCode != runCode)
                {
                    sb.Append($"\x1b[{runCode}m");
                    for (var k = runStart; k < col; k++)
                        sb.Append(render.Cells[row][k].Character);
                    runStart = col;
                    runCode  = nextCode;
                }
            }

            sb.Append("\x1b[0m\n");
        }

        return sb.ToString();
    }

    public string BuildMonochromeAnsiRender(RichAsciiRender render)
    {
        var sb = new StringBuilder(render.Height * (render.Width + 1));
        for (var row = 0; row < render.Height; row++)
        {
            for (var col = 0; col < render.Width; col++)
                sb.Append(render.Cells[row][col].Character);
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

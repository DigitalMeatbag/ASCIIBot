using ASCIIBot.Models;
using System.Text;

namespace ASCIIBot.Services;

public sealed class AnsiColorService
{
    private static readonly AnsiColor[] Palette =
    [
        new(  0,   0,   0, 30),   // Black
        new(170,   0,   0, 31),   // Red
        new(  0, 170,   0, 32),   // Green
        new(170, 170,   0, 33),   // Yellow
        new(  0,   0, 170, 34),   // Blue
        new(170,   0, 170, 35),   // Magenta
        new(  0, 170, 170, 36),   // Cyan
        new(170, 170, 170, 37),   // White
        new( 85,  85,  85, 90),   // Bright Black
        new(255,  85,  85, 91),   // Bright Red
        new( 85, 255,  85, 92),   // Bright Green
        new(255, 255,  85, 93),   // Bright Yellow
        new( 85,  85, 255, 94),   // Bright Blue
        new(255,  85, 255, 95),   // Bright Magenta
        new( 85, 255, 255, 96),   // Bright Cyan
        new(255, 255, 255, 97),   // Bright White
    ];

    public AnsiColor NearestColor(byte r, byte g, byte b)
    {
        AnsiColor best   = Palette[0];
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

    public string BuildAnsiRender(AsciiRenderResult result)
    {
        var sb = new StringBuilder(result.Rows * (result.Columns * 8 + 8));

        for (var y = 0; y < result.Rows; y++)
        {
            var runStart = 0;
            var runCode  = NearestColor(result.Colors[y][0].R, result.Colors[y][0].G, result.Colors[y][0].B).ForegroundCode;

            for (var x = 1; x <= result.Columns; x++)
            {
                int nextCode;
                if (x < result.Columns)
                {
                    var c = result.Colors[y][x];
                    nextCode = NearestColor(c.R, c.G, c.B).ForegroundCode;
                }
                else
                {
                    nextCode = -1; // flush sentinel
                }

                if (nextCode != runCode)
                {
                    sb.Append($"\x1b[{runCode}m");
                    sb.Append(result.Chars[y], runStart, x - runStart);
                    runStart = x;
                    runCode  = nextCode;
                }
            }

            sb.Append("\x1b[0m\n");
        }

        return sb.ToString();
    }
}

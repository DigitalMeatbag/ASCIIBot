using ASCIIBot.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ASCIIBot.Services;

public sealed class AsciiRenderService
{
    private const string Ramp = "@%#*+=-:. ";

    public AsciiRenderResult Render(Image<Rgba32> image, SizePreset preset)
    {
        image.Mutate(x => x.AutoOrient());

        var (cols, rows) = ComputeDimensions(image.Width, image.Height, preset);

        image.Mutate(x => x.Resize(cols, rows, KnownResamplers.Lanczos3));

        var chars  = new char[rows][];
        var colors = new (byte R, byte G, byte B)[rows][];

        for (var y = 0; y < rows; y++)
        {
            chars[y]  = new char[cols];
            colors[y] = new (byte, byte, byte)[cols];

            for (var x = 0; x < cols; x++)
            {
                var pixel = image[x, y];

                // Alpha-composite onto white
                var a  = pixel.A / 255.0;
                var r  = (byte)(pixel.R * a + 255 * (1 - a));
                var g  = (byte)(pixel.G * a + 255 * (1 - a));
                var b  = (byte)(pixel.B * a + 255 * (1 - a));

                var luma     = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                var charIdx  = Math.Min(Ramp.Length - 1, (int)(luma / 256.0 * Ramp.Length));
                chars[y][x]  = Ramp[charIdx];
                colors[y][x] = (r, g, b);
            }
        }

        return new AsciiRenderResult
        {
            Chars   = chars,
            Colors  = colors,
            Columns = cols,
            Rows    = rows,
        };
    }

    public static (int cols, int rows) ComputeDimensions(int imageW, int imageH, SizePreset preset)
    {
        var cols = preset.Columns;
        var rows = (int)(cols * (double)imageH / imageW * 0.5);

        if (rows > preset.MaxLines)
        {
            rows = preset.MaxLines;
            cols = (int)(rows * (double)imageW / imageH * 2.0);
        }

        // Ensure at least 1×1
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);

        return (cols, rows);
    }

    public static string ToPlainText(AsciiRenderResult result)
    {
        var sb = new System.Text.StringBuilder(result.Rows * (result.Columns + 1));
        for (var y = 0; y < result.Rows; y++)
        {
            sb.Append(result.Chars[y]);
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

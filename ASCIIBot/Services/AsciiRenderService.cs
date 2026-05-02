using ASCIIBot.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ASCIIBot.Services;

public sealed class AsciiRenderService
{
    private const string Ramp = " .:-=+*#%@";

    // Default dark background for transparent pixel compositing (#0B0D10)
    private const byte BgR = 0x0B;
    private const byte BgG = 0x0D;
    private const byte BgB = 0x10;

    public RichAsciiRender Render(Image<Rgba32> image, SizePreset size, DetailPreset detail)
    {
        image.Mutate(x => x.AutoOrient());
        var (cols, rows) = ComputeDimensions(image.Width, image.Height, size);
        return RenderFrame(image, cols, rows, detail);
    }

    // Renders a pre-oriented frame at explicit grid dimensions (used for animation frames).
    public RichAsciiRender RenderFrame(Image<Rgba32> image, int cols, int rows, DetailPreset detail)
    {
        var cells = new RichAsciiCell[rows][];

        for (var row = 0; row < rows; row++)
        {
            cells[row] = new RichAsciiCell[cols];
            for (var col = 0; col < cols; col++)
            {
                var (r, g, b) = SampleCell(image, col, row, cols, rows, detail.SampleWindowScale);
                var luma      = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                var charIdx   = Math.Min(Ramp.Length - 1, (int)(luma / 256.0 * Ramp.Length));

                cells[row][col] = new RichAsciiCell
                {
                    Row        = row,
                    Column     = col,
                    Character  = Ramp[charIdx],
                    Foreground = new RgbColor(r, g, b),
                };
            }
        }

        return new RichAsciiRender { Width = cols, Height = rows, Cells = cells };
    }

    private static (byte R, byte G, byte B) SampleCell(
        Image<Rgba32> image, int col, int row,
        int outputCols, int outputRows,
        double detailScale)
    {
        int srcW = image.Width;
        int srcH = image.Height;

        double cellLeft   = (double)col       * srcW / outputCols;
        double cellRight  = (double)(col + 1) * srcW / outputCols;
        double cellTop    = (double)row        * srcH / outputRows;
        double cellBottom = (double)(row + 1)  * srcH / outputRows;

        double cellCenterX = (cellLeft + cellRight)  / 2.0;
        double cellCenterY = (cellTop  + cellBottom) / 2.0;

        double windowWidth  = Math.Max(1.0, (cellRight  - cellLeft)   * detailScale);
        double windowHeight = Math.Max(1.0, (cellBottom - cellTop)    * detailScale);

        double sampleLeft   = Math.Clamp(cellCenterX - windowWidth  / 2.0, 0, srcW);
        double sampleRight  = Math.Clamp(cellCenterX + windowWidth  / 2.0, 0, srcW);
        double sampleTop    = Math.Clamp(cellCenterY - windowHeight / 2.0, 0, srcH);
        double sampleBottom = Math.Clamp(cellCenterY + windowHeight / 2.0, 0, srcH);

        // Collect all pixels whose center (x+0.5, y+0.5) falls in [sampleLeft, sampleRight) × [sampleTop, sampleBottom)
        int xMin = (int)Math.Max(0,        Math.Floor(sampleLeft));
        int xMax = (int)Math.Min(srcW - 1, Math.Ceiling(sampleRight));
        int yMin = (int)Math.Max(0,        Math.Floor(sampleTop));
        int yMax = (int)Math.Min(srcH - 1, Math.Ceiling(sampleBottom));

        double sumR = 0, sumG = 0, sumB = 0;
        int    count = 0;

        for (int py = yMin; py <= yMax; py++)
        {
            double cy = py + 0.5;
            if (cy < sampleTop || cy >= sampleBottom) continue;

            for (int px = xMin; px <= xMax; px++)
            {
                double cx = px + 0.5;
                if (cx < sampleLeft || cx >= sampleRight) continue;

                var (r, g, b) = CompositeOnDark(image[px, py]);
                sumR += r;
                sumG += g;
                sumB += b;
                count++;
            }
        }

        if (count > 0)
        {
            return (
                (byte)Math.Round(sumR / count, MidpointRounding.AwayFromZero),
                (byte)Math.Round(sumG / count, MidpointRounding.AwayFromZero),
                (byte)Math.Round(sumB / count, MidpointRounding.AwayFromZero)
            );
        }

        // Nearest-pixel fallback: find pixel nearest to (cellCenterX, cellCenterY)
        int nx = (int)Math.Clamp(Math.Floor(cellCenterX), 0, srcW - 1);
        int ny = (int)Math.Clamp(Math.Floor(cellCenterY), 0, srcH - 1);
        return CompositeOnDark(image[nx, ny]);
    }

    private static (byte R, byte G, byte B) CompositeOnDark(Rgba32 pixel)
    {
        var a = pixel.A / 255.0;
        var r = (byte)Math.Round(pixel.R * a + BgR * (1 - a), MidpointRounding.AwayFromZero);
        var g = (byte)Math.Round(pixel.G * a + BgG * (1 - a), MidpointRounding.AwayFromZero);
        var b = (byte)Math.Round(pixel.B * a + BgB * (1 - a), MidpointRounding.AwayFromZero);
        return (r, g, b);
    }

    public static (int cols, int rows) ComputeDimensions(int imageW, int imageH, SizePreset preset)
    {
        const double aspectCorrection = 0.5;

        int targetCols = preset.Columns;
        int maxRows    = preset.MaxLines;

        int candidateRows = RoundAwayFromZero((double)imageH / imageW * targetCols * aspectCorrection);
        candidateRows     = Math.Clamp(candidateRows, 1, maxRows);

        if (candidateRows < maxRows)
        {
            return (targetCols, candidateRows);
        }

        int outputRows = maxRows;
        int outputCols = RoundAwayFromZero((double)imageW / imageH * (outputRows / aspectCorrection));
        outputCols     = Math.Clamp(outputCols, 1, targetCols);
        return (outputCols, outputRows);
    }

    // round_away_from_zero: for non-negative values equivalent to floor(value + 0.5)
    public static int RoundAwayFromZero(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

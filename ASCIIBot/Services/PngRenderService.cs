using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Reflection;

namespace ASCIIBot.Services;

public sealed class PngRenderService
{
    private const int    FontSize       = 14;
    private const int    Padding        = 12;
    private static readonly Color Background        = Color.FromRgb(0x0B, 0x0D, 0x10);
    private static readonly Color MonochromeFg      = Color.FromRgb(0xE6, 0xED, 0xF3);
    private const double MonochromeLuma = 0.2126 * 0xE6 + 0.7152 * 0xED + 0.0722 * 0xF3; // ≈ 234.4
    private const double ContrastFloor = 96.0;

    private readonly BotOptions _options;
    private readonly ILogger<PngRenderService> _logger;
    private readonly Font _font;
    private readonly int  _cellWidth;
    private readonly int  _cellHeight;

    public PngRenderService(IOptions<BotOptions> options, ILogger<PngRenderService> logger)
    {
        _options = options.Value;
        _logger  = logger;

        var collection = new FontCollection();
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ASCIIBot.Assets.CascadiaMono-Regular.ttf")
            ?? throw new InvalidOperationException("Embedded font not found.");

        var family = collection.Add(stream);
        _font = family.CreateFont(FontSize);

        // Measure a single character to get stable cell dimensions
        var opts    = new TextOptions(_font);
        var metrics = TextMeasurer.MeasureBounds("M", opts);
        _cellWidth  = (int)Math.Ceiling(metrics.Width);
        _cellHeight = (int)Math.Ceiling(TextMeasurer.MeasureBounds("M\nM", opts).Height - metrics.Height);
        if (_cellHeight <= 0)
            _cellHeight = (int)Math.Ceiling(metrics.Height);
    }

    public byte[]? TryRenderPng(RichAsciiRender render, bool colorEnabled)
    {
        int imgWidth  = Padding * 2 + render.Width  * _cellWidth;
        int imgHeight = Padding * 2 + render.Height * _cellHeight;

        if (imgWidth  > _options.RenderPngMaxWidth ||
            imgHeight > _options.RenderPngMaxHeight)
        {
            _logger.LogDebug("PNG pixel dimensions {W}x{H} exceed limits, rejecting", imgWidth, imgHeight);
            return null;
        }

        using var image = new Image<Rgba32>(imgWidth, imgHeight);
        image.Mutate(ctx =>
        {
            ctx.Fill(Background);

            for (var row = 0; row < render.Height; row++)
            {
                for (var col = 0; col < render.Width; col++)
                {
                    var cell = render.Cells[row][col];
                    var fg   = colorEnabled
                        ? AdjustContrast(cell.Foreground)
                        : MonochromeFg;

                    float x = Padding + col * _cellWidth;
                    float y = Padding + row * _cellHeight;

                    ctx.DrawText(
                        cell.Character.ToString(),
                        _font,
                        fg,
                        new PointF(x, y));
                }
            }
        });

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        var bytes = ms.ToArray();

        if (bytes.Length > _options.RenderPngByteLimit)
        {
            _logger.LogDebug("PNG byte size {Bytes} exceeds limit, rejecting", bytes.Length);
            return null;
        }

        return bytes;
    }

    private static Color AdjustContrast(RgbColor fg)
    {
        var luma = 0.2126 * fg.R + 0.7152 * fg.G + 0.0722 * fg.B;
        if (luma >= ContrastFloor)
            return Color.FromRgb(fg.R, fg.G, fg.B);

        // Blend toward monochrome foreground until luma reaches the floor
        var t  = (ContrastFloor - luma) / (MonochromeLuma - luma);
        var r  = (byte)Math.Round((1 - t) * fg.R + t * 0xE6, MidpointRounding.AwayFromZero);
        var g  = (byte)Math.Round((1 - t) * fg.G + t * 0xED, MidpointRounding.AwayFromZero);
        var b  = (byte)Math.Round((1 - t) * fg.B + t * 0xF3, MidpointRounding.AwayFromZero);
        return Color.FromRgb(r, g, b);
    }
}

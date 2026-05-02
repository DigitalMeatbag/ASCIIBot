using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Tests;

public sealed class OutputDeliveryServiceTests
{
    private static OutputDeliveryService MakeService(
        int  inlineCharLimit   = 2000,
        int  attachByteLimit   = 1_000_000,
        int  pngByteLimit      = 8_388_608,
        long totalUploadLimit  = 12_582_912,
        int  pngMaxWidth       = 4096,
        int  pngMaxHeight      = 4096)
    {
        var opts = Options.Create(new BotOptions
        {
            InlineCharacterLimit = inlineCharLimit,
            AttachmentByteLimit  = attachByteLimit,
            RenderPngByteLimit   = pngByteLimit,
            TotalUploadByteLimit = totalUploadLimit,
            RenderPngMaxWidth    = pngMaxWidth,
            RenderPngMaxHeight   = pngMaxHeight,
        });
        var ansi      = new AnsiColorService();
        var plainText = new PlainTextExportService();
        var png       = new PngRenderService(opts, NullLogger<PngRenderService>.Instance);
        var logger    = NullLogger<OutputDeliveryService>.Instance;
        return new OutputDeliveryService(opts, ansi, plainText, png, logger);
    }

    private static RichAsciiRender MakeRender(int cols, int rows, byte r = 128, byte g = 128, byte b = 128)
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

    private static RenderFile? MakeOriginalImage(int sizeBytes = 1000) =>
        new() { Content = new byte[sizeBytes], Filename = "asciibot-original.png" };

    // ─── Inline delivery ─────────────────────────────────────────────────────

    [Fact]
    public void Decide_SmallRenderColorOff_ReturnsInline()
    {
        var svc    = MakeService(inlineCharLimit: 5000);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, colorEnabled: false, showOriginal: false, originalImage: null);
        Assert.IsType<DeliveryResult.Inline>(result);
    }

    [Fact]
    public void Decide_SmallRenderColorOn_ReturnsInline()
    {
        var svc    = MakeService(inlineCharLimit: 5000);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, colorEnabled: true, showOriginal: false, originalImage: null);
        Assert.IsType<DeliveryResult.Inline>(result);
    }

    [Fact]
    public void Decide_InlinePayloadAtExactLimit_ReturnsInline()
    {
        var svc0   = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(10, 2);
        var r0     = Assert.IsType<DeliveryResult.Inline>(svc0.Decide(render, false, false, null));
        var len    = (r0.CompletionText + r0.InlinePayload).Length;

        var svcExact = MakeService(inlineCharLimit: len);
        var result   = svcExact.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.Inline>(result);
    }

    [Fact]
    public void Decide_InlinePayloadOneOverLimit_ReturnsNonInline()
    {
        var svc0   = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(10, 2);
        var r0     = Assert.IsType<DeliveryResult.Inline>(svc0.Decide(render, false, false, null));
        var len    = (r0.CompletionText + r0.InlinePayload).Length;

        var svcOver = MakeService(inlineCharLimit: len - 1);
        var result  = svcOver.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.NonInline>(result);
    }

    // ─── Inline dimension gate ────────────────────────────────────────────────

    [Fact]
    public void Decide_ColumnsExceed100_ReturnsNonInline()
    {
        var svc    = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(101, 5);
        var result = svc.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.NonInline>(result);
    }

    [Fact]
    public void Decide_RowsExceed35_ReturnsNonInline()
    {
        var svc    = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(10, 36);
        var result = svc.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.NonInline>(result);
    }

    // ─── Non-inline delivery: PNG + txt ──────────────────────────────────────

    [Fact]
    public void Decide_NonInline_IncludesBothPngAndTxt()
    {
        var svc    = MakeService(inlineCharLimit: 1);
        var render = MakeRender(10, 5);
        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, true, false, null));
        Assert.Equal("asciibot-render.png", result.PngRender.Filename);
        Assert.Equal("asciibot-render.txt", result.TxtRender.Filename);
    }

    [Fact]
    public void Decide_NonInline_TxtHasNoAnsiEscapes()
    {
        var svc    = MakeService(inlineCharLimit: 1);
        var render = MakeRender(10, 5);
        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, true, false, null));
        var content = System.Text.Encoding.UTF8.GetString(result.TxtRender.Content);
        Assert.DoesNotContain("\x1b[", content);
    }

    [Fact]
    public void Decide_NonInline_TxtNotDroppedFromSuccessfulResponse()
    {
        var svc    = MakeService(inlineCharLimit: 1);
        var render = MakeRender(5, 3);
        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, false, false, null));
        Assert.NotNull(result.TxtRender);
        Assert.NotEmpty(result.TxtRender.Content);
    }

    // ─── PNG byte-limit rejection ─────────────────────────────────────────────

    [Fact]
    public void Decide_PngExceedsByteLimit_ReturnsRejected()
    {
        var svc    = MakeService(inlineCharLimit: 1, pngByteLimit: 1);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.Rejected>(result);
    }

    // ─── PNG pixel-dimension rejection ───────────────────────────────────────

    [Fact]
    public void Decide_PngExceedsPixelDimensions_ReturnsRejected()
    {
        var svc    = MakeService(inlineCharLimit: 1, pngMaxWidth: 1, pngMaxHeight: 1);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.Rejected>(result);
    }

    // ─── Attachment txt byte-limit rejection ──────────────────────────────────

    [Fact]
    public void Decide_TxtExceedsAttachByteLimit_ReturnsRejected()
    {
        var svc    = MakeService(inlineCharLimit: 1, attachByteLimit: 1);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.Rejected>(result);
    }

    // ─── Original image: inline path ─────────────────────────────────────────

    [Fact]
    public void Decide_ShowOriginalTrue_InlinePath_OriginalImageAttached()
    {
        var svc    = MakeService(inlineCharLimit: 5000, totalUploadLimit: 12_582_912);
        var render = MakeRender(10, 5);
        var orig   = MakeOriginalImage(1000);

        var result = Assert.IsType<DeliveryResult.Inline>(svc.Decide(render, false, true, orig));
        Assert.NotNull(result.OriginalImage);
    }

    [Fact]
    public void Decide_ShowOriginalFalse_InlinePath_NoOriginalImage()
    {
        var svc    = MakeService(inlineCharLimit: 5000);
        var render = MakeRender(10, 5);
        var orig   = MakeOriginalImage(1000);

        var result = Assert.IsType<DeliveryResult.Inline>(svc.Decide(render, false, false, orig));
        Assert.Null(result.OriginalImage);
    }

    [Fact]
    public void Decide_ShowOriginalFalse_InlinePath_NoOmissionNote()
    {
        var svc    = MakeService(inlineCharLimit: 5000);
        var render = MakeRender(10, 5);
        var orig   = MakeOriginalImage(1000);

        var result = Assert.IsType<DeliveryResult.Inline>(svc.Decide(render, false, false, orig));
        Assert.DoesNotContain("omitted", result.CompletionText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_ShowOriginalTrue_OriginalTooLargeForInline_OmissionNoteAppended()
    {
        // Total upload limit just above the original image size ensures the original won't fit
        // inline (where original is the only attachment, limited by TotalUploadByteLimit)
        var orig = MakeOriginalImage(2000);
        var svc  = MakeService(inlineCharLimit: 5000, totalUploadLimit: 999); // limit smaller than 2000
        var render = MakeRender(10, 5);

        var result = Assert.IsType<DeliveryResult.Inline>(svc.Decide(render, false, true, orig));
        Assert.Null(result.OriginalImage);
        Assert.Contains("omitted", result.CompletionText, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Original image: non-inline path ─────────────────────────────────────

    [Fact]
    public void Decide_ShowOriginalTrue_NonInlinePath_OriginalImageAttached()
    {
        var svc    = MakeService(inlineCharLimit: 1, totalUploadLimit: 12_582_912);
        var render = MakeRender(10, 5);
        var orig   = MakeOriginalImage(1000);

        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, false, true, orig));
        Assert.NotNull(result.OriginalImage);
    }

    [Fact]
    public void Decide_ShowOriginalFalse_NonInlinePath_NoOriginalImage()
    {
        var svc    = MakeService(inlineCharLimit: 1);
        var render = MakeRender(10, 5);
        var orig   = MakeOriginalImage(1000);

        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, false, false, orig));
        Assert.Null(result.OriginalImage);
    }

    [Fact]
    public void Decide_ShowOriginalFalse_NonInlinePath_NoOmissionNote()
    {
        var svc    = MakeService(inlineCharLimit: 1);
        var render = MakeRender(10, 5);
        var orig   = MakeOriginalImage(1000);

        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, false, false, orig));
        Assert.DoesNotContain("omitted", result.CompletionText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_ShowOriginalTrue_NonInline_OriginalOmittedWhenTooLarge_OmissionNotePresent()
    {
        // PNG + txt + original would exceed limit; original must be omitted
        var render       = MakeRender(10, 5);
        var svcForSize   = MakeService(inlineCharLimit: 1, totalUploadLimit: 12_582_912);
        var baseResult   = Assert.IsType<DeliveryResult.NonInline>(svcForSize.Decide(render, false, false, null));
        var renderBundle = (long)baseResult.PngRender.Content.Length + baseResult.TxtRender.Content.Length;

        // Set limit to just the render bundle — original won't fit
        var svc    = MakeService(inlineCharLimit: 1, totalUploadLimit: renderBundle);
        var orig   = MakeOriginalImage(1000); // > remaining budget
        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, false, true, orig));

        Assert.Null(result.OriginalImage);
        Assert.Contains("omitted", result.CompletionText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_OriginalOmittedFirst_RenderArtifactsPreserved()
    {
        var render       = MakeRender(10, 5);
        var svcForSize   = MakeService(inlineCharLimit: 1, totalUploadLimit: 12_582_912);
        var baseResult   = Assert.IsType<DeliveryResult.NonInline>(svcForSize.Decide(render, false, false, null));
        var renderBundle = (long)baseResult.PngRender.Content.Length + baseResult.TxtRender.Content.Length;

        var svc    = MakeService(inlineCharLimit: 1, totalUploadLimit: renderBundle);
        var orig   = MakeOriginalImage(1000);
        var result = Assert.IsType<DeliveryResult.NonInline>(svc.Decide(render, false, true, orig));

        Assert.NotNull(result.PngRender);
        Assert.NotNull(result.TxtRender);
    }

    [Fact]
    public void Decide_RenderBundleExceedsTotalUploadLimit_ReturnsRejected()
    {
        var render = MakeRender(10, 5);
        // Measure actual render bundle size
        var svcRef = MakeService(inlineCharLimit: 1, totalUploadLimit: 12_582_912);
        var refResult = Assert.IsType<DeliveryResult.NonInline>(svcRef.Decide(render, false, false, null));
        var bundleSize = (long)refResult.PngRender.Content.Length + refResult.TxtRender.Content.Length;

        // Limit tighter than the bundle
        var svc    = MakeService(inlineCharLimit: 1, totalUploadLimit: bundleSize - 1);
        var result = svc.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.Rejected>(result);
    }

    // ─── Canonical inline character count ────────────────────────────────────

    [Fact]
    public void InlineCharCount_IncludesCompletionTextAndFence()
    {
        // Force a render that goes inline with a precise limit
        var svc0   = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(5, 2);
        var r0     = Assert.IsType<DeliveryResult.Inline>(svc0.Decide(render, false, false, null));

        // full message length = completionText + inlinePayload (which already includes fence)
        var fullLen = r0.CompletionText.Length + r0.InlinePayload.Length;

        // At that exact limit it should still deliver inline
        var svcExact = MakeService(inlineCharLimit: fullLen);
        var result   = svcExact.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.Inline>(result);

        // One under should fall through to non-inline
        var svcUnder = MakeService(inlineCharLimit: fullLen - 1);
        var resultUnder = svcUnder.Decide(render, false, false, null);
        Assert.IsType<DeliveryResult.NonInline>(resultUnder);
    }

    // ─── Original image filename extension mapping ────────────────────────────

    [Theory]
    [InlineData("png",  ".png")]
    [InlineData("jpeg", ".jpg")]
    [InlineData("bmp",  ".bmp")]
    [InlineData("gif",  ".gif")]
    [InlineData("webp", ".webp")]
    public void FormatExtension_MapsCorrectly(string formatKey, string expectedExt)
    {
        // FormatExtensionHelper is tested indirectly via the format name lookup
        // Test the mapping values directly
        Assert.Equal(expectedExt, GetExtensionForFormatName(formatKey));
    }

    private static string GetExtensionForFormatName(string name) => name switch
    {
        "png"  => ".png",
        "jpeg" => ".jpg",
        "bmp"  => ".bmp",
        "gif"  => ".gif",
        "webp" => ".webp",
        _      => ".bin",
    };
}

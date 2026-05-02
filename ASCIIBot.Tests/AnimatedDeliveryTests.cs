using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Tests;

/// <summary>§25.8 Animated delivery: no inline/txt, WebP attachment, show_original, limits.</summary>
public sealed class AnimatedDeliveryTests
{
    private static OutputDeliveryService MakeService(
        long totalUploadLimit   = 10_000_000,
        int  animWebPByteLimit  = 8_388_608,
        int  pngByteLimit       = 8_388_608) =>
        new(Options.Create(new BotOptions
        {
            TotalUploadByteLimit   = totalUploadLimit,
            AnimationWebPByteLimit = animWebPByteLimit,
            RenderPngByteLimit     = pngByteLimit,
            RenderPngMaxWidth      = 4096,
            RenderPngMaxHeight     = 4096,
            InlineCharacterLimit   = 2000,
            AttachmentByteLimit    = 1_000_000,
        }),
        new AnsiColorService(),
        new PlainTextExportService(),
        new PngRenderService(
            Options.Create(new BotOptions
            {
                RenderPngByteLimit = pngByteLimit,
                RenderPngMaxWidth  = 4096,
                RenderPngMaxHeight = 4096,
            }),
            NullLogger<PngRenderService>.Instance),
        NullLogger<OutputDeliveryService>.Instance);

    private static byte[]    SmallWebP()        => new byte[1_000];
    private static RenderFile OriginalFile(int size = 500) =>
        new() { Content = new byte[size], Filename = "asciibot-original.gif" };

    // ─── Delivery result type ─────────────────────────────────────────────────

    [Fact]
    public void DecideAnimated_ReturnsAnimatedResult_NotInlineOrNonInline()
    {
        var svc = MakeService();
        var result = svc.DecideAnimated(SmallWebP(), showOriginal: false, originalImage: null);
        Assert.IsType<DeliveryResult.Animated>(result);
        Assert.IsNotType<DeliveryResult.Inline>(result);
        Assert.IsNotType<DeliveryResult.NonInline>(result);
    }

    // ─── WebP render attachment ───────────────────────────────────────────────

    [Fact]
    public void DecideAnimated_AnimatedResultContainsWebPRender()
    {
        var svc    = MakeService();
        var webp   = SmallWebP();
        var result = svc.DecideAnimated(webp, showOriginal: false, originalImage: null);
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.Same(webp, anim.WebPRender.Content);
        Assert.Equal("asciibot-render.webp", anim.WebPRender.Filename);
    }

    // ─── show_original behavior ───────────────────────────────────────────────

    [Fact]
    public void DecideAnimated_ShowOriginalFalse_NoOriginalAttachment()
    {
        var svc    = MakeService();
        var result = svc.DecideAnimated(SmallWebP(), showOriginal: false, originalImage: OriginalFile());
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.Null(anim.OriginalImage);
    }

    [Fact]
    public void DecideAnimated_ShowOriginalFalse_NoOmissionNote()
    {
        var svc    = MakeService();
        var result = svc.DecideAnimated(SmallWebP(), showOriginal: false, originalImage: OriginalFile());
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.DoesNotContain("omitted", anim.CompletionText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecideAnimated_ShowOriginalTrue_WithinLimits_IncludesOriginal()
    {
        var svc    = MakeService(totalUploadLimit: 10_000_000);
        var result = svc.DecideAnimated(SmallWebP(), showOriginal: true, originalImage: OriginalFile(500));
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.NotNull(anim.OriginalImage);
    }

    [Fact]
    public void DecideAnimated_ShowOriginalTrue_OriginalExceedsLimit_OmitsOriginal()
    {
        // WebP=500 bytes, original=600 bytes, total limit=1000 bytes
        var svc    = MakeService(totalUploadLimit: 1000, animWebPByteLimit: 8_388_608);
        var webp   = new byte[500];
        var result = svc.DecideAnimated(webp, showOriginal: true, originalImage: OriginalFile(600));
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.Null(anim.OriginalImage);
    }

    [Fact]
    public void DecideAnimated_OriginalOmittedDueToLimits_OmissionNotePresent()
    {
        var svc    = MakeService(totalUploadLimit: 1000, animWebPByteLimit: 8_388_608);
        var webp   = new byte[500];
        var result = svc.DecideAnimated(webp, showOriginal: true, originalImage: OriginalFile(600));
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.Contains("omitted", anim.CompletionText, StringComparison.OrdinalIgnoreCase);
    }

    // ─── WebP never dropped ───────────────────────────────────────────────────

    [Fact]
    public void DecideAnimated_WebPAlwaysPresent_InSuccessfulResult()
    {
        var svc    = MakeService();
        var result = svc.DecideAnimated(SmallWebP(), showOriginal: true, originalImage: OriginalFile());
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.NotNull(anim.WebPRender);
        Assert.NotNull(anim.WebPRender.Content);
    }

    // ─── Total upload limit: WebP alone ──────────────────────────────────────

    [Fact]
    public void DecideAnimated_WebPAloneExceedsTotalLimit_Rejected()
    {
        var svc    = MakeService(totalUploadLimit: 100, animWebPByteLimit: 8_388_608);
        var result = svc.DecideAnimated(new byte[200], showOriginal: false, originalImage: null);
        var rej    = Assert.IsType<DeliveryResult.Rejected>(result);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Animated WebP byte limit ─────────────────────────────────────────────

    [Fact]
    public void DecideAnimated_WebPExceedsByteLimit_Rejected()
    {
        var svc    = MakeService(animWebPByteLimit: 100);
        var result = svc.DecideAnimated(new byte[200], showOriginal: false, originalImage: null);
        var rej    = Assert.IsType<DeliveryResult.Rejected>(result);
        Assert.Contains("rejected", rej.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecideAnimated_WebPAtByteLimit_ReturnsAnimated()
    {
        var svc    = MakeService(animWebPByteLimit: 200);
        var result = svc.DecideAnimated(new byte[200], showOriginal: false, originalImage: null);
        Assert.IsType<DeliveryResult.Animated>(result);
    }

    // ─── Completion text ─────────────────────────────────────────────────────

    [Fact]
    public void DecideAnimated_CompletionText_ContainsAnimatedKeyword()
    {
        var svc    = MakeService();
        var result = svc.DecideAnimated(SmallWebP(), showOriginal: false, originalImage: null);
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.Contains("Animated", anim.CompletionText);
    }

    // ─── Original omitted first (WebP preserved) ─────────────────────────────

    [Fact]
    public void DecideAnimated_OriginalOmittedFirst_WebPStillPresent()
    {
        // WebP=500, original=600, total limit=1000 → original omitted, WebP kept
        var svc    = MakeService(totalUploadLimit: 1000, animWebPByteLimit: 8_388_608);
        var webp   = new byte[500];
        var result = svc.DecideAnimated(webp, showOriginal: true, originalImage: OriginalFile(600));
        var anim   = Assert.IsType<DeliveryResult.Animated>(result);
        Assert.NotNull(anim.WebPRender);
        Assert.Null(anim.OriginalImage);
    }
}

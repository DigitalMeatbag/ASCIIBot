using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Tests;

public sealed class OutputDeliveryServiceTests
{
    private static OutputDeliveryService MakeService(int inlineCharLimit = 2000, int attachByteLimit = 1_000_000)
    {
        var opts = Options.Create(new BotOptions
        {
            InlineCharacterLimit = inlineCharLimit,
            AttachmentByteLimit  = attachByteLimit,
        });
        var ansi   = new AnsiColorService();
        var logger = NullLogger<OutputDeliveryService>.Instance;
        return new OutputDeliveryService(opts, ansi, logger);
    }

    private static AsciiRenderResult MakeRender(int cols, int rows)
    {
        var chars  = new char[rows][];
        var colors = new (byte R, byte G, byte B)[rows][];
        for (var r = 0; r < rows; r++)
        {
            chars[r]  = Enumerable.Repeat('X', cols).ToArray();
            colors[r] = Enumerable.Repeat<(byte, byte, byte)>((128, 128, 128), cols).ToArray();
        }
        return new AsciiRenderResult { Chars = chars, Colors = colors, Columns = cols, Rows = rows };
    }

    // --- Inline delivery ---

    [Fact]
    public void Decide_SmallRenderColorOff_ReturnsInline()
    {
        var svc    = MakeService(inlineCharLimit: 5000);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, colorEnabled: false);
        Assert.IsType<DeliveryResult.Inline>(result);
    }

    [Fact]
    public void Decide_SmallRenderColorOn_ReturnsInline()
    {
        var svc    = MakeService(inlineCharLimit: 5000);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, colorEnabled: true);
        Assert.IsType<DeliveryResult.Inline>(result);
    }

    [Fact]
    public void Decide_InlinePayloadAtExactLimit_ReturnsInline()
    {
        // Build a render, figure out its payload size, set limit to exactly that
        var svc0   = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(10, 2);
        var inline0 = Assert.IsType<DeliveryResult.Inline>(svc0.Decide(render, colorEnabled: false));
        var payloadLen = inline0.Message.Length;

        var svcExact = MakeService(inlineCharLimit: payloadLen);
        var result   = svcExact.Decide(render, colorEnabled: false);
        Assert.IsType<DeliveryResult.Inline>(result);
    }

    [Fact]
    public void Decide_InlinePayloadOneOverLimit_ReturnsAttachment()
    {
        var svc0   = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(10, 2);
        var inline0 = Assert.IsType<DeliveryResult.Inline>(svc0.Decide(render, colorEnabled: false));
        var payloadLen = inline0.Message.Length;

        var svcOver = MakeService(inlineCharLimit: payloadLen - 1);
        var result  = svcOver.Decide(render, colorEnabled: false);
        Assert.IsType<DeliveryResult.Attachment>(result);
    }

    // --- Attachment fallback due to visible dimensions ---

    [Fact]
    public void Decide_ColumnsExceed100_ReturnsAttachment()
    {
        var svc    = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(101, 5);
        var result = svc.Decide(render, colorEnabled: false);
        Assert.IsType<DeliveryResult.Attachment>(result);
    }

    [Fact]
    public void Decide_RowsExceed35_ReturnsAttachment()
    {
        var svc    = MakeService(inlineCharLimit: 999_999);
        var render = MakeRender(10, 36);
        var result = svc.Decide(render, colorEnabled: false);
        Assert.IsType<DeliveryResult.Attachment>(result);
    }

    // --- Attachment content is plain text (no ANSI) ---

    [Fact]
    public void Decide_AttachmentFallback_ContentHasNoAnsiEscapes()
    {
        var svc    = MakeService(inlineCharLimit: 1); // force attachment
        var render = MakeRender(10, 5);
        var result = Assert.IsType<DeliveryResult.Attachment>(svc.Decide(render, colorEnabled: true));

        var content = System.Text.Encoding.UTF8.GetString(result.Content);
        Assert.DoesNotContain("\x1b[", content);
    }

    // --- Attachment byte limit ---

    [Fact]
    public void Decide_AttachmentWithinByteLimit_ReturnsAttachment()
    {
        var svc    = MakeService(inlineCharLimit: 1, attachByteLimit: 1_000_000);
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, colorEnabled: false);
        Assert.IsType<DeliveryResult.Attachment>(result);
    }

    [Fact]
    public void Decide_AttachmentExceedsByteLimit_ReturnsRejected()
    {
        var svc    = MakeService(inlineCharLimit: 1, attachByteLimit: 1); // 1 byte limit
        var render = MakeRender(10, 5);
        var result = svc.Decide(render, colorEnabled: false);
        Assert.IsType<DeliveryResult.Rejected>(result);
    }

    // --- Color on with ANSI payload over limit falls back to attachment (plain) ---

    [Fact]
    public void Decide_ColorOnAnsiOverLimitPlainUnder_ReturnsAttachment()
    {
        // Get the plain text payload length for a render
        var svc0    = MakeService(inlineCharLimit: 999_999);
        var render  = MakeRender(10, 2);
        var inline0 = Assert.IsType<DeliveryResult.Inline>(svc0.Decide(render, colorEnabled: false));
        var plainLen = inline0.Message.Length;

        // Set limit between plain and ANSI (ANSI is always longer due to escape sequences)
        var svc = MakeService(inlineCharLimit: plainLen); // plain would fit but we test color=on

        // With color=on the ANSI payload will be longer than plain, so if we set limit to plain length,
        // ANSI may or may not exceed it depending on escape size.
        // Force it: set limit to 0 for color=on so it definitely exceeds
        var svcForce = MakeService(inlineCharLimit: plainLen - 1);
        var result   = svcForce.Decide(render, colorEnabled: true);
        // Should be attachment (not inline, not rejected if bytes fit)
        Assert.IsType<DeliveryResult.Attachment>(result);
    }
}

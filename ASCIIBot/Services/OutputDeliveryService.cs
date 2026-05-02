using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace ASCIIBot.Services;

public sealed class OutputDeliveryService
{
    private readonly BotOptions _options;
    private readonly AnsiColorService _ansi;
    private readonly ILogger<OutputDeliveryService> _logger;

    public OutputDeliveryService(
        IOptions<BotOptions> options,
        AnsiColorService ansi,
        ILogger<OutputDeliveryService> logger)
    {
        _options = options.Value;
        _ansi    = ansi;
        _logger  = logger;
    }

    public DeliveryResult Decide(AsciiRenderResult render, bool colorEnabled)
    {
        // Step 1: visible dimension check
        if (render.Columns > 100 || render.Rows > 35)
        {
            _logger.LogDebug("Render exceeds visible thresholds ({Cols}x{Rows}), using attachment", render.Columns, render.Rows);
            return AttachmentOrRejected(render);
        }

        // Step 2/3/4: inline payload check
        string inlinePayload;
        if (colorEnabled)
        {
            var ansiText    = _ansi.BuildAnsiRender(render);
            inlinePayload   = BuildInlineMessage(ansiText);
        }
        else
        {
            var plainText   = AsciiRenderService.ToPlainText(render);
            inlinePayload   = BuildInlineMessage(plainText);
        }

        if (inlinePayload.Length <= _options.InlineCharacterLimit)
        {
            _logger.LogDebug("Delivering inline ({Chars} chars)", inlinePayload.Length);
            return new DeliveryResult.Inline { Message = inlinePayload };
        }

        // Inline too large — fall through to attachment
        _logger.LogDebug("Inline payload too large ({Chars} chars), using attachment", inlinePayload.Length);
        return AttachmentOrRejected(render);
    }

    private DeliveryResult AttachmentOrRejected(AsciiRenderResult render)
    {
        var plainText = AsciiRenderService.ToPlainText(render);
        var bytes     = Encoding.UTF8.GetBytes(plainText);

        if (bytes.Length > _options.AttachmentByteLimit)
        {
            _logger.LogDebug("Attachment too large ({Bytes} bytes), rejecting", bytes.Length);
            return new DeliveryResult.Rejected
            {
                Message = "The rendered output exceeds delivery limits. Processing has been rejected.",
            };
        }

        _logger.LogDebug("Delivering as attachment ({Bytes} bytes)", bytes.Length);
        return new DeliveryResult.Attachment
        {
            Message  = "Rendering complete. Output has been attached as text.",
            Content  = bytes,
            Filename = "ascii.txt",
        };
    }

    private static string BuildInlineMessage(string renderContent) =>
        $"Rendering complete.\n```ansi\n{renderContent}\n```";
}

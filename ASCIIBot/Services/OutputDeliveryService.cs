using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace ASCIIBot.Services;

public sealed class OutputDeliveryService
{
    private readonly BotOptions           _options;
    private readonly AnsiColorService     _ansi;
    private readonly PlainTextExportService _plainText;
    private readonly PngRenderService     _png;
    private readonly ILogger<OutputDeliveryService> _logger;

    private const string CompletionTextInline    = "Rendering complete.";
    private const string CompletionTextNonInline = "Rendering complete. Output has been attached.";
    private const string CompletionTextAnimated  = "Rendering complete. Animated output has been attached.";
    internal const string OmissionNote           = "Original image display was omitted due to delivery limits.";

    public OutputDeliveryService(
        IOptions<BotOptions>    options,
        AnsiColorService        ansi,
        PlainTextExportService  plainText,
        PngRenderService        png,
        ILogger<OutputDeliveryService> logger)
    {
        _options   = options.Value;
        _ansi      = ansi;
        _plainText = plainText;
        _png       = png;
        _logger    = logger;
    }

    public DeliveryResult Decide(
        RichAsciiRender render,
        bool            colorEnabled,
        bool            showOriginal,
        RenderFile? originalImage)
    {
        // Step 1-2: plain text
        var plainText      = _plainText.Export(render);
        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

        // Step 3: inline dimension eligibility
        bool dimensionEligible = render.Width <= 100 && render.Height <= 35;

        if (dimensionEligible)
        {
            var inlineResult = TryInline(render, colorEnabled, showOriginal, originalImage, plainText);
            if (inlineResult is not null)
            {
                _logger.LogDebug("Delivering inline ({Cols}x{Rows})", render.Width, render.Height);
                return inlineResult;
            }
        }
        else
        {
            _logger.LogDebug("Render exceeds inline dimension gate ({Cols}x{Rows}), using non-inline", render.Width, render.Height);
        }

        return BuildNonInline(render, colorEnabled, showOriginal, originalImage, plainTextBytes);
    }

    private DeliveryResult.Inline? TryInline(
        RichAsciiRender render,
        bool            colorEnabled,
        bool            showOriginal,
        RenderFile? originalImage,
        string          plainText)
    {
        string inlinePayload;
        try
        {
            inlinePayload = colorEnabled
                ? _ansi.BuildAnsiRender(render)
                : _ansi.BuildMonochromeAnsiRender(render);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ANSI export failed; falling through to non-inline path");
            return null;
        }

        string fence = $"\n```ansi\n{inlinePayload}\n```";

        // Try with original image
        if (showOriginal && originalImage is not null)
        {
            string completionText  = CompletionTextInline;
            int    charCount       = CountInlineChars(completionText, fence);
            if (charCount <= _options.InlineCharacterLimit &&
                originalImage.Content.LongLength <= _options.TotalUploadByteLimit)
            {
                return new DeliveryResult.Inline
                {
                    CompletionText = completionText,
                    InlinePayload  = fence,
                    OriginalImage  = originalImage,
                };
            }

            // Original image doesn't fit — try with omission note
            string completionWithNote = CompletionTextInline + "\n" + OmissionNote;
            int    charCountWithNote  = CountInlineChars(completionWithNote, fence);
            if (charCountWithNote <= _options.InlineCharacterLimit)
            {
                return new DeliveryResult.Inline
                {
                    CompletionText = completionWithNote,
                    InlinePayload  = fence,
                    OriginalImage  = null,
                };
            }

            // Even with the omission note it doesn't fit — fall through
            return null;
        }
        else
        {
            string completionText = CompletionTextInline;
            int    charCount      = CountInlineChars(completionText, fence);
            if (charCount <= _options.InlineCharacterLimit)
            {
                return new DeliveryResult.Inline
                {
                    CompletionText = completionText,
                    InlinePayload  = fence,
                    OriginalImage  = null,
                };
            }
            return null;
        }
    }

    private DeliveryResult BuildNonInline(
        RichAsciiRender render,
        bool            colorEnabled,
        bool            showOriginal,
        RenderFile? originalImage,
        byte[]          plainTextBytes)
    {
        // Check txt attachment byte limit
        if (plainTextBytes.Length > _options.AttachmentByteLimit)
        {
            _logger.LogDebug("Plain text too large ({Bytes} bytes), rejecting", plainTextBytes.Length);
            return Rejected("The rendered output exceeds delivery limits. Processing has been rejected.");
        }

        // Generate PNG
        var pngBytes = _png.TryRenderPng(render, colorEnabled);
        if (pngBytes is null)
        {
            _logger.LogDebug("PNG generation failed or exceeded limits, rejecting");
            return Rejected("The rendered output exceeds delivery limits. Processing has been rejected.");
        }

        var pngAttachment = new RenderFile { Content = pngBytes,       Filename = "asciibot-render.png" };
        var txtAttachment = new RenderFile { Content = plainTextBytes,  Filename = "asciibot-render.txt" };

        long renderBundleSize = (long)pngBytes.Length + plainTextBytes.Length;

        // Can the render bundle fit in the total upload budget?
        if (renderBundleSize > _options.TotalUploadByteLimit)
        {
            _logger.LogDebug("Render bundle ({Bytes} bytes) exceeds total upload limit, rejecting", renderBundleSize);
            return Rejected("The rendered output exceeds delivery limits. Processing has been rejected.");
        }

        // Try to include original image
        if (showOriginal && originalImage is not null)
        {
            long withOriginal = renderBundleSize + originalImage.Content.LongLength;
            if (withOriginal <= _options.TotalUploadByteLimit)
            {
                _logger.LogDebug("Delivering non-inline with original image ({TotalBytes} bytes)", withOriginal);
                return new DeliveryResult.NonInline
                {
                    CompletionText = CompletionTextNonInline,
                    PngRender      = pngAttachment,
                    TxtRender      = txtAttachment,
                    OriginalImage  = originalImage,
                };
            }

            // Original doesn't fit — omit with note
            _logger.LogDebug("Original image omitted due to delivery limits");
            return new DeliveryResult.NonInline
            {
                CompletionText = CompletionTextNonInline + "\n" + OmissionNote,
                PngRender      = pngAttachment,
                TxtRender      = txtAttachment,
                OriginalImage  = null,
            };
        }

        _logger.LogDebug("Delivering non-inline ({TotalBytes} bytes)", renderBundleSize);
        return new DeliveryResult.NonInline
        {
            CompletionText = CompletionTextNonInline,
            PngRender      = pngAttachment,
            TxtRender      = txtAttachment,
            OriginalImage  = null,
        };
    }

    public DeliveryResult DecideAnimated(byte[] webpBytes, bool showOriginal, RenderFile? originalImage)
    {
        // Animated WebP byte limit check
        if (webpBytes.Length > _options.AnimationWebPByteLimit)
        {
            _logger.LogDebug("Animated WebP ({Bytes} bytes) exceeds byte limit, rejecting", webpBytes.Length);
            return Rejected("The rendered animation exceeds delivery limits. Processing has been rejected.");
        }

        // WebP alone must fit in total upload budget
        if ((long)webpBytes.Length > _options.TotalUploadByteLimit)
        {
            _logger.LogDebug("Animated WebP ({Bytes} bytes) exceeds total upload limit, rejecting", webpBytes.Length);
            return Rejected("The rendered animation exceeds delivery limits. Processing has been rejected.");
        }

        var webpFile = new RenderFile { Content = webpBytes, Filename = "asciibot-render.webp" };

        if (!showOriginal || originalImage is null)
        {
            return new DeliveryResult.Animated
            {
                CompletionText = CompletionTextAnimated,
                WebPRender     = webpFile,
                OriginalImage  = null,
            };
        }

        // Try to include original image
        long withOriginal = (long)webpBytes.Length + originalImage.Content.LongLength;
        if (withOriginal <= _options.TotalUploadByteLimit)
        {
            _logger.LogDebug("Delivering animated with original ({TotalBytes} bytes)", withOriginal);
            return new DeliveryResult.Animated
            {
                CompletionText = CompletionTextAnimated,
                WebPRender     = webpFile,
                OriginalImage  = originalImage,
            };
        }

        // Original doesn't fit — omit with note
        _logger.LogDebug("Animated original image omitted due to delivery limits");
        return new DeliveryResult.Animated
        {
            CompletionText = CompletionTextAnimated + "\n" + OmissionNote,
            WebPRender     = webpFile,
            OriginalImage  = null,
        };
    }

    // Canonical inline character count per spec §11.2
    private static int CountInlineChars(string completionText, string fence) =>
        completionText.Length + fence.Length;

    private static DeliveryResult.Rejected Rejected(string message) =>
        new() { Message = message };
}

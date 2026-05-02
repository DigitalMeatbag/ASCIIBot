using ASCIIBot.Models;
using ASCIIBot.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace ASCIIBot.Modules;

public sealed class AsciiInteractionModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ConcurrencyGate        _concurrency;
    private readonly ImageDownloadService   _downloader;
    private readonly ImageValidationService _validator;
    private readonly AsciiRenderService     _renderer;
    private readonly OutputDeliveryService  _delivery;
    private readonly ILogger<AsciiInteractionModule> _logger;

    public AsciiInteractionModule(
        ConcurrencyGate         concurrency,
        ImageDownloadService    downloader,
        ImageValidationService  validator,
        AsciiRenderService      renderer,
        OutputDeliveryService   delivery,
        ILogger<AsciiInteractionModule> logger)
    {
        _concurrency = concurrency;
        _downloader  = downloader;
        _validator   = validator;
        _renderer    = renderer;
        _delivery    = delivery;
        _logger      = logger;
    }

    [SlashCommand("ascii", "Convert an image to ASCII art")]
    public async Task AsciiAsync(
        IAttachment image,
        [Summary("size"),   Choice("small", "small"), Choice("medium", "medium"), Choice("large", "large")]
        string size          = "medium",
        [Summary("color"),  Choice("on", "on"), Choice("off", "off")]
        string color         = "on",
        [Summary("detail"), Choice("low", "low"), Choice("normal", "normal"), Choice("high", "high")]
        string detail        = "normal",
        [Summary("show_original")]
        bool   showOriginal  = true)
    {
        var userId = Context.User.Id;
        _logger.LogInformation(
            "Request accepted from user {UserId} size={Size} color={Color} detail={Detail} show_original={ShowOriginal}",
            userId, size, color, detail, showOriginal);

        await DeferAsync(ephemeral: false);

        if (!_concurrency.TryAcquire(userId, out var handle, out var rejection))
        {
            var busyMsg = rejection == ConcurrencyRejection.UserBusy
                ? "A request from this user is already being processed. Please resubmit after it has completed."
                : "Processing capacity has been reached. Please resubmit later.";
            _logger.LogInformation("Request rejected for user {UserId}: {Reason}", userId, rejection);
            await FollowupAsync(busyMsg);
            return;
        }

        IUserMessage ackMsg;
        try
        {
            ackMsg = await FollowupAsync("Request received. Processing has begun.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send acknowledgement for user {UserId}", userId);
            handle.Dispose();
            return;
        }

        using var timerCts = new CancellationTokenSource();
        var statusTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), timerCts.Token);
                await ackMsg.ModifyAsync(m => m.Content = "Processing remains active.");
            }
            catch (OperationCanceledException) { }
        }, timerCts.Token);

        try
        {
            await ProcessRequestAsync(image, size, color, detail, showOriginal, userId, handle);
        }
        finally
        {
            await timerCts.CancelAsync();
            try { await statusTask; } catch { /* already handled */ }
            handle.Dispose();
        }
    }

    private async Task ProcessRequestAsync(
        IAttachment       image,
        string            size,
        string            color,
        string            detail,
        bool              showOriginal,
        ulong             userId,
        ConcurrencyHandle handle)
    {
        _ = handle;

        MemoryStream imageStream;
        try
        {
            imageStream = await _downloader.DownloadAsync(image.Url, image.Size, CancellationToken.None);
        }
        catch (ImageTooLargeException)
        {
            _logger.LogInformation("Rejected oversized attachment from user {UserId}", userId);
            await FollowupAsync("The submitted image exceeds the maximum source file size. Processing has been rejected.");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for user {UserId}", userId);
            await FollowupAsync("Processing failed due to an internal error.");
            return;
        }

        await using (imageStream)
        {
            var validationResult = await _validator.ValidateAsync(imageStream);

            if (validationResult is ValidationResult.Error validationError)
            {
                _logger.LogInformation("Validation rejected for user {UserId}: {Message}", userId, validationError.Message);
                await FollowupAsync(validationError.Message);
                return;
            }

            var ok = (ValidationResult.Ok)validationResult;
            using var decodedImage = ok.Image;

            RenderFile? originalFile = null;
            if (showOriginal)
            {
                var ext = FormatExtensionHelper.GetExtension(ok.Format);
                originalFile = new RenderFile
                {
                    Content  = ok.OriginalBytes,
                    Filename = $"asciibot-original{ext}",
                };
            }

            RichAsciiRender render;
            try
            {
                var preset       = SizePreset.FromString(size);
                var detailPreset = DetailPreset.FromString(detail);
                render = _renderer.Render(decodedImage, preset, detailPreset);
                _logger.LogInformation(
                    "Rich render completed for user {UserId}: {Cols}x{Rows}",
                    userId, render.Width, render.Height);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Render failed for user {UserId}", userId);
                await FollowupAsync("The submitted image could not be rendered. Processing has failed.");
                return;
            }

            var colorEnabled   = color != "off";
            var deliveryResult = _delivery.Decide(render, colorEnabled, showOriginal, originalFile);

            _logger.LogInformation("Delivery mode selected for user {UserId}: {Mode}",
                userId, deliveryResult.GetType().Name);

            switch (deliveryResult)
            {
                case DeliveryResult.Inline inline:
                    await DeliverInlineAsync(inline);
                    break;

                case DeliveryResult.NonInline nonInline:
                    await DeliverNonInlineAsync(nonInline);
                    break;

                case DeliveryResult.Rejected rejected:
                    _logger.LogInformation("Delivery rejected for user {UserId}", userId);
                    await FollowupAsync(rejected.Message);
                    break;
            }
        }
    }

    private async Task DeliverInlineAsync(DeliveryResult.Inline inline)
    {
        try
        {
            var fullMessage = inline.CompletionText + inline.InlinePayload;

            if (inline.OriginalImage is { } orig)
            {
                using var origStream = new MemoryStream(orig.Content);
                await Context.Interaction.FollowupWithFileAsync(
                    fileStream: origStream,
                    fileName:   orig.Filename,
                    text:       fullMessage);
            }
            else
            {
                await FollowupAsync(fullMessage);
            }
        }
        catch (Exception ex) when (IsPermissionException(ex))
        {
            _logger.LogError(ex, "Permission failure delivering inline result");
            try { await FollowupAsync("The rendered output could not be delivered due to insufficient permissions. Processing has failed."); }
            catch { /* nothing more we can do */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver inline result");
            try { await FollowupAsync("The rendered output could not be delivered. Processing has failed."); }
            catch { /* nothing more we can do */ }
        }
    }

    private async Task DeliverNonInlineAsync(DeliveryResult.NonInline nonInline)
    {
        try
        {
            await UploadNonInlineAsync(nonInline, includeOriginal: nonInline.OriginalImage is not null);
        }
        catch (Exception ex) when (IsUploadTooLargeException(ex) && nonInline.OriginalImage is not null)
        {
            _logger.LogWarning(ex, "Upload too large with original image; retrying without it");
            try
            {
                var text = nonInline.CompletionText.Contains("omitted")
                    ? nonInline.CompletionText
                    : nonInline.CompletionText + "\n" + OutputDeliveryService.OmissionNote;
                var omitted = new DeliveryResult.NonInline
                {
                    CompletionText = text,
                    PngRender      = nonInline.PngRender,
                    TxtRender      = nonInline.TxtRender,
                    OriginalImage  = null,
                };
                await UploadNonInlineAsync(omitted, includeOriginal: false);
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Failed to upload non-inline result even without original image");
                try { await FollowupAsync("The rendered output could not be delivered. Processing has failed."); }
                catch { /* nothing more we can do */ }
            }
        }
        catch (Exception ex) when (IsPermissionException(ex))
        {
            _logger.LogError(ex, "Permission failure uploading non-inline result");
            try { await FollowupAsync("The rendered output could not be delivered due to insufficient permissions. Processing has failed."); }
            catch { /* nothing more we can do */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload non-inline result");
            try { await FollowupAsync("The rendered output could not be delivered. Processing has failed."); }
            catch { /* nothing more we can do */ }
        }
    }

    private async Task UploadNonInlineAsync(DeliveryResult.NonInline nonInline, bool includeOriginal)
    {
        using var pngStream = new MemoryStream(nonInline.PngRender.Content);
        using var txtStream = new MemoryStream(nonInline.TxtRender.Content);

        var attachments = new List<Discord.FileAttachment>
        {
            new(pngStream, nonInline.PngRender.Filename),
            new(txtStream, nonInline.TxtRender.Filename),
        };

        MemoryStream? origStream = null;
        try
        {
            if (includeOriginal && nonInline.OriginalImage is { } orig)
            {
                origStream = new MemoryStream(orig.Content);
                attachments.Add(new Discord.FileAttachment(origStream, orig.Filename));
            }

            await Context.Interaction.FollowupWithFilesAsync(
                attachments: attachments,
                text:        nonInline.CompletionText);
        }
        finally
        {
            origStream?.Dispose();
        }
    }

    private static bool IsPermissionException(Exception ex) =>
        ex.Message.Contains("Missing Permissions", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Missing Access",      StringComparison.OrdinalIgnoreCase);

    private static bool IsUploadTooLargeException(Exception ex) =>
        ex.Message.Contains("Request entity too large", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("40005",                   StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("payload_too_large",        StringComparison.OrdinalIgnoreCase);
}

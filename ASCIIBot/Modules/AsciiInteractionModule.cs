using ASCIIBot.Models;
using ASCIIBot.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace ASCIIBot.Modules;

public sealed class AsciiInteractionModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ConcurrencyGate _concurrency;
    private readonly ImageDownloadService _downloader;
    private readonly ImageValidationService _validator;
    private readonly AsciiRenderService _renderer;
    private readonly OutputDeliveryService _delivery;
    private readonly ILogger<AsciiInteractionModule> _logger;

    public AsciiInteractionModule(
        ConcurrencyGate concurrency,
        ImageDownloadService downloader,
        ImageValidationService validator,
        AsciiRenderService renderer,
        OutputDeliveryService delivery,
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
        [Summary("size"), Choice("small", "small"), Choice("medium", "medium"), Choice("large", "large")]
        string size = "medium",
        [Summary("color"), Choice("on", "on"), Choice("off", "off")]
        string color = "on")
    {
        var userId = Context.User.Id;
        _logger.LogInformation("Request accepted from user {UserId} size={Size} color={Color}", userId, size, color);

        // Defer publicly — shows loading indicator in channel
        await DeferAsync(ephemeral: false);

        // Concurrency check
        if (!_concurrency.TryAcquire(userId, out var handle, out var rejection))
        {
            var busyMsg = rejection == ConcurrencyRejection.UserBusy
                ? "A request from this user is already being processed. Please resubmit after it has completed."
                : "Processing capacity has been reached. Please resubmit later.";
            _logger.LogInformation("Request rejected for user {UserId}: {Reason}", userId, rejection);
            await FollowupAsync(busyMsg);
            return;
        }

        // Public acknowledgement — sent immediately so the user sees it before conversion
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

        // 10-second long-running status notice
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
            await ProcessRequestAsync(image, size, color, userId, handle);
        }
        finally
        {
            await timerCts.CancelAsync();
            try { await statusTask; } catch { /* already handled */ }
            handle.Dispose();
        }
    }

    private async Task ProcessRequestAsync(
        IAttachment image,
        string size,
        string color,
        ulong userId,
        ConcurrencyHandle handle)
    {
        _ = handle; // handle lifetime managed by caller

        // Download
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
            // Validate
            var validationResult = await _validator.ValidateAsync(imageStream);

            if (validationResult is ValidationResult.Error validationError)
            {
                _logger.LogInformation("Validation rejected for user {UserId}: {Message}", userId, validationError.Message);
                await FollowupAsync(validationError.Message);
                return;
            }

            var ok = (ValidationResult.Ok)validationResult;
            using var decodedImage = ok.Image;

            // Render
            AsciiRenderResult renderResult;
            try
            {
                var preset = SizePreset.FromString(size);
                renderResult = _renderer.Render(decodedImage, preset);
                _logger.LogInformation("Conversion completed for user {UserId}: {Cols}x{Rows}", userId, renderResult.Columns, renderResult.Rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Render failed for user {UserId}", userId);
                await FollowupAsync("The submitted image could not be rendered. Processing has failed.");
                return;
            }

            // Deliver
            var colorEnabled  = color != "off";
            var deliveryResult = _delivery.Decide(renderResult, colorEnabled);

            switch (deliveryResult)
            {
                case DeliveryResult.Inline inline:
                    _logger.LogInformation("Delivering inline for user {UserId}", userId);
                    await DeliverInlineAsync(inline.Message);
                    break;

                case DeliveryResult.Attachment attachment:
                    _logger.LogInformation("Delivering attachment for user {UserId}", userId);
                    await DeliverAttachmentAsync(attachment);
                    break;

                case DeliveryResult.Rejected rejected:
                    _logger.LogInformation("Delivery rejected (output too large) for user {UserId}", userId);
                    await FollowupAsync(rejected.Message);
                    break;
            }
        }
    }

    private async Task DeliverInlineAsync(string message)
    {
        try
        {
            await FollowupAsync(message);
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
            await FollowupAsync("The rendered output could not be delivered. Processing has failed.");
        }
    }

    private async Task DeliverAttachmentAsync(DeliveryResult.Attachment attachment)
    {
        try
        {
            using var stream = new MemoryStream(attachment.Content);
            await Context.Interaction.FollowupWithFileAsync(
                fileStream: stream,
                fileName:   attachment.Filename,
                text:       attachment.Message);
        }
        catch (Exception ex) when (IsPermissionException(ex))
        {
            _logger.LogError(ex, "Permission failure uploading attachment");
            try { await FollowupAsync("The rendered output could not be delivered due to insufficient permissions. Processing has failed."); }
            catch { /* nothing more we can do */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload attachment");
            await FollowupAsync("The rendered output could not be delivered. Processing has failed.");
        }
    }

    private static bool IsPermissionException(Exception ex) =>
        ex.Message.Contains("Missing Permissions", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Missing Access",      StringComparison.OrdinalIgnoreCase);
}

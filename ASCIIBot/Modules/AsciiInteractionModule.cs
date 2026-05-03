using ASCIIBot.Models;
using ASCIIBot.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Modules;

public sealed class AsciiInteractionModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ConcurrencyGate               _concurrency;
    private readonly ImageDownloadService          _downloader;
    private readonly ImageValidationService        _validator;
    private readonly AsciiRenderService            _renderer;
    private readonly AnimationInspectionService    _animInspection;
    private readonly AnimationSamplingService      _animSampling;
    private readonly AnimatedAsciiRenderService    _animRenderer;
    private readonly AnimatedWebPExportService     _animExporter;
    private readonly OutputDeliveryService         _delivery;
    private readonly IMp4InspectionService         _mp4Inspector;
    private readonly IMp4FrameExtractionService    _mp4Extractor;
    private readonly BotOptions                    _options;
    private readonly ILogger<AsciiInteractionModule> _logger;

    public AsciiInteractionModule(
        ConcurrencyGate                  concurrency,
        ImageDownloadService             downloader,
        ImageValidationService           validator,
        AsciiRenderService               renderer,
        AnimationInspectionService       animInspection,
        AnimationSamplingService         animSampling,
        AnimatedAsciiRenderService       animRenderer,
        AnimatedWebPExportService        animExporter,
        OutputDeliveryService            delivery,
        IMp4InspectionService            mp4Inspector,
        IMp4FrameExtractionService       mp4Extractor,
        IOptions<BotOptions>             options,
        ILogger<AsciiInteractionModule>  logger)
    {
        _concurrency    = concurrency;
        _downloader     = downloader;
        _validator      = validator;
        _renderer       = renderer;
        _animInspection = animInspection;
        _animSampling   = animSampling;
        _animRenderer   = animRenderer;
        _animExporter   = animExporter;
        _delivery       = delivery;
        _mp4Inspector   = mp4Inspector;
        _mp4Extractor   = mp4Extractor;
        _options        = options.Value;
        _logger         = logger;
    }

    [MessageCommand("ASCII this")]
    public async Task AsciiThisAsync(IMessage targetMessage)
    {
        var userId = Context.User.Id;

        // Resolve media: attachments take unconditional priority
        string? mediaUrl = null;
        long?   reportedSize = null;

        if (targetMessage.Attachments.Count > 0)
        {
            var attachment = targetMessage.Attachments.First();
            mediaUrl     = attachment.Url;
            reportedSize = attachment.Size;
        }
        else
        {
            // Fallback to gifv embed only when there are no attachments
            var gifv = targetMessage.Embeds.FirstOrDefault(
                e => e.Type == EmbedType.Gifv && e.Video?.Url is not null);
            if (gifv is not null)
            {
                mediaUrl = gifv.Video!.Value.Url;
            }
        }

        if (mediaUrl is null)
        {
            await RespondAsync("No supported media found in that message.", ephemeral: true);
            return;
        }

        _logger.LogInformation(
            "Context command from user {UserId} targeting message {MessageId}",
            userId, targetMessage.Id);

        await DeferAsync(ephemeral: false);

        if (!_concurrency.TryAcquire(userId, out var handle, out var rejection))
        {
            var busyMsg = rejection == ConcurrencyRejection.UserBusy
                ? "A request from this user is already being processed. Please resubmit after it has completed."
                : "Processing capacity has been reached. Please resubmit later.";
            _logger.LogInformation("Context command rejected for user {UserId}: {Reason}", userId, rejection);
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
            _logger.LogError(ex, "Failed to send acknowledgement for context command user {UserId}", userId);
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
            // Hardcoded defaults per spec §5.2
            await ProcessRequestAsync(mediaUrl, reportedSize, "large", "on", "high", showOriginal: false, userId);
        }
        finally
        {
            await timerCts.CancelAsync();
            try { await statusTask; } catch { /* already handled */ }
            handle.Dispose();
        }
    }

    [SlashCommand("ascii", "Convert an image to ASCII art")]
    public async Task AsciiAsync(IAttachment image,
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
            await ProcessRequestAsync(image.Url, image.Size, size, color, detail, showOriginal, userId);
        }
        finally
        {
            await timerCts.CancelAsync();
            try { await statusTask; } catch { /* already handled */ }
            handle.Dispose();
        }
    }

    private async Task ProcessRequestAsync(
        string mediaUrl,
        long?  reportedSize,
        string size,
        string color,
        string detail,
        bool   showOriginal,
        ulong  userId)
    {
        MemoryStream imageStream;
        try
        {
            imageStream = await _downloader.DownloadAsync(mediaUrl, reportedSize, CancellationToken.None);
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

            // Route to MP4 pipeline
            if (validationResult is ValidationResult.Mp4Ok mp4Ok)
            {
                _logger.LogInformation("Routing MP4 for user {UserId}", userId);
                await ProcessMp4Async(mp4Ok, size, color, detail, showOriginal, userId);
                return;
            }

            // Route to animated pipeline
            if (validationResult is ValidationResult.AnimatedOk animOk)
            {
                _logger.LogInformation("Routing animated {Format} for user {UserId}",
                    animOk.Format.Name, userId);
                using (animOk.Image)
                {
                    await ProcessAnimatedAsync(animOk, size, color, detail, showOriginal, userId);
                }
                return;
            }

            // Static pipeline
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

    private async Task ProcessAnimatedAsync(
        ValidationResult.AnimatedOk animOk,
        string                      size,
        string                      color,
        string                      detail,
        bool                        showOriginal,
        ulong                       userId)
    {
        var sizePreset   = SizePreset.FromString(size);
        var detailPreset = DetailPreset.FromString(detail);
        var colorEnabled = color != "off";

        // Inspect animation metadata and enforce limits
        var inspectionResult = _animInspection.Inspect(animOk.Image, animOk.Format);
        if (inspectionResult is AnimationInspectionResult.Rejected inspRej)
        {
            _logger.LogInformation("Animation inspection rejected for user {UserId}: {Message}",
                userId, inspRej.Message);
            await FollowupAsync(inspRej.Message);
            return;
        }
        var inspection = (AnimationInspectionResult.Ok)inspectionResult;

        // Compute output grid
        var (cols, rows) = AsciiRenderService.ComputeDimensions(
            inspection.CanvasWidth, inspection.CanvasHeight, sizePreset);

        // Determine sampled frames
        var sampling     = _animSampling.Sample(inspection.SourceDurationMs, inspection.FrameStartTimesMs);
        int sampledCount = sampling.SelectedSourceFrameIndices.Length;

        // Cost fuse
        long totalCells = (long)cols * rows * sampledCount;
        _logger.LogInformation(
            "Animation for user {UserId}: {W}x{H} grid, {Frames} frames, {Cells} cells",
            userId, cols, rows, sampledCount, totalCells);

        if (totalCells > _options.AnimationMaxOutputCells)
        {
            _logger.LogInformation("Animation cost fuse exceeded for user {UserId}: {Cells}", userId, totalCells);
            await FollowupAsync("The submitted animation exceeds processing limits. Processing has been rejected.");
            return;
        }

        // Render frames
        AnimatedAsciiRender animRender;
        try
        {
            animRender = _animRenderer.Render(
                animOk.Image, cols, rows, sampling, detailPreset, colorEnabled);
            _logger.LogInformation(
                "Animated render completed for user {UserId}: {Frames} frames {Cols}x{Rows}",
                userId, animRender.Frames.Length, cols, rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Animated render failed for user {UserId}", userId);
            await FollowupAsync("The submitted animation could not be rendered. Processing has failed.");
            return;
        }

        // Export to animated WebP
        byte[]? webpBytes;
        try
        {
            webpBytes = _animExporter.Export(animRender, colorEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Animated WebP export failed for user {UserId}", userId);
            await FollowupAsync("The submitted animation could not be rendered. Processing has failed.");
            return;
        }

        if (webpBytes is null)
        {
            _logger.LogInformation("Animated frame pixel dimensions exceeded limits for user {UserId}", userId);
            await FollowupAsync("The rendered animation exceeds delivery limits. Processing has been rejected.");
            return;
        }

        _logger.LogInformation("Animated WebP export: {Bytes} bytes for user {UserId}", webpBytes.Length, userId);

        // Build original file
        RenderFile? originalFile = null;
        if (showOriginal)
        {
            var ext = FormatExtensionHelper.GetExtension(animOk.Format);
            originalFile = new RenderFile
            {
                Content  = animOk.OriginalBytes,
                Filename = $"asciibot-original{ext}",
            };
        }

        // Delivery decision
        var deliveryResult = _delivery.DecideAnimated(webpBytes, showOriginal, originalFile);
        _logger.LogInformation("Animated delivery decision for user {UserId}: {Mode}",
            userId, deliveryResult.GetType().Name);

        switch (deliveryResult)
        {
            case DeliveryResult.Animated animated:
                await DeliverAnimatedAsync(animated);
                break;

            case DeliveryResult.Rejected rejected:
                _logger.LogInformation("Animated delivery rejected for user {UserId}: {Message}",
                    userId, rejected.Message);
                await FollowupAsync(rejected.Message);
                break;
        }
    }

    private async Task ProcessMp4Async(
        ValidationResult.Mp4Ok mp4Ok,
        string                 size,
        string                 color,
        string                 detail,
        bool                   showOriginal,
        ulong                  userId)
    {
        var sizePreset   = SizePreset.FromString(size);
        var detailPreset = DetailPreset.FromString(detail);
        var colorEnabled = color != "off";

        await using var tempJob = new Mp4TempJob();

        // Write source MP4 to temp file
        try
        {
            await File.WriteAllBytesAsync(tempJob.SourceFilePath, mp4Ok.OriginalBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write MP4 temp file for user {UserId}", userId);
            await FollowupAsync("Processing failed due to an internal error.");
            return;
        }

        // Inspect via ffprobe (15s timeout)
        Mp4InspectionOutcome inspectionOutcome;
        using (var inspCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            inspectionOutcome = await _mp4Inspector.InspectAsync(tempJob.SourceFilePath, inspCts.Token);
        }

        if (inspectionOutcome is Mp4InspectionOutcome.Rejected inspRej)
        {
            _logger.LogInformation("MP4 inspection rejected for user {UserId}: {Message}", userId, inspRej.Message);
            await FollowupAsync(inspRej.Message);
            return;
        }
        if (inspectionOutcome is Mp4InspectionOutcome.TimedOut)
        {
            _logger.LogInformation("MP4 inspection timed out for user {UserId}", userId);
            await FollowupAsync("The submitted video could not be inspected in time. Processing has failed.");
            return;
        }
        if (inspectionOutcome is Mp4InspectionOutcome.Failed)
        {
            _logger.LogInformation("MP4 inspection failed for user {UserId}", userId);
            await FollowupAsync("The submitted video could not be inspected. Processing has failed.");
            return;
        }

        var inspection = ((Mp4InspectionOutcome.Ok)inspectionOutcome).Result;
        _logger.LogInformation(
            "MP4 inspected for user {UserId}: {W}x{H} {Dur}ms codec={Codec}",
            userId, inspection.VideoWidth, inspection.VideoHeight, inspection.DurationMs, inspection.CodecName);

        // Duration limit
        if (inspection.DurationMs > _options.AnimationMaxDurationMs)
        {
            _logger.LogInformation("MP4 duration exceeded for user {UserId}: {DurMs}ms", userId, inspection.DurationMs);
            await FollowupAsync("The submitted video exceeds the maximum source duration. Processing has been rejected.");
            return;
        }

        // Dimension limit (reuse canvas check from animation inspection)
        if (inspection.VideoWidth <= 0 || inspection.VideoHeight <= 0)
        {
            _logger.LogInformation("MP4 invalid dimensions for user {UserId}", userId);
            await FollowupAsync("The submitted video could not be inspected. Processing has been rejected.");
            return;
        }

        // Compute output grid
        var (cols, rows) = AsciiRenderService.ComputeDimensions(
            inspection.VideoWidth, inspection.VideoHeight, sizePreset);

        // Sample frames (MP4 path — no source-frame count limit)
        var sampling = _animSampling.SampleMp4(inspection.DurationMs);

        // Cost fuse
        long totalCells = (long)cols * rows * sampling.FrameCount;
        _logger.LogInformation(
            "MP4 for user {UserId}: {W}x{H} grid, {Frames} frames, {Cells} cells",
            userId, cols, rows, sampling.FrameCount, totalCells);

        if (totalCells > _options.AnimationMaxOutputCells)
        {
            _logger.LogInformation("MP4 cost fuse exceeded for user {UserId}: {Cells}", userId, totalCells);
            await FollowupAsync("The submitted video exceeds processing limits. Processing has been rejected.");
            return;
        }

        // Extract frames via ffmpeg (60s timeout)
        Mp4ExtractionOutcome extractionOutcome;
        using (var extractCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
        {
            extractionOutcome = await _mp4Extractor.ExtractFramesAsync(
                tempJob.SourceFilePath, sampling.SampleTimes, tempJob.FrameDirectory, extractCts.Token);
        }

        if (extractionOutcome is Mp4ExtractionOutcome.TimedOut)
        {
            _logger.LogInformation("MP4 frame extraction timed out for user {UserId}", userId);
            await FollowupAsync("The submitted video could not be processed in time. Processing has failed.");
            return;
        }
        if (extractionOutcome is Mp4ExtractionOutcome.Failed exFail)
        {
            _logger.LogInformation("MP4 frame extraction failed for user {UserId}: {Reason}", userId, exFail.Reason);
            await FollowupAsync("The submitted video could not be processed. Processing has failed.");
            return;
        }

        var extraction      = (Mp4ExtractionOutcome.Ok)extractionOutcome;
        var frameFilePaths  = extraction.FrameFilePaths;
        tempJob.RegisterFrameFiles(frameFilePaths);

        // Load extracted PNGs via ImageSharp
        var loadedFrames = new List<Image<Rgba32>>(frameFilePaths.Length);
        try
        {
            foreach (var framePath in frameFilePaths)
            {
                var img = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(framePath);
                loadedFrames.Add(img);
            }
        }
        catch (Exception ex)
        {
            foreach (var img in loadedFrames) img.Dispose();
            _logger.LogError(ex, "Failed to load extracted frame PNG for user {UserId}", userId);
            await FollowupAsync("The submitted video could not be rendered. Processing has failed.");
            return;
        }

        // Render all frames to ASCII
        AnimatedAsciiRender animRender;
        try
        {
            animRender = _animRenderer.RenderFromExtractedFrames(
                loadedFrames, cols, rows, sampling.SampleTimes, sampling.OutputDurations, detailPreset, colorEnabled);
            _logger.LogInformation(
                "MP4 animated render completed for user {UserId}: {Frames} frames {Cols}x{Rows}",
                userId, animRender.Frames.Length, cols, rows);
        }
        catch (Exception ex)
        {
            foreach (var img in loadedFrames) img.Dispose();
            _logger.LogError(ex, "MP4 animated render failed for user {UserId}", userId);
            await FollowupAsync("The submitted video could not be rendered. Processing has failed.");
            return;
        }
        finally
        {
            foreach (var img in loadedFrames) img.Dispose();
        }

        // Export to animated WebP
        byte[]? webpBytes;
        try
        {
            webpBytes = _animExporter.Export(animRender, colorEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MP4 animated WebP export failed for user {UserId}", userId);
            await FollowupAsync("The submitted video could not be rendered. Processing has failed.");
            return;
        }

        if (webpBytes is null)
        {
            _logger.LogInformation("MP4 animated frame pixel dimensions exceeded limits for user {UserId}", userId);
            await FollowupAsync("The rendered animation exceeds delivery limits. Processing has been rejected.");
            return;
        }

        _logger.LogInformation("MP4 animated WebP export: {Bytes} bytes for user {UserId}", webpBytes.Length, userId);

        // Build original file
        RenderFile? originalFile = null;
        if (showOriginal)
        {
            originalFile = new RenderFile
            {
                Content  = mp4Ok.OriginalBytes,
                Filename = "asciibot-original.mp4",
            };
        }

        // Delivery decision (same path as GIF/WebP)
        var deliveryResult = _delivery.DecideAnimated(webpBytes, showOriginal, originalFile);
        _logger.LogInformation("MP4 delivery decision for user {UserId}: {Mode}",
            userId, deliveryResult.GetType().Name);

        switch (deliveryResult)
        {
            case DeliveryResult.Animated animated:
                await DeliverAnimatedAsync(animated);
                break;

            case DeliveryResult.Rejected rejected:
                _logger.LogInformation("MP4 delivery rejected for user {UserId}: {Message}", userId, rejected.Message);
                await FollowupAsync(rejected.Message);
                break;
        }
    }

    private async Task DeliverAnimatedAsync(DeliveryResult.Animated animated)
    {
        try
        {
            await UploadAnimatedAsync(animated, includeOriginal: animated.OriginalImage is not null);
        }
        catch (Exception ex) when (IsUploadTooLargeException(ex) && animated.OriginalImage is not null)
        {
            _logger.LogWarning(ex, "Upload too large with original; retrying animated without original");
            try
            {
                var text = animated.CompletionText.Contains("omitted")
                    ? animated.CompletionText
                    : animated.CompletionText + "\n" + OutputDeliveryService.OmissionNote;
                var omitted = new DeliveryResult.Animated
                {
                    CompletionText = text,
                    WebPRender     = animated.WebPRender,
                    OriginalImage  = null,
                };
                await UploadAnimatedAsync(omitted, includeOriginal: false);
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Failed to upload animated result even without original");
                try { await FollowupAsync("The rendered animation could not be delivered. Processing has failed."); }
                catch { /* nothing more we can do */ }
            }
        }
        catch (Exception ex) when (IsPermissionException(ex))
        {
            _logger.LogError(ex, "Permission failure delivering animated result");
            try { await FollowupAsync("The rendered output could not be delivered due to insufficient permissions. Processing has failed."); }
            catch { /* nothing more we can do */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver animated result");
            try { await FollowupAsync("The rendered animation could not be delivered. Processing has failed."); }
            catch { /* nothing more we can do */ }
        }
    }

    private async Task UploadAnimatedAsync(DeliveryResult.Animated animated, bool includeOriginal)
    {
        using var webpStream = new MemoryStream(animated.WebPRender.Content);
        var attachments = new List<Discord.FileAttachment>
        {
            new(webpStream, animated.WebPRender.Filename),
        };

        MemoryStream? origStream = null;
        try
        {
            if (includeOriginal && animated.OriginalImage is { } orig)
            {
                origStream = new MemoryStream(orig.Content);
                attachments.Add(new Discord.FileAttachment(origStream, orig.Filename));
            }

            await Context.Interaction.FollowupWithFilesAsync(
                attachments: attachments,
                text:        animated.CompletionText);
        }
        finally
        {
            origStream?.Dispose();
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

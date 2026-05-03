using ASCIIBot.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ASCIIBot.Services;

public sealed class Mp4InspectionService : IMp4InspectionService
{
    private readonly ILogger<Mp4InspectionService> _logger;

    public Mp4InspectionService(ILogger<Mp4InspectionService> logger)
    {
        _logger = logger;
    }

    public async Task<Mp4InspectionOutcome> InspectAsync(string tempFilePath, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = "ffprobe",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("quiet");
        process.StartInfo.ArgumentList.Add("-print_format");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add("-show_streams");
        process.StartInfo.ArgumentList.Add("-show_format");
        process.StartInfo.ArgumentList.Add(tempFilePath);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ffprobe");
            return new Mp4InspectionOutcome.Failed();
        }

        string stdout;
        try
        {
            // Drain stdout (JSON result) and stderr concurrently to prevent pipe-buffer deadlock.
            var readTask   = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            stdout = await readTask;
            await stderrTask;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            _logger.LogWarning("ffprobe timed out inspecting MP4");
            return new Mp4InspectionOutcome.TimedOut();
        }

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("ffprobe exited with code {Code}", process.ExitCode);
            return new Mp4InspectionOutcome.Rejected(){ Message = "The submitted video could not be inspected. Processing has been rejected." };
        }

        return ParseFfprobeOutput(stdout);
    }

    private Mp4InspectionOutcome ParseFfprobeOutput(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root is null)
                return new Mp4InspectionOutcome.Failed();

            var streams = root["streams"]?.AsArray();
            if (streams is null)
                return new Mp4InspectionOutcome.Rejected() { Message = "The submitted video could not be inspected. Processing has been rejected." };

            JsonObject? videoStream = null;
            foreach (var stream in streams)
            {
                if (stream?["codec_type"]?.GetValue<string>() == "video")
                {
                    videoStream = stream.AsObject();
                    break;
                }
            }

            if (videoStream is null)
                return new Mp4InspectionOutcome.Rejected() { Message = "The submitted video could not be inspected. Processing has been rejected." };

            var codecName = videoStream["codec_name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(codecName))
                return new Mp4InspectionOutcome.Rejected() { Message = "The submitted video could not be inspected. Processing has been rejected." };

            // Check codec is decodable — ffprobe won't expose it in streams if it can't open it,
            // but we guard against unknown codec names explicitly.
            if (codecName is "none" or "unknown")
                return new Mp4InspectionOutcome.Rejected() { Message = "The submitted file type is not supported. Processing has been rejected." };

            var width  = videoStream["width"]?.GetValue<int>()  ?? 0;
            var height = videoStream["height"]?.GetValue<int>() ?? 0;
            if (width <= 0 || height <= 0)
                return new Mp4InspectionOutcome.Rejected() { Message = "The submitted video could not be inspected. Processing has been rejected." };

            // Duration: prefer stream duration, fall back to format duration
            long durationMs = 0;
            var streamDurStr = videoStream["duration"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(streamDurStr) && double.TryParse(streamDurStr,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var streamDurSec) && streamDurSec > 0)
            {
                durationMs = (long)(streamDurSec * 1000);
            }
            else
            {
                var formatDurStr = root["format"]?["duration"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(formatDurStr) && double.TryParse(formatDurStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var formatDurSec) && formatDurSec > 0)
                {
                    durationMs = (long)(formatDurSec * 1000);
                }
            }

            if (durationMs <= 0)
                return new Mp4InspectionOutcome.Rejected() { Message = "The submitted video could not be inspected. Processing has been rejected." };

            _logger.LogInformation("ffprobe: codec={Codec} {W}x{H} duration={DurMs}ms",
                codecName, width, height, durationMs);

            return new Mp4InspectionOutcome.Ok
            {
                Result = new Mp4InspectionResult
                {
                    DurationMs  = durationMs,
                    VideoWidth  = width,
                    VideoHeight = height,
                    CodecName   = codecName,
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse ffprobe output");
            return new Mp4InspectionOutcome.Failed();
        }
    }
}

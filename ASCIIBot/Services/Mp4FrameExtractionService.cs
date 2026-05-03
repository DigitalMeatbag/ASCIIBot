using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ASCIIBot.Services;

public sealed class Mp4FrameExtractionService : IMp4FrameExtractionService
{
    private readonly ILogger<Mp4FrameExtractionService> _logger;

    public Mp4FrameExtractionService(ILogger<Mp4FrameExtractionService> logger)
    {
        _logger = logger;
    }

    public async Task<Mp4ExtractionOutcome> ExtractFramesAsync(
        string     tempFilePath,
        TimeSpan[] sampleTimes,
        string     outputDirectory,
        CancellationToken ct)
    {
        var framePaths = new string[sampleTimes.Length];
        for (int i = 0; i < sampleTimes.Length; i++)
            framePaths[i] = Path.Combine(outputDirectory, $"frame_{i:D4}.png");

        _logger.LogInformation(
            "Extracting {Count} frames from MP4 at timestamps: {Timestamps}",
            sampleTimes.Length,
            string.Join(", ", Array.ConvertAll(sampleTimes, t => $"{t.TotalSeconds:F3}s")));

        for (int i = 0; i < sampleTimes.Length; i++)
        {
            var ts       = sampleTimes[i];
            var outPath  = framePaths[i];
            var seekSecs = ts.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = "ffmpeg",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            process.StartInfo.ArgumentList.Add("-ss");
            process.StartInfo.ArgumentList.Add(seekSecs);
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(tempFilePath);
            process.StartInfo.ArgumentList.Add("-vframes");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add("-an");
            process.StartInfo.ArgumentList.Add(outPath);

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start ffmpeg for frame {Index}", i);
                return new Mp4ExtractionOutcome.Failed { Reason = "ffmpeg process could not be started" };
            }

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("ffmpeg frame extraction timed out at frame {Index}", i);
                return new Mp4ExtractionOutcome.TimedOut();
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("ffmpeg exited with code {Code} for frame {Index}", process.ExitCode, i);
                return new Mp4ExtractionOutcome.Failed { Reason = $"ffmpeg exited with code {process.ExitCode}" };
            }

            if (!File.Exists(outPath))
            {
                _logger.LogWarning("Expected frame file missing after extraction: {Path}", outPath);
                return new Mp4ExtractionOutcome.Failed { Reason = $"Frame file missing after extraction: frame {i}" };
            }
        }

        _logger.LogInformation("Frame extraction complete: {Count} frames extracted", framePaths.Length);
        return new Mp4ExtractionOutcome.Ok { FrameFilePaths = framePaths };
    }
}

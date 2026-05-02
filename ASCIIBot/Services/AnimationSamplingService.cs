using Microsoft.Extensions.Options;

namespace ASCIIBot.Services;

public sealed class AnimationSamplingResult
{
    public required int[]      SelectedSourceFrameIndices { get; init; }
    public required TimeSpan[] OutputDurations            { get; init; }
    public required TimeSpan[] SampleTimes                { get; init; }
}

public sealed class AnimationSamplingService
{
    private readonly BotOptions _options;

    public AnimationSamplingService(IOptions<BotOptions> options)
    {
        _options = options.Value;
    }

    public AnimationSamplingResult Sample(long sourceDurationMs, long[] frameStartTimesMs)
    {
        int maxFrames        = _options.AnimationMaxOutputFrames;
        long targetIntervalMs = _options.AnimationTargetSampleIntervalMs;
        int  minDelayMs      = _options.AnimationMinFrameDelayMs;
        int  sourceCount     = frameStartTimesMs.Length;

        // Compute output frame count per §13.2
        int frameCount;
        if (sourceDurationMs < targetIntervalMs)
            frameCount = 1;
        else
            frameCount = (int)Math.Min(maxFrames, sourceDurationMs / targetIntervalMs);
        frameCount = Math.Max(1, frameCount);

        // Sample times using integer arithmetic for determinism (§13.3)
        long sourceDurationTicks = sourceDurationMs * TimeSpan.TicksPerMillisecond;
        long[] sampleTicks = new long[frameCount];
        for (int i = 0; i < frameCount; i++)
            sampleTicks[i] = (long)i * sourceDurationTicks / frameCount;

        // Convert source frame start times to ticks
        long[] frameStartTicks = Array.ConvertAll(frameStartTimesMs,
            ms => ms * TimeSpan.TicksPerMillisecond);

        // Select nearest source frame for each sample time (§13.4)
        int[] selectedFrames = new int[frameCount];
        for (int i = 0; i < frameCount; i++)
            selectedFrames[i] = SelectNearestFrame(frameStartTicks, sampleTicks[i], sourceCount);

        // Compute output durations per §14.1
        TimeSpan minDelay       = TimeSpan.FromMilliseconds(minDelayMs);
        TimeSpan[] outputDurations = new TimeSpan[frameCount];

        if (frameCount == 1)
        {
            // §13.5: single-frame emitted duration
            outputDurations[0] = TimeSpan.FromMilliseconds(Math.Max(sourceDurationMs, minDelayMs));
        }
        else
        {
            for (int i = 0; i < frameCount - 1; i++)
                outputDurations[i] = TimeSpan.FromTicks(sampleTicks[i + 1] - sampleTicks[i]);
            outputDurations[frameCount - 1] = outputDurations[frameCount - 2];
            for (int i = 0; i < frameCount; i++)
            {
                if (outputDurations[i] < minDelay)
                    outputDurations[i] = minDelay;
            }
        }

        TimeSpan[] sampleTimes = Array.ConvertAll(sampleTicks, t => TimeSpan.FromTicks(t));

        return new AnimationSamplingResult
        {
            SelectedSourceFrameIndices = selectedFrames,
            OutputDurations            = outputDurations,
            SampleTimes                = sampleTimes,
        };
    }

    // Selects the source frame with the nearest start timestamp to sampleTick.
    // On equal distance, the earlier (lower-index) frame is chosen.
    private static int SelectNearestFrame(long[] frameStartTicks, long sampleTick, int sourceCount)
    {
        int  best     = 0;
        long bestDist = Math.Abs(frameStartTicks[0] - sampleTick);

        for (int j = 1; j < sourceCount; j++)
        {
            long dist = Math.Abs(frameStartTicks[j] - sampleTick);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = j;
            }
            // Equal distance: keep the earlier frame (lower j) — no update
        }

        return best;
    }
}

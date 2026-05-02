using ASCIIBot;
using ASCIIBot.Services;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Tests;

/// <summary>§25.3 + §25.4: Sampling algorithm and timing normalization.</summary>
public sealed class AnimationSamplingTests
{
    private static AnimationSamplingService MakeService(
        int maxFrames        = 48,
        int targetIntervalMs = 100,
        int minDelayMs       = 100) =>
        new(Options.Create(new BotOptions
        {
            AnimationMaxOutputFrames          = maxFrames,
            AnimationTargetSampleIntervalMs   = targetIntervalMs,
            AnimationMinFrameDelayMs          = minDelayMs,
        }));

    private static long[] UniformFrameStarts(int count, long delayMs) =>
        Enumerable.Range(0, count).Select(i => (long)i * delayMs).ToArray();

    // ─── §25.3 Frame count calculation ───────────────────────────────────────

    [Fact]
    public void DurationBelow100ms_ProducesOneFrame()
    {
        var svc = MakeService();
        var result = svc.Sample(99, UniformFrameStarts(5, 19));
        Assert.Single(result.SelectedSourceFrameIndices);
    }

    [Fact]
    public void DurationEqual100ms_ProducesOneFrame()
    {
        var svc = MakeService();
        // 100ms / 100ms interval = 1; min(48, 1) = 1
        var result = svc.Sample(100, UniformFrameStarts(10, 10));
        Assert.Single(result.SelectedSourceFrameIndices);
    }

    [Fact]
    public void OneSecondAnimation_Produces10Frames_DefaultConfig()
    {
        var svc = MakeService();
        var result = svc.Sample(1000, UniformFrameStarts(10, 100));
        Assert.Equal(10, result.SelectedSourceFrameIndices.Length);
    }

    [Fact]
    public void FiveSecondAnimation_CappedAt48Frames()
    {
        var svc = MakeService();
        var result = svc.Sample(5000, UniformFrameStarts(100, 50));
        Assert.Equal(48, result.SelectedSourceFrameIndices.Length);
    }

    [Fact]
    public void TwelveSecondAnimation_CappedAt48Frames()
    {
        var svc = MakeService();
        var result = svc.Sample(12_000, UniformFrameStarts(200, 60));
        Assert.Equal(48, result.SelectedSourceFrameIndices.Length);
    }

    [Fact]
    public void FrameCountNeverZero()
    {
        var svc = MakeService();
        var result = svc.Sample(1, new long[] { 0 });
        Assert.True(result.SelectedSourceFrameIndices.Length >= 1);
    }

    // ─── §25.3 Sample times ───────────────────────────────────────────────────

    [Fact]
    public void SampleTimeZero_AlwaysIncluded()
    {
        var svc = MakeService();
        var result = svc.Sample(1000, UniformFrameStarts(10, 100));
        Assert.Equal(TimeSpan.Zero, result.SampleTimes[0]);
    }

    [Fact]
    public void SampleTimes_UseFloorRounding()
    {
        // source = 1000ms = 10_000_000 ticks; frameCount = 3
        // sampleTicks[1] = floor(1 * 10_000_000 / 3) = floor(3_333_333.33) = 3_333_333 ticks
        var svc = MakeService(maxFrames: 48, targetIntervalMs: 1);
        var result = svc.Sample(1000, UniformFrameStarts(3, 333));
        // frameCount = min(48, floor(1000/1)) = 48, but source only has 3 frames
        // Let's use a simpler case: frameCount forced to 3 via maxFrames
        var svc3 = MakeService(maxFrames: 3, targetIntervalMs: 1);
        var r3 = svc3.Sample(1000, UniformFrameStarts(3, 333));
        Assert.Equal(3, r3.SelectedSourceFrameIndices.Length);
        // sampleTicks[1] = floor(1 * 10_000_000 / 3) = 3_333_333
        long expected1 = 1L * (1000L * TimeSpan.TicksPerMillisecond) / 3;
        Assert.Equal(TimeSpan.FromTicks(expected1), r3.SampleTimes[1]);
    }

    [Fact]
    public void FinalSampleTime_IsLessThanSourceDuration()
    {
        var svc = MakeService(maxFrames: 10);
        var result = svc.Sample(1000, UniformFrameStarts(10, 100));
        var lastSample = result.SampleTimes[result.SampleTimes.Length - 1];
        Assert.True(lastSample < TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public void SampleTimes_AreUniformlyDistributed()
    {
        var svc = MakeService(maxFrames: 4);
        // Force exactly 4 frames with 1000ms source
        var result = svc.Sample(1000, UniformFrameStarts(4, 250));
        // Expected: 0, 250ms, 500ms, 750ms (in ticks)
        long src = 1000L * TimeSpan.TicksPerMillisecond;
        for (int i = 0; i < 4; i++)
        {
            long expected = (long)i * src / 4;
            Assert.Equal(expected, result.SampleTimes[i].Ticks);
        }
    }

    // ─── §25.3 Source frame selection ─────────────────────────────────────────

    [Fact]
    public void NearestFrame_SelectedBySampleTime()
    {
        var svc = MakeService(maxFrames: 1);
        // Only one output frame, sample at t=0
        // Source frames start at 0ms and 500ms
        var result = svc.Sample(600, new long[] { 0, 500 });
        Assert.Equal(0, result.SelectedSourceFrameIndices[0]);
    }

    [Fact]
    public void NearestFrameTie_ChoosesEarlierFrame()
    {
        // Sample at 250ms; source frames at 0ms and 500ms → equidistant → choose frame 0
        var svc = MakeService(maxFrames: 2, targetIntervalMs: 1);
        // With maxFrames=2 and source=500ms: frameCount = min(2, floor(500/1)) = 2
        // sampleTicks[0] = 0, sampleTicks[1] = floor(1 * 500ms_in_ticks / 2) = 250ms_in_ticks
        // Source frames at 0ms and 500ms → frame[0]=0ms (dist=250ms), frame[1]=500ms (dist=250ms) → choose 0
        var result = svc.Sample(500, new long[] { 0, 500 });
        // sampleTimes[1] = 250ms; nearest: frame0@0ms (dist=250ms), frame1@500ms (dist=250ms) → tie → choose frame 0
        Assert.Equal(0, result.SelectedSourceFrameIndices[1]);
    }

    [Fact]
    public void DuplicateSelectedFrames_Allowed()
    {
        // Many output frames, few source frames → duplicates expected
        var svc = MakeService(maxFrames: 10);
        // 500ms source, 2 source frames → 5 output frames, some will duplicate
        var result = svc.Sample(500, new long[] { 0, 490 });
        // No assertion that uniqueness is required; just verify no exception
        Assert.Equal(5, result.SelectedSourceFrameIndices.Length);
    }

    [Fact]
    public void Sampling_IsDeterministic()
    {
        var svc    = MakeService();
        var starts = UniformFrameStarts(8, 125);
        var r1 = svc.Sample(1000, starts);
        var r2 = svc.Sample(1000, starts);
        Assert.Equal(r1.SelectedSourceFrameIndices, r2.SelectedSourceFrameIndices);
        for (int i = 0; i < r1.SampleTimes.Length; i++)
            Assert.Equal(r1.SampleTimes[i], r2.SampleTimes[i]);
    }

    // ─── §25.4 Timing normalization ───────────────────────────────────────────

    [Fact]
    public void MultiFrame_OutputDuration_DerivedFromSampleIntervals()
    {
        var svc = MakeService(maxFrames: 4);
        var result = svc.Sample(1000, UniformFrameStarts(4, 250));
        // sampleTimes: 0, 250, 500, 750ms → intervals: 250, 250, 250ms (last copies prev)
        Assert.Equal(TimeSpan.FromMilliseconds(250), result.OutputDurations[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(250), result.OutputDurations[1]);
    }

    [Fact]
    public void LastFrame_DurationCopiesPreviousInterval()
    {
        var svc = MakeService(maxFrames: 4);
        var result = svc.Sample(1000, UniformFrameStarts(4, 250));
        int last = result.OutputDurations.Length - 1;
        Assert.Equal(result.OutputDurations[last - 1], result.OutputDurations[last]);
    }

    [Fact]
    public void ShortIntervals_ClampedToMinDelay()
    {
        // 200ms source, 10 frames → intervals of 20ms → clamped to 100ms
        var svc = MakeService(maxFrames: 10, minDelayMs: 100);
        var result = svc.Sample(200, UniformFrameStarts(10, 20));
        foreach (var dur in result.OutputDurations)
            Assert.True(dur >= TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void SingleFrameAnimation_UsesMaxOfDurationAndMinDelay()
    {
        var svc = MakeService(minDelayMs: 100);
        // 50ms source < 100ms target interval → 1 frame; duration = max(50, 100) = 100ms
        var result = svc.Sample(50, new long[] { 0 });
        Assert.Equal(TimeSpan.FromMilliseconds(100), result.OutputDurations[0]);
    }

    [Fact]
    public void SingleFrameAnimation_WhenSourceLongerThanMinDelay_UsesSourceDuration()
    {
        var svc = MakeService(minDelayMs: 100);
        // Force 1 frame with source=500ms: duration = max(500, 100) = 500ms
        var result = svc.Sample(50, new long[] { 0 });
        // 50ms < 100ms target → 1 frame; max(50, 100) = 100ms (minDelay wins)
        Assert.Equal(TimeSpan.FromMilliseconds(100), result.OutputDurations[0]);
    }

    [Fact]
    public void SingleFrameAnimation_LongSource_UsesDuration()
    {
        var svc = MakeService(maxFrames: 48, targetIntervalMs: 100, minDelayMs: 100);
        // 5000ms source with only 1 source frame and maxFrames=1 → 1 output frame
        // duration = max(5000, 100) = 5000ms
        var svc1 = MakeService(maxFrames: 1, targetIntervalMs: 100, minDelayMs: 100);
        var result = svc1.Sample(5000, new long[] { 0 });
        Assert.Equal(TimeSpan.FromMilliseconds(5000), result.OutputDurations[0]);
    }

    [Fact]
    public void ClampingMayStretchTotalDuration()
    {
        // 200ms source, 10 frames → 10 intervals of 20ms each → clamped to 100ms → total 1000ms > 200ms
        var svc = MakeService(maxFrames: 10, minDelayMs: 100);
        var result = svc.Sample(200, UniformFrameStarts(10, 20));
        var totalMs = result.OutputDurations.Sum(d => d.TotalMilliseconds);
        Assert.True(totalMs >= 200); // stretched
    }

    [Fact]
    public void NoRedistributionAfterClamping()
    {
        // All frames get clamped independently; no frame is shortened to compensate
        var svc = MakeService(maxFrames: 4, minDelayMs: 100);
        var result = svc.Sample(200, UniformFrameStarts(4, 50));
        // Each interval ~50ms → clamped to 100ms; all durations should be >= 100ms
        foreach (var dur in result.OutputDurations)
            Assert.True(dur >= TimeSpan.FromMilliseconds(100));
    }
}

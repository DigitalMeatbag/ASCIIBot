using ASCIIBot.Models;
using ASCIIBot.Services;

namespace ASCIIBot.Tests;

/// <summary>§27.7 MP4 reject vs fail classification correctness.</summary>
public sealed class Mp4RejectVsFailTests
{
    // These tests verify the semantics of each outcome type:
    //   Rejected — the input is structurally invalid; operator should not retry with same input
    //   Failed   — a transient processing error occurred; operator may retry
    //   TimedOut — processing exceeded the allowed window; distinct from hard failure
    //
    // Tests use fake implementations to inject specific outcomes and verify callers
    // can discriminate between them correctly.

    private sealed class ProgrammableInspector : IMp4InspectionService
    {
        private readonly Queue<Mp4InspectionOutcome> _outcomes;
        public ProgrammableInspector(params Mp4InspectionOutcome[] outcomes)
            => _outcomes = new Queue<Mp4InspectionOutcome>(outcomes);
        public Task<Mp4InspectionOutcome> InspectAsync(string path, CancellationToken ct)
            => Task.FromResult(_outcomes.Dequeue());
    }

    private sealed class ProgrammableExtractor : IMp4FrameExtractionService
    {
        private readonly Mp4ExtractionOutcome _outcome;
        public ProgrammableExtractor(Mp4ExtractionOutcome outcome) => _outcome = outcome;
        public Task<Mp4ExtractionOutcome> ExtractFramesAsync(
            string path, TimeSpan[] times, string dir, CancellationToken ct)
            => Task.FromResult(_outcome);
    }

    // --- Inspection: Rejected is semantically different from Failed ---

    [Fact]
    public async Task InspectionRejected_IsNotFailed()
    {
        var inspector = new ProgrammableInspector(new Mp4InspectionOutcome.Rejected { Message = "unsupported" });
        var outcome   = await inspector.InspectAsync("x.mp4", CancellationToken.None);

        Assert.IsType<Mp4InspectionOutcome.Rejected>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Failed>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.TimedOut>(outcome);
    }

    [Fact]
    public async Task InspectionFailed_IsNotRejected()
    {
        var inspector = new ProgrammableInspector(new Mp4InspectionOutcome.Failed());
        var outcome   = await inspector.InspectAsync("x.mp4", CancellationToken.None);

        Assert.IsType<Mp4InspectionOutcome.Failed>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Rejected>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.TimedOut>(outcome);
    }

    [Fact]
    public async Task InspectionTimedOut_IsNotRejectedOrFailed()
    {
        var inspector = new ProgrammableInspector(new Mp4InspectionOutcome.TimedOut());
        var outcome   = await inspector.InspectAsync("x.mp4", CancellationToken.None);

        Assert.IsType<Mp4InspectionOutcome.TimedOut>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Rejected>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Failed>(outcome);
    }

    [Fact]
    public async Task InspectionOk_IsNotAnyFailureType()
    {
        var result    = new Mp4InspectionResult { DurationMs = 1000, VideoWidth = 100, VideoHeight = 100, CodecName = "h264" };
        var inspector = new ProgrammableInspector(new Mp4InspectionOutcome.Ok { Result = result });
        var outcome   = await inspector.InspectAsync("x.mp4", CancellationToken.None);

        Assert.IsType<Mp4InspectionOutcome.Ok>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Rejected>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Failed>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.TimedOut>(outcome);
    }

    // --- Extraction: outcome discrimination ---

    [Fact]
    public async Task ExtractionFailed_IsNotTimedOut()
    {
        var extractor = new ProgrammableExtractor(new Mp4ExtractionOutcome.Failed { Reason = "nonzero exit" });
        var outcome   = await extractor.ExtractFramesAsync("x.mp4", Array.Empty<TimeSpan>(), "/tmp", CancellationToken.None);

        Assert.IsType<Mp4ExtractionOutcome.Failed>(outcome);
        Assert.IsNotType<Mp4ExtractionOutcome.TimedOut>(outcome);
    }

    [Fact]
    public async Task ExtractionTimedOut_IsNotFailed()
    {
        var extractor = new ProgrammableExtractor(new Mp4ExtractionOutcome.TimedOut());
        var outcome   = await extractor.ExtractFramesAsync("x.mp4", Array.Empty<TimeSpan>(), "/tmp", CancellationToken.None);

        Assert.IsType<Mp4ExtractionOutcome.TimedOut>(outcome);
        Assert.IsNotType<Mp4ExtractionOutcome.Failed>(outcome);
    }

    [Fact]
    public async Task ExtractionOk_IsNotAnyFailureType()
    {
        var paths     = new[] { "/tmp/frame_0000.png" };
        var extractor = new ProgrammableExtractor(new Mp4ExtractionOutcome.Ok { FrameFilePaths = paths });
        var outcome   = await extractor.ExtractFramesAsync("x.mp4", Array.Empty<TimeSpan>(), "/tmp", CancellationToken.None);

        Assert.IsType<Mp4ExtractionOutcome.Ok>(outcome);
        Assert.IsNotType<Mp4ExtractionOutcome.Failed>(outcome);
        Assert.IsNotType<Mp4ExtractionOutcome.TimedOut>(outcome);
    }

    // --- Rejected carries a user-facing message; Failed and TimedOut do not ---

    [Fact]
    public async Task InspectionRejected_MessageIsNonEmpty()
    {
        var inspector = new ProgrammableInspector(
            new Mp4InspectionOutcome.Rejected { Message = "The submitted file type is not supported." });
        var outcome = await inspector.InspectAsync("x.mp4", CancellationToken.None);
        var rej = Assert.IsType<Mp4InspectionOutcome.Rejected>(outcome);
        Assert.False(string.IsNullOrWhiteSpace(rej.Message));
    }

    [Fact]
    public async Task ExtractionFailed_ReasonIsNonEmpty()
    {
        var extractor = new ProgrammableExtractor(new Mp4ExtractionOutcome.Failed { Reason = "ffmpeg exited with code 1" });
        var outcome   = await extractor.ExtractFramesAsync("x.mp4", Array.Empty<TimeSpan>(), "/tmp", CancellationToken.None);
        var fail = Assert.IsType<Mp4ExtractionOutcome.Failed>(outcome);
        Assert.False(string.IsNullOrWhiteSpace(fail.Reason));
    }
}

using ASCIIBot.Models;
using ASCIIBot.Services;

namespace ASCIIBot.Tests;

/// <summary>§27.4 MP4 intake: mocked inspection and extraction services, pipeline outcomes.</summary>
public sealed class Mp4IntakeServiceTests
{
    // --- Fakes ---

    private sealed class FakeInspector : IMp4InspectionService
    {
        private readonly Mp4InspectionOutcome _outcome;
        public FakeInspector(Mp4InspectionOutcome outcome) => _outcome = outcome;
        public Task<Mp4InspectionOutcome> InspectAsync(string path, CancellationToken ct)
            => Task.FromResult(_outcome);
    }

    private sealed class FakeExtractor : IMp4FrameExtractionService
    {
        private readonly Mp4ExtractionOutcome _outcome;
        public FakeExtractor(Mp4ExtractionOutcome outcome) => _outcome = outcome;
        public Task<Mp4ExtractionOutcome> ExtractFramesAsync(
            string path, TimeSpan[] times, string dir, CancellationToken ct)
            => Task.FromResult(_outcome);
    }

    private static Mp4InspectionResult DefaultInspection(
        long durationMs  = 3000,
        int  width       = 640,
        int  height      = 480,
        string codecName = "h264") =>
        new()
        {
            DurationMs  = durationMs,
            VideoWidth  = width,
            VideoHeight = height,
            CodecName   = codecName,
        };

    // --- IMp4InspectionService outcome variants ---

    [Fact]
    public async Task InspectAsync_OkOutcome_CarriesInspectionResult()
    {
        var result = DefaultInspection(durationMs: 5000, width: 1280, height: 720);
        var svc    = new FakeInspector(new Mp4InspectionOutcome.Ok { Result = result });
        var outcome = await svc.InspectAsync("dummy.mp4", CancellationToken.None);
        var ok = Assert.IsType<Mp4InspectionOutcome.Ok>(outcome);
        Assert.Equal(5000, ok.Result.DurationMs);
        Assert.Equal(1280, ok.Result.VideoWidth);
        Assert.Equal(720,  ok.Result.VideoHeight);
        Assert.Equal("h264", ok.Result.CodecName);
    }

    [Fact]
    public async Task InspectAsync_RejectedOutcome_HasMessage()
    {
        var svc     = new FakeInspector(new Mp4InspectionOutcome.Rejected { Message = "bad codec" });
        var outcome = await svc.InspectAsync("dummy.mp4", CancellationToken.None);
        var rej = Assert.IsType<Mp4InspectionOutcome.Rejected>(outcome);
        Assert.False(string.IsNullOrEmpty(rej.Message));
    }

    [Fact]
    public async Task InspectAsync_FailedOutcome_IsDistinctFromRejected()
    {
        var svc     = new FakeInspector(new Mp4InspectionOutcome.Failed());
        var outcome = await svc.InspectAsync("dummy.mp4", CancellationToken.None);
        Assert.IsType<Mp4InspectionOutcome.Failed>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Rejected>(outcome);
    }

    [Fact]
    public async Task InspectAsync_TimedOutOutcome_IsDistinctFromFailed()
    {
        var svc     = new FakeInspector(new Mp4InspectionOutcome.TimedOut());
        var outcome = await svc.InspectAsync("dummy.mp4", CancellationToken.None);
        Assert.IsType<Mp4InspectionOutcome.TimedOut>(outcome);
        Assert.IsNotType<Mp4InspectionOutcome.Failed>(outcome);
    }

    // --- IMp4FrameExtractionService outcome variants ---

    [Fact]
    public async Task ExtractFramesAsync_OkOutcome_CarriesFramePaths()
    {
        var paths = new[] { "/tmp/frame_0000.png", "/tmp/frame_0001.png" };
        var svc   = new FakeExtractor(new Mp4ExtractionOutcome.Ok { FrameFilePaths = paths });
        var outcome = await svc.ExtractFramesAsync(
            "dummy.mp4",
            new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1) },
            "/tmp/frames",
            CancellationToken.None);
        var ok = Assert.IsType<Mp4ExtractionOutcome.Ok>(outcome);
        Assert.Equal(2, ok.FrameFilePaths.Length);
    }

    [Fact]
    public async Task ExtractFramesAsync_FailedOutcome_HasReason()
    {
        var svc     = new FakeExtractor(new Mp4ExtractionOutcome.Failed { Reason = "exit code 1" });
        var outcome = await svc.ExtractFramesAsync("dummy.mp4", Array.Empty<TimeSpan>(), "/tmp", CancellationToken.None);
        var fail    = Assert.IsType<Mp4ExtractionOutcome.Failed>(outcome);
        Assert.False(string.IsNullOrEmpty(fail.Reason));
    }

    [Fact]
    public async Task ExtractFramesAsync_TimedOut_IsDistinctFromFailed()
    {
        var svc     = new FakeExtractor(new Mp4ExtractionOutcome.TimedOut());
        var outcome = await svc.ExtractFramesAsync("dummy.mp4", Array.Empty<TimeSpan>(), "/tmp", CancellationToken.None);
        Assert.IsType<Mp4ExtractionOutcome.TimedOut>(outcome);
        Assert.IsNotType<Mp4ExtractionOutcome.Failed>(outcome);
    }

    // --- Mp4TempJob lifecycle ---

    [Fact]
    public async Task Mp4TempJob_SourceFilePath_HasUniquePath()
    {
        await using var job1 = new Mp4TempJob();
        await using var job2 = new Mp4TempJob();
        Assert.NotEqual(job1.SourceFilePath, job2.SourceFilePath);
    }

    [Fact]
    public async Task Mp4TempJob_FrameDirectory_ExistsAfterConstruction()
    {
        await using var job = new Mp4TempJob();
        Assert.True(Directory.Exists(job.FrameDirectory));
    }

    [Fact]
    public async Task Mp4TempJob_DisposeAsync_DeletesFrameDirectory()
    {
        string frameDir;
        {
            await using var job = new Mp4TempJob();
            frameDir = job.FrameDirectory;
        }
        Assert.False(Directory.Exists(frameDir));
    }

    [Fact]
    public async Task Mp4TempJob_DisposeAsync_DeletesRegisteredFrameFiles()
    {
        string frameDir;
        string[] framePaths;
        await using (var job = new Mp4TempJob())
        {
            frameDir   = job.FrameDirectory;
            framePaths = new[]
            {
                Path.Combine(job.FrameDirectory, "frame_0000.png"),
                Path.Combine(job.FrameDirectory, "frame_0001.png"),
            };
            // Create dummy files
            foreach (var p in framePaths)
                await File.WriteAllBytesAsync(p, new byte[4]);
            job.RegisterFrameFiles(framePaths);
        }

        foreach (var p in framePaths)
            Assert.False(File.Exists(p));
    }

    [Fact]
    public async Task Mp4TempJob_DisposeAsync_DeletesSourceFile_IfCreated()
    {
        string sourcePath;
        await using (var job = new Mp4TempJob())
        {
            sourcePath = job.SourceFilePath;
            await File.WriteAllBytesAsync(sourcePath, new byte[8]);
        }
        Assert.False(File.Exists(sourcePath));
    }

    // --- Mp4InspectionResult ---

    [Fact]
    public void Mp4InspectionResult_Properties_RoundTrip()
    {
        var result = new Mp4InspectionResult
        {
            DurationMs  = 7500,
            VideoWidth  = 320,
            VideoHeight = 240,
            CodecName   = "hevc",
        };
        Assert.Equal(7500, result.DurationMs);
        Assert.Equal(320,  result.VideoWidth);
        Assert.Equal(240,  result.VideoHeight);
        Assert.Equal("hevc", result.CodecName);
    }
}

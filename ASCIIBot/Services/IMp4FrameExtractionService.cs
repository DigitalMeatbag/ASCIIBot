namespace ASCIIBot.Services;

public abstract class Mp4ExtractionOutcome
{
    private Mp4ExtractionOutcome() { }

    public sealed class Ok : Mp4ExtractionOutcome
    {
        public required string[] FrameFilePaths { get; init; }
    }

    public sealed class Failed : Mp4ExtractionOutcome
    {
        public required string Reason { get; init; }
    }

    public sealed class TimedOut : Mp4ExtractionOutcome { }
}

public interface IMp4FrameExtractionService
{
    Task<Mp4ExtractionOutcome> ExtractFramesAsync(
        string     tempFilePath,
        TimeSpan[] sampleTimes,
        string     outputDirectory,
        CancellationToken ct);
}

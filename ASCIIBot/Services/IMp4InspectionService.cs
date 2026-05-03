using ASCIIBot.Models;

namespace ASCIIBot.Services;

public abstract class Mp4InspectionOutcome
{
    private Mp4InspectionOutcome() { }

    public sealed class Ok : Mp4InspectionOutcome
    {
        public required Mp4InspectionResult Result { get; init; }
    }

    public sealed class Rejected : Mp4InspectionOutcome
    {
        public required string Message { get; init; }
    }

    public sealed class Failed : Mp4InspectionOutcome { }

    public sealed class TimedOut : Mp4InspectionOutcome { }
}

public interface IMp4InspectionService
{
    Task<Mp4InspectionOutcome> InspectAsync(string tempFilePath, CancellationToken ct);
}

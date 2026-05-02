namespace ASCIIBot.Models;

public abstract class AnimationInspectionResult
{
    private AnimationInspectionResult() { }

    public sealed class Ok : AnimationInspectionResult
    {
        public required int    CanvasWidth       { get; init; }
        public required int    CanvasHeight      { get; init; }
        public required int    SourceFrameCount  { get; init; }
        public required long   SourceDurationMs  { get; init; }
        public required long[] FrameStartTimesMs { get; init; }
    }

    public sealed class Rejected : AnimationInspectionResult
    {
        public required string Message { get; init; }
    }
}

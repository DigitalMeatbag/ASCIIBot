namespace ASCIIBot.Models;

public sealed class AnimatedAsciiFrame
{
    public required int             Index      { get; init; }
    public required TimeSpan        SampleTime { get; init; }
    public required TimeSpan        Duration   { get; init; }
    public required RichAsciiRender Render     { get; init; }
}

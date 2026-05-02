namespace ASCIIBot.Models;

public sealed class AnimatedAsciiRender
{
    public required int                  Width     { get; init; }
    public required int                  Height    { get; init; }
    public required int                  LoopCount { get; init; }
    public required AnimatedAsciiFrame[] Frames    { get; init; }
}

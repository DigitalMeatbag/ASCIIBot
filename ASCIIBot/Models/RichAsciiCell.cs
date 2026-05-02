namespace ASCIIBot.Models;

public sealed class RichAsciiCell
{
    public required int Row       { get; init; }
    public required int Column    { get; init; }
    public required char Character { get; init; }
    public required RgbColor Foreground { get; init; }
    public RgbColor? Background { get; init; } = null;
}

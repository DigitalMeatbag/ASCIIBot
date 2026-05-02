namespace ASCIIBot.Models;

public sealed class AsciiRenderResult
{
    public required char[][] Chars   { get; init; }
    public required (byte R, byte G, byte B)[][] Colors { get; init; }
    public required int Columns { get; init; }
    public required int Rows    { get; init; }
}

namespace ASCIIBot.Models;

public sealed class RichAsciiRender
{
    public required int Width  { get; init; }
    public required int Height { get; init; }

    // Cells[row][col]
    public required RichAsciiCell[][] Cells { get; init; }
}

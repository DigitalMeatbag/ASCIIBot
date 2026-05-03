namespace ASCIIBot.Models;

public sealed class Mp4InspectionResult
{
    public required long   DurationMs   { get; init; }
    public required int    VideoWidth   { get; init; }
    public required int    VideoHeight  { get; init; }
    public required string CodecName    { get; init; }
}

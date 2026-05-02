namespace ASCIIBot;

public sealed class BotOptions
{
    public string? DiscordToken          { get; set; }
    public int     MaxGlobalJobs         { get; set; } = 3;
    public int     MaxJobsPerUser        { get; set; } = 1;
    public string  LogLevel              { get; set; } = "Information";
    public int     AttachmentByteLimit   { get; set; } = 1_000_000;
    public int     InlineCharacterLimit  { get; set; } = 2_000;
    public int     RenderPngByteLimit    { get; set; } = 8_388_608;   // 8 MiB
    public long    TotalUploadByteLimit  { get; set; } = 12_582_912;  // 12 MiB
    public int     RenderPngMaxWidth     { get; set; } = 4096;
    public int     RenderPngMaxHeight    { get; set; } = 4096;
}

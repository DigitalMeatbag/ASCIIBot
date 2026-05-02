namespace ASCIIBot;

public sealed class BotOptions
{
    public string? DiscordToken          { get; set; }
    public int     MaxGlobalJobs         { get; set; } = 3;
    public int     MaxJobsPerUser        { get; set; } = 1;
    public string  LogLevel              { get; set; } = "Information";
    public int     AttachmentByteLimit   { get; set; } = 1_000_000;
    public int     InlineCharacterLimit  { get; set; } = 2_000;
    public int     RenderPngByteLimit    { get; set; } = 8_388_608;
    public long    TotalUploadByteLimit  { get; set; } = 10_000_000;
    public int     RenderPngMaxWidth     { get; set; } = 4096;
    public int     RenderPngMaxHeight    { get; set; } = 4096;

    // Source image limits
    public int     SourceImageByteLimit     { get; set; } = 10_485_760;  // 10 MiB
    public int     MaxDecodedImageWidth     { get; set; } = 4096;
    public int     MaxDecodedImageHeight    { get; set; } = 4096;

    // Animation config
    public int     AnimationMaxDurationMs       { get; set; } = 12_000;
    public int     AnimationMaxOutputFrames     { get; set; } = 48;
    public int     AnimationTargetSampleIntervalMs { get; set; } = 100;
    public int     AnimationMinFrameDelayMs     { get; set; } = 100;
    public int     AnimationWebPByteLimit       { get; set; } = 8_388_608;  // 8 MiB
    public int     AnimationMaxOutputCells      { get; set; } = 300_000;
    public int     AnimationMaxSourceFrames     { get; set; } = 1_000;
}

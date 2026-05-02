namespace ASCIIBot;

public sealed class BotOptions
{
    public string? DiscordToken        { get; set; }
    public int     MaxGlobalJobs       { get; set; } = 3;
    public int     MaxJobsPerUser      { get; set; } = 1;
    public string  LogLevel            { get; set; } = "Information";
    public int     AttachmentByteLimit { get; set; } = 1_000_000;
    public int     InlineCharacterLimit{ get; set; } = 2_000;
}

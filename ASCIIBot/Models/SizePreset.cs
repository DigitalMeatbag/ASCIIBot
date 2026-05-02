namespace ASCIIBot.Models;

public record SizePreset(int Columns, int MaxLines)
{
    public static SizePreset Small  => new(48,  18);
    public static SizePreset Medium => new(72,  26);
    public static SizePreset Large  => new(100, 35);

    public static SizePreset FromString(string size) => size switch
    {
        "small" => Small,
        "large" => Large,
        _       => Medium,
    };
}

namespace ASCIIBot.Models;

public record DetailPreset(double SampleWindowScale)
{
    public static DetailPreset Low    => new(1.00);
    public static DetailPreset Normal => new(0.75);
    public static DetailPreset High   => new(0.50);

    public static DetailPreset FromString(string? detail) => detail switch
    {
        "low"  => Low,
        "high" => High,
        _      => Normal,
    };
}

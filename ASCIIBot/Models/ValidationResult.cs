using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Models;

public abstract class ValidationResult
{
    private ValidationResult() { }

    public sealed class Ok : ValidationResult
    {
        public required Image<Rgba32> Image { get; init; }
    }

    public sealed class Error : ValidationResult
    {
        public required string Message { get; init; }
    }
}

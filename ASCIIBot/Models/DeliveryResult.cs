namespace ASCIIBot.Models;

public abstract class DeliveryResult
{
    private DeliveryResult() { }

    public sealed class Inline : DeliveryResult
    {
        public required string Message { get; init; }
    }

    public sealed class Attachment : DeliveryResult
    {
        public required string Message  { get; init; }
        public required byte[] Content  { get; init; }
        public required string Filename { get; init; }
    }

    public sealed class Rejected : DeliveryResult
    {
        public required string Message { get; init; }
    }
}

namespace ASCIIBot.Models;

public sealed class RenderFile
{
    public required byte[]  Content  { get; init; }
    public required string  Filename { get; init; }
}

public abstract class DeliveryResult
{
    private DeliveryResult() { }

    public sealed class Inline : DeliveryResult
    {
        public required string      CompletionText  { get; init; }
        public required string      InlinePayload   { get; init; }
        public RenderFile?          OriginalImage   { get; init; }
    }

    public sealed class NonInline : DeliveryResult
    {
        public required string      CompletionText { get; init; }
        public required RenderFile  PngRender      { get; init; }
        public required RenderFile  TxtRender      { get; init; }
        public RenderFile?          OriginalImage  { get; init; }
    }

    public sealed class Animated : DeliveryResult
    {
        public required string     CompletionText { get; init; }
        public required RenderFile WebPRender     { get; init; }
        public RenderFile?         OriginalImage  { get; init; }
    }

    public sealed class Rejected : DeliveryResult
    {
        public required string Message { get; init; }
    }
}

using ASCIIBot.Modules;
using ASCIIBot.Services;

namespace ASCIIBot.Tests;

/// <summary>§27.6 Context command: media resolution logic, hardcoded defaults, gifv embed fallback.</summary>
public sealed class ContextCommandTests
{
    // These tests exercise the media resolution logic directly, isolated from Discord interaction.
    // The resolution algorithm: attachment(s) → exclusive; gifv embed → fallback only if no attachments.
    // We replicate the same conditional structure used in AsciiInteractionModule.AsciiThisAsync.

    private static (string? mediaUrl, long? reportedSize) ResolveMedia(
        IReadOnlyList<FakeAttachment> attachments,
        IReadOnlyList<FakeEmbed>      embeds)
    {
        if (attachments.Count > 0)
        {
            var a = attachments[0];
            return (a.Url, a.Size);
        }

        foreach (var embed in embeds)
        {
            if (embed.IsGifv && embed.VideoUrl is not null)
                return (embed.VideoUrl, null);
        }

        return (null, null);
    }

    // --- Attachment takes priority ---

    [Fact]
    public void MediaResolution_SingleAttachment_UsesAttachmentUrl()
    {
        var (url, _) = ResolveMedia(
            [new FakeAttachment("https://cdn.discord.com/file.mp4", 1000)],
            []);
        Assert.Equal("https://cdn.discord.com/file.mp4", url);
    }

    [Fact]
    public void MediaResolution_AttachmentReportsSize()
    {
        var (_, size) = ResolveMedia(
            [new FakeAttachment("https://cdn.discord.com/file.mp4", 2048)],
            []);
        Assert.Equal(2048, size);
    }

    [Fact]
    public void MediaResolution_AttachmentAndGifvEmbed_UsesAttachment()
    {
        var (url, _) = ResolveMedia(
            [new FakeAttachment("https://cdn.discord.com/attachment.mp4", 1000)],
            [new FakeEmbed(IsGifv: true, VideoUrl: "https://cdn.discord.com/embed.mp4")]);
        Assert.Equal("https://cdn.discord.com/attachment.mp4", url);
    }

    [Fact]
    public void MediaResolution_MultipleAttachments_UsesFirstOnly()
    {
        var (url, _) = ResolveMedia(
            [
                new FakeAttachment("https://cdn.discord.com/first.mp4", 100),
                new FakeAttachment("https://cdn.discord.com/second.mp4", 200),
            ],
            []);
        Assert.Equal("https://cdn.discord.com/first.mp4", url);
    }

    // --- Gifv embed fallback ---

    [Fact]
    public void MediaResolution_NoAttachment_GifvEmbed_UsesEmbedUrl()
    {
        var (url, _) = ResolveMedia(
            [],
            [new FakeEmbed(IsGifv: true, VideoUrl: "https://cdn.discord.com/embed.mp4")]);
        Assert.Equal("https://cdn.discord.com/embed.mp4", url);
    }

    [Fact]
    public void MediaResolution_GifvEmbedFallback_SizeIsNull()
    {
        var (_, size) = ResolveMedia(
            [],
            [new FakeEmbed(IsGifv: true, VideoUrl: "https://cdn.discord.com/embed.mp4")]);
        Assert.Null(size);
    }

    [Fact]
    public void MediaResolution_NonGifvEmbedOnly_ReturnsNull()
    {
        var (url, _) = ResolveMedia(
            [],
            [new FakeEmbed(IsGifv: false, VideoUrl: "https://cdn.discord.com/image.png")]);
        Assert.Null(url);
    }

    // --- No media ---

    [Fact]
    public void MediaResolution_NoAttachmentNoEmbed_ReturnsNull()
    {
        var (url, size) = ResolveMedia([], []);
        Assert.Null(url);
        Assert.Null(size);
    }

    [Fact]
    public void MediaResolution_GifvEmbedWithNullVideoUrl_ReturnsNull()
    {
        var (url, _) = ResolveMedia(
            [],
            [new FakeEmbed(IsGifv: true, VideoUrl: null)]);
        Assert.Null(url);
    }

    // --- Hardcoded defaults ---

    [Fact]
    public void HardcodedDefaults_Size_IsMedium()
    {
        Assert.Equal("medium", AsciiInteractionModule.ContextDefaultSize);
    }

    [Fact]
    public void HardcodedDefaults_Color_IsOn()
    {
        Assert.Equal("on", AsciiInteractionModule.ContextDefaultColor);
    }

    [Fact]
    public void HardcodedDefaults_Detail_IsNormal()
    {
        Assert.Equal("normal", AsciiInteractionModule.ContextDefaultDetail);
    }

    [Fact]
    public void HardcodedDefaults_ShowOriginal_IsTrue()
    {
        Assert.True(AsciiInteractionModule.ContextDefaultShowOriginal);
    }

    // --- Helpers ---

    private sealed record FakeAttachment(string Url, long Size);

    private sealed record FakeEmbed(bool IsGifv, string? VideoUrl);
}

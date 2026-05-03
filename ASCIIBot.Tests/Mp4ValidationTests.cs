using ASCIIBot;
using ASCIIBot.Models;
using ASCIIBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Tests;

/// <summary>§27.3 MP4 validation: ftyp detection, byte-limit enforcement, duration/dimension limits.</summary>
public sealed class Mp4ValidationTests
{
    private static ImageValidationService MakeService(
        int maxWidth  = 4096,
        int maxHeight = 4096) =>
        new(Options.Create(new BotOptions
        {
            MaxDecodedImageWidth  = maxWidth,
            MaxDecodedImageHeight = maxHeight,
        }), NullLogger<ImageValidationService>.Instance);

    // --- ftyp atom detection ---

    [Fact]
    public async Task ValidateAsync_Mp4WithFtypAtom_ReturnsMp4Ok()
    {
        var svc  = MakeService();
        var mp4  = MakeFtypMp4();
        using var stream = new MemoryStream(mp4);
        var result = await svc.ValidateAsync(stream);
        Assert.IsType<ValidationResult.Mp4Ok>(result);
    }

    [Fact]
    public async Task ValidateAsync_Mp4Ok_OriginalBytesPreserved()
    {
        var svc  = MakeService();
        var mp4  = MakeFtypMp4();
        using var stream = new MemoryStream(mp4);
        var result = await svc.ValidateAsync(stream);
        var mp4Ok = Assert.IsType<ValidationResult.Mp4Ok>(result);
        Assert.Equal(mp4, mp4Ok.OriginalBytes);
    }

    [Fact]
    public async Task ValidateAsync_BytesWithoutFtyp_ReturnsError()
    {
        var svc = MakeService();
        // Bytes that look like a size+type but with wrong type
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x10, 0x6D, 0x6F, 0x6F, 0x76 }; // 'moov' not 'ftyp'
        using var stream = new MemoryStream(bytes);
        var result = await svc.ValidateAsync(stream);
        Assert.IsNotType<ValidationResult.Mp4Ok>(result);
    }

    [Fact]
    public async Task ValidateAsync_TooFewBytes_ReturnsError()
    {
        var svc   = MakeService();
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x66, 0x74 }; // only 6 bytes, not enough for ftyp check
        using var stream = new MemoryStream(bytes);
        var result = await svc.ValidateAsync(stream);
        Assert.IsNotType<ValidationResult.Mp4Ok>(result);
    }

    [Fact]
    public async Task ValidateAsync_ExactlyElevenBytes_ReturnsError()
    {
        var svc   = MakeService();
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x10, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F }; // 11 bytes
        using var stream = new MemoryStream(bytes);
        var result = await svc.ValidateAsync(stream);
        // Less than 12 bytes: ftyp detection might return false, but let's just verify it's not a parse success
        // (the ftyp check requires >= 12; here we have 11, but bytes[4..7] = 'ftyp' — however the file
        // is too short to be a real MP4 and the IsMp4ByFtyp guard is on bytes.Length >= 12)
        Assert.IsNotType<ValidationResult.Mp4Ok>(result);
    }

    [Fact]
    public async Task ValidateAsync_EmptyStream_ReturnsError()
    {
        var svc = MakeService();
        using var stream = new MemoryStream(Array.Empty<byte>());
        var result = await svc.ValidateAsync(stream);
        Assert.IsNotType<ValidationResult.Mp4Ok>(result);
    }

    [Fact]
    public async Task ValidateAsync_PngBytes_DoesNotReturnMp4Ok()
    {
        var svc = MakeService();
        // PNG magic bytes
        var pngMagic = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        using var stream = new MemoryStream(pngMagic);
        var result = await svc.ValidateAsync(stream);
        Assert.IsNotType<ValidationResult.Mp4Ok>(result);
    }

    // --- Helpers ---

    /// <summary>
    /// Builds a minimal synthetic MP4 byte sequence with a valid ftyp box header.
    /// Not a real playable MP4 — just enough for ftyp detection to fire.
    /// </summary>
    private static byte[] MakeFtypMp4()
    {
        // ftyp box: [size 4 bytes][type "ftyp" 4 bytes][major brand "isom" 4 bytes][minor version 4 bytes]
        // = 16 bytes minimum
        var bytes = new byte[16];
        bytes[0] = 0x00; bytes[1] = 0x00; bytes[2] = 0x00; bytes[3] = 0x10; // box size = 16
        bytes[4] = 0x66; bytes[5] = 0x74; bytes[6] = 0x79; bytes[7] = 0x70; // 'ftyp'
        bytes[8] = 0x69; bytes[9] = 0x73; bytes[10] = 0x6F; bytes[11] = 0x6D; // major brand 'isom'
        bytes[12] = 0x00; bytes[13] = 0x00; bytes[14] = 0x00; bytes[15] = 0x00; // minor version
        return bytes;
    }
}

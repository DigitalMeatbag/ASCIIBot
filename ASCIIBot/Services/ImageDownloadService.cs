using Microsoft.Extensions.Logging;

namespace ASCIIBot.Services;

public sealed class ImageDownloadService
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MiB

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ImageDownloadService> _logger;

    public ImageDownloadService(IHttpClientFactory httpFactory, ILogger<ImageDownloadService> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <summary>
    /// Downloads up to 10 MiB from <paramref name="url"/> into a <see cref="MemoryStream"/>.
    /// Throws <see cref="ImageTooLargeException"/> if the reported or streamed size exceeds the limit.
    /// </summary>
    public async Task<MemoryStream> DownloadAsync(string url, long? reportedSize, CancellationToken ct)
    {
        if (reportedSize.HasValue && reportedSize.Value > MaxBytes)
            throw new ImageTooLargeException();

        var client   = _httpFactory.CreateClient("images");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        var ms     = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > MaxBytes)
                throw new ImageTooLargeException();
            ms.Write(buffer, 0, read);
        }

        ms.Position = 0;
        _logger.LogDebug("Downloaded {Bytes} bytes", ms.Length);
        return ms;
    }
}

public sealed class ImageTooLargeException : Exception
{
    public ImageTooLargeException() : base("Source image exceeds 10 MiB limit.") { }
}

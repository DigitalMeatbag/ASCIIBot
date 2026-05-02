using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Services;

public sealed class ImageDownloadService
{
    private readonly BotOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ImageDownloadService> _logger;

    public ImageDownloadService(
        IOptions<BotOptions>          options,
        IHttpClientFactory            httpFactory,
        ILogger<ImageDownloadService> logger)
    {
        _options     = options.Value;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    public async Task<MemoryStream> DownloadAsync(string url, long? reportedSize, CancellationToken ct)
    {
        long limit = _options.SourceImageByteLimit;

        if (reportedSize.HasValue && reportedSize.Value > limit)
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
            if (ms.Length + read > limit)
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
    public ImageTooLargeException() : base("Source image exceeds configured size limit.") { }
}

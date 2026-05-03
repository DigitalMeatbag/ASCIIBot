namespace ASCIIBot.Services;

public sealed class Mp4TempJob : IAsyncDisposable
{
    private readonly List<string> _frameFiles = new();

    public string SourceFilePath { get; }
    public string FrameDirectory { get; }

    public Mp4TempJob()
    {
        var baseName   = Path.Combine(Path.GetTempPath(), $"asciibot_{Guid.NewGuid():N}");
        SourceFilePath = baseName + ".mp4";
        FrameDirectory = baseName + "_frames";
        Directory.CreateDirectory(FrameDirectory);
    }

    public void RegisterFrameFiles(IEnumerable<string> files) =>
        _frameFiles.AddRange(files);

    public async ValueTask DisposeAsync()
    {
        try { if (File.Exists(SourceFilePath)) File.Delete(SourceFilePath); } catch { /* best-effort */ }

        foreach (var f in _frameFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best-effort */ }
        }

        try
        {
            if (Directory.Exists(FrameDirectory))
                Directory.Delete(FrameDirectory, recursive: true);
        }
        catch { /* best-effort */ }

        await ValueTask.CompletedTask;
    }
}

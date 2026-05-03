using ASCIIBot.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASCIIBot.Services;

public sealed class AnimatedAsciiRenderService
{
    private readonly AsciiRenderService _renderer;

    public AnimatedAsciiRenderService(AsciiRenderService renderer)
    {
        _renderer = renderer;
    }

    public AnimatedAsciiRender Render(
        Image<Rgba32>          sourceImage,
        int                    cols,
        int                    rows,
        AnimationSamplingResult sampling,
        DetailPreset           detail,
        bool                   colorEnabled)
    {
        int frameCount = sampling.SelectedSourceFrameIndices.Length;
        var frames     = new AnimatedAsciiFrame[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            int sourceFrameIdx = sampling.SelectedSourceFrameIndices[i];

            // CloneFrame returns a fully composited frame — do not AutoOrient
            using var composited = sourceImage.Frames.CloneFrame(sourceFrameIdx);
            var richRender = _renderer.RenderFrame(composited, cols, rows, detail);

            frames[i] = new AnimatedAsciiFrame
            {
                Index      = i,
                SampleTime = sampling.SampleTimes[i],
                Duration   = sampling.OutputDurations[i],
                Render     = richRender,
            };
        }

        return new AnimatedAsciiRender
        {
            Width     = cols,
            Height    = rows,
            LoopCount = 0,
            Frames    = frames,
        };
    }

    /// <summary>
    /// Builds an <see cref="AnimatedAsciiRender"/> from pre-extracted frame images (MP4 pipeline).
    /// Each image in <paramref name="extractedFrames"/> is treated as a fully composited source frame.
    /// </summary>
    public AnimatedAsciiRender RenderFromExtractedFrames(
        IReadOnlyList<SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>> extractedFrames,
        int          cols,
        int          rows,
        TimeSpan[]   sampleTimes,
        TimeSpan[]   outputDurations,
        DetailPreset detail,
        bool         colorEnabled)
    {
        int frameCount = extractedFrames.Count;
        var frames     = new AnimatedAsciiFrame[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            var richRender = _renderer.RenderFrame(extractedFrames[i], cols, rows, detail);

            frames[i] = new AnimatedAsciiFrame
            {
                Index      = i,
                SampleTime = sampleTimes[i],
                Duration   = outputDurations[i],
                Render     = richRender,
            };
        }

        return new AnimatedAsciiRender
        {
            Width     = cols,
            Height    = rows,
            LoopCount = 0,
            Frames    = frames,
        };
    }
}

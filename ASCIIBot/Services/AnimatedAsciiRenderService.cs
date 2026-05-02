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
}

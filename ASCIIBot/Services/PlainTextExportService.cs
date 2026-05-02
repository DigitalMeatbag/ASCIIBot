using ASCIIBot.Models;
using System.Text;

namespace ASCIIBot.Services;

public sealed class PlainTextExportService
{
    public string Export(RichAsciiRender render)
    {
        var sb = new StringBuilder(render.Height * (render.Width + 1));
        for (var row = 0; row < render.Height; row++)
        {
            for (var col = 0; col < render.Width; col++)
                sb.Append(render.Cells[row][col].Character);
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

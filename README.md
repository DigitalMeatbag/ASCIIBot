# ASCIIBot

A Discord bot that converts images to ASCII art via slash command. Supports static images and animated GIF/WebP inputs.

## Usage

```
/ascii image:<attachment> [size] [color] [detail] [show_original]
```

| Option | Values | Default | Description |
|--------|--------|---------|-------------|
| `image` | attachment | — | Image to convert (required) |
| `size` | `small`, `medium`, `large` | `medium` | Output character grid size |
| `color` | `on`, `off` | `on` | ANSI color in Discord `ansi` code blocks |
| `detail` | `low`, `normal`, `high` | `normal` | Sampling detail within the size budget |
| `show_original` | true/false | `true` | Attach the original image alongside the render |

All responses are public in-channel. The bot acknowledges accepted requests immediately and posts the result when conversion completes.

## Output

### Static images

Small renders fit inline as a Discord `ansi` code block. Larger renders are delivered as a PNG image and plain `.txt` attachment. If neither fits within delivery limits, the request is rejected with an explanation.

| Size | Columns | Max rows |
|------|--------:|---------:|
| `small` | 48 | 18 |
| `medium` | 72 | 26 |
| `large` | 100 | 35 |

### Animated images

Animated inputs produce an animated WebP attachment. Routing is automatic — no animation-specific options are required. The original image is attached when `show_original=true` and delivery limits allow it.

Animation limits:

| Limit | Default |
|-------|---------|
| Max source duration | 12 seconds |
| Max source frames | 1,000 |
| Max output frames | 48 |
| Min output frame delay | 100 ms |
| Max animated WebP size | 8 MiB |
| Max total output cells | 300,000 |

Animations exceeding any limit are rejected with an explanation. There is no automatic fallback to static rendering for animated inputs.

## Supported Formats

| Format | Support |
|--------|---------|
| PNG | Static |
| JPEG | Static |
| BMP | Static |
| GIF, single-frame | Static |
| GIF, animated | Animated |
| WebP, static | Static |
| WebP, animated | Animated |
| APNG | Rejected |
| MP4, MOV, WebM, AVIF, TIFF, SVG | Rejected |

Source files above 10 MiB or decoded canvas dimensions above 4096×4096 are rejected.

## Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Discord application with a bot token ([Discord Developer Portal](https://discord.com/developers/applications))

### Configuration

All configuration is via environment variables:

**Common**

| Variable | Required | Default | Description |
|----------|:--------:|--------:|-------------|
| `ASCIIBot_DiscordToken` | yes | — | Discord bot token |
| `ASCIIBot_MaxGlobalJobs` | no | `3` | Max concurrent jobs across all users |
| `ASCIIBot_MaxJobsPerUser` | no | `1` | Max concurrent jobs per user |
| `ASCIIBot_LogLevel` | no | `Information` | Log level (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`) |
| `ASCIIBot_SourceImageByteLimit` | no | `10485760` | Max downloaded source image size in bytes (10 MiB) |
| `ASCIIBot_MaxDecodedImageWidth` | no | `4096` | Max decoded canvas width in pixels |
| `ASCIIBot_MaxDecodedImageHeight` | no | `4096` | Max decoded canvas height in pixels |
| `ASCIIBot_AttachmentByteLimit` | no | `1000000` | Max `.txt` attachment size in bytes |
| `ASCIIBot_InlineCharacterLimit` | no | `2000` | Max inline message characters including formatting |
| `ASCIIBot_RenderPngByteLimit` | no | `8388608` | Max rendered PNG size in bytes (8 MiB) |
| `ASCIIBot_RenderPngMaxWidth` | no | `4096` | Max rendered PNG width in pixels |
| `ASCIIBot_RenderPngMaxHeight` | no | `4096` | Max rendered PNG height in pixels |
| `ASCIIBot_TotalUploadByteLimit` | no | `10000000` | Max total bytes for all files in one completion response |

**Animation**

| Variable | Required | Default | Description |
|----------|:--------:|--------:|-------------|
| `ASCIIBot_AnimationMaxDurationMs` | no | `12000` | Max accepted source animation duration in milliseconds |
| `ASCIIBot_AnimationMaxSourceFrames` | no | `1000` | Max accepted source frame count |
| `ASCIIBot_AnimationMaxOutputFrames` | no | `48` | Max sampled output frames |
| `ASCIIBot_AnimationTargetSampleIntervalMs` | no | `100` | Target interval used to derive output frame count |
| `ASCIIBot_AnimationMinFrameDelayMs` | no | `100` | Minimum emitted output frame delay in milliseconds |
| `ASCIIBot_AnimationWebPByteLimit` | no | `8388608` | Max generated animated WebP size in bytes (8 MiB) |
| `ASCIIBot_AnimationMaxOutputCells` | no | `300000` | Max `cols × rows × frames` cost fuse |

### Running

```bash
export ASCIIBot_DiscordToken=your_token_here
dotnet run --project ASCIIBot
```

On Windows:

```powershell
$env:ASCIIBot_DiscordToken = "your_token_here"
dotnet run --project ASCIIBot
```

### Inviting the Bot

Generate an OAuth2 URL from the Discord Developer Portal with scopes `bot` and `applications.commands`, and bot permissions `Send Messages` and `Attach Files`.

Global slash command registration can take up to one hour to propagate after first startup.

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ASCIIBot/ASCIIBot.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ASCIIBot.dll"]
```

```bash
docker build -t asciibot .
docker run -e ASCIIBot_DiscordToken=your_token_here asciibot
```

## Development

```bash
dotnet build ASCIIBot.slnx
dotnet test ASCIIBot.slnx
```

217 unit tests covering rendering, color mapping, delivery decisions, image validation, animation inspection, sampling, rendering, WebP export, and concurrency.

## Stack

- .NET 10
- [Discord.Net](https://github.com/discord-net/Discord.Net) 3.x
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 3.x
- [SixLabors.ImageSharp.Drawing](https://github.com/SixLabors/ImageSharp.Drawing) 2.x
- Microsoft.Extensions.Hosting

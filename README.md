# ASCIIBot

A Discord bot that converts static images to ASCII art via slash command.

## Usage

```
/ascii image:<attachment> [size] [color]
```

| Option | Values | Default | Description |
|--------|--------|---------|-------------|
| `image` | attachment | — | Image to convert (required) |
| `size` | `small`, `medium`, `large` | `medium` | Output character grid size |
| `color` | `on`, `off` | `on` | ANSI color in Discord `ansi` code blocks |

All responses are public in-channel. The bot acknowledges accepted requests immediately and posts the result when conversion completes.

## Output

Small renders fit inline as a Discord `ansi` code block. Larger renders are delivered as a plain `.txt` attachment.

| Size | Columns | Max rows |
|------|--------:|---------:|
| `small` | 48 | 18 |
| `medium` | 72 | 26 |
| `large` | 100 | 35 |

## Supported Formats

PNG, JPEG, BMP, static GIF, static WebP. Animated images are rejected. Source files above 10 MiB or decoded dimensions above 4096×4096 are rejected.

## Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Discord application with a bot token ([Discord Developer Portal](https://discord.com/developers/applications))

### Configuration

All configuration is via environment variables:

| Variable | Required | Default | Description |
|----------|:--------:|---------|-------------|
| `ASCIIBot_DiscordToken` | yes | — | Discord bot token |
| `ASCIIBot_MaxGlobalJobs` | no | `3` | Max concurrent jobs across all users |
| `ASCIIBot_MaxJobsPerUser` | no | `1` | Max concurrent jobs per user |
| `ASCIIBot_LogLevel` | no | `Information` | Log level (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`) |
| `ASCIIBot_AttachmentByteLimit` | no | `1000000` | Max `.txt` attachment size in bytes |
| `ASCIIBot_InlineCharacterLimit` | no | `1500` | Max inline message characters including formatting |

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

47 unit tests covering rendering, color mapping, delivery decisions, image validation, and concurrency.

## Stack

- .NET 10
- [Discord.Net](https://github.com/discord-net/Discord.Net) 3.x
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 3.x
- Microsoft.Extensions.Hosting

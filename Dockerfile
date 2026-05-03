FROM mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim AS build
WORKDIR /src

COPY ASCIIBot.slnx ./
COPY ASCIIBot/ASCIIBot.csproj ASCIIBot/
COPY ASCIIBot.Tests/ASCIIBot.Tests.csproj ASCIIBot.Tests/
RUN dotnet restore ASCIIBot.slnx

COPY . .
RUN dotnet publish ASCIIBot/ASCIIBot.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --gid 1000 appgroup \
    && useradd --uid 1000 --gid appgroup --no-create-home appuser

COPY --from=build /app/publish .

USER appuser

ENTRYPOINT ["./ASCIIBot"]

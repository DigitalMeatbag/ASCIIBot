using ASCIIBot;
using ASCIIBot.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddEnvironmentVariables(prefix: "ASCIIBot_");
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<BotOptions>(ctx.Configuration);

        services.AddHttpClient("images");

        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.None,
        };
        services.AddSingleton(socketConfig);
        services.AddSingleton<DiscordSocketClient>();

        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { DefaultRunMode = RunMode.Async }
        ));

        services.AddSingleton<ConcurrencyGate>();
        services.AddSingleton<ImageDownloadService>();
        services.AddSingleton<ImageValidationService>();
        services.AddSingleton<AsciiRenderService>();
        services.AddSingleton<AnsiColorService>();
        services.AddSingleton<OutputDeliveryService>();

        services.AddHostedService<BotWorker>();
    })
    .ConfigureLogging((ctx, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        var levelStr = ctx.Configuration["LogLevel"] ?? "Information";
        if (Enum.TryParse<LogLevel>(levelStr, ignoreCase: true, out var level))
            logging.SetMinimumLevel(level);
        else
            logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

await host.RunAsync();

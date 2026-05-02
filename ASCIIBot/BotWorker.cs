using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace ASCIIBot;

internal sealed class BotWorker : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService  _interactions;
    private readonly BotOptions          _options;
    private readonly IConfiguration      _config;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BotWorker>  _logger;
    private readonly IServiceProvider    _services;

    public BotWorker(
        DiscordSocketClient      client,
        InteractionService       interactions,
        IOptions<BotOptions>     options,
        IConfiguration           config,
        IHostApplicationLifetime lifetime,
        ILogger<BotWorker>       logger,
        IServiceProvider         services)
    {
        _client       = client;
        _interactions = interactions;
        _options      = options.Value;
        _config       = config;
        _lifetime     = lifetime;
        _logger       = logger;
        _services     = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ValidateConfig())
        {
            _lifetime.StopApplication();
            return;
        }

        _client.Log                += OnLog;
        _client.Ready              += OnReady;
        _client.InteractionCreated += OnInteractionCreated;

        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);

        await _client.LoginAsync(TokenType.Bot, _options.DiscordToken);
        await _client.StartAsync();

        _logger.LogInformation("ASCIIBot started.");

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await _client.StopAsync();
        _logger.LogInformation("ASCIIBot stopped.");
    }

    private bool ValidateConfig()
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(_options.DiscordToken))
        {
            _logger.LogCritical("ASCIIBot_DiscordToken is required but was not provided.");
            valid = false;
        }

        valid &= ValidateIntOption("MaxGlobalJobs",        _options.MaxGlobalJobs);
        valid &= ValidateIntOption("MaxJobsPerUser",       _options.MaxJobsPerUser);
        valid &= ValidateIntOption("AttachmentByteLimit",  _options.AttachmentByteLimit);
        valid &= ValidateIntOption("InlineCharacterLimit", _options.InlineCharacterLimit);
        valid &= ValidateIntOption("RenderPngByteLimit",   _options.RenderPngByteLimit);
        valid &= ValidateLongOption("TotalUploadByteLimit", _options.TotalUploadByteLimit);
        valid &= ValidateIntOption("RenderPngMaxWidth",    _options.RenderPngMaxWidth);
        valid &= ValidateIntOption("RenderPngMaxHeight",   _options.RenderPngMaxHeight);

        return valid;
    }

    private bool ValidateIntOption(string key, int boundValue)
    {
        var raw = _config[key];
        if (raw is not null && !int.TryParse(raw, out _))
        {
            _logger.LogError("ASCIIBot_{Key} has an invalid value '{Raw}' (must be a positive integer).", key, raw);
            return false;
        }
        if (boundValue <= 0)
        {
            _logger.LogError("ASCIIBot_{Key} must be greater than 0 (got {Value}).", key, boundValue);
            return false;
        }
        return true;
    }

    private bool ValidateLongOption(string key, long boundValue)
    {
        var raw = _config[key];
        if (raw is not null && !long.TryParse(raw, out _))
        {
            _logger.LogError("ASCIIBot_{Key} has an invalid value '{Raw}' (must be a positive integer).", key, raw);
            return false;
        }
        if (boundValue <= 0)
        {
            _logger.LogError("ASCIIBot_{Key} must be greater than 0 (got {Value}).", key, boundValue);
            return false;
        }
        return true;
    }

    private async Task OnReady()
    {
        try
        {
            await _interactions.RegisterCommandsGloballyAsync();
            _logger.LogInformation("Slash commands registered globally.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register slash commands.");
        }
    }

    private async Task OnInteractionCreated(SocketInteraction interaction)
    {
        try
        {
            var ctx = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(ctx, _services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception dispatching interaction.");
        }
    }

    private Task OnLog(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
            LogSeverity.Error    => Microsoft.Extensions.Logging.LogLevel.Error,
            LogSeverity.Warning  => Microsoft.Extensions.Logging.LogLevel.Warning,
            LogSeverity.Info     => Microsoft.Extensions.Logging.LogLevel.Information,
            LogSeverity.Verbose  => Microsoft.Extensions.Logging.LogLevel.Debug,
            LogSeverity.Debug    => Microsoft.Extensions.Logging.LogLevel.Trace,
            _                    => Microsoft.Extensions.Logging.LogLevel.Information,
        };

        _logger.Log(level, "[Discord.Net/{Source}] {Message}", msg.Source, msg.Message ?? msg.Exception?.Message);
        return Task.CompletedTask;
    }
}

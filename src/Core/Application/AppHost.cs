using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ComCross.Core.Services;
using ComCross.Shared.Services;
using ComCross.Shared.Models;
using System.Text.Json;
using System.Globalization;

namespace ComCross.Core.Application;

/// <summary>
/// Core implementation of the application host.
/// Orchestrates the lifecycle of all backend services.
/// </summary>
public class AppHost : IAppHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AppHost> _logger;
    private readonly SemaphoreSlim _shutdownGate = new(1, 1);
    private bool _isInitialized;
    private bool _isShutdown;

    public IServiceProvider ServiceProvider => _serviceProvider;

    public AppHost(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger<AppHost>>();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        _logger.LogInformation("Initializing ComCross Core Engine...");

        try
        {
            // 1. Initialize Database & Migrations
            var db = _serviceProvider.GetRequiredService<AppDatabase>();
            await db.InitializeAsync();
            
            // 2. Load Settings
            var settings = _serviceProvider.GetRequiredService<SettingsService>();
            await settings.InitializeAsync();

            // 2.1 Apply UI culture as early as possible (before UI composition).
            var localization = _serviceProvider.GetRequiredService<ILocalizationService>();
            var desiredCulture = ResolveCulture(settings.Current, localization);
            if (!string.Equals(localization.CurrentCulture, desiredCulture, StringComparison.Ordinal))
            {
                localization.SetCulture(desiredCulture);
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(desiredCulture);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch
            {
                // Best-effort; localization service already has fallback behavior.
            }

            // 3. Start Background Services
            var pluginManager = _serviceProvider.GetRequiredService<PluginManagerService>();
            await pluginManager.InitializeAsync();

            // Start listener auto-accept orchestration (subscribes to host events).
            _ = _serviceProvider.GetRequiredService<ListenerAutoAcceptService>();

            // 3.1 Start shared-memory ingest + frame->MessageStream pump + backpressure bridge
            _ = _serviceProvider.GetRequiredService<SharedMemoryIngestService>();
            _ = _serviceProvider.GetRequiredService<FrameStoreMessageStreamPumpService>();
            _ = _serviceProvider.GetRequiredService<SharedMemoryBackpressureBridgeService>();
            _ = _serviceProvider.GetRequiredService<ExtensionBridgeService>();

            // 3.2 Start session descriptor persistence (committed state -> workspace-state.json)
            _ = _serviceProvider.GetRequiredService<SessionDescriptorPersistenceService>();

            _isInitialized = true;
            _logger.LogInformation("Core Engine initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize Core Engine.");
            throw;
        }
    }

    private static string ResolveCulture(AppSettings settings, ILocalizationService localization)
    {
        static bool IsSupported(string code, ILocalizationService l)
            => l.AvailableCultures.Any(c => string.Equals(c.Code, code, StringComparison.Ordinal));

        var configured = settings.Language;

        if (settings.FollowSystemLanguage)
        {
            // Prefer the user's UI language; fall back to neutral language mapping.
            var system = CultureInfo.CurrentUICulture.Name;
            if (IsSupported(system, localization))
            {
                return system;
            }

            var neutral = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var mapped = localization.AvailableCultures
                .FirstOrDefault(c => c.Code.StartsWith(neutral + "-", StringComparison.Ordinal))
                ?.Code;

            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }
        }

        if (!string.IsNullOrWhiteSpace(configured) && IsSupported(configured, localization))
        {
            return configured;
        }

        return "en-US";
    }

    public async Task ShutdownAsync()
    {
        if (_isShutdown)
        {
            return;
        }

        _logger.LogInformation("Shutting down ComCross Core Engine...");

        await _shutdownGate.WaitAsync();
        try
        {
            if (_isShutdown)
            {
                return;
            }

            var deviceService = _serviceProvider.GetRequiredService<DeviceService>();
            await deviceService.DisposeAsync();

            var pluginManager = _serviceProvider.GetRequiredService<PluginManagerService>();
            await pluginManager.ShutdownAsync();

            _isShutdown = true;
            _logger.LogInformation("Core Engine shutdown complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Core Engine shutdown.");
        }
        finally
        {
            _shutdownGate.Release();
        }
    }

    public async Task NotifyLanguageChangedAsync(string cultureCode)
    {
        _logger.LogInformation("Core Engine: Notifying language change to {Culture}", cultureCode);
        
        try
        {
            var pluginManager = _serviceProvider.GetRequiredService<PluginManagerService>();
            await pluginManager.NotifyPluginsAsync(PluginNotification.LanguageChanged(cultureCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying language change");
        }
    }

    public void Dispose()
    {
        _shutdownGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
        _shutdownGate.Dispose();
    }
}

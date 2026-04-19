using ComCross.Core.Extensions;
using ComCross.Core.Services;
using ComCross.Plugins.Flow;
using ComCross.Plugins.Network;
using ComCross.Plugins.Serial;
using ComCross.Plugins.Stats;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace ComCross.Tests.Core;

[Collection("plugin-manager-integration")]
public sealed class PluginManagerIntegrationTests
{
    private static int? GetExtensionHostPid(ExtensionRuntimeService runtimeService)
    {
        var field = typeof(ExtensionRuntimeService).GetField("_hostProcess", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var process = field?.GetValue(runtimeService) as System.Diagnostics.Process;
        return process?.HasExited == false ? process.Id : null;
    }

    [Fact]
    public async Task DisableExtensionPlugin_DoesNotReloadBusPlane()
    {
        await using var harness = await PluginManagerHarness.CreateAsync();

        var pluginManager = harness.Services.GetRequiredService<PluginManagerService>();
        var extensionRuntime = harness.Services.GetRequiredService<ExtensionRuntimeService>();
        await pluginManager.InitializeAsync();

        var serialBefore = pluginManager.GetRuntime("serial.adapter");
        var statsBefore = pluginManager.GetRuntime("serial.stats");
        var flowBefore = pluginManager.GetRuntime("serial.flow");
        var hostPidBefore = GetExtensionHostPid(extensionRuntime);

        Assert.NotNull(serialBefore);
        Assert.NotNull(statsBefore);
        Assert.NotNull(flowBefore);
        Assert.NotNull(hostPidBefore);

        var result = await pluginManager.SetPluginEnabledAsync("serial.stats", enabled: false);

        Assert.True(result.Success);
        Assert.Same(serialBefore, pluginManager.GetRuntime("serial.adapter"));
        Assert.Null(pluginManager.GetRuntime("serial.stats"));
        Assert.NotNull(pluginManager.GetRuntime("serial.flow"));
        Assert.Equal(hostPidBefore, GetExtensionHostPid(extensionRuntime));
        Assert.Contains(pluginManager.GetAllRuntimes(), runtime =>
            runtime.Info.Manifest.Id == "serial.stats" && runtime.State == PluginLoadState.Disabled);
    }

    [Fact]
    public async Task DisableBusPlugin_DoesNotReloadExtensionPlane()
    {
        await using var harness = await PluginManagerHarness.CreateAsync();

        var pluginManager = harness.Services.GetRequiredService<PluginManagerService>();
        await pluginManager.InitializeAsync();

        var serialBefore = pluginManager.GetRuntime("serial.adapter");
        var statsBefore = pluginManager.GetRuntime("serial.stats");

        Assert.NotNull(serialBefore);
        Assert.NotNull(statsBefore);

        var result = await pluginManager.SetPluginEnabledAsync("serial.adapter", enabled: false);

        Assert.True(result.Success);
        Assert.Null(pluginManager.GetRuntime("serial.adapter"));
        Assert.Same(statsBefore, pluginManager.GetRuntime("serial.stats"));
        Assert.Contains(pluginManager.GetAllRuntimes(), runtime =>
            runtime.Info.Manifest.Id == "serial.adapter" && runtime.State == PluginLoadState.Disabled);
    }

    [Fact]
    public async Task DisablePlugin_IsBlocked_WhenPluginHasActiveSessions()
    {
        await using var harness = await PluginManagerHarness.CreateAsync();

        var pluginManager = harness.Services.GetRequiredService<PluginManagerService>();
        var sessionHosts = harness.Services.GetRequiredService<SessionHostRuntimeService>();
        await pluginManager.InitializeAsync();

        var serialRuntime = Assert.Single(pluginManager.GetAllRuntimes(), runtime => runtime.Info.Manifest.Id == "serial.adapter");
        await sessionHosts.EnsureStartedAsync(serialRuntime.Info, "session-lock");

        try
        {
            var result = await pluginManager.SetPluginEnabledAsync("serial.adapter", enabled: false);

            Assert.False(result.Success);
            Assert.Equal(PluginToggleFailureReason.ActiveSessions, result.FailureReason);
            Assert.NotNull(pluginManager.GetRuntime("serial.adapter"));
        }
        finally
        {
            await sessionHosts.StopAsync("session-lock", TimeSpan.FromSeconds(1), "test-cleanup");
        }
    }

    [Fact]
    public async Task UnknownPluginToggle_ReturnsNotFound()
    {
        await using var harness = await PluginManagerHarness.CreateAsync();

        var pluginManager = harness.Services.GetRequiredService<PluginManagerService>();
        await pluginManager.InitializeAsync();

        var result = await pluginManager.SetPluginEnabledAsync("missing.plugin", enabled: false);

        Assert.False(result.Success);
        Assert.Equal(PluginToggleFailureReason.NotFound, result.FailureReason);
    }

    [Fact]
    public async Task ExecuteActionAsync_RoutesCustomPluginAction_ToSessionHost()
    {
        await using var harness = await PluginManagerHarness.CreateAsync();

        var pluginManager = harness.Services.GetRequiredService<PluginManagerService>();
        var sessionHosts = harness.Services.GetRequiredService<SessionHostRuntimeService>();
        var protocol = harness.Services.GetRequiredService<PluginHostProtocolService>();
        await pluginManager.InitializeAsync();

        var networkRuntime = Assert.Single(pluginManager.GetAllRuntimes(), runtime => runtime.Info.Manifest.Id == "network.adapter");
        await sessionHosts.EnsureStartedAsync(
            networkRuntime.Info,
            "listener-action",
            capabilityId: "tcp.server",
            supportsMultiSession: true,
            multiSessionGroupId: "listener-action");

        try
        {
            var (ok, error, _) = await protocol.ExecuteActionAsync(
                networkRuntime,
                "network.reject-all-pending",
                "listener-action",
                JsonSerializer.SerializeToElement(new { }),
                TimeSpan.FromSeconds(3));

            Assert.False(ok);
            Assert.Equal("Listener session not found.", error);
        }
        finally
        {
            await sessionHosts.StopAsync("listener-action", TimeSpan.FromSeconds(1), "test-cleanup");
        }
    }

    private sealed class PluginManagerHarness : IAsyncDisposable
    {
        private readonly string _configDirectory;
        private readonly string _pluginsDirectory;
        private readonly string? _pluginsBackupDirectory;

        private PluginManagerHarness(IServiceProvider services, string configDirectory, string pluginsDirectory, string? pluginsBackupDirectory)
        {
            Services = services;
            _configDirectory = configDirectory;
            _pluginsDirectory = pluginsDirectory;
            _pluginsBackupDirectory = pluginsBackupDirectory;
        }

        public IServiceProvider Services { get; }

        public static async Task<PluginManagerHarness> CreateAsync()
        {
            var baseDir = AppContext.BaseDirectory;
            var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            var pluginsDirectory = Path.Combine(baseDir, "plugins");
            var pluginsBackupDirectory = Directory.Exists(pluginsDirectory)
                ? pluginsDirectory + ".bak-" + Guid.NewGuid().ToString("N")
                : null;

            if (pluginsBackupDirectory is not null)
            {
                Directory.Move(pluginsDirectory, pluginsBackupDirectory);
            }

            Directory.CreateDirectory(pluginsDirectory);

            CopyHostOutputs(repoRoot, baseDir, "PluginHost");
            CopyHostOutputs(repoRoot, baseDir, "ExtensionHost");
            CopyHostOutputs(repoRoot, baseDir, "SessionHost");

            CopyPluginAssembly(typeof(FlowTool).Assembly.Location, Path.Combine(pluginsDirectory, "z-serial.flow"));
            CopyPluginAssembly(typeof(NetworkBusAdapterPlugin).Assembly.Location, Path.Combine(pluginsDirectory, "z-network.adapter"));
            CopyPluginAssembly(typeof(SerialBusAdapterPlugin).Assembly.Location, Path.Combine(pluginsDirectory, "z-serial.adapter"));
            CopyPluginAssembly(typeof(StatsTool).Assembly.Location, Path.Combine(pluginsDirectory, "z-serial.stats"));

            var configDirectory = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDirectory);

            var services = new ServiceCollection();
            services.AddComCrossCore();
            services.AddSingleton(new ConfigService(configDirectory));

            var provider = services.BuildServiceProvider();
            var database = provider.GetRequiredService<AppDatabase>();
            await database.InitializeAsync();
            await provider.GetRequiredService<SettingsService>().InitializeAsync();

            return new PluginManagerHarness(provider, configDirectory, pluginsDirectory, pluginsBackupDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            if (Services.GetService<PluginManagerService>() is { } pluginManager)
            {
                try
                {
                    await pluginManager.ShutdownAsync();
                }
                catch
                {
                }
            }

            if (Services is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            try
            {
                if (Directory.Exists(_pluginsDirectory))
                {
                    Directory.Delete(_pluginsDirectory, recursive: true);
                }

                if (!string.IsNullOrWhiteSpace(_pluginsBackupDirectory) && Directory.Exists(_pluginsBackupDirectory))
                {
                    Directory.Move(_pluginsBackupDirectory, _pluginsDirectory);
                }
            }
            catch
            {
            }

            try
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
            catch
            {
            }
        }

        private static void CopyHostOutputs(string repoRoot, string baseDir, string projectName)
        {
            var sourceDir = Path.Combine(repoRoot, "src", projectName, "bin", "Release", "net8.0");
            foreach (var file in Directory.GetFiles(sourceDir, $"ComCross.{projectName}*"))
            {
                var target = Path.Combine(baseDir, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
            }
        }

        private static void CopyPluginAssembly(string assemblyPath, string pluginDir)
        {
            Directory.CreateDirectory(pluginDir);
            var target = Path.Combine(pluginDir, Path.GetFileName(assemblyPath));
            File.Copy(assemblyPath, target, overwrite: true);
        }

    }
}

[CollectionDefinition("plugin-manager-integration", DisableParallelization = true)]
public sealed class PluginManagerIntegrationCollection
{
}

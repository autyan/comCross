using ComCross.Core.Extensions;
using ComCross.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class WorkloadLifecycleTests
{
    [Fact]
    public async Task DeleteWorkloadAsync_RemovesPersistedWorkload_AndResolvesActiveToDefault()
    {
        await using var harness = await TestHarness.CreateAsync();

        var workloadService = harness.Services.GetRequiredService<WorkloadService>();
        var configService = harness.Services.GetRequiredService<ConfigService>();

        var created = await workloadService.CreateWorkloadAsync("Delete Me");
        Assert.True(await workloadService.SetActiveWorkloadAsync(created.Id));

        var (success, errorMessage) = await workloadService.DeleteWorkloadAsync(created.Id);

        Assert.True(success, errorMessage);

        var persisted = await configService.LoadWorkspaceStateAsync();
        Assert.NotNull(persisted);
        Assert.DoesNotContain(persisted!.Workloads, workload => workload.Id == created.Id);
        Assert.Equal(persisted.GetDefaultWorkload()?.Id, persisted.ActiveWorkloadId);
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly string _configDirectory;

        private TestHarness(IServiceProvider services, string configDirectory)
        {
            Services = services;
            _configDirectory = configDirectory;
        }

        public IServiceProvider Services { get; }

        public static Task<TestHarness> CreateAsync()
        {
            var configDirectory = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDirectory);

            var services = new ServiceCollection();
            services.AddComCrossCore();
            services.AddSingleton(new ConfigService(configDirectory));

            return Task.FromResult(new TestHarness(services.BuildServiceProvider(), configDirectory));
        }

        public async ValueTask DisposeAsync()
        {
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
                Directory.Delete(_configDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }
}

using System.Text.Json;
using ComCross.Core.Services;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task InitializeAsync_MigratesLegacyLogsIntoSessionStorage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var settingsPath = Path.Combine(tempDir, "app-settings.json");
            await File.WriteAllTextAsync(
                settingsPath,
                """
                {
                  "Logs": {
                    "MaxFileSizeMb": 7,
                    "MaxTotalSizeMb": 123,
                    "MaxPerSessionSizeMb": 45
                  }
                }
                """);

            var database = new AppDatabase(tempDir);
            await database.InitializeAsync();
            var settings = new SettingsService(new ConfigService(tempDir), database);

            await settings.InitializeAsync();

            Assert.Equal(7, settings.Current.SessionStorage.SegmentSizeLimitMb);
            Assert.Equal(123, settings.Current.SessionStorage.GlobalSizeLimitMb);
            Assert.Equal(45, settings.Current.SessionStorage.PerSessionSizeLimitMb);
            Assert.Null(settings.Current.Logs);

            await settings.SaveAsync();
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
            Assert.True(saved.RootElement.TryGetProperty("SessionStorage", out _));
            Assert.False(saved.RootElement.TryGetProperty("Logs", out _));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }
}

using ComCross.Core.Services;
using ComCross.Shared.Models;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class CommandDefaultsTests
{
    [Fact]
    public async Task InitializeAsync_SeedsEditableDefaultCommands_WhenSettingsAreNew()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var database = new AppDatabase(tempDir);
            await database.InitializeAsync();
            var localization = new LocalizationService();
            localization.SetCulture("zh-CN");
            var settings = new SettingsService(new ConfigService(tempDir), database);
            var defaults = new CommandDefaultService(settings, localization);

            await settings.InitializeAsync();
            await defaults.EnsureInitializedAsync();

            Assert.True(settings.Current.Commands.DefaultsInitialized);
            Assert.Contains(settings.Current.Commands.GlobalCommands, c => c.Name == "AT" && c.IsPreset && c.IsPinned);
            Assert.Contains(settings.Current.Commands.GlobalCommands, c => c.Name == "CRLF" && c.IsPreset && c.IsPinned);
            Assert.Contains(settings.Current.Commands.GlobalCommands, c => c.Name == "Heartbeat" && c.IsPreset && c.IsPinned);
            Assert.Contains(settings.Current.Commands.GlobalCommands, c => c.Name == "JSON Ping" && c.Group == "默认快捷指令" && !c.IsPinned);
            Assert.Equal(3, settings.Current.Commands.GlobalCommands.Count(c => c.IsPinned));
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

    [Fact]
    public async Task InitializeAsync_DoesNotAppendDefaults_WhenExistingStorageHasCommands()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var database = new AppDatabase(tempDir);
            await database.InitializeAsync();
            var config = new ConfigService(tempDir);
            await config.SaveAppSettingsAsync(new AppSettings
            {
                Commands = new CommandSettings
                {
                    GlobalCommands =
                    [
                        new CommandDefinition
                        {
                            Id = "user-command",
                            Name = "User Command",
                            Payload = "custom"
                        }
                    ]
                }
            });

            var localization = new LocalizationService();
            var settings = new SettingsService(config, database);
            var defaults = new CommandDefaultService(settings, localization);

            await settings.InitializeAsync();
            await defaults.EnsureInitializedAsync();

            Assert.True(settings.Current.Commands.DefaultsInitialized);
            var command = Assert.Single(settings.Current.Commands.GlobalCommands);
            Assert.Equal("User Command", command.Name);
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

    [Fact]
    public async Task EnsureInitializedAsync_UsesEnglishFallback_WhenCultureHasNoResource()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comcross-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var database = new AppDatabase(tempDir);
            await database.InitializeAsync();
            var localization = new LocalizationService();
            localization.SetCulture("fr-FR");
            var settings = new SettingsService(new ConfigService(tempDir), database);
            var defaults = new CommandDefaultService(settings, localization);

            await settings.InitializeAsync();
            await defaults.EnsureInitializedAsync();

            Assert.Contains(settings.Current.Commands.GlobalCommands, c =>
                c.Name == "JSON Ping" && c.Group == "Default Quick Commands");
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

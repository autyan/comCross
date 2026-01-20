using System;
using System.Threading.Tasks;

namespace ComCross.Core.Application;

/// <summary>
/// Defines the top-level application host that manages the core lifecycle.
/// </summary>
public interface IAppHost : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the service provider for the application.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Initializes the core engine, databases, and services.
    /// This should be called before the UI starts.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Shuts down the application and cleans up all core resources and plugins.
    /// </summary>
    Task ShutdownAsync();
    
    /// <summary>
    /// Notifies all components and plugins about a language change.
    /// </summary>
    Task NotifyLanguageChangedAsync(string cultureCode);
}

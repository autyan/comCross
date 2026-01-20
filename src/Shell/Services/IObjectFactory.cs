using System;

namespace ComCross.Shell.Services;

/// <summary>
/// Centralized factory for creating objects using DI + runtime arguments.
/// This is used to avoid manual "new" of Views/ViewModels while still allowing
/// passing runtime values (e.g., list item models, command instances).
/// </summary>
public interface IObjectFactory
{
    T Create<T>(params object?[] args) where T : notnull;
}

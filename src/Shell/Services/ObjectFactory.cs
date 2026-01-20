using System;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Services;

public sealed class ObjectFactory : IObjectFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ObjectFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T Create<T>(params object?[] args) where T : notnull
    {
        var parameters = args is null ? Array.Empty<object>() : Array.ConvertAll(args, static a => a!);
        return ActivatorUtilities.CreateInstance<T>(_serviceProvider, parameters);
    }
}

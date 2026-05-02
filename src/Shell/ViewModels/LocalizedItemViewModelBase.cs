using System;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public abstract class LocalizedItemViewModelBase<TContext> : BaseViewModel, IInitializable<TContext>
{
    private bool _isInitialized;

    protected LocalizedItemViewModelBase(ILocalizationService localization)
        : base(localization)
    {
    }

    public void Init(TContext context)
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException($"{GetType().Name} already initialized.");
        }

        _isInitialized = true;
        OnInit(context);
    }

    protected abstract void OnInit(TContext context);
}

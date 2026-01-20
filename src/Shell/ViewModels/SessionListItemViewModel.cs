using System;
using System.ComponentModel;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class SessionListItemViewModel : BaseViewModel, IInitializable<Session>
{
    private Session? _session;
    private bool _isInitialized;

    public SessionListItemViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    public void Init(Session session)
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("SessionListItemViewModel already initialized.");
        }

        _isInitialized = true;
        _session = session;
        _session.PropertyChanged += OnSessionPropertyChanged;

        // Ensure UI refresh on initial attach.
        OnPropertyChanged(null);
    }

    public Session Session => _session ?? throw new InvalidOperationException("SessionListItemViewModel not initialized.");

    public string Name => Session.Name;
    public string Port => Session.Port;
    public long RxBytes => Session.RxBytes;
    public long TxBytes => Session.TxBytes;
    public SessionStatus Status => Session.Status;

    public string TxLabel => L["status.tx"];
    public string RxLabel => L["status.rx"];

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Session.Name):
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(Session.RxBytes):
                OnPropertyChanged(nameof(RxBytes));
                break;
            case nameof(Session.TxBytes):
                OnPropertyChanged(nameof(TxBytes));
                break;
            case nameof(Session.Status):
                OnPropertyChanged(nameof(Status));
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session != null)
            {
                _session.PropertyChanged -= OnSessionPropertyChanged;
            }
        }

        base.Dispose(disposing);
    }
}

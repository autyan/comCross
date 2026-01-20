using System;
using System.ComponentModel;
using ComCross.Shared.Models;
using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class SessionListItemViewModel : BaseViewModel, IDisposable
{
    private readonly Session _session;

    public SessionListItemViewModel(ILocalizationService localization, Session session)
        : base(localization)
    {
        _session = session;
        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    public Session Session => _session;

    public string Name => _session.Name;
    public string Port => _session.Port;
    public long RxBytes => _session.RxBytes;
    public long TxBytes => _session.TxBytes;
    public SessionStatus Status => _session.Status;

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

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionPropertyChanged;
    }
}

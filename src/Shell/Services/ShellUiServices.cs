using System;
namespace ComCross.Shell.Services;

public static class ShellUiServices
{
    private static IConnectDialogService? _connectDialogService;
    private static ISessionRenameDialogService? _sessionRenameDialogService;
    private static ITestConnectDialogService? _testConnectDialogService;

    public static void Initialize(
        IConnectDialogService connectDialogService,
        ISessionRenameDialogService sessionRenameDialogService,
        ITestConnectDialogService testConnectDialogService)
    {
        _connectDialogService = connectDialogService;
        _sessionRenameDialogService = sessionRenameDialogService;
        _testConnectDialogService = testConnectDialogService;
    }

    public static IConnectDialogService ConnectDialogService
        => _connectDialogService ?? throw new InvalidOperationException("ShellUiServices not initialized");

    public static ISessionRenameDialogService SessionRenameDialogService
        => _sessionRenameDialogService ?? throw new InvalidOperationException("ShellUiServices not initialized");

    public static ITestConnectDialogService TestConnectDialogService
        => _testConnectDialogService ?? throw new InvalidOperationException("ShellUiServices not initialized");
}

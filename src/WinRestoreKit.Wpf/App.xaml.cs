using System;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;

namespace WinRestoreKit.Wpf;

public partial class App : Application
{
    private WpfThemeService themes;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegistryThemeSettings settings = new RegistryThemeSettings();
        WpfDialogService dialogs = new WpfDialogService(Dispatcher, () => MainWindow);
        themes = new WpfThemeService(Resources, settings, new WindowsThemeDetector());
        WpfUpdatePresenter updates = new WpfUpdatePresenter(new UpdateCheckService(), dialogs,
                                                             new ExternalLinkService());
        ShellViewModel shell = new ShellViewModel(themes, updates,
            VersionInfo.GetCurrentVersion(typeof(App).Assembly));
        MainWindow window = new MainWindow(shell);

        Utils.UrlFailureUi = (url, exception) => Dispatcher.BeginInvoke(() =>
            dialogs.ShowWarning("Could not open this link in your browser:\n\n" + url + "\n\n" +
                                exception.Message, "Unable to open link"));

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        themes?.Dispose();
        base.OnExit(e);
    }
}

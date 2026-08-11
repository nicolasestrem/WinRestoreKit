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

        // Shell construction (theme service, version read, registry theme settings) runs here, before
        // the window exists. An exception in that path - like the OsHelper null-deref that once
        // terminated the WinForms shell via WER with no dialog - must not escape silently. Show the
        // diagnostic, then rethrow so WER / the Event Log keeps the real stack. Mirrors the catch that
        // lived around Application.Run(new MainForm()) in the WinForms Program.Main.
        try
        {
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
        catch (Exception ex)
        {
            MessageBox.Show(StartupDiagnostics.DescribeStartupFailure(ex),
                "WinRestoreKit", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        themes?.Dispose();
        base.OnExit(e);
    }
}

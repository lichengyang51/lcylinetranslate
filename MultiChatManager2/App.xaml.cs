using System;
using System.IO;
using System.Windows;

namespace MultiChatManager2
{
    public partial class App : Application
    {
        private LicenseManager? _licenseManager;

        protected override async void OnStartup(
            StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode =
                ShutdownMode.OnExplicitShutdown;

            string dataFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MultiChatManager2");

            string functionUrl =
                "https://iyadtuwabsmiohkyfvqv.supabase.co/functions/v1/verify-license";

            string publishableKey =
                "sb_publishable_7VocoBlUphbJLxqq62_MXg_5vLWlnHF";

            _licenseManager =
                new LicenseManager(
                    dataFolder,
                    functionUrl,
                    publishableKey);

            bool licensePassed =
                false;

            try
            {
                LicenseResult savedLicenseResult =
                    await _licenseManager
                        .VerifySavedLicenseAsync();

                if (savedLicenseResult.Success)
                {
                    licensePassed =
                        true;
                }
                else
                {
                    ActivationWindow activationWindow =
                        new ActivationWindow(
                            _licenseManager);

                    bool? activationResult =
                        activationWindow.ShowDialog();

                    licensePassed =
                        activationResult == true;
                }

                if (!licensePassed)
                {
                    Shutdown();
                    return;
                }

                MainWindow mainWindow =
                    new MainWindow();

                MainWindow =
                    mainWindow;

                ShutdownMode =
                    ShutdownMode.OnMainWindowClose;

                mainWindow.Show();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "程序启动失败：\n\n" +
                    exception.Message,
                    "MultiChatManager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
            }
        }

        protected override void OnExit(
            ExitEventArgs e)
        {
            _licenseManager?.Dispose();

            base.OnExit(e);
        }
    }
}

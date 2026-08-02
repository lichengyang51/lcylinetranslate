using System.Windows;
using System.Windows.Controls;

namespace MultiChatManager2
{
    public sealed class AccountPlatformSelectionWindow : Window
    {
        public string SelectedPlatform { get; private set; } =
            string.Empty;

        public AccountPlatformSelectionWindow()
        {
            Title =
                "选择应用";

            Width =
                360;

            Height =
                205;

            ResizeMode =
                ResizeMode.NoResize;

            WindowStartupLocation =
                WindowStartupLocation.CenterOwner;

            Grid grid =
                new Grid
                {
                    Margin =
                        new Thickness(20)
                };

            grid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

            grid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        new GridLength(1, GridUnitType.Star)
                });

            TextBlock title =
                new TextBlock
                {
                    Text =
                        "请选择要添加的应用：",
                    FontSize =
                        15,
                    FontWeight =
                        FontWeights.SemiBold,
                    Margin =
                        new Thickness(0, 0, 0, 16)
                };

            StackPanel options =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            Button lineButton =
                new Button
                {
                    Content =
                        "LINE",
                    Width =
                        130,
                    Height =
                        52,
                    Margin =
                        new Thickness(0, 0, 12, 0),
                    FontSize =
                        15
                };

            Button whatsAppButton =
                new Button
                {
                    Content =
                        "WhatsApp",
                    Width =
                        130,
                    Height =
                        52,
                    FontSize =
                        15
                };

            lineButton.Click +=
                (_, __) => SelectPlatform("LINE");

            whatsAppButton.Click +=
                (_, __) => SelectPlatform("WhatsApp");

            options.Children.Add(
                lineButton);

            options.Children.Add(
                whatsAppButton);

            Grid.SetRow(
                options,
                1);

            grid.Children.Add(
                title);

            grid.Children.Add(
                options);

            Content =
                grid;
        }

        private void SelectPlatform(
            string platform)
        {
            SelectedPlatform =
                platform;

            DialogResult =
                true;
        }
    }
}

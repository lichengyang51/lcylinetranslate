using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiChatManager2
{
    internal static class ActivationCodeDisplayMask
    {
        private static readonly Regex VipActivationCodePattern =
            new(
                @"^VIP-[A-Za-z0-9-]+$",
                RegexOptions.CultureInvariant |
                RegexOptions.IgnoreCase);

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(WindowLoaded));
        }

        private static void WindowLoaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Window window ||
                !string.Equals(
                    window.Title,
                    "激活状态",
                    StringComparison.Ordinal))
            {
                return;
            }

            EnableMasking(window);
        }

        private static void EnableMasking(Window window)
        {
            foreach (TextBlock textBlock in
                     FindVisualChildren<TextBlock>(window))
            {
                DependencyPropertyDescriptor?
                    textDescriptor =
                        DependencyPropertyDescriptor.FromProperty(
                            TextBlock.TextProperty,
                            typeof(TextBlock));

                textDescriptor?.AddValueChanged(
                    textBlock,
                    (_, _) => MaskActivationCode(textBlock));

                MaskActivationCode(textBlock);
            }
        }

        private static void MaskActivationCode(TextBlock textBlock)
        {
            string text = textBlock.Text ?? string.Empty;

            if (!VipActivationCodePattern.IsMatch(text))
            {
                return;
            }

            textBlock.Text = "VIP-****-****";
        }

        private static System.Collections.Generic.IEnumerable<T>
            FindVisualChildren<T>(DependencyObject parent)
            where T : DependencyObject
        {
            int childCount =
                VisualTreeHelper.GetChildrenCount(parent);

            for (int index = 0;
                 index < childCount;
                 index++)
            {
                DependencyObject child =
                    VisualTreeHelper.GetChild(parent, index);

                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T descendant in
                         FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}

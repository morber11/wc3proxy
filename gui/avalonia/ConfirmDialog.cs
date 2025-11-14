using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace wc3proxy.avalonia
{
    public static class ConfirmDialog
    {
        public static Task<bool> ShowConfirm(Window? parent, string title, string message)
        {
            var tcs = new TaskCompletionSource<bool>();

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 150,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var btnYes = new Button { Content = "Yes", Width = 80, Margin = new Thickness(4, 0) };
            var btnNo = new Button { Content = "No", Width = 80, Margin = new Thickness(4, 0) };

            btnYes.Click += (_, __) => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(true); dialog.Close(); };
            btnNo.Click += (_, __) => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(false); dialog.Close(); };

            btnPanel.Children.Add(btnNo);
            btnPanel.Children.Add(btnYes);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;

            dialog.Closed += (_, __) => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(false); };

            // show dialog without awaiting — callers will await the Task returned here
            if (parent is not null)
            {
                _ = dialog.ShowDialog(parent);
            }
            else
            {
                dialog.Show();
            }

            return tcs.Task;
        }

        public static Task ShowAlert(Window? parent, string title, string message)
        {
            var tcs = new TaskCompletionSource<bool>();

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 140,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var btn = new Button { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            btn.Click += (_, __) => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(true); dialog.Close(); };
            panel.Children.Add(btn);

            dialog.Content = panel;
            dialog.Closed += (_, __) => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(true); };

            if (parent is not null)
            {
                _ = dialog.ShowDialog(parent);
            }
            else
            {
                dialog.Show();
            }
            
            return tcs.Task;
        }
    }
}

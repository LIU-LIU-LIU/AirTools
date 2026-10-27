using System.Windows;
using System.Windows.Controls;
using AirTools.Services;

namespace AirTools.Agent;

internal sealed class CredentialWindow : Window
{
    public CredentialWindow(LaunchTask task, Action save)
    {
        Title = "AirTools · SSH 凭据"; Width = 420; Height = 220; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = task.Name + "\n密码 / 私钥口令（当前 Windows 用户加密保存）", TextWrapping = TextWrapping.Wrap });
        var password = new PasswordBox { Margin = new Thickness(0, 15, 0, 15) }; panel.Children.Add(password);
        var button = new Button { Content = "保存凭据", Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) => { var old = task.EncryptedPassword; try { task.EncryptedPassword = TaskRunner.Protect(password.Password); save(); password.Clear(); Close(); } catch (Exception ex) { task.EncryptedPassword = old; MessageBox.Show(ex.Message, "保存失败"); } };
        panel.Children.Add(button); Content = panel;
    }
}

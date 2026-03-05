using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MqttExplorer.Avalonia.ViewModels;

namespace MqttExplorer.Avalonia.Views;

public partial class AboutWindow : Window
{
    private readonly AboutDialogViewModel _viewModel;

    public AboutWindow()
        : this(AboutDialogViewModel.Create())
    {
    }

    public AboutWindow(AboutDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnReportIssuePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.ReportIssueMailTo,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Ignore failures opening the default mail client.
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using MqttExplorer.Avalonia.ViewModels;
using MqttExplorer.Avalonia.Views;

namespace MqttExplorer.Avalonia;

public partial class App : Application
{
    private bool _aboutDialogVisible;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            if (OperatingSystem.IsMacOS())
            {
                ConfigureMacOsMenu(desktop);
            }

            desktop.Exit += async (_, _) => { await viewModel.DisposeAsync(); };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private async Task ShowAboutDialogAsync(Window owner)
    {
        if (_aboutDialogVisible)
        {
            return;
        }

        _aboutDialogVisible = true;
        try
        {
            var dialog = new AboutWindow(AboutDialogViewModel.Create());
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _aboutDialogVisible = false;
        }
    }

    private void ConfigureMacOsMenu(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is null)
        {
            return;
        }

        var aboutItem = new NativeMenuItem("Acerca de Explorador MQTT");
        aboutItem.Click += async (_, _) => await ShowAboutDialogAsync(desktop.MainWindow);

        var quitItem = new NativeMenuItem("Salir de Explorador MQTT");
        quitItem.Click += (_, _) => desktop.Shutdown();

        var appMenuItems = new NativeMenu();
        appMenuItems.Items.Add(aboutItem);
        appMenuItems.Items.Add(new NativeMenuItemSeparator());
        appMenuItems.Items.Add(quitItem);
        NativeMenu.SetMenu(desktop.MainWindow, appMenuItems);
    }

}
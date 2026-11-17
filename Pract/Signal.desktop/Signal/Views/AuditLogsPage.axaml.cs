using Avalonia.Controls;
using Avalonia.Interactivity;
using Signal.Services;
using Signal.ViewModels;

namespace Signal.Views;

public partial class AuditLogsPage : UserControl
{
    public AuditLogsPage()
    {
        InitializeComponent();
        DataContext = new AuditLogsPageViewModel(ApiService.Instance);
    }

    private async void AuditLogsPage_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AuditLogsPageViewModel vm)
            await vm.LoadAuditLogsCommand.ExecuteAsync(null);
    }
}
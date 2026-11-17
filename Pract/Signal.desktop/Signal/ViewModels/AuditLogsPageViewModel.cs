using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signal.Dtos;
using Signal.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Signal.ViewModels
{
    public partial class AuditLogsPageViewModel(ApiService apiService) : ViewModelBase
    {
        private const int PageSize = 15;
        private readonly ApiService apiService = apiService;

        public ObservableCollection<AuditLogDto> Logs { get; set; } = new();

        [ObservableProperty]
        private int currentPage = 1;

        [ObservableProperty]
        private int totalPages = 1;

        [ObservableProperty]
        private int totalLogs;

        [ObservableProperty]
        private string? entityFilter;

        [ObservableProperty]
        private string? actionFilter;

        [ObservableProperty]
        private string? userLoginFilter;

        [RelayCommand]
        public async Task LoadAuditLogsAsync()
        {
            var response = await apiService.GetAuditLogsAsync(
                CurrentPage,
                PageSize,
                EntityFilter,
                ActionFilter,
                UserLoginFilter);

            if (response is not null)
            {
                Logs.Clear();
                foreach (var log in response.Logs)
                {
                    Logs.Add(log);
                }

                TotalLogs = response.TotalCount;
                TotalPages = Math.Max(1, (int)Math.Ceiling((double)TotalLogs / PageSize));
            }

            NextPageCommand.NotifyCanExecuteChanged();
            PrevPageCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private async Task ApplyFilterAsync()
        {
            CurrentPage = 1;
            await LoadAuditLogsAsync();
        }

        [RelayCommand]
        private async Task ResetFilterAsync()
        {
            EntityFilter = string.Empty;
            ActionFilter = string.Empty;
            UserLoginFilter = string.Empty;
            CurrentPage = 1;
            await LoadAuditLogsAsync();
        }

        [RelayCommand(CanExecute = nameof(CanGoNextPage))]
        private async Task NextPageAsync()
        {
            CurrentPage++;
            await LoadAuditLogsAsync();
        }

        [RelayCommand(CanExecute = nameof(CanGoPrevPage))]
        private async Task PrevPageAsync()
        {
            CurrentPage--;
            await LoadAuditLogsAsync();
        }

        private bool CanGoNextPage() => CurrentPage < TotalPages;
        private bool CanGoPrevPage() => CurrentPage > 1;
    }
}
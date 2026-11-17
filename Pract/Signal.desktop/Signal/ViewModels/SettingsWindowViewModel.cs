using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signal.Models;
using Signal.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Signal.ViewModels
{
    public partial class SettingsWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        string address = "";
        [ObservableProperty]
        string port = "";
        [ObservableProperty]
        bool useHttps = true;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
        bool isConnected;
        [ObservableProperty]
        bool isSaved;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(TryConnectCommand))]
        bool isDataCorrect;
        public ObservableCollection<string> Errors { get; } = new();
        [ObservableProperty]
        string connectionState = "Проверить";

        string savedAddress;
        string savedPort;
        bool savedUseHttps;

        public SettingsWindowViewModel()
        {
            var settings = SettingsService.Load();
            savedAddress = settings.Address;
            savedPort = settings.Port;
            savedUseHttps = settings.UseHttps;
            Address = savedAddress;
            Port = savedPort;
            UseHttps = savedUseHttps;
            IsConnected = IsSaved = Address == savedAddress && Port == savedPort && UseHttps == savedUseHttps;
        }

        void ValidateInput()
        {
            Errors.Clear();
            IsDataCorrect = true;
            if (string.IsNullOrWhiteSpace(Address))
            {
                Errors.Add("Адрес не может быть пустым");
                IsDataCorrect = false;
            }

            if (string.IsNullOrWhiteSpace(Port))
            {
                Errors.Add("Порт не может быть пустым");
                IsDataCorrect = false;
            }
            else if (!Int32.TryParse(Port, out int intPort) || intPort < 0 || intPort > 65535)
            {
                Errors.Add("Порт может быть только числом от 0 до 65535");
                IsDataCorrect = false;
            }

            IsSaved = IsConnected = Port == savedPort && Address == savedAddress && UseHttps == savedUseHttps;
        }

        [RelayCommand]
        async Task TryConnectAsync()
        {
            ConnectionState = "Подключение...";
            var scheme = UseHttps ? "https" : "http";
            IsConnected = await ApiService.Instance.CheckConnectionAsync(new Uri($"{scheme}://{Address}:{Port}/api/ping"));
            ConnectionState = "Проверить";
        }

        [RelayCommand]
        void SaveSettings()
        {
            var settings = new NetworkSettings()
            {
                Address = Address,
                Port = Port,
                UseHttps = UseHttps
            };
            SettingsService.Save(settings);
            ApiService.Instance.ConfigureClient();

            var loaded = SettingsService.Load();
            savedAddress = loaded.Address;
            savedPort = loaded.Port;
            savedUseHttps = loaded.UseHttps;

            IsSaved = Address == savedAddress && Port == savedPort && UseHttps == savedUseHttps;
        }

        partial void OnAddressChanged(string value) => ValidateInput();
        partial void OnPortChanged(string value) => ValidateInput();
        partial void OnUseHttpsChanged(bool value) => ValidateInput();
    }
}
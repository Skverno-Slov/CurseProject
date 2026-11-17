using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signal.Models;
using Signal.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace Signal.ViewModels
{
    public partial class JsonConfigPageViewModel : ViewModelBase
    {
        readonly TopLevel? topLevel;
        readonly string localConfigPath = Path.Combine("configs", "devices.json");
        readonly JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };


        public ObservableCollection<DeviceConfig> Devices { get; set; } = new();
        public ObservableCollection<AnnotationItemViewModel> CurrentAnnotations { get; set; } = new();

        [ObservableProperty]
        DeviceConfig? selectedDevice;
        [ObservableProperty]
        ParamConfig? selectedParam;
        [ObservableProperty]
        AnnotationItemViewModel? selectedAnnotation;
        [ObservableProperty]
        string statusMessage = string.Empty;

        public JsonConfigPageViewModel(TopLevel? topLevel)
        {
            this.topLevel = topLevel;
            LoadConfig();
        }

        partial void OnSelectedDeviceChanged(DeviceConfig? value)
        {
            SelectedParam = null;
            CurrentAnnotations.Clear();
        }

        partial void OnSelectedParamChanged(ParamConfig? value)
        {
            CurrentAnnotations.Clear();
            if (value?.Annotations is null) return;

            foreach (var kvp in value.Annotations)
            {
                CurrentAnnotations.Add(new AnnotationItemViewModel(kvp.Key, kvp.Value, SyncAnnotationsToDictionary));
            }
        }

        private void SyncAnnotationsToDictionary()
        {
            if (SelectedParam is null) return;

            SelectedParam.Annotations ??= new Dictionary<string, string>();
            SelectedParam.Annotations.Clear();

            foreach (var item in CurrentAnnotations)
            {
                if (!string.IsNullOrWhiteSpace(item.Key))
                {
                    SelectedParam.Annotations[item.Key] = item.Value ?? string.Empty;
                }
            }

            if (SelectedParam.Annotations.Count == 0) SelectedParam.Annotations = null;
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(localConfigPath))
                {
                    var dir = Path.GetDirectoryName(localConfigPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(localConfigPath, "[]");
                }

                string json = File.ReadAllText(localConfigPath);
                var list = JsonSerializer.Deserialize<List<DeviceConfig>>(json, jsonOptions) ?? new List<DeviceConfig>();
                Devices.Clear();
                foreach (var item in list)
                {
                    Devices.Add(item);
                }
                StatusMessage = "Конфигурация успешно загружена";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка чтения файла: {ex.Message}";
            }
        }

        [RelayCommand]
        private void SaveConfig()
        {
            try
            {
                SyncAnnotationsToDictionary();

                var dir = Path.GetDirectoryName(localConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(Devices.ToList(), jsonOptions);
                File.WriteAllText(localConfigPath, json);
                StatusMessage = "Изменения сохранены";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка сохранения: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ExportConfigAsync()
        {
            if (topLevel is null)
                return;

            SyncAnnotationsToDictionary();

            var options = new FilePickerSaveOptions
            {
                Title = "Экспорт JSON-конфигурации",
                DefaultExtension = "json",
                SuggestedFileName = "devices.json",
                FileTypeChoices = new[]
                {
                new FilePickerFileType("JSON файлы (*.json)") { Patterns = new[] { "*.json" } },
                new FilePickerFileType("Все файлы (*.*)") { Patterns = new[] { "*.*" } }
            }
            };

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
            if (file is not null)
            {
                try
                {
                    string json = JsonSerializer.Serialize(Devices.ToList(), jsonOptions);
                    await using var stream = await file.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(json);
                    StatusMessage = "Файл успешно экспортирован";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка экспорта: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private async Task ImportConfigAsync()
        {
            if (topLevel is null)
                return;

            var options = new FilePickerOpenOptions
            {
                Title = "Импорт JSON-конфигурации",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON файлы (*.json)") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("Все файлы (*.*)") { Patterns = new[] { "*.*" } }
                }
            };

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            var file = files.FirstOrDefault();
            if (file is not null)
            {
                try
                {
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    string json = await reader.ReadToEndAsync();

                    var list = JsonSerializer.Deserialize<List<DeviceConfig>>(json, jsonOptions);
                    if (list is null)
                    {
                        StatusMessage = "Ошибка: неверный формат JSON-файла";
                        return;
                    }

                    SelectedDevice = null;
                    SelectedParam = null;
                    CurrentAnnotations.Clear();
                    Devices.Clear();

                    foreach (var item in list)
                    {
                        Devices.Add(item);
                    }

                    var dir = Path.GetDirectoryName(localConfigPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string normalizedJson = JsonSerializer.Serialize(Devices.ToList(), jsonOptions);
                    File.WriteAllText(localConfigPath, normalizedJson);

                    SelectedDevice = Devices.FirstOrDefault();
                    StatusMessage = $"Файл успешно импортирован (устройств: {Devices.Count})";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка импорта файла: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private async Task UploadToServerAsync()
        {
            try
            {
                SyncAnnotationsToDictionary();
                SaveConfig();

                StatusMessage = "Загрузка конфигурации на сервер...";
                bool isSaved = await ApiService.Instance.SaveAgentConfigAsync(Devices.ToList());
                StatusMessage = isSaved
                    ? "Конфигурация успешно загружена на сервер"
                    : "Ошибка загрузки конфигурации на сервер";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки на сервер: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ImportFromServerAsync()
        {
            try
            {
                SyncAnnotationsToDictionary();
                StatusMessage = "Импорт конфигурации с сервера...";

                var serverDevices = await ApiService.Instance.GetAgentConfigAsync();
                if (serverDevices is null)
                {
                    StatusMessage = "Ошибка импорта конфигурации с сервера";
                    return;
                }

                var existingByIp = Devices
                    .Where(d => !string.IsNullOrWhiteSpace(d.Ip))
                    .GroupBy(d => d.Ip.Trim())
                    .ToDictionary(g => g.Key, g => g.First());

                SelectedDevice = null;
                SelectedParam = null;
                CurrentAnnotations.Clear();
                Devices.Clear();

                foreach (var serverDev in serverDevices)
                {
                    if (!string.IsNullOrWhiteSpace(serverDev.Ip) &&
                        existingByIp.TryGetValue(serverDev.Ip.Trim(), out var localDev))
                    {
                        if (string.IsNullOrWhiteSpace(serverDev.Login))
                            serverDev.Login = localDev.Login;
                        if (string.IsNullOrWhiteSpace(serverDev.Password))
                            serverDev.Password = localDev.Password;

                        if (localDev.Params is not null && serverDev.Params is not null)
                        {
                            foreach (var sParam in serverDev.Params)
                            {
                                var matchedLocalParam = localDev.Params.FirstOrDefault(
                                    lp => lp.Command == sParam.Command || lp.Name == sParam.Name);
                                if (matchedLocalParam?.Annotations is not null && sParam.Annotations is null)
                                {
                                    sParam.Annotations = new Dictionary<string, string>(matchedLocalParam.Annotations);
                                }
                            }
                        }
                    }

                    Devices.Add(serverDev);
                }

                var dir = Path.GetDirectoryName(localConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(Devices.ToList(), jsonOptions);
                File.WriteAllText(localConfigPath, json);

                SelectedDevice = Devices.FirstOrDefault();
                StatusMessage = $"Импортировано с сервера устройств: {Devices.Count}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка импорта с сервера: {ex.Message}";
            }
        }

        [RelayCommand]
        private void AddParam()
        {
            if (SelectedDevice is null)
                return;

            var newParam = new ParamConfig { Command = "cmd?", Name = "new_param" };
            var updatedList = SelectedDevice.Params.ToList();
            updatedList.Add(newParam);
            SelectedDevice.Params = updatedList;

            var currentDevice = SelectedDevice;
            SelectedDevice = null;
            SelectedDevice = currentDevice;
            SelectedParam = newParam;
        }

        [RelayCommand]
        private void DeleteParam()
        {
            if (SelectedDevice is null || SelectedParam is null)
                return;

            var updatedList = SelectedDevice.Params.ToList();
            updatedList.Remove(SelectedParam);
            SelectedDevice.Params = updatedList;

            var currentDevice = SelectedDevice;
            SelectedDevice = null;
            SelectedDevice = currentDevice;
        }

        [RelayCommand]
        private void AddAnnotation()
        {
            if (SelectedParam is null) return;

            var newItem = new AnnotationItemViewModel("0", "Описание", SyncAnnotationsToDictionary);
            CurrentAnnotations.Add(newItem);
            SyncAnnotationsToDictionary();
            SelectedAnnotation = newItem;
        }

        [RelayCommand]
        private void DeleteAnnotation()
        {
            if (SelectedAnnotation is null) return;

            CurrentAnnotations.Remove(SelectedAnnotation);
            SyncAnnotationsToDictionary();
            SelectedAnnotation = null;
        }
    }
}
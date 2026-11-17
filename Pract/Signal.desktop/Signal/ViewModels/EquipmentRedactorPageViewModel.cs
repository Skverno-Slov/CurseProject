using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signal.Dtos;
using Signal.Models;
using Signal.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Signal.ViewModels
{
    public partial class EquipmentRedactorPageViewModel : ViewModelBase
    {
        private readonly ApiService apiService;
        private readonly EquipmentDto? editingEquipment;

        [ObservableProperty]
        string name = string.Empty;
        [ObservableProperty]
        string serialNumber = string.Empty;
        [ObservableProperty]
        string model = string.Empty;
        [ObservableProperty]
        string statusName = "Ok";
        [ObservableProperty]
        string ipAddress = string.Empty;
        [ObservableProperty]
        bool isEditMode;

        public EquipmentRedactorPageViewModel(ApiService service, EquipmentDto? item = null)
        {
            apiService = service;
            editingEquipment = item;
            IsEditMode = editingEquipment != null;

            if (IsEditMode && editingEquipment != null)
            {
                Name = editingEquipment.Name;
                SerialNumber = editingEquipment.SerialNumber;
                Model = editingEquipment.Model;
                StatusName = editingEquipment.StatusName;
                IpAddress = editingEquipment.IpAddress ?? string.Empty;
            }
        }

        [RelayCommand]
        void Close()
        {
            MainViewModelHandler.Instance.CurrentPage = new EquipmentPage();
        }

        [RelayCommand]
        async Task SaveAsync()
        {
            if (IsEditMode && editingEquipment != null)
            {
                if (Name != editingEquipment.Name ||
                    SerialNumber != editingEquipment.SerialNumber ||
                    Model != editingEquipment.Model ||
                    StatusName != editingEquipment.StatusName ||
                    IpAddress != (editingEquipment.IpAddress ?? string.Empty))
                {
                    var updateDto = new EquipmentDto(
                        editingEquipment.EquipmentId,
                        SerialNumber,
                        Name,
                        Model,
                        StatusName,
                        string.IsNullOrWhiteSpace(IpAddress) ? null : IpAddress.Trim(),
                        editingEquipment.IsDeleted,
                        editingEquipment.PollEnabled
                    );

                    await apiService.UpdateEquipmentAsync(editingEquipment.EquipmentId, updateDto);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(SerialNumber))
                {
                    var createDto = new EquipmentDto(
                        0,
                        SerialNumber.Trim(),
                        Name.Trim(),
                        string.IsNullOrWhiteSpace(Model) ? "Other" : Model.Trim(),
                        string.IsNullOrWhiteSpace(StatusName) ? "Ok" : StatusName.Trim(),
                        string.IsNullOrWhiteSpace(IpAddress) ? null : IpAddress.Trim(),
                        false,
                        true
                    );

                    await apiService.AddEquipmentAsync(createDto);
                }
            }

            Close();
        }
    }
}
using System.Text.Json.Serialization;

namespace Signal.Server.Data.Dtos
{
    public record EquipmentDto(
        [property: JsonPropertyName("equipmentId")] int EquipmentId,
        [property: JsonPropertyName("serialNumber")] string SerialNumber,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("statusName")] string StatusName,
        [property: JsonPropertyName("ipAddress")] string? IpAddress = null,
        [property: JsonPropertyName("isDeleted")] bool IsDeleted = false,
        [property: JsonPropertyName("pollEnabled")] bool PollEnabled = true
    );
}
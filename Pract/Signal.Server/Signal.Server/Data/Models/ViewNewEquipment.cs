using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Signal.Server.Data.Models;

[Keyless]
public partial class ViewNewEquipment
{
    [Column("equipment_id")]
    public int? EquipmentId { get; set; }

    [Column("status_id")]
    public int? StatusId { get; set; }

    [Column("serial_number")]
    [StringLength(100)]
    public string? SerialNumber { get; set; }

    [Column("name")]
    [StringLength(150)]
    public string? Name { get; set; }

    [Column("model")]
    [StringLength(100)]
    public string? Model { get; set; }

    [Column("ip_address")]
    [StringLength(39)]
    public string? IpAddress { get; set; }
}
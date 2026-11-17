using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Signal.Server.Data.Models;

[Table("equipment_command")]
[Index("EquipmentId", "CommandString", Name = "uq_equipment_cmd", IsUnique = true)]
public partial class EquipmentCommand
{
    [Key]
    [Column("command_id")]
    public int CommandId { get; set; }

    [Column("equipment_id")]
    public int EquipmentId { get; set; }

    [Column("reading_type_id")]
    public int ReadingTypeId { get; set; }

    [Column("command_string")]
    [StringLength(50)]
    public string CommandString { get; set; } = null!;

    [Column("invalid_value")]
    [StringLength(50)]
    public string? InvalidValue { get; set; }

    [Column("poll_interval_seconds")]
    public int PollIntervalSeconds { get; set; } = 30;

    [ForeignKey("EquipmentId")]
    [InverseProperty("EquipmentCommands")]
    public virtual Equipment Equipment { get; set; } = null!;

    [ForeignKey("ReadingTypeId")]
    [InverseProperty("EquipmentCommands")]
    public virtual ReadingType ReadingType { get; set; } = null!;
}
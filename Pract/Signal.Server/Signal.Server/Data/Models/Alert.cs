using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Signal.Server.Data.Models;

[Table("alert")]
public partial class Alert
{
    [Key]
    [Column("alert_id")]
    public int AlertId { get; set; }

    [Column("equipment_id")]
    public int EquipmentId { get; set; }

    [Column("reading_log_id")]
    public int? ReadingLogId { get; set; }

    [Column("severity")]
    [StringLength(20)]
    public string Severity { get; set; } = "WARNING";

    [Column("message")]
    [StringLength(255)]
    public string Message { get; set; } = null!;

    [Column("is_resolved")]
    public bool IsResolved { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [ForeignKey("EquipmentId")]
    [InverseProperty("Alerts")]
    public virtual Equipment Equipment { get; set; } = null!;

    [ForeignKey("ReadingLogId")]
    [InverseProperty("Alerts")]
    public virtual ReadingLog? ReadingLog { get; set; }

    [InverseProperty("Alert")]
    public virtual ICollection<UserAlert> UserAlerts { get; set; } = new List<UserAlert>();
}
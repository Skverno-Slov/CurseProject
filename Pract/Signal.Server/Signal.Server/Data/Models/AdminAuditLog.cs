using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Signal.Server.Data.Models;

[Table("admin_audit_log")]
[Index("CreatedAt", Name = "idx_audit_created_at", AllDescending = true)]
[Index("UserId", Name = "idx_audit_user_id")]
public partial class AdminAuditLog
{
    [Key]
    [Column("audit_log_id")]
    public int AuditLogId { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Column("action")]
    [StringLength(100)]
    public string Action { get; set; } = null!;

    [Column("entity_name")]
    [StringLength(50)]
    public string EntityName { get; set; } = null!;

    [Column("entity_id")]
    public int? EntityId { get; set; }

    [Column("details", TypeName = "jsonb")]
    public string? Details { get; set; }

    [Column("ip_address")]
    [StringLength(45)]
    public string? IpAddress { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("AdminAuditLogs")]
    public virtual User? User { get; set; }
}
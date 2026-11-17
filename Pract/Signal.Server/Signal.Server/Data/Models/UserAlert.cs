using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Signal.Server.Data.Models;

[Table("user_alert")]
[Index("AlertId", "UserId", Name = "uq_alert_user", IsUnique = true)]
public partial class UserAlert
{
    [Key]
    [Column("user_alert_id")]
    public int UserAlertId { get; set; }

    [Column("alert_id")]
    public int AlertId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("is_read")]
    public bool IsRead { get; set; }

    [Column("read_at")]
    public DateTime? ReadAt { get; set; }

    [ForeignKey("AlertId")]
    [InverseProperty("UserAlerts")]
    public virtual Alert Alert { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("UserAlerts")]
    public virtual User User { get; set; } = null!;
}
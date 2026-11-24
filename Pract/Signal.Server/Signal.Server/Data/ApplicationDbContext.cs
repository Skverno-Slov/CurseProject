using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Signal.Server.Data.Models;
using Microsoft.Extensions.Configuration;

namespace Signal.Server.Data;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext()
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AdminAuditLog> AdminAuditLogs { get; set; }

    public virtual DbSet<Alert> Alerts { get; set; }

    public virtual DbSet<Equipment> Equipment { get; set; }

    public virtual DbSet<EquipmentCommand> EquipmentCommands { get; set; }

    public virtual DbSet<ReadingLog> ReadingLogs { get; set; }

    public virtual DbSet<ReadingType> ReadingTypes { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Session> Sessions { get; set; }

    public virtual DbSet<Status> Statuses { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserAlert> UserAlerts { get; set; }

    public virtual DbSet<ViewLatestEquipmentReading> ViewLatestEquipmentReadings { get; set; }

    public virtual DbSet<ViewNewEquipment> ViewNewEquipments { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var basePath = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                optionsBuilder.UseNpgsql(connectionString);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminAuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId).HasName("admin_audit_log_pkey");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.User).WithMany(p => p.AdminAuditLogs)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_audit_user");
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(e => e.AlertId).HasName("alert_pkey");

            entity.HasIndex(e => e.EquipmentId, "idx_alert_equipment_active")
                .HasFilter("(is_resolved = false)");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.IsResolved).HasDefaultValue(false);
            entity.Property(e => e.Severity).HasDefaultValueSql("'WARNING'::character varying");

            entity.ToTable("alert", t => t.HasCheckConstraint(
                "chk_alert_severity",
                "severity::text = ANY (ARRAY['INFO'::character varying, 'WARNING'::character varying, 'CRITICAL'::character varying, 'FATAL'::character varying]::text[])"));

            entity.HasOne(d => d.Equipment).WithMany(p => p.Alerts)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_alert_equipment");

            entity.HasOne(d => d.ReadingLog).WithMany(p => p.Alerts)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_alert_reading_log");
        });

        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.HasKey(e => e.EquipmentId).HasName("equipment_pkey");

            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.PollEnabled).HasDefaultValue(true);

            entity.HasOne(d => d.Status).WithMany(p => p.Equipment)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("equipment_status_id_fkey");
        });

        modelBuilder.Entity<EquipmentCommand>(entity =>
        {
            entity.HasKey(e => e.CommandId).HasName("equipment_command_pkey");

            entity.Property(e => e.PollIntervalSeconds).HasDefaultValue(30);

            entity.HasOne(d => d.Equipment).WithMany(p => p.EquipmentCommands)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_cmd_equipment");

            entity.HasOne(d => d.ReadingType).WithMany(p => p.EquipmentCommands)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cmd_reading_type");
        });

        modelBuilder.Entity<ReadingLog>(entity =>
        {
            entity.HasKey(e => e.ReadingLogId).HasName("reading_log_pkey");

            entity.Property(e => e.WriteDateTime).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Equipment).WithMany(p => p.ReadingLogs)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("reading_log_equipment_id_fkey");

            entity.HasOne(d => d.ReadingType).WithMany(p => p.ReadingLogs)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("reading_log_reading_type_id_fkey");

            entity.HasOne(d => d.Status).WithMany(p => p.ReadingLogs)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("reading_log_status_id_fkey");
        });

        modelBuilder.Entity<ReadingType>(entity =>
        {
            entity.HasKey(e => e.ReadingTypeId).HasName("reading_type_pkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("role_pkey");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("session_pkey");

            entity.HasOne(d => d.User).WithMany(p => p.Sessions)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("session_user_id_fkey");
        });

        modelBuilder.Entity<Status>(entity =>
        {
            entity.HasKey(e => e.StatusId).HasName("status_pkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("user_pkey");

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserRole",
                    r => r.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .HasConstraintName("user_role_role_id_fkey"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .HasConstraintName("user_role_user_id_fkey"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId").HasName("user_role_pkey");
                        j.ToTable("user_role");
                        j.IndexerProperty<int>("UserId").HasColumnName("user_id");
                        j.IndexerProperty<int>("RoleId").HasColumnName("role_id");
                    });
        });

        modelBuilder.Entity<UserAlert>(entity =>
        {
            entity.HasKey(e => e.UserAlertId).HasName("user_alert_pkey");

            entity.HasIndex(e => e.UserId, "idx_user_alert_unread")
                .HasFilter("(is_read = false)");

            entity.Property(e => e.IsRead).HasDefaultValue(false);

            entity.HasOne(d => d.Alert).WithMany(p => p.UserAlerts)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_alert_alert");

            entity.HasOne(d => d.User).WithMany(p => p.UserAlerts)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_alert_user");
        });

        modelBuilder.Entity<ViewLatestEquipmentReading>(entity =>
        {
            entity.ToView("view_latest_equipment_readings");
        });

        modelBuilder.Entity<ViewNewEquipment>(entity =>
        {
            entity.ToView("view_new_equipment");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
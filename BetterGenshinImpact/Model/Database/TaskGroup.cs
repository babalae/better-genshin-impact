using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BetterGenshinImpact.Model.Database;

[Table("task_group")]
public class TaskGroup
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("order_index")]
    public int OrderIndex { get; set; }
    
    [Column("checked")]
    [MaxLength(50)]
    public bool Checked { get; set; }

    [Column("task_name")]
    [Required]
    [MaxLength(255)]
    public string TaskName { get; set; } = string.Empty;

    [Column("task_params")]
    public string? TaskParams { get; set; }

    [Column("schedule_expression")]
    [MaxLength(255)]
    public string? ScheduleExpression { get; set; }

    [Column("schedule_type")]
    [MaxLength(50)]
    public string? ScheduleType { get; set; }
    
    [Column("schedule_enabled")]
    [MaxLength(50)]
    public bool ScheduleEnabled { get; set; }

    [Column("next_run_time")]
    public DateTime? NextRunTime { get; set; }

    [Column("last_run_time")]
    public DateTime? LastRunTime { get; set; }

    [Column("hotkey")]
    [MaxLength(128)]
    public string? Hotkey { get; set; }
    
    [Column("hotkey_enabled")]
    [MaxLength(128)]
    public string? HotkeyEnabled { get; set; }

    [Column("category")]
    [MaxLength(128)]
    public string? Category { get; set; }
}
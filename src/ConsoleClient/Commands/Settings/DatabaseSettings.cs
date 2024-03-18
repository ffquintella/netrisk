using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class DatabaseSettings: CommandSettings
{
    [Description("One of the operations to execute. Valid values are: status, init, backup, restore.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";
    
    [CommandArgument(1, "[backupDir]")]
    public string? BackupPath { get; set; }
    
    [CommandArgument(2, "[backupPwd]")]
    public string? BackupPwd { get; set; }
}
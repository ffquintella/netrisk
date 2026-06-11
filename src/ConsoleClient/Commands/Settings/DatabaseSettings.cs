using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class DatabaseSettings: CommandSettings
{
    [Description("One of the operations to execute. Valid values are: status, init, update, backup, restore, fixData, upgrade-schema.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";

    [CommandArgument(1, "[backupDir]")]
    public string? BackupPath { get; set; }

    [CommandArgument(2, "[backupPwd]")]
    public string? BackupPwd { get; set; }

    [Description("Fixes the risk catalog migrating it to the new database structure")]
    [CommandOption("--catalog")]
    public bool? FixCatalog { get; set; }

    // ---- upgrade-schema (Track 6) options ----

    [Description("Track 6 phase identifier to operate on (e.g. 1, 2, 6a, 6b).")]
    [CommandOption("--phase")]
    public string? Phase { get; set; }

    [Description("Target environment profile for upgrade-schema (homolog|prod). Defaults to homolog.")]
    [CommandOption("--env")]
    public string? Environment { get; set; }

    [Description("upgrade-schema: read-only pre-flight checks only; mutates nothing.")]
    [CommandOption("--check")]
    public bool Check { get; set; }

    [Description("upgrade-schema: print the exact SQL the phase would apply plus an impact summary; mutates nothing.")]
    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }

    [Description("upgrade-schema: required confirmation for destructive phases (e.g. 6b) in non-interactive use.")]
    [CommandOption("--yes")]
    public bool Yes { get; set; }

    [Description("upgrade-schema --dry-run: file path to write the generated SQL to (for attaching to a change request).")]
    [CommandOption("--output")]
    public string? Output { get; set; }
}
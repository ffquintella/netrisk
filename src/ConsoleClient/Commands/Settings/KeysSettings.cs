using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class KeysSettings : CommandSettings
{
    [Description("Operation to execute. Valid values are: create, rotate, show.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";

    [Description("Overwrite an existing key (invalidates the current website enrollment).")]
    [CommandOption("--force")]
    public bool Force { get; set; }

    [Description("Website base URL. When given to 'rotate', the new public key is installed on the website using a request signed with the current key.")]
    [CommandOption("--website-url")]
    public string? WebsiteUrl { get; set; }

    [Description("Skip TLS certificate validation when contacting the website (self-signed certs).")]
    [CommandOption("--insecure")]
    public bool Insecure { get; set; }
}

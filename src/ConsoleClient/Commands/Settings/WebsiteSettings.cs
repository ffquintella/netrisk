using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class WebsiteSettings : CommandSettings
{
    [Description("Operation to execute. Valid values are: enroll.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";

    [Description("Website base URL, e.g. https://site:6443")]
    [CommandOption("--url")]
    public string? Url { get; set; }

    [Description("Skip TLS certificate validation when contacting the website (self-signed certs).")]
    [CommandOption("--insecure")]
    public bool Insecure { get; set; }
}

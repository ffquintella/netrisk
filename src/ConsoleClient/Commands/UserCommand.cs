using System.Diagnostics.CodeAnalysis;
using System.Text;
using ConsoleClient.Commands.Settings;
using DAL.Entities;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;
using static BCrypt.Net.BCrypt;

namespace ConsoleClient.Commands;

public class UserCommand: Command<UserSettings>
{
    private IUsersService UsersService { get; }
    private IPermissionsService PermissionsService { get; }
    
    public UserCommand(IUsersService usersService, IPermissionsService permissionsService)
    {
      UsersService = usersService;
      PermissionsService = permissionsService;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] UserSettings settings)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        
        switch (settings.Operation)
        {
            case "add":
                ExecuteAdd(context, settings);
                return 0;
            case "remove":
                ExecuteRemove(context, settings);
                return 0;
            case "list":
                ExecuteList(context, settings);
                return 0;
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: add, remove, list [/]");
                return -1;
        }
            
    }

    private void ExecuteAdd(CommandContext context, UserSettings settings)
    {
        //var name = AnsiConsole.Ask<string>("New user [green]login[/]?");
        
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("New user [green]login[/]?")
                .ValidationErrorMessage("[red]This user already exists or the name is invalid![/]")
                .Validate(name =>
                {
                    var existingUser = UsersService.GetUser(name);
                    return existingUser == null;
                }));
        
        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("New user [blue]password[/]?")
                .PromptStyle("green")
                .ValidationErrorMessage("[red]That's not a valid password[/]")
                .Validate(pwd =>
                {
                    if(settings.IgnorePwdComplexity == true)
                        return true;
                    return UsersService.CheckPasswordComplexity(pwd);
                })
                .Secret()
            );
        
        var admin = AnsiConsole.Confirm("Is this user Admin?", false);
        
        // Create a table
        var table = new Table();

        // Add some columns
        table.AddColumn("Variable");
        table.AddColumn(new TableColumn("Value").Centered());

        // Add some rows
        table.AddRow("Name", $"[green]{name}[/]");
        table.AddRow("Password", $"[white]***[/]"); 
        table.AddRow("Admin", $"[white]{admin.ToString()}[/]");

        // Render the table to the console
        AnsiConsole.Write(table);
        
        var cont = AnsiConsole.Confirm("Create this user?");

        if (cont)
        {
            List<Permission> permissions;
            if (admin)
            {
                permissions = PermissionsService.GetAllPermissions();
            }
            else
            {
                permissions = PermissionsService.GetDefaultPermissions();
            }
            
            var user = new User()
            {
                Name = name,
                Password = Encoding.UTF8.GetBytes(HashPassword(password, 15)),
                Admin = admin,
                
            };
        }


    }
    
    private void ExecuteRemove(CommandContext context, UserSettings settings)
    {
    }
    
    private void ExecuteList(CommandContext context, UserSettings settings)
    {
        AnsiConsole.MarkupLine("###############");
        AnsiConsole.MarkupLine("  Active users ");
        AnsiConsole.MarkupLine("---------------");
        
        var users = UsersService.ListActiveUsers();
        foreach (var user in users)
        {
            AnsiConsole.MarkupLine("[bold]User: {0}[/]", user.Name);
        }
    }
}
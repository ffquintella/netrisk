using System.Diagnostics.CodeAnalysis;
using System.Text;
using ConsoleClient.Commands.Settings;
using DAL.Entities;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;
using Tools;
using static BCrypt.Net.BCrypt;

namespace ConsoleClient.Commands;

public class UserCommand: Command<UserSettings>
{
    private IUsersService UsersService { get; }
    private IPermissionsService PermissionsService { get; }
    
    private IRolesService RolesService { get; }
    
    public UserCommand(IUsersService usersService, IPermissionsService permissionsService, IRolesService rolesService)
    {
      UsersService = usersService;
      PermissionsService = permissionsService;
      RolesService = rolesService;
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
        
        var login = AnsiConsole.Prompt(
            new TextPrompt<string>("New user [green]login[/]?")
                .ValidationErrorMessage("[red]This user already exists or the name is invalid![/]")
                .Validate(name =>
                {
                    var existingUser = UsersService.GetUser(name);
                    return existingUser == null;
                }));
        
        var name = AnsiConsole.Ask<string>("New user [green]full name[/]?");
        var email = AnsiConsole.Ask<string>("New user [green]E-mail[/]?");

        var userType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What is the user [green]type[/]?")
                .AddChoices(new[]
                {
                    "Local", "SAML"
                }));

        string password = RandomGenerator.RandomString(12);
        if (userType == "Local")
        {
            password = AnsiConsole.Prompt(
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
            
            AnsiConsole.Prompt(
                new TextPrompt<string>("Confirm user [blue]password[/]?")
                    .PromptStyle("green")
                    .ValidationErrorMessage("[red]Password does not match[/]")
                    .Validate(pwd =>
                    {
                        if(pwd == password)
                            return true;
                        return false;
                    })
                    .Secret()
            );
        }

        var roles = RolesService.GetRoles();
        
        
        var userRole = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What is the user [green]role[/]?")
                .AddChoices( roles.Select(r=> r.Name).ToArray()));

        
        var admin = AnsiConsole.Confirm("Is this user Admin?", false);
        
        // Create a table
        var table = new Table();

        // Add some columns
        table.AddColumn("Variable");
        table.AddColumn(new TableColumn("Value").Centered());

        // Add some rows
        table.AddRow("Login", $"[teal]{login}[/]");
        table.AddRow("Name", $"[green]{name}[/]");
        table.AddRow("E-mail", $"[green]{email}[/]");
        table.AddRow("Type", $"[white]{userType}[/]");
        table.AddRow("Password", $"[white]***[/]"); 
        table.AddRow("Admin", $"[white]{admin.ToString()}[/]");
        table.AddRow("Role", $"[white]{userRole}[/]");

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
            
            var role = roles.FirstOrDefault(r => r.Name == userRole);
            
            var user = new User()
            {
                Username = Encoding.UTF8.GetBytes(login),
                Name = name,
                Email = Encoding.UTF8.GetBytes(email),
                Enabled = true,
                Password = Encoding.UTF8.GetBytes(HashPassword(password, 15)),
                Admin = admin,
                Type = userType,
                Permissions = permissions,
                RoleId = role!.Value,
                LastPasswordChangeDate = DateTime.Today,
                ChangePassword = 0,
                MultiFactor = 0
            };
            
            UsersService.CreateUser(user);
        }


    }
    
    private void ExecuteRemove(CommandContext context, UserSettings settings)
    {        
        AnsiConsole.MarkupLine("###############");
        AnsiConsole.MarkupLine("  Active users ");
        AnsiConsole.MarkupLine("---------------");
        
        var users = UsersService.ListActiveUsers();
        int i = 1;
        foreach (var user in users)
        {
            AnsiConsole.MarkupLine("{0} - [bold]User: {1}[/]",i, user.Name);
            i++;
        }
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
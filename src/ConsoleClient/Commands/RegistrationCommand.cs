using System;
using System.Collections.Generic;
using System.ComponentModel;
using DAL.Entities;
using Serilog;
using ServerServices;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class RegistrationCommand: Command<RegistrationCommand.RegistrationSettings>
{
    
    public sealed class RegistrationSettings: CommandSettings
    {
        [Description("One of the operations to execute. Valid values are: list, approve, reject, delete.")]
        [CommandArgument(0, "<operation>")]
        public string Operation { get; set; } = "";
    
        [CommandOption("-i|--id")]
        public int? Id { get; set; }
    
        [CommandOption("--all")]
        public bool? All { get; set; } 
    
    }
    
    IClientRegistrationService _registrationService;
    public RegistrationCommand(IClientRegistrationService clientRegistrationService)
    {
        _registrationService = clientRegistrationService;
        
    }
    public override int Execute(CommandContext context, RegistrationSettings settings)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        
        switch (settings.Operation)
        {
            case "list":
                ExecuteList(context, settings);
                return 0;
            case "approve":
                ExecuteApprove(context, settings);
                return 0;
            case "reject":
                ExecuteReject(context, settings);
                return 0;
            case "delete":
                ExecuteDelete(context, settings);
                return 0;
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: list, approve, reject or delete [/]");
                return -1;
        }
    }
    private void ExecuteApprove(CommandContext context, RegistrationSettings settings)
    {
        
        var registrations = _registrationService.GetRequested();

        AnsiConsole.MarkupLine("[blue]**********************[/]");
        AnsiConsole.MarkupLine("[bold]Loading registrations requests[/]");
        AnsiConsole.MarkupLine("[blue]----------------------[/]");
        
        foreach (var registration in registrations)
        {
            if(registration.Name == null) registration.Name = "N/A";
            AnsiConsole.MarkupLine("{0}. {1} - {2} | {3} : {4} ", registration.Id, registration.RegistrationDate, registration.Name, registration.ExternalId, registration.Status);
        }
        
        AnsiConsole.MarkupLine("[white]======================[/]");
        
        int id;
        if (settings.Id == null)
        {
            id = AnsiConsole.Ask<int>("Choose the id you want to [green]approve[/]?");
        }
        else
        {
            id = (int)settings.Id;
        }


        var regReq = _registrationService.GetRegistrationById(id);
        if (regReq == null)
        {
            AnsiConsole.MarkupLine("[Red]*** Invalid ID ***[/]");
            return;
        }

        regReq.Status = "approved";
        
        _registrationService.Update(regReq);

        var loggedUser = Environment.UserName;
        Log.Information("Registration: {0} approved by {1}", id, loggedUser);

    }
    
    private void ExecuteReject(CommandContext context, RegistrationSettings settings)
    {
        
        var registrations = _registrationService.GetRequested();

        AnsiConsole.MarkupLine("[blue]**********************[/]");
        AnsiConsole.MarkupLine("[bold]Loading registrations requests[/]");
        AnsiConsole.MarkupLine("[blue]----------------------[/]");
        
        foreach (var registration in registrations)
        {
            if(registration.Name == null) registration.Name = "N/A";
            AnsiConsole.MarkupLine("{0}. {1} - {2} | {3} : {4} ", registration.Id, registration.RegistrationDate, registration.Name, registration.ExternalId, registration.Status);
        }
        
        AnsiConsole.MarkupLine("[white]======================[/]");
        
        int id;
        if (settings.Id == null)
        {
            id = AnsiConsole.Ask<int>("Choose the id you want to [red]reject[/]?");
        }
        else
        {
            id = (int)settings.Id;
        }


        var regReq = _registrationService.GetRegistrationById(id);
        if (regReq == null)
        {
            AnsiConsole.MarkupLine("[Red]*** Invalid ID ***[/]");
            return;
        }

        regReq.Status = "rejected";
        
        _registrationService.Update(regReq);

        var loggedUser = Environment.UserName;
        Log.Information("Registration: {0} rejected by {1}", id, loggedUser);

    }

    private void ExecuteDelete(CommandContext context, RegistrationSettings settings)
    {
        
        var registrations = _registrationService.GetAll();

        AnsiConsole.MarkupLine("[blue]************************[/]");
        AnsiConsole.MarkupLine("Loading [bold]all[/] requests");
        AnsiConsole.MarkupLine("[blue]------------------------[/]");
        
        foreach (var registration in registrations)
        {
            if(registration.Name == null) registration.Name = "N/A";
            AnsiConsole.MarkupLine("{0}. {1} - {2} | {3} : {4} ", registration.Id, registration.RegistrationDate, registration.Name, registration.ExternalId, registration.Status);
        }
        
        AnsiConsole.MarkupLine("[white]========================[/]");
        
        int id;
        if (settings.Id == null)
        {
            id = AnsiConsole.Ask<int>("Choose the id you want to [red]delete[/]?");
        }
        else
        {
            id = (int)settings.Id;
        }


        var regReq = _registrationService.GetRegistrationById(id);
        if (regReq == null)
        {
            AnsiConsole.MarkupLine("[Red]*** Invalid ID ***[/]");
            return;
        }
        
        _registrationService.Delete(regReq);

        var loggedUser = Environment.UserName;
        Log.Information("Registration: {0} deleted by {1}", id, loggedUser);

    }

    
    private void ExecuteList(CommandContext context, RegistrationSettings settings)
    {
        List<AddonsClientRegistration> registrations;
        if(settings.All != null && settings.All == true) registrations = _registrationService.GetAll();
        else registrations = _registrationService.GetRequested();

        
        AnsiConsole.MarkupLine("[blue]**********************************[/]");
        if(settings.All != null && settings.All == true) AnsiConsole.MarkupLine("Loading [bold]all[/] registrations");
        else AnsiConsole.MarkupLine("Loading registrations [bold]requests[/]");
        AnsiConsole.MarkupLine("[blue]----------------------------------[/]");
        
        foreach (var registration in registrations)
        {
            string color;
            switch (registration.Status)
            {   
                case "requested":
                    color = "blue";
                    break;
                case "accepted":
                    color = "green";
                    break;
                case "rejected":
                    color = "red";
                    break;
                default:
                    color = "white";
                    break;
            }
            if(registration.Name == null) registration.Name = "N/A";
            AnsiConsole.MarkupLine("{0}. {1} - {2} | {3} : [{5}]{4}[/] ", registration.Id, registration.RegistrationDate, registration.Name, registration.ExternalId, registration.Status, color);
        }
        
        AnsiConsole.MarkupLine("[white]======================[/]");
    }
    
    
}
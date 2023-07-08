// See https://aka.ms/new-console-template for more information

using System.Net.Mime;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Query.ExpressionTranslators.Internal;
using DAL;

Console.WriteLine("This is a test application!");


var configuration =  new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddUserSecrets<Program>()
    .AddJsonFile($"appsettings.json");
            
var config = configuration.Build();

var connectionString = config["Database:ConnectionString"];

Console.WriteLine("ConnectionString:" + connectionString);

var dalManager = new DALManager(config);

var dbContext = dalManager.GetContext();

var risks = dbContext.Risks.ToList();

int i = 0;
foreach (var risk in risks)
{
    i++;
    Console.WriteLine("Database Risks");
    Console.WriteLine("Risk {0}:{1}", i ,risk.Subject);
}

Environment.Exit(-1);
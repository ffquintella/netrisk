using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;

namespace DAL;

using Microsoft.Extensions.Configuration;

public class DALManager
{
    // requires using Microsoft.Extensions.Configuration;
    private readonly IConfiguration Configuration;
    private string ConnectionString;
    
    public DALManager(IConfiguration configuration)
    {
        Configuration = configuration;
        ConnectionString = Configuration["Database:ConnectionString"];

    }

    public SRDbContext GetContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SRDbContext>();
        optionsBuilder.UseMySql(ConnectionString, Microsoft.EntityFrameworkCore.ServerVersion.Parse("8.0.29-mysql"));

        SRDbContext dbContext = new SRDbContext(optionsBuilder.Options);
        return dbContext;
    }
}
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

public class AuditableContext: NRDbContext
{
    public AuditableContext(DbContextOptions<NRDbContext> options) : base(options)
    {
    }
    
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        //await AuditChanges();
        return await base.SaveChangesAsync(cancellationToken);
    }
    

}
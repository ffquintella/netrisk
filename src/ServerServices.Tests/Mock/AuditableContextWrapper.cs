using DAL.Context;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.Tests.Mock;

public class AuditableContextWrapper : AuditableContext
{
    public AuditableContextWrapper() : base(new DbContextOptionsBuilder<NRDbContext>().Options)
    {
    }
}
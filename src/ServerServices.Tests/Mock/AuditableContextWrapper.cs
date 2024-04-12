using System.Collections.Generic;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.Tests.Mock;

public class AuditableContextWrapper: AuditableContext, IAuditableContext
{

    public AuditableContextWrapper(): base(new DbContextOptions<NRDbContext>())
    {

    }

    public virtual DbSet<Comment> Comments
    {
        get;
        set;
    }

    // Implement other methods and properties...
}
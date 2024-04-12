using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.Tests.Mock;

public interface IAuditableContext
{
    // Add the methods and properties you want to mock here
    DbSet<Comment> Comments { get; set; }
}
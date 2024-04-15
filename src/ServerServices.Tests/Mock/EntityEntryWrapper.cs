using DAL.Context;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;

namespace ServerServices.Tests.Mock;

public class EntityEntryWrapper<T>  where T : class 
{
    public T Entity { get; set; }

    /*
    public EntityEntryWrapper() : base((InternalEntityEntry)null)
    {

    }
    
    public EntityEntryWrapper([NotNull] InternalEntityEntry internalEntry) : base(internalEntry)
    {
    }
    */
}
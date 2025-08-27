using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Collections.Generic;
using System.Linq;

namespace DAL.Tools;

public static class EFComparers
{
    public static readonly ValueComparer<List<char>?> ListCharComparer =
        new ValueComparer<List<char>?>(
            (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
            c => c != null ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())) : 0,
            c => c != null ? c.ToList() : null
        );
}
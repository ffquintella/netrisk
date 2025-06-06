using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DAL.Tools;

public static class EFConverters
{
    public static readonly ValueConverter<List<char>, string> CharListConverter =
        new ValueConverter<List<char>, string>(
            v => new string(v.ToArray()),
            v => v.ToList());

}
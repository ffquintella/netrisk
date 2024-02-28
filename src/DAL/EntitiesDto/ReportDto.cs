using DAL.Entities;

namespace DAL.EntitiesDto;

public class ReportDto: Report
{
    #pragma warning disable CS8764 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
    public new  NrFile? File { get; set; }
    public new  User? Creator { get; set; }
    #pragma warning restore CS8764
}
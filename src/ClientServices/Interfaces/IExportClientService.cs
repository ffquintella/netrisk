namespace ClientServices.Interfaces
{
    public enum ExportFormat
    {
        Pdf,
        Csv,
        Xlsx
    }

    public interface IExportClientService
    {
        Task<byte[]> ExportAsync(string entityType, ExportFormat format, string? filter = null, string? sort = null, string? reportTitle = null);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class TestExportDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Score { get; set; }
    public DateTime Date { get; set; }
    public bool IsActive { get; set; }
}

[TestSubject(typeof(Services.ExportService))]
public class ExportServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IExportService _svc;

    public ExportServiceInMemoryTest()
    {
        _svc = GetService<IExportService>();
    }

    private List<TestExportDto> GetTestData()
    {
        return new List<TestExportDto>
        {
            new() { Id = 1, Name = "Item One", Score = 8.5, Date = new DateTime(2026, 6, 15), IsActive = true },
            new() { Id = 2, Name = "Item Two", Score = 9.2, Date = new DateTime(2026, 6, 16), IsActive = false }
        };
    }

    [Fact]
    public async Task TestExportCsv()
    {
        var data = GetTestData();
        var result = await _svc.ExportAsync(data, ExportFormat.Csv, "Csv Report");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);

        var text = Encoding.UTF8.GetString(result);
        // Should contain header line
        Assert.Contains("Id,Name,Score,Date,IsActive", text);
        // Should contain data rows
        Assert.Contains("Item One", text);
        Assert.Contains("Item Two", text);
    }

    [Fact]
    public async Task TestCsvFormulaInjectionEscaping()
    {
        var data = new List<TestExportDto>
        {
            new() { Id = 3, Name = "=SUM(1,2)", Score = 1.0, Date = DateTime.Now, IsActive = true },
            new() { Id = 4, Name = "+1+2", Score = 2.0, Date = DateTime.Now, IsActive = true },
            new() { Id = 5, Name = "-3", Score = 3.0, Date = DateTime.Now, IsActive = true },
            new() { Id = 6, Name = "@injection", Score = 4.0, Date = DateTime.Now, IsActive = true }
        };

        var result = await _svc.ExportAsync(data, ExportFormat.Csv, "Csv Formula Escaped Report");
        Assert.NotNull(result);

        var text = Encoding.UTF8.GetString(result);
        // Formulas starting with =, +, -, @ must be escaped with an apostrophe '
        Assert.Contains("'=SUM(1,2)", text);
        Assert.Contains("'+1+2", text);
        Assert.Contains("'-3", text);
        Assert.Contains("'@injection", text);
    }

    [Fact]
    public async Task TestExportExcel()
    {
        var data = GetTestData();
        var result = await _svc.ExportAsync(data, ExportFormat.Xlsx, "Excel Report");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        
        // Verify XLSX signature (PKZIP files start with 'PK')
        Assert.Equal('P', (char)result[0]);
        Assert.Equal('K', (char)result[1]);
    }

    [Fact]
    public async Task TestExportPdfPlaceholder()
    {
        var data = GetTestData();
        var result = await _svc.ExportAsync(data, ExportFormat.Pdf, "Pdf Report");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);

        // Verify PDF signature (%PDF-)
        var pdfSignature = Encoding.ASCII.GetString(result, 0, 5);
        Assert.Equal("%PDF-", pdfSignature);
    }
}

﻿using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Serilog;
using ServerServices.Helpers;
using ServerServices.Services;

namespace ServerServices.Reports;

public abstract class TemplatedPdfReport(Report report, IStringLocalizer localizer, IDalService dalService)
{

    #region PROPERTIES
    
    private Report Report { get; set; } = report;
    public int TitleFontSize { get; set; } = 14;
    public int FooterFontSize { get; set; } = 9;
    public string ReportTitle { get; set; } = "";
    public Document? Document { get; set; }
    public Section? ActiveSection { get; set; }
    public bool UseCmykColor { get; set; } = true;
    public string FontName { get; set; } = "Arial";

    private const PdfFontEmbedding embedding = PdfFontEmbedding.EmbedCompleteFontFile;
       // PdfFontEmbedding.Always;
    protected IStringLocalizer Localizer { get; } = localizer;
    private string ImagesDirectory { get; set; } = Path.Combine(Path.GetDirectoryName(Assembly.GetAssembly(typeof(TemplatedPdfReport))!.Location)!, "Images");
    
    protected IDalService DalService { get; } = dalService;

    #endregion
    
    #region METHODS
    public async Task<byte[]> GenerateReportAsync(string title = "")
    {
        
        GlobalFontSettings.FontResolver ??= new FontResolver();
        
        Document = new Document();
        
        Style style = Document!.Styles["Normal"]!;
        style.Font.Name = FontName;
        
        Document.UseCmykColor = UseCmykColor;
        
        ReportTitle = title;
        
        /*var culture = new CultureInfo("pt-BR"); // Set the culture 
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;*/

        
        Document.Info.Title = title;
        Document.Info.Author = Localizer["Netrisk - Risk Management System"];
        Document.Info.Subject = "Report";

        ActiveSection = Document.AddSection();
        
        Document =  await AddHeader();
        Document = await AddBody();
        Document =  await AddFooter();
        
        return await SaveReport();
    }

    private async Task<Document> AddHeader()
    {
        if(Document == null)
            throw new Exception("Document is null");
        return await Task.Run(() =>
        {
            
            if(ActiveSection == null)
                throw new Exception("ActiveSection is null");

            var paragraph = ActiveSection.AddParagraph();
            
            paragraph.Format.Alignment = ParagraphAlignment.Center;
            
            paragraph.Format.Font.Name = FontName;
            
            var logo =paragraph.AddImage(Path.Combine(ImagesDirectory, "NetRisk.png"));
            
            logo.ScaleWidth = 0.2;
            logo.ScaleHeight = 0.2;

            paragraph.AddSpace(3);
            
            paragraph.Format.Font.Size = TitleFontSize;

            paragraph.AddFormattedText(ReportTitle, TextFormat.Bold);
            
            return Document;
        });
    }

    protected abstract Task<Document> AddBody();

    private async Task<Document> AddFooter()
    {
        if(Document == null)
            throw new Exception("Document is null");
        return await Task.Run(() =>
        {
            //var section = Document.LastSection;

            var paragraph = ActiveSection!.Footers.Primary.AddParagraph();

            paragraph.Format.Alignment = ParagraphAlignment.Center;
            paragraph.Format.Font.Size = FooterFontSize;
            paragraph.AddNumPagesField();

            return Document;
        });
    }
    
    private async Task<byte[]> SaveReport()
    {
        if(Document == null)
            throw new Exception("Document is null");

        try
        {
            var pdfRenderer = new PdfDocumentRenderer();
            pdfRenderer.Document = Document;
            pdfRenderer.RenderDocument();

            await using MemoryStream ms = new MemoryStream();
            pdfRenderer.PdfDocument.Save(ms, false);
            return ms.ToArray();
        }catch(Exception e)
        {
            Log.Error("Error saving report {Message}", e.Message);
            throw;
        }

        
    }
    #endregion
    
}
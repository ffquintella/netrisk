using System.Diagnostics;
using DAL.Entities;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ServerServices.Helpers;

namespace ServerServices.Reports;

public abstract class TemplatedPdfReport(Report report)
{
    private Report Report { get; set; } = report;
    
    public int TitleFontSize { get; set; } = 14;
    public int FooterFontSize { get; set; } = 9;
    
    public string ReportTitle { get; set; } = "";
    public Document? Document { get; set; }
    
    public bool UseCmykColor { get; set; } = true;
    
    public string FontName { get; set; } = "Arial";

    
    const PdfFontEmbedding embedding = PdfFontEmbedding.Always;

    public async Task GenerateReport(string title = "")
    {
        GlobalFontSettings.FontResolver = new FontResolver();
        
        Document = new Document();
        
        // Define a fonte padrão
        Style style = Document.Styles["Normal"];
        style.Font.Name = FontName;
        
        
        Document.UseCmykColor = UseCmykColor;
        
        
        ReportTitle = title;
        
        Document.Info.Title = title;
        Document.Info.Author = "Netrisk - Risk Management System";
        Document.Info.Subject = "Report";

        Document =  await AddHeader();
        Document = await AddBody();
        Document =  await AddFooter();
        
        
        
        SaveReport();
    }
   
    public async Task<Document> AddHeader()
    {
        if(Document == null)
            throw new Exception("Document is null");
        return await Task.Run(() =>
        {
            var section = Document.AddSection();

            //section.AddImage()

            var paragraph = section.AddParagraph();

            paragraph.Format.Font.Size = TitleFontSize;

            paragraph.AddFormattedText(ReportTitle, TextFormat.Bold);
            
            return Document;
        });
    }

    public abstract Task<Document> AddBody();
    
    public async Task<Document> AddFooter()
    {
        if(Document == null)
            throw new Exception("Document is null");
        return await Task.Run(() =>
        {
            var section = Document.LastSection;

            var paragraph = section.AddParagraph();

            paragraph.Format.Alignment = ParagraphAlignment.Right;
            paragraph.Format.Font.Size = FooterFontSize;
            paragraph.AddNumPagesField();

            return Document;
        });
    }
    
    public async void SaveReport()
    {
        if(Document == null)
            throw new Exception("Document is null");

        await Task.Run(() =>
        {
            PdfDocumentRenderer pdfRenderer = new PdfDocumentRenderer();
            pdfRenderer.Document = Document;
            pdfRenderer.RenderDocument();

            pdfRenderer.PdfDocument.Save("f:/tmp/report.pdf");

        });
        //Process.Start("f:/tmp/report.pdf");
    }
    
}
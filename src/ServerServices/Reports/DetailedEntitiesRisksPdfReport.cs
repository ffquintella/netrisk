using DAL.Entities;
using MigraDoc.DocumentObjectModel;

namespace ServerServices.Reports;

public class DetailedEntitiesRisksPdfReport(Report report) : TemplatedPdfReport(report)
{

    public override async Task<Document> AddBody()
    {
        if(Document == null)
            throw new Exception("Document is null");
        
        return await Task.Run(() =>
        {
            var section = Document.AddSection();

            //section.AddImage()

            var paragraph = section.AddParagraph();

            paragraph.Format.Font.Size = TitleFontSize;

            paragraph.AddFormattedText("TESTE REPORT", TextFormat.Bold);

            return Document;
        });
    }
}
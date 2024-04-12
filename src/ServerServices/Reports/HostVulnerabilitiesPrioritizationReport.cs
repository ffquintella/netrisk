using DAL.Entities;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using ServerServices.Services;

namespace ServerServices.Reports;

public class HostVulnerabilitiesPrioritizationReport(Report report, IStringLocalizer localizer, IDalService dalService): TemplatedPdfReport(report, localizer, dalService)
{
    public int BodyFontSize { get; set; } = 12;
    public override async Task<Document> AddBody()
    {
        if(Document == null)
            throw new Exception("Document is null");

        return await Task.Run(async () =>
        {
            if (ActiveSection == null)
                throw new Exception("ActiveSection is null");

            var paragraph = ActiveSection.AddParagraph();

            paragraph.Format.Font.Size = TitleFontSize;
            
            paragraph.AddFormattedText(Localizer["Host Vulnerability Prioritization Report"] , TextFormat.Bold);
            paragraph.AddLineBreak();
            
            
            paragraph.Format.Font.Size = BodyFontSize;
            
            
            // Create a new TextFrame
            TextFrame textBox = new TextFrame();

            // Set the height and width of the TextFrame
            textBox.Height = "3.0cm";
            textBox.Width = "7.0cm";

            // Set the contents of the TextFrame
            var tbp = textBox.AddParagraph();
                tbp.Format.Font.Size = BodyFontSize;
            tbp.AddFormattedText(Localizer["This report uses the Common Vulnerability Scoring System (CVSS) as well as other metrics to prioritize vulnerabilities on the host."] , TextFormat.Bold);
            

            // Add the TextFrame to a section
            ActiveSection.Add(textBox);
            
            paragraph = ActiveSection.AddParagraph();
            paragraph.AddLineBreak();

            var table = ActiveSection.AddTable();
            
            // Add the statistics table
            await AddStatisticsTable(table);
            
            paragraph = ActiveSection.AddParagraph();
            
            paragraph.AddLineBreak();
            paragraph.AddLineBreak();
            
            paragraph.Format.Font.Size = BodyFontSize + 2;
            paragraph.AddFormattedText(Localizer["Hosts"] , TextFormat.Bold);
            paragraph.AddLineBreak();
            
            paragraph.Format.Font.Size = BodyFontSize;
            
            using var dbContext = DalService.GetContext();
            
            var vulnerabilitiesHosts = dbContext.Vulnerabilities
                .Where(v => v.Cvss3BaseScore > 5 && v.ExploitAvaliable == true && v.HostId != null && v.ExploitabilityEasy == "Exploits are available" && v.Cvss3TemporalScore > 5)
                .AsEnumerable()
                .AsParallel()
                .GroupBy(v=> v.HostId)
                .OrderByDescending( g => g.Sum(v=> v.Cvss3TemporalScore))
                .ToList();

            foreach (var vulnerabilitiesHost in vulnerabilitiesHosts)
            {
                var host = dbContext.Hosts.Find(vulnerabilitiesHost.Key);
                
                if(host == null)
                    continue;
                
                paragraph.AddFormattedText(host.HostName!, TextFormat.Bold);
                paragraph.AddLineBreak();
                paragraph.AddText( "IP: " + host.Ip );
                paragraph.AddLineBreak();
                paragraph.AddText( "OS: " + host.Os );
                paragraph.AddLineBreak();
                paragraph.AddText( Localizer["Score"] + ": " + vulnerabilitiesHost.Sum(v=> v.Cvss3TemporalScore));
                paragraph.AddLineBreak();
                
                paragraph.AddFormattedText( Localizer["Vulnerabilities"] , TextFormat.Italic);
                paragraph.AddLineBreak();

                paragraph.AddText( Localizer["Critical Count"] + ": " + vulnerabilitiesHost.Where(v => v.Cvss3BaseScore > 8 ).Count().ToString() );
                paragraph.AddLineBreak();
                paragraph.AddText( Localizer["High Count"] + ": " + vulnerabilitiesHost.Where(v => v.Cvss3BaseScore <= 8 &&  v.Cvss3BaseScore > 6 ).Count().ToString() );
                paragraph.AddLineBreak();
                paragraph.AddText( Localizer["Medium Count"] + ": " + vulnerabilitiesHost.Where(v => v.Cvss3BaseScore <= 6 &&  v.Cvss3BaseScore > 4 ).Count().ToString() );
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                
                //Critical 
                foreach (var vul in vulnerabilitiesHost.Where(v => v.Cvss3BaseScore > 8))
                {        
                    paragraph.AddFormattedText( "ID:" + vul.Id , TextFormat.Bold);
                    paragraph.AddLineBreak();
                    paragraph.AddText( Localizer["Description"] + ": " + vul.Description );
                    paragraph.AddLineBreak();
                    paragraph.AddText( Localizer["Solution"] + ": " + vul.Solution );
                    paragraph.AddLineBreak();
                    
                }
            }
            
            return Document;
        });
        
    }
    
    private async Task AddStatisticsTable(Table table)
    {
        if(Document == null)
            throw new Exception("Document is null");
        
        if(ActiveSection == null)
            throw new Exception("ActiveSection is null");

        await Task.Run(() =>
        {
            var style = Document.Styles.AddStyle("Table", "Normal");
            style.Font.Name = "Verdana";
            style.Font.Name = "Times New Roman";
            style.Font.Size = 9;
            
            var tableBlue = new Color(100,100,200);
            
            // Create the item table
            //var table = ActiveSection.AddTable();
            table.Style = "Table";
            table.Borders.Color = tableBlue;
            table.Borders.Width = 0.25;
            table.Borders.Left.Width = 0.5;
            table.Borders.Right.Width = 0.5;
            table.Rows.LeftIndent = 0;
            
            // Before you can add a row, you must define the columns
            var column = table.AddColumn("3cm");
            column.Format.Alignment = ParagraphAlignment.Center;
            
            column = table.AddColumn("2.5cm");
            column.Format.Alignment = ParagraphAlignment.Right;
            
            column = table.AddColumn("2.5cm");
            column.Format.Alignment = ParagraphAlignment.Right;
            
            column = table.AddColumn("2.5cm");
            column.Format.Alignment = ParagraphAlignment.Right;
            
            column = table.AddColumn("2.5cm");
            column.Format.Alignment = ParagraphAlignment.Right;
            
  
            // Create the header of the table
            Row row = table.AddRow();
            row.HeadingFormat = true;
            row.Format.Alignment = ParagraphAlignment.Center;
            row.Format.Font.Bold = true;
            row.Shading.Color = tableBlue;
            
            row.Cells[0].AddParagraph(Localizer["Computer"]);
            row.Cells[0].Format.Font.Bold = false;
            row.Cells[0].Format.Alignment = ParagraphAlignment.Left;
            row.Cells[0].VerticalAlignment = VerticalAlignment.Bottom;
            //row.Cells[0].MergeDown = 1;
            row.Cells[1].AddParagraph(Localizer["Critical"]);
            row.Cells[1].Format.Alignment = ParagraphAlignment.Left;
            //row.Cells[1].MergeRight = 3;

            row.Cells[2].AddParagraph(Localizer["High"]);
            row.Cells[2].Format.Alignment = ParagraphAlignment.Left;
            row.Cells[2].VerticalAlignment = VerticalAlignment.Bottom;

            row.Cells[3].AddParagraph(Localizer["Medium"]);
            row.Cells[3].Format.Alignment = ParagraphAlignment.Left;
            row.Cells[3].VerticalAlignment = VerticalAlignment.Bottom;
            
            row.Cells[4].AddParagraph(Localizer["Total"]);
            row.Cells[4].Format.Alignment = ParagraphAlignment.Left;
            row.Cells[4].VerticalAlignment = VerticalAlignment.Bottom;
            
            using var dbContext = DalService.GetContext();
            
            var vulnerabilitiesHosts = dbContext.Vulnerabilities
                .Where(v => v.Cvss3BaseScore > 5 && v.ExploitAvaliable == true && v.HostId != null && v.ExploitabilityEasy == "Exploits are available" && v.Cvss3TemporalScore > 5)
                .AsEnumerable()
                .AsParallel()
                .GroupBy(v=> v.HostId)
                .OrderByDescending( g => g.Sum(v=> v.Cvss3TemporalScore))
                .ToList();

            foreach (var vulnerabilitiesHost in vulnerabilitiesHosts)
            {
                var host = dbContext.Hosts.Find(vulnerabilitiesHost.Key);
                
                if(host == null)
                    continue;
                
                row = table.AddRow();
                row.Cells[0].AddParagraph(host.HostName!);
                row.Cells[1].AddParagraph(vulnerabilitiesHost.Where(v => v.Cvss3BaseScore > 8 ).Count().ToString());
                row.Cells[2].AddParagraph(vulnerabilitiesHost.Where(v => v.Cvss3BaseScore <= 8 &&  v.Cvss3BaseScore > 6 ).Count().ToString());
                row.Cells[3].AddParagraph(vulnerabilitiesHost.Where(v => v.Cvss3BaseScore <= 6 &&  v.Cvss3BaseScore > 4 ).Count().ToString());
                row.Cells[4].AddParagraph(vulnerabilitiesHost.Count().ToString());
            }
        });
        
    }
}
using DAL.Entities;
using Microsoft.Extensions.Localization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using ServerServices.Services;

namespace ServerServices.Reports;

public class HostVulnerabilitiesPrioritizationReport(Report report, IStringLocalizer localizer, DALService dalService): TemplatedPdfReport(report, localizer, dalService)
{
    public int BodyFontSize { get; set; } = 12;
    public override async Task<Document> AddBody()
    {
        if(Document == null)
            throw new Exception("Document is null");

        return await Task.Run(() =>
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
                
                paragraph.AddFormattedText(host.HostName, TextFormat.Bold);
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
                    paragraph.AddText( Localizer["Description"] + ": " + vul.Description );
                    paragraph.AddLineBreak();
                    paragraph.AddText( Localizer["Solution"] + ": " + vul.Solution );
                    paragraph.AddLineBreak();
                    
                }
            }
            
            return Document;
        });
        
        
    }
}
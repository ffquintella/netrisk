using DAL.Entities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using ServerServices.Interfaces;
using Sieve.Models;
using Sieve.Services;

namespace API;

public class ApplicationSieveProcessor(IOptions<SieveOptions> options, ILocalizationService localization)
    : SieveProcessor(options)
{
    public ILocalizationService Localization { get; } = localization;

    protected override SievePropertyMapper MapProperties(SievePropertyMapper mapper)
    {
        
        var Localizer = Localization.GetLocalizer();
        
        mapper.Property<Vulnerability>(p => p.Title)
            .CanSort()
            .HasName(Localizer["title"])
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.Id)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.Score)
            .CanSort()
            .HasName(Localizer["Score"])
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.Severity)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["impact"]);
        
        mapper.Property<Vulnerability>(p => p.Status)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["status"]);
        
        mapper.Property<Vulnerability>(p => p.FirstDetection)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["first_detection"]);
        
        mapper.Property<Vulnerability>(p => p.LastDetection)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["last_detection"]);
        
        mapper.Property<Vulnerability>(p => p.DetectionCount)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["detections"]);
        
        mapper.Property<Vulnerability>(p => p.AnalystId)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["analyst"]);
        
        mapper.Property<Vulnerability>(p => p.HostId)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["host"]);
        
        mapper.Property<Vulnerability>(p => p.ImportSource)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["source"]);
        
        mapper.Property<Vulnerability>(p => p.Technology)
            .CanSort()
            .CanFilter()
            .HasName(Localizer["technology"]);
        
        /*
        mapper.Property<Post>(p => p.Title)
            .CanFilter()
            .HasName("a_different_query_name_here");

        mapper.Property<Post>(p => p.CommentCount)
            .CanSort();

        mapper.Property<Post>(p => p.DateCreated)
            .CanSort()
            .CanFilter()
            .HasName("created_on");*/

        return mapper;
    }


}
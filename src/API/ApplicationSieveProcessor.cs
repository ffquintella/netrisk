using DAL.Entities;
using Microsoft.Extensions.Options;
using Sieve.Models;
using Sieve.Services;

namespace API;

public class ApplicationSieveProcessor: SieveProcessor
{
    /*public ApplicationSieveProcessor(
        IOptions<SieveOptions> options, 
        ISieveCustomSortMethods customSortMethods, 
        ISieveCustomFilterMethods customFilterMethods) 
        : base(options, customSortMethods, customFilterMethods)
    {
    }*/
    
    public ApplicationSieveProcessor(IOptions<SieveOptions> options) 
        : base(options)
    {
    }

    protected override SievePropertyMapper MapProperties(SievePropertyMapper mapper)
    {
        mapper.Property<Vulnerability>(p => p.Title)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.Id)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.Score)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.Severity)
            .CanSort()
            .CanFilter()
            .HasName("impact");;
        
        mapper.Property<Vulnerability>(p => p.Status)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.FirstDetection)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.LastDetection)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.DetectionCount)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.AnalystId)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.HostId)
            .CanSort()
            .CanFilter();
        
        mapper.Property<Vulnerability>(p => p.ImportSource)
            .CanSort()
            .CanFilter();
        
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
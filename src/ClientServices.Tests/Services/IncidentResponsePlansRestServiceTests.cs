using System.Threading.Tasks;
using ClientServices.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClientServices.Tests.Services;

public class IncidentResponsePlansRestServiceTests: BaseServiceTest
{
    private readonly IIncidentResponsePlansService _incidentResponsePlansService;
    
    public IncidentResponsePlansRestServiceTests()
    {
        _incidentResponsePlansService = _serviceProvider.GetRequiredService<IIncidentResponsePlansService>();
    }
    
    [Fact]
    public async Task TestGetAllAsync()
    {
        
        var plans = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(plans);
        
        Assert.Equal(2, plans.Count);
        
    }

}
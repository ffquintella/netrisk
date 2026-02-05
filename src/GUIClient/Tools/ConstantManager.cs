using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient.Tools;

public class ConstantManager
{
    private readonly IRisksService _risksService;

    public List<Likelihood>? Likelihoods { get; private set; } = new List<Likelihood>();
    public List<Impact>? Impacts { get; private set; } = new List<Impact>();

    public List<RiskCatalog>? RiskTypes { get; private set; } = new List<RiskCatalog>();

    public ConstantManager()
    {
        _risksService = GetService<IRisksService>();

        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        Likelihoods = await _risksService.GetProbabilitiesAsync();
        Impacts = await _risksService.GetImpactsAsync();
        RiskTypes = await _risksService.GetRiskTypesAsync();

        if (Likelihoods == null)
            throw new Exception("Could not load likelihoods");
        if (Impacts == null)
            throw new Exception("Could not load impacts");
        if (RiskTypes == null)
            throw new Exception("Could not load RiskTypes");
    }

    protected static T GetService<T>()
    {
        var result = Program.ServiceProvider.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    }
}

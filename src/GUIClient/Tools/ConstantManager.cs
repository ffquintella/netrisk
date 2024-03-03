using System;
using System.Collections.Generic;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Splat;

namespace GUIClient.Tools;

public class ConstantManager
{
    
    private readonly IRisksService _risksService;

    public List<Likelihood>? Likelihoods { get; private set; }
    public List<Impact>? Impacts { get; private set; }
    
    public List<RiskCatalog>? RiskTypes { get; private set; } 
        
    public ConstantManager()
    {
        _risksService = GetService<IRisksService>();
        LoadData();
    }
    
    private async void LoadData()
    {
        Likelihoods = await _risksService.GetProbabilitiesAsync();
        Impacts = await _risksService.GetImpactsAsync();
        RiskTypes = await _risksService.GetRiskTypesAsync();
        
        if(Likelihoods == null)
            throw new Exception("Could not load likelihoods");
        if(Impacts == null)
            throw new Exception("Could not load impacts");
        if(RiskTypes == null)
            throw new Exception("Could not load RiskTypes");
    }
    
    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
    
}
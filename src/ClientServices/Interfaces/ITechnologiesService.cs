using System.Collections.Generic;
using DAL.Entities;

namespace ClientServices.Interfaces;

public interface ITechnologiesService
{
    /// <summary>
    /// Get all technologies
    /// </summary>
    /// <returns></returns>
    public List<Technology> GetAll();
}
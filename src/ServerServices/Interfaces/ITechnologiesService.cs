using DAL.Entities;

namespace ServerServices.Interfaces;

public interface ITechnologiesService
{
    public List<Technology> GetAll();
}
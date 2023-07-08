using AutoMapper;
using DAL.Entities;
using Model;

namespace API.ClassMapping;

public class ClientProfile: Profile
{
    public ClientProfile()
    {
        CreateMap<AddonsClientRegistration, Client>();
    }
}
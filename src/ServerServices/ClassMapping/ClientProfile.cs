using AutoMapper;
using DAL.Entities;
using Model;

namespace ServerServices.ClassMapping;

public class ClientProfile: Profile
{
    public ClientProfile()
    {
        CreateMap<ClientRegistration, Client>();
    }
}
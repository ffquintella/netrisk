using AutoMapper;
using DAL.Entities;
using Model.DTO;

namespace ServerServices.ClassMapping;

public class HostsServiceProfile: Profile
{
    public HostsServiceProfile()
    {
        CreateMap<HostsServiceDto,HostsService>();
            

    }
}
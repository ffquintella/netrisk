using Mapster;
using System.Text;
using DAL.Entities;
using Model.DTO;

namespace ServerServices;

public static class MapsterConfiguration
{
    public static void RegisterMappings()
    {
        TypeAdapterConfig<User, UserDto>.NewConfig()
            .Map(dest => dest.Email,
                src => src.Email != null
                    ? Encoding.UTF8.GetString(src.Email)
                    : null)
            .Map(dest => dest.UserName, src => src.Login);
    }
}
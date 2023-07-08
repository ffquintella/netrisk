using System.Globalization;
using System.Text;
using AutoMapper;
using DAL.Entities;
using Model;
using Model.DTO;

namespace API.ClassMapping;

public class UserProfile: Profile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>().ForMember(dst => dst.Email,
            map => map.MapFrom<string>(src => Encoding.UTF8.GetString(src.Email)))
            .ForMember(dst => dst.UserName,
                map => map.MapFrom<string>(src => Encoding.UTF8.GetString(src.Username)))
            .ForMember(dst => dst.Id,
            map => map.MapFrom(src => src.Value));
        
        CreateMap<UserDto, User>().ForMember(dst => dst.Email,
                map => map.MapFrom<byte[]>(src => Encoding.UTF8.GetBytes(src.Email)))
            .ForMember(dst => dst.Username,
                map => map.MapFrom<byte[]>(src => Encoding.UTF8.GetBytes(src.UserName)))
            .ForMember(dst => dst.Value,
                map => map.MapFrom(src => src.Id));

        CreateMap<User, User>()
            .ForMember(src => src.CustomDisplaySettings, opt => opt.Ignore())
            .ForMember(src => src.CustomReviewregularlyDisplaySettings, opt => opt.Ignore())
            .ForMember(src => src.CustomPerformReviewsDisplaySettings, opt => opt.Ignore())
            .ForMember(src => src.CustomPlanMitigationDisplaySettings, opt => opt.Ignore())
            .ForMember(src => src.CustomRisksAndIssuesSettings, opt => opt.Ignore());
    }
}
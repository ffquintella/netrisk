using AutoMapper;
using DAL.Entities;
using Model.DTO;

namespace ServerServices.ClassMapping;

public class MgmtReviewProfile: Profile
{
    public MgmtReviewProfile()
    {
        CreateMap<MgmtReviewDto, MgmtReview>();
    }
}
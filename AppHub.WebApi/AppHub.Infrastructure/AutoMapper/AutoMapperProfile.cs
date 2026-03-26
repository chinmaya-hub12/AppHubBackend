using AutoMapper;
using AppHub.Core.Dto;
using AppHub.Core.Entity;

namespace AppHub.Infrastructure.AutoMapper;

public class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        // Original mapping – now fully wired up
        CreateMap<sp_UserDetail, UserDto>()
            .ForMember(dest => dest.UserId,      opt => opt.MapFrom(src => src.UserId))
            .ForMember(dest => dest.UserTypeId,  opt => opt.MapFrom(src => src.UserTypeId))
            .ForMember(dest => dest.UserTypeName,opt => opt.MapFrom(src => src.UserTypeName))
            .ForMember(dest => dest.MobileNo,    opt => opt.MapFrom(src => src.MobileNo))
            .ForMember(dest => dest.Username,    opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.IsActive,    opt => opt.MapFrom(src => true))
            .ForMember(dest => dest.IsDeleted,   opt => opt.MapFrom(src => false));
    }
}

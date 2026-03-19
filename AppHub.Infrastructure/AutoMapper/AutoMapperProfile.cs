using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac.Builder;
using AutoMapper;
using AppHub.Core.Dto;

namespace AppHub.Infrastructure.AutoMapper;
public class AutoMapperProfile : Profile
{
  public AutoMapperProfile()
  {
    //CreateMap<FinMaster, FinMasterDto>().ReverseMap();
    
  }
}

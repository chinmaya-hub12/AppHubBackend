using Ardalis.EFCore.Extensions;
using AppHub.Core.Entity;
using AppHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AppHub.Infrastructure.Data;
public class AppDbContext : DbContext
{
  private readonly IMediator? _mediator;

  //public AppDbContext(DbContextOptions options) : base(options)
  //{
  //}

  //public AppDbContext(DbContextOptions<AppDbContext> options, IMediator? mediator)
  //    : base(options)
  //{
  //  _mediator = mediator;
  //}
  public AppDbContext(DbContextOptions<AppDbContext> options)
     : base(options)
  {
  }

    public DbSet<User_Credential> User_Credential => Set<User_Credential>();
    public DbSet<sp_UserDetail> sp_UserDetail => Set<sp_UserDetail>();

    public override int SaveChanges()
  {
    return SaveChangesAsync().GetAwaiter().GetResult();
  }
}

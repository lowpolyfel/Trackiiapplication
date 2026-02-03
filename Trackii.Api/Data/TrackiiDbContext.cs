using Microsoft.EntityFrameworkCore;
using Trackii.Api.Models;

namespace Trackii.Api.Data;

public sealed class TrackiiDbContext : DbContext
{
    public TrackiiDbContext(DbContextOptions<TrackiiDbContext> options) : base(options)
    {
    }

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Subfamily> Subfamilies => Set<Subfamily>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RouteStep> RouteSteps => Set<RouteStep>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WipItem> WipItems => Set<WipItem>();
    public DbSet<WipStepExecution> WipStepExecutions => Set<WipStepExecution>();
    public DbSet<ScanEvent> ScanEvents => Set<ScanEvent>();
    public DbSet<UnregisteredPart> UnregisteredParts => Set<UnregisteredPart>();
    public DbSet<WipReworkLog> WipReworkLogs => Set<WipReworkLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("location");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Active).HasColumnName("active");
        });

        modelBuilder.Entity<Token>(entity =>
        {
            entity.ToTable("tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Code).HasColumnName("code");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("role");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Active).HasColumnName("active");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("user");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username");
            entity.Property(e => e.Password).HasColumnName("password");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.HasOne(e => e.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(e => e.RoleId);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DeviceUid).HasColumnName("device_uid");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.HasOne(e => e.Location)
                .WithMany(l => l.Devices)
                .HasForeignKey(e => e.LocationId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Devices)
                .HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<Area>(entity =>
        {
            entity.ToTable("area");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Active).HasColumnName("active");
        });

        modelBuilder.Entity<Family>(entity =>
        {
            entity.ToTable("family");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AreaId).HasColumnName("id_area");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.HasOne(e => e.Area)
                .WithMany(a => a.Families)
                .HasForeignKey(e => e.AreaId);
        });

        modelBuilder.Entity<Subfamily>(entity =>
        {
            entity.ToTable("subfamily");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FamilyId).HasColumnName("id_family");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.Property(e => e.ActiveRouteId).HasColumnName("active_route_id");
            entity.HasOne(e => e.Family)
                .WithMany(f => f.Subfamilies)
                .HasForeignKey(e => e.FamilyId);
            entity.HasOne(e => e.ActiveRoute)
                .WithMany()
                .HasForeignKey(e => e.ActiveRouteId);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("product");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SubfamilyId).HasColumnName("id_subfamily");
            entity.Property(e => e.PartNumber).HasColumnName("part_number");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.HasOne(e => e.Subfamily)
                .WithMany(s => s.Products)
                .HasForeignKey(e => e.SubfamilyId);
        });

        modelBuilder.Entity<Route>(entity =>
        {
            entity.ToTable("route");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SubfamilyId).HasColumnName("subfamily_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.Active).HasColumnName("active");
            entity.HasOne(e => e.Subfamily)
                .WithMany()
                .HasForeignKey(e => e.SubfamilyId);
        });

        modelBuilder.Entity<RouteStep>(entity =>
        {
            entity.ToTable("route_step");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.Property(e => e.StepNumber).HasColumnName("step_number");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.HasOne(e => e.Route)
                .WithMany(r => r.Steps)
                .HasForeignKey(e => e.RouteId);
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => e.LocationId);
        });

        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.ToTable("work_order");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WoNumber).HasColumnName("wo_number");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.HasOne(e => e.Product)
                .WithMany(p => p.WorkOrders)
                .HasForeignKey(e => e.ProductId);
            entity.HasOne(e => e.WipItem)
                .WithOne(w => w.WorkOrder)
                .HasForeignKey<WipItem>(w => w.WorkOrderId);
        });

        modelBuilder.Entity<WipItem>(entity =>
        {
            entity.ToTable("wip_item");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkOrderId).HasColumnName("wo_order_id");
            entity.Property(e => e.CurrentStepId).HasColumnName("current_step_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.RouteId).HasColumnName("route_id");
            entity.HasOne(e => e.CurrentStep)
                .WithMany()
                .HasForeignKey(e => e.CurrentStepId);
            entity.HasOne(e => e.Route)
                .WithMany()
                .HasForeignKey(e => e.RouteId);
        });

        modelBuilder.Entity<WipStepExecution>(entity =>
        {
            entity.ToTable("wip_step_execution");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WipItemId).HasColumnName("wip_item_id");
            entity.Property(e => e.RouteStepId).HasColumnName("route_step_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.DeviceId).HasColumnName("device_id");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.CreatedAt).HasColumnName("create_at");
            entity.Property(e => e.QtyIn).HasColumnName("qty_in");
            entity.Property(e => e.QtyScrap).HasColumnName("qty_scrap");
            entity.HasOne(e => e.WipItem)
                .WithMany(w => w.StepExecutions)
                .HasForeignKey(e => e.WipItemId);
            entity.HasOne(e => e.RouteStep)
                .WithMany(r => r.StepExecutions)
                .HasForeignKey(e => e.RouteStepId);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId);
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => e.LocationId);
        });

        modelBuilder.Entity<ScanEvent>(entity =>
        {
            entity.ToTable("scan_event");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WipItemId).HasColumnName("wip_item_id");
            entity.Property(e => e.RouteStepId).HasColumnName("route_step_id");
            entity.Property(e => e.ScanType).HasColumnName("scan_type");
            entity.Property(e => e.Ts).HasColumnName("ts");
            entity.HasOne(e => e.WipItem)
                .WithMany(w => w.ScanEvents)
                .HasForeignKey(e => e.WipItemId);
            entity.HasOne(e => e.RouteStep)
                .WithMany()
                .HasForeignKey(e => e.RouteStepId);
        });

        modelBuilder.Entity<UnregisteredPart>(entity =>
        {
            entity.ToTable("unregistered_parts");
            entity.HasKey(e => e.PartId);
            entity.Property(e => e.PartId).HasColumnName("part_id");
            entity.Property(e => e.PartNumber).HasColumnName("part_number");
            entity.Property(e => e.CreationDateTime).HasColumnName("creation_datetime");
            entity.Property(e => e.Active).HasColumnName("active");
        });

        modelBuilder.Entity<WipReworkLog>(entity =>
        {
            entity.ToTable("wip_rework_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WipItemId).HasColumnName("wip_item_id");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.DeviceId).HasColumnName("device_id");
            entity.Property(e => e.Qty).HasColumnName("qty");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasOne(e => e.WipItem)
                .WithMany(w => w.ReworkLogs)
                .HasForeignKey(e => e.WipItemId);
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => e.LocationId);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId);
        });
    }
}

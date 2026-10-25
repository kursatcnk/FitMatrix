using FitMatrix.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FitMatrix.Api.Data;

public class FitMatrixDbContext(DbContextOptions<FitMatrixDbContext> options) : DbContext(options)
{
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<EquipmentCategory> EquipmentCategories => Set<EquipmentCategory>();
    public DbSet<EquipmentProduct> EquipmentProducts => Set<EquipmentProduct>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductSource> ProductSources => Set<ProductSource>();
    public DbSet<ProductSpecification> ProductSpecifications => Set<ProductSpecification>();
    public DbSet<ProductLookupCache> ProductLookupCaches => Set<ProductLookupCache>();
    public DbSet<DeskProject> DeskProjects => Set<DeskProject>();
    public DbSet<DeskProjectItem> DeskProjectItems => Set<DeskProjectItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Slug).HasMaxLength(140);
            entity.Property(x => x.WebsiteUrl).HasMaxLength(500);
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<EquipmentCategory>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Slug).HasMaxLength(140);
            entity.Property(x => x.EditorModel).HasMaxLength(60);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EquipmentProduct>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(220);
            entity.Property(x => x.ModelName).HasMaxLength(160);
            entity.Property(x => x.ManufacturerPartNumber).HasMaxLength(100);
            entity.Property(x => x.Gtin).HasMaxLength(20);
            entity.Property(x => x.EditorModel).HasMaxLength(60);
            entity.Property(x => x.DisplayColor).HasMaxLength(20);
            entity.Property(x => x.DataQuality).HasMaxLength(40);
            entity.Property(x => x.WidthCm).HasPrecision(9, 2);
            entity.Property(x => x.HeightCm).HasPrecision(9, 2);
            entity.Property(x => x.DepthCm).HasPrecision(9, 2);
            entity.Property(x => x.FootprintWidthCm).HasPrecision(9, 2);
            entity.Property(x => x.FootprintDepthCm).HasPrecision(9, 2);
            entity.Property(x => x.WeightKg).HasPrecision(9, 3);
            entity.HasIndex(x => x.Gtin).IsUnique().HasFilter("[Gtin] IS NOT NULL");
            entity.HasIndex(x => new { x.BrandId, x.ManufacturerPartNumber }).IsUnique().HasFilter("[ManufacturerPartNumber] IS NOT NULL");
        });

        modelBuilder.Entity<AttributeDefinition>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(140);
            entity.Property(x => x.ValueType).HasMaxLength(20);
            entity.Property(x => x.Unit).HasMaxLength(30);
            entity.HasIndex(x => new { x.CategoryId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<ProductAttributeValue>(entity =>
        {
            entity.HasKey(x => new { x.ProductId, x.AttributeDefinitionId });
            entity.Property(x => x.NumericValue).HasPrecision(18, 4);
            entity.Property(x => x.TextValue).HasMaxLength(1000);
            entity.HasOne(x => x.AttributeDefinition).WithMany(x => x.Values).HasForeignKey(x => x.AttributeDefinitionId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Product).WithMany(x => x.AttributeValues).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductSource>(entity =>
        {
            entity.Property(x => x.Provider).HasMaxLength(100);
            entity.Property(x => x.SourceUrl).HasMaxLength(1000);
            entity.Property(x => x.ExternalId).HasMaxLength(160);
            entity.Property(x => x.LicenseName).HasMaxLength(160);
        });

        modelBuilder.Entity<ProductSpecification>(entity =>
        {
            entity.Property(x => x.Group).HasMaxLength(120);
            entity.Property(x => x.Name).HasMaxLength(180);
            entity.Property(x => x.NormalizedKey).HasMaxLength(320);
            entity.Property(x => x.Value).HasMaxLength(2000);
            entity.Property(x => x.Unit).HasMaxLength(60);
            entity.Property(x => x.SourceUrl).HasMaxLength(1000);
            entity.HasIndex(x => new { x.ProductId, x.NormalizedKey }).IsUnique();
            entity.HasOne(x => x.Product).WithMany(x => x.Specifications).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductLookupCache>(entity =>
        {
            entity.Property(x => x.NormalizedQuery).HasMaxLength(180);
            entity.HasIndex(x => x.NormalizedQuery).IsUnique();
        });

        modelBuilder.Entity<DeskProject>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.DeskWidthCm).HasPrecision(9, 2);
            entity.Property(x => x.DeskDepthCm).HasPrecision(9, 2);
            entity.Property(x => x.DeskHeightCm).HasPrecision(9, 2);
            entity.Property(x => x.RoomWidthCm).HasPrecision(9, 2);
            entity.Property(x => x.RoomDepthCm).HasPrecision(9, 2);
            entity.Property(x => x.RoomWallGapCm).HasPrecision(9, 2);
        });

        modelBuilder.Entity<DeskProjectItem>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(220);
            entity.Property(x => x.CategorySlug).HasMaxLength(140);
            entity.Property(x => x.EditorModel).HasMaxLength(60);
            entity.Property(x => x.DisplayColor).HasMaxLength(20);
            entity.Property(x => x.MountMode).HasMaxLength(20).HasDefaultValue("surface");
            entity.Property(x => x.ScreenOrientation).HasMaxLength(20).HasDefaultValue("landscape");
            foreach (var property in new[] { nameof(DeskProjectItem.WidthCm), nameof(DeskProjectItem.HeightCm), nameof(DeskProjectItem.DepthCm), nameof(DeskProjectItem.FootprintWidthCm), nameof(DeskProjectItem.FootprintDepthCm), nameof(DeskProjectItem.PositionXcm), nameof(DeskProjectItem.PositionZcm), nameof(DeskProjectItem.RotationDegrees), nameof(DeskProjectItem.ElevationCm), nameof(DeskProjectItem.TiltDegrees) })
            {
                entity.Property<decimal>(property).HasPrecision(9, 2);
            }
            entity.HasOne(x => x.Project).WithMany(x => x.Items).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.EquipmentProduct).WithMany().HasForeignKey(x => x.EquipmentProductId).OnDelete(DeleteBehavior.SetNull);
        });

        CatalogSeed.Configure(modelBuilder);
    }
}

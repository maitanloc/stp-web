using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stp.Core.Domain;
using Stp.Core.Excel;
using Stp.Core.Macros;
using Stp.Core.Sync;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Macros;
using Stp.Infrastructure.Persistence;
using Stp.Infrastructure.Storage;
using Xunit;

namespace Stp.Tests;

public sealed class DatabaseSeedRunner
{
    private const string ConnectionString = "Server=.\\SQLEXPRESS;Database=StpArchive;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private sealed class ContextFactory : IDbContextFactory<StpDbContext>
    {
        private readonly DbContextOptions<StpDbContext> _options;
        public ContextFactory(DbContextOptions<StpDbContext> options) => _options = options;
        public StpDbContext CreateDbContext() => new(_options);
        public Task<StpDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new StpDbContext(_options));
    }

    [Fact]
    public async Task SeedAllDataIntoDatabaseAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<StpDbContext>();
        optionsBuilder.UseSqlServer(ConnectionString);

        var factory = new ContextFactory(optionsBuilder.Options);
        using var db = factory.CreateDbContext();

        // 1. Ensure migrations applied
        await db.Database.MigrateAsync();

        // 2. Seed Default Admin User
        if (!await db.StpUsers.AnyAsync())
        {
            var admin = new StpUser
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                Username = "admin",
                DisplayName = "Quản trị",
                Role = "admin",
                IsActive = true,
                PasswordHash = string.Empty
            };
            admin.PasswordHash = new PasswordHasher<StpUser>().HashPassword(admin, "stp2026");
            db.StpUsers.Add(admin);
            await db.SaveChangesAsync();
        }

        // 3. Seed Default Macro Versions
        var macroService = new MacroService(
            db,
            new MacroTemplateProvider(),
            NullLogger<MacroService>.Instance);
        await macroService.GetActiveAsync();

        db.Database.SetCommandTimeout(180);

        // 4. Seed Workbook & Dossiers from onedrive_file.xlsx (if not already seeded)
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../onedrive_file.xlsx")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../onedrive_file.xlsx")),
            @"d:\A\stp-web\onedrive_file.xlsx",
            Path.GetFullPath("onedrive_file.xlsx")
        };
        var excelPath = candidates.FirstOrDefault(File.Exists);

        if (!await db.Dossiers.AnyAsync() && excelPath is not null && File.Exists(excelPath))
        {
            FileStorage.SanitizeExcelPackage(excelPath);

            var inspector = new ExcelInspector(NullLogger<ExcelInspector>.Instance);
            var syncService = new ArchiveSyncService(
                db,
                inspector,
                NullLogger<ArchiveSyncService>.Instance);

            var outcome = await syncService.ApplyAsync(new SyncRequest
            {
                FilePath = excelPath,
                SourceFileName = "DK KET HON.xlsx"
            });

            Assert.True(outcome.Success, $"Sync failed: {outcome.ErrorMessage}");
            Assert.True(outcome.AddedCount + outcome.UpdatedCount > 0, $"No dossiers synced! Added: {outcome.AddedCount}, Updated: {outcome.UpdatedCount}");

            // 5. Seed Catalog Items from Dossiers
            CatalogSeeder.SeedFromArchive(factory, minCount: 10);
        }

        // Verify row counts
        var userCount = await db.StpUsers.CountAsync();
        var macroCount = await db.MacroVersions.CountAsync();
        var dossierCount = await db.Dossiers.CountAsync();

        Assert.True(userCount > 0, "Users phải được seed.");
        Assert.True(macroCount > 0, "Macro versions phải được seed.");
        Assert.True(dossierCount > 0, "Dossiers phải có dữ liệu trong DB.");
    }
}

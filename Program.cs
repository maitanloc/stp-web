using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using StpWeb.Services;

var builder = WebApplication.CreateBuilder(args);

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("vi-VN");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("vi-VN");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100_000_000;
});

builder.Services.AddRazorPages();
builder.Services.Configure<FormOptions>(options =>
{
    // Workbook gốc khoảng 5-6 MB; để rộng cho các file lớn hơn.
    options.MultipartBodyLengthLimit = 100_000_000;
});

builder.Services.AddSingleton<ArchiveStore>();
builder.Services.AddScoped<ExcelImportService>();
builder.Services.AddScoped<DossierFilter>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();

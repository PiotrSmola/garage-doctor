using GarageDoctor.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddMongo(new MongoSettings
{
    ConnectionString = builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://mongo:27017",
    DatabaseName = builder.Configuration["MongoDatabase"] ?? "garagedoctor"
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseRouting();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

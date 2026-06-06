using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServerOptionsAction: sqlOptions =>
    {
        // Tells Entity Framework to retry connecting automatically if Azure is waking up or drops transiently
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    }));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>() //Enable roles
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        options.Conventions.AddAreaPageRoute("Identity", "/Account/RegisterChoice", "/Identity/Account/RegisterChoice");
        options.Conventions.AddAreaPageRoute("Identity", "/Account/RegisterClient", "/Identity/Account/RegisterClient");
        options.Conventions.AddAreaPageRoute("Identity", "/Account/RegisterArtist", "/Identity/Account/RegisterArtist");
    });


var app = builder.Build();

//Seed Roles On Startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = { "Client", "Artist", "Admin" };

    //Create Roles If They Don't Already Exist
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    //Create the admin user automatically
    string adminEmail = "admin@example.com";
    string adminPassword = "Admin@123";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        var newAdmin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var createAdminResult = await userManager.CreateAsync(newAdmin, adminPassword);
        if (createAdminResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newAdmin, "Admin");
        }
    }

    string artistEmail = "artist@example.com";
    string artistPassword = "Artist@123";

    var artistUser = await userManager.FindByEmailAsync(artistEmail);
    if (artistUser == null)
    {
        var newArtist = new ApplicationUser
        {
            UserName = artistEmail,
            Email = artistEmail,
            EmailConfirmed = true
        };

        var createArtistResult = await userManager.CreateAsync(newArtist, artistPassword);
        if (createArtistResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newArtist, "Artist");
        }
    }

    string clientEmail = "client@example.com";
    string clientPassword = "Client@123";

    var clientUser = await userManager.FindByEmailAsync(clientEmail);
    if (clientUser == null)
    {
        var newClient = new ApplicationUser
        {
            UserName = clientEmail,
            Email = clientEmail,
            EmailConfirmed = true
        };

        var createClientResult = await userManager.CreateAsync(newClient, clientPassword);
        if (createClientResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newClient, "Client");
        }

        var cultureInfo = new CultureInfo("en-ZA");
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
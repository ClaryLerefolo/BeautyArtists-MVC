using BeautyArtists.Data;
using BeautyArtists.Hubs;
using BeautyArtists.Models;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;

// Global culture configuration
var cultureInfo = new CultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServerOptionsAction: sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    }));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Production Identity setup requiring confirmed accounts
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ??? ? AUTHENTICATION & AUTHORIZATION (MOVED TO CORRECT PLACE) ???
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();


//builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>();

// ??? OTHER SERVICES ???
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));

builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddTransient<ICommunicationService, CommunicationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddHttpClient<IPaystackService, PaystackService>();
builder.Services.AddHttpClient();
builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        options.Conventions.AddAreaPageRoute("Identity", "/Account/RegisterChoice", "/Identity/Account/RegisterChoice");
        options.Conventions.AddAreaPageRoute("Identity", "/Account/RegisterClient", "/Identity/Account/RegisterClient");
        options.Conventions.AddAreaPageRoute("Identity", "/Account/RegisterArtist", "/Identity/Account/RegisterArtist");
    });
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = int.MaxValue;
});
var app = builder.Build();

// Seed Roles and Demo Users On Startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = { "Client", "Artist", "Admin" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // Seed Admin
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

    // Seed Demo Artist
    string artistEmail = "artist@example.com";
    string artistPassword = "Artist@123";

    var artistUser = await userManager.FindByEmailAsync(artistEmail);
    if (artistUser == null)
    {
        var newArtist = new ApplicationUser
        {
            UserName = artistEmail,
            Email = artistEmail,
            FirstName = "Test",
            LastName = "Artist",
            EmailConfirmed = true
        };

        var createArtistResult = await userManager.CreateAsync(newArtist, artistPassword);
        if (createArtistResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newArtist, "Artist");
        }
    }

    // Seed Demo Client
    string clientEmail = "client@example.com";
    string clientPassword = "Client@123";

    var clientUser = await userManager.FindByEmailAsync(clientEmail);
    if (clientUser == null)
    {
        var newClient = new ApplicationUser
        {
            UserName = clientEmail,
            Email = clientEmail,
            FirstName = "Test",
            LastName = "Client",
            EmailConfirmed = true
        };

        var createClientResult = await userManager.CreateAsync(newClient, clientPassword);
        if (createClientResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newClient, "Client");
        }
    }
}

// Configure HTTP request pipeline
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

//AUTHENTICATION & AUTHORIZATION (KEEP HERE)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
//app.MapHub<ChatHub>("/chatHub");

app.Run();
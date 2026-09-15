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
using Hangfire;
using Hangfire.SqlServer;

// Global culture configuration
var cultureInfo = new CultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

var builder = WebApplication.CreateBuilder(args);

// ??? DATABASE ???
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

// ??? IDENTITY ???
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// ??? SERVICES ???
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));

builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ICommunicationService, CommunicationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddHttpClient<IPaystackService, PaystackService>();
builder.Services.AddScoped<DepositReminderJob>();
builder.Services.AddScoped<DepositExpiryJob>();

// ??? HANGFIRE ???
builder.Services.AddHangfire(config =>
    config.UseSqlServerStorage(connectionString));

builder.Services.AddHangfireServer();

// ??? OTHER ???
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

// ??? SEED ROLES + ADMIN ONLY ???
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

    // ??? Seed Admin ONLY ???
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
}

// ??? HTTP PIPELINE ???
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

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// ??? SCHEDULE BACKGROUND JOBS ???
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    recurringJobManager.AddOrUpdate<DepositReminderJob>(
        "deposit-reminders",
        job => job.SendReminders(),
        Cron.Hourly);

    recurringJobManager.AddOrUpdate<DepositExpiryJob>(
        "deposit-expiry",
        job => job.CancelExpiredBookings(),
        Cron.Hourly);
}

app.Run();
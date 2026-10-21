using System.Security.Claims;
using System.Threading.RateLimiting;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Models;
using MickMouse.Web.Services;

if (args.Length > 0 && args[0] is "--export-sql" or "--export-dynamo")
{
    if (args.Length != 2) throw new ArgumentException("Pass one output path after the export command.");
    if (args[0] == "--export-sql") await SubmissionExports.WriteSqlAsync(args[1]);
    else await SubmissionExports.WriteDynamoAsync(args[1]);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var mode = builder.Configuration["App:Mode"] ?? "Local";
if (mode is not ("Local" or "Aws")) throw new InvalidOperationException("App:Mode must be Local or Aws.");
var isLocal = mode == "Local";
var dataDirectory = Path.GetFullPath(builder.Configuration["App:DataDirectory"] ?? "App_Data", builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MediaValidation.MaxBytes + 1048576);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MediaValidation.MaxBytes + 1048576);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Keep the default loopback trusted proxies for the local NGINX reverse proxy.
});

if (isLocal)
{
    var databasePath = Path.Combine(dataDirectory, "mick-mouse.db");
    var previousDatabasePath = Path.Combine(dataDirectory, "moss-and-mic.db");
    if (!File.Exists(databasePath) && File.Exists(previousDatabasePath)) databasePath = previousDatabasePath;
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
    builder.Services.AddSingleton<ICommentStore>(new LocalCommentStore(dataDirectory));
    builder.Services.AddSingleton<IMediaStore>(new LocalMediaStore(dataDirectory));
}
else
{
    var regionName = builder.Configuration["AWS:Region"] ?? throw new InvalidOperationException("Set AWS:Region.");
    var region = RegionEndpoint.GetBySystemName(regionName);
    using var parameterClient = new AmazonSimpleSystemsManagementClient(region);
    var parameterName = builder.Configuration["AWS:ConnectionParameter"] ?? throw new InvalidOperationException("Set AWS:ConnectionParameter.");
    var connectionString = await ParameterStoreSettings.LoadConnectionStringAsync(parameterClient, parameterName);
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString,
        sql => sql.EnableRetryOnFailure(3)));
    builder.Services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(region));
    builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(region));
    builder.Services.AddSingleton<ICommentStore>(services => new DynamoCommentStore(
        services.GetRequiredService<IAmazonDynamoDB>(), builder.Configuration["AWS:CommentsTable"] ?? throw new InvalidOperationException("Set AWS:CommentsTable.")));
    builder.Services.AddSingleton<IMediaStore>(services => new S3MediaStore(
        services.GetRequiredService<IAmazonS3>(), builder.Configuration["AWS:MediaBucket"] ?? throw new InvalidOperationException("Set AWS:MediaBucket.")));
}

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, RoleClaimsPrincipalFactory>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(options =>
{
    options.ApplicationCookie!.Configure(cookie =>
    {
        cookie.LoginPath = "/Account/Login"; cookie.AccessDeniedPath = "/Account/AccessDenied";
        cookie.Cookie.Name = "MickMouse.Account"; cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookie.ExpireTimeSpan = TimeSpan.FromHours(8); cookie.SlidingExpiration = true;
    });
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<EpisodeCleanup>();
builder.Services.AddScoped<AccountDeletion>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    void AddPolicy(string name, int permits) => options.AddPolicy(name, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    AddPolicy("auth", 30); AddPolicy("comments", 30); AddPolicy("plays", 120);
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler("/Home/Error");
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");
if (!isLocal && builder.Configuration.GetValue("App:RequireHttps", false))
{
    app.UseHsts(); app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; media-src 'self' https://*.amazonaws.com; script-src 'self'; style-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
// Validate stored account state for every authenticated request. A role change or
// disabled account must not retain privileges through an old cookie.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var manager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.GetUserAsync(context.User);
        var stampClaim = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>()
            .Value.ClaimsIdentity.SecurityStampClaimType;
        if (user is null || user.IsDisabled || !context.User.IsInRole(user.Role.ToString()) ||
            context.User.FindFirstValue(stampClaim) != user.SecurityStamp)
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }
    }
    await next();
});
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok", mode }));
app.MapControllerRoute("default", "{controller=Episodes}/{action=Index}/{id?}");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (isLocal)
    {
        await db.Database.EnsureCreatedAsync();
        var sampleSource = Path.Combine(app.Environment.WebRootPath, "media", "samples");
        var sampleDestination = Path.Combine(dataDirectory, "samples");
        Directory.CreateDirectory(sampleDestination);
        if (Directory.Exists(sampleSource))
            foreach (var file in Directory.EnumerateFiles(sampleSource))
                File.Copy(file, Path.Combine(sampleDestination, Path.GetFileName(file)), overwrite: true);
        if (builder.Configuration.GetValue("App:SeedDemo", true))
            await DemoSeed.InitializeAsync(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<ICommentStore>(), TimeProvider.System, seedComments: true);
    }
    else if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException("RDS cannot be reached. Apply the SQL script and check the Parameter Store value and security groups.");
}

await app.RunAsync();

public partial class Program { }

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;
using WorkFlowPro.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// UC-11: DbContext + SignalR Clients.User(userId) cần user id từ claim.
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();

builder.Services.AddRazorPages();
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddDbContext<WorkFlowProDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.User.RequireUniqueEmail = true;

    options.SignIn.RequireConfirmedEmail =
        builder.Configuration.GetValue<bool>("Auth:RequireEmailConfirmation");

    // UC-02 / SEC-03: 5 lần sai -> khóa 5 phút.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<WorkFlowProDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),

            NameClaimType = ClaimTypes.NameIdentifier
        };

        // SignalR websocket doesn't always include headers; allow token in querystring.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // FallbackPolicy: chỉ cookie → unauthenticated request redirect về /Login (302).
    // Không thêm JWT ở đây để tránh JWT challenge ghi đè redirect thành 401.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .Build();

    // DefaultPolicy ([Authorize] không có args): accept cả cookie lẫn JWT Bearer.
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(
            IdentityConstants.ApplicationScheme,
            JwtBearerDefaults.AuthenticationScheme)
        .Build();

    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("platform_role", "admin"));

    // UC-12: Only PM in current workspace can CRUD Projects.
    options.AddPolicy("IsPM", policy =>
        policy.Requirements.Add(new IsPmRequirement()));

    // UC-09: PM trong workspace hiện tại hoặc Platform Admin (DB / claim).
    options.AddPolicy("CanManageWorkspaceRoles", policy =>
        policy.Requirements.Add(new CanManageWorkspaceRolesRequirement()));
});

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ITaskHistoryService, TaskHistoryService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IWorkspaceOnboardingService, WorkspaceOnboardingService>();
builder.Services.AddScoped<ICurrentWorkspaceService, CurrentWorkspaceService>();
builder.Services.AddScoped<IUserWorkspaceService, UserWorkspaceService>();
builder.Services.AddScoped<IWorkspaceCreationService, WorkspaceCreationService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ITaskAssignmentService, TaskAssignmentService>();
builder.Services.AddScoped<IKanbanService, KanbanService>();
builder.Services.AddScoped<IKpiDashboardService, KpiDashboardService>();
builder.Services.AddScoped<IMemberProfileService, MemberProfileService>();
builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// UC-15/UC-02: bơm claim CurrentWorkspaceId/workspace_id theo request/user.
builder.Services.AddScoped<IClaimsTransformation, WorkspaceClaimsTransformation>();

builder.Services.AddScoped<IAuthorizationHandler, IsPmAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanManageWorkspaceRolesHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapHub<KanbanHub>("/hubs/kanban");
app.MapHub<TaskHub>("/hubs/task");
app.MapHub<NotificationHub>("/hubs/notification");

app.Run();

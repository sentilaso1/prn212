using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Extensions;
using WorkFlowPro.Services;

namespace WorkFlowPro.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly WorkFlowProDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwt;
    private readonly IConfiguration _config;

    public AuthController(
        WorkFlowProDbContext db,
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwt,
        IConfiguration config)
    {
        _db = db;
        _userManager = userManager;
        _jwt = jwt;
        _config = config;
    }

    public sealed record RegisterRequest(
        string Email,
        string Password,
        string? CompanyName,
        bool RequestPmWorkspace = false);

    public sealed record LoginRequest(
        string Email,
        string Password);

    public sealed record SwitchWorkspaceRequest(Guid WorkspaceId);

    public sealed record TokenResponse(
        string accessToken,
        Guid? activeWorkspaceId,
        IReadOnlyList<object> workspaces);

    [HttpPost("register")]
    public async Task<ActionResult<object>> Register([FromBody] RegisterRequest request)
    {
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Email/password is required.");

        var requireEmailConfirmation = _config.GetValue<bool>("Auth:RequireEmailConfirmation");

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null) return Conflict("Email already exists.");

        var isPm = request.RequestPmWorkspace;
        if (!isPm && !RegistrationEmailRules.IsGmailConsumerEmail(email))
            return BadRequest("Tài khoản thường (API) chỉ chấp nhận email @gmail.com.");

        if (isPm && string.IsNullOrWhiteSpace(request.CompanyName))
            return BadRequest("CompanyName is required when RequestPmWorkspace is true.");

        var display = string.IsNullOrWhiteSpace(request.CompanyName)
            ? email.Split('@')[0]
            : request.CompanyName.Trim();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = display,
            IsPlatformAdmin = false,
            EmailConfirmed = !requireEmailConfirmation,
            AccountStatus = isPm ? AccountStatus.PendingApproval : AccountStatus.Approved,
            AwaitingPmWorkspaceApproval = isPm,
            PendingWorkspaceName = isPm ? request.CompanyName?.Trim() : null
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(createResult.Errors.Select(e => e.Description));
        }

        if (isPm)
        {
            return Accepted(new
            {
                message = "PM registration pending platform admin approval.",
                userId = user.Id
            });
        }

        var token = _jwt.GenerateAccessToken(user, null);
        return Ok(new TokenResponse(token, null, Array.Empty<object>()));
    }

    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login([FromBody] LoginRequest request)
    {
        var email = request.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null) return Unauthorized("Invalid credentials.");

        var requireEmailConfirmation = _config.GetValue<bool>("Auth:RequireEmailConfirmation");
        if (requireEmailConfirmation && !user.EmailConfirmed)
            return Unauthorized("Email not confirmed.");

        var passwordOk = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordOk) return Unauthorized("Invalid credentials.");

        if (user.AccountStatus != AccountStatus.Approved)
            return Unauthorized("Account is not approved.");

        var workspaceIds = await _db.WorkspaceMembers
            .Where(m => m.UserId == user.Id)
            .Select(m => m.WorkspaceId)
            .Distinct()
            .ToListAsync();

        var workspaces = await _db.Workspaces
            .Where(w => workspaceIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name })
            .ToListAsync();

        Guid? activeWorkspaceId = workspaces.Count > 0 ? workspaces[0].Id : null;
        var token = _jwt.GenerateAccessToken(user, activeWorkspaceId);

        return Ok(new TokenResponse(token, activeWorkspaceId, workspaces.Cast<object>().ToList()));
    }

    [Authorize]
    [HttpPost("switch-workspace")]
    public async Task<ActionResult<TokenResponse>> SwitchWorkspace([FromBody] SwitchWorkspaceRequest request)
    {
        var userId = User.GetUserId();
        var tokenUser = await _userManager.FindByIdAsync(userId);
        if (tokenUser is null) return Unauthorized();

        var isMember = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == request.WorkspaceId);

        if (!isMember) return Forbid();

        var token = _jwt.GenerateAccessToken(tokenUser, request.WorkspaceId);

        var workspaces = await _db.WorkspaceMembers
            .Where(m => m.UserId == userId)
            .Join(_db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { w.Id, w.Name })
            .ToListAsync();

        return Ok(new TokenResponse(
            accessToken: token,
            activeWorkspaceId: request.WorkspaceId,
            workspaces: workspaces.Cast<object>().ToList()));
    }

    // Token hashing helper for reset/invite flows (RB-10: store only hash)
    private static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static string GenerateRandomToken(int bytes = 32)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer);
    }
}


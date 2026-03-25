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
        string CompanyName);

    public sealed record LoginRequest(
        string Email,
        string Password);

    public sealed record SwitchWorkspaceRequest(Guid WorkspaceId);

    public sealed record TokenResponse(
        string accessToken,
        Guid activeWorkspaceId,
        IReadOnlyList<object> workspaces);

    [HttpPost("register")]
    public async Task<ActionResult<TokenResponse>> Register([FromBody] RegisterRequest request)
    {
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Email/password is required.");

        var requireEmailConfirmation = _config.GetValue<bool>("Auth:RequireEmailConfirmation");

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null) return Conflict("Email already exists.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.CompanyName,
            IsPlatformAdmin = false,
            EmailConfirmed = !requireEmailConfirmation
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(createResult.Errors.Select(e => e.Description));
        }

        var workspace = new Workspace
        {
            Name = request.CompanyName,
            Description = null
        };

        var profile = new MemberProfile
        {
            UserId = user.Id,
            Level = MemberLevel.Junior
        };

        var membership = new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = WorkspaceMemberRole.PM,
            SubRole = "PM"
        };

        _db.Workspaces.Add(workspace);
        _db.MemberProfiles.Add(profile);
        _db.WorkspaceMembers.Add(membership);
        await _db.SaveChangesAsync();

        var token = _jwt.GenerateAccessToken(user, workspace.Id);
        var workspaces = new[]
        {
            new { workspace.Id, workspace.Name }
        }.Cast<object>().ToList();

        return Ok(new TokenResponse(token, workspace.Id, workspaces));
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

        var workspaceIds = await _db.WorkspaceMembers
            .Where(m => m.UserId == user.Id)
            .Select(m => m.WorkspaceId)
            .Distinct()
            .ToListAsync();

        if (workspaceIds.Count == 0) return Forbid("User has no workspace.");

        var workspaces = await _db.Workspaces
            .Where(w => workspaceIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name })
            .ToListAsync();

        var activeWorkspaceId = workspaces[0].Id;
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


using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public sealed class InvitationService : IInvitationService
{
    private static readonly string[] AllowedSubRoles = ["BA", "DEV", "Designer", "QA"];
    private readonly WorkFlowProDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvitationService> _logger;
    private readonly INotificationService _notifications;

    public InvitationService(
        WorkFlowProDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<InvitationService> logger,
        INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _logger = logger;
        _notifications = notifications;
    }

    public async Task<InviteMembersResult> InviteMembersAsync(
        Guid workspaceId,
        string emailsRaw,
        WorkspaceMemberRole role,
        string? subRole,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (workspaceId == Guid.Empty)
        {
            errors.Add("Workspace không hợp lệ.");
            return new InviteMembersResult { Errors = errors };
        }

        if (!IsAllowedRole(role))
        {
            errors.Add("Role không hợp lệ.");
            return new InviteMembersResult { Errors = errors };
        }

        var normalizedSubRole = string.IsNullOrWhiteSpace(subRole) ? null : subRole.Trim();

        if (string.IsNullOrWhiteSpace(normalizedSubRole))
            errors.Add("SubRole là bắt buộc.");
        else if (!IsAllowedSubRole(normalizedSubRole))
            errors.Add("SubRole không hợp lệ. Chỉ chấp nhận: BA, DEV, Designer, QA.");

        var emails = ParseEmails(emailsRaw)
            .Select(NormalizeEmail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (emails.Count == 0)
            errors.Add("Vui lòng nhập ít nhất 1 email.");

        foreach (var email in emails)
        {
            if (!IsValidEmail(email))
            {
                errors.Add($"Email '{email}' không hợp lệ.");
                continue;
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                var isAlreadyMember = await _db.WorkspaceMembers.AnyAsync(
                    m => m.WorkspaceId == workspaceId && m.UserId == existingUser.Id,
                    cancellationToken);

                if (isAlreadyMember)
                {
                    errors.Add($"Email '{email}' đã là thành viên của workspace này.");
                    continue;
                }
            }
        }

        if (errors.Count > 0)
            return new InviteMembersResult { Errors = errors };

        var workspace = await _db.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        var now = DateTime.UtcNow;
        var workspaceName = workspace?.Name ?? "workspace";
        var roleLabel = role == WorkspaceMemberRole.PM ? "PM" : "Member";
        var debugAcceptLinks = new List<string>();

        foreach (var email in emails)
        {
            var tokenPlain = GenerateTokenPlain();
            var tokenHash = ComputeSha256Hex(tokenPlain);
            var acceptPath = $"/Invite/Accept?token={Uri.EscapeDataString(tokenPlain)}";

            var invite = new WorkspaceInviteToken
            {
                WorkspaceId = workspaceId,
                Email = email,
                TokenHash = tokenHash,
                Role = role,
                SubRole = normalizedSubRole,
                Status = InviteStatus.Pending,
                AcceptUrl = acceptPath,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7)
            };

            _db.WorkspaceInviteTokens.Add(invite);
            await _db.SaveChangesAsync(cancellationToken);

            debugAcceptLinks.Add($"{email} => {acceptPath}");

            var subRoleSuffix = string.IsNullOrWhiteSpace(normalizedSubRole)
                ? string.Empty
                : $" (SubRole: {normalizedSubRole})";

            var notifMessage =
                $"Bạn được mời vào workspace \"{workspaceName}\". Vai trò: {roleLabel}.{subRoleSuffix}";

            var inviteeUser = await _userManager.FindByEmailAsync(email);
            if (inviteeUser is not null)
            {
                await _notifications.CreateAndPushAsync(
                    inviteeUser.Id,
                    NotificationType.WorkspaceInvite,
                    notifMessage,
                    workspaceId: null,
                    redirectUrl: acceptPath,
                    cancellationToken: cancellationToken);
            }
        }

        return new InviteMembersResult
        {
            Errors = Array.Empty<string>(),
            IsDryRun = false,
            DebugAcceptLinks = debugAcceptLinks
        };
    }

    public async Task<InviteInfoResult?> GetInviteInfoAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var tokenHash = ComputeSha256Hex(token);

        var invite = await _db.WorkspaceInviteTokens
            .Include(t => t.Workspace)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (invite is null)
            return null;

        return new InviteInfoResult
        {
            WorkspaceName = invite.Workspace?.Name ?? "Workspace",
            Email = invite.Email,
            Role = invite.Role,
            SubRole = invite.SubRole,
            Status = invite.Status
        };
    }

    public async Task<AcceptInviteResult> AcceptInviteAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new AcceptInviteResult { Success = false, ErrorMessage = "Token không hợp lệ." };

        var tokenHash = ComputeSha256Hex(token);

        var invitation = await _db.WorkspaceInviteTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (invitation is null)
            return new AcceptInviteResult { Success = false, ErrorMessage = "Token không tồn tại hoặc đã sai." };

        var now = DateTime.UtcNow;

        if (invitation.UsedAtUtc is not null)
            return new AcceptInviteResult { Success = false, ErrorMessage = "Lời mời này đã được xử lý." };

        if (invitation.ExpiresAtUtc <= now)
            return new AcceptInviteResult { Success = false, ErrorMessage = "Lời mời đã hết hạn." };

        var workspaceId = invitation.WorkspaceId;
        var normalizedEmail = NormalizeEmail(invitation.Email);

        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            var password = GenerateRandomInvitePassword();
            user = new ApplicationUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                DisplayName = normalizedEmail,
                EmailConfirmed = true,
            };

            var createRes = await _userManager.CreateAsync(user, password);
            if (!createRes.Succeeded)
            {
                var msg = createRes.Errors.FirstOrDefault()?.Description ?? "Tạo người dùng thất bại.";
                return new AcceptInviteResult { Success = false, ErrorMessage = msg };
            }
        }

        var member = await _db.WorkspaceMembers.FirstOrDefaultAsync(
            m => m.WorkspaceId == workspaceId && m.UserId == user.Id,
            cancellationToken);

        if (member is null)
        {
            member = new WorkspaceMember
            {
                WorkspaceId = workspaceId,
                UserId = user.Id,
                Role = invitation.Role,
                SubRole = invitation.SubRole,
                JoinedAtUtc = now
            };
            _db.WorkspaceMembers.Add(member);
        }
        else
        {
            member.Role = invitation.Role;
            member.SubRole = invitation.SubRole;
        }

        var profile = await _db.MemberProfiles.FirstOrDefaultAsync(
            p => p.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            profile = new MemberProfile { UserId = user.Id };
            _db.MemberProfiles.Add(profile);
        }

        invitation.UsedAtUtc = now;
        invitation.Status = InviteStatus.Accepted;

        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var wsName = await _db.Workspaces.AsNoTracking()
                .Where(w => w.Id == workspaceId)
                .Select(w => w.Name)
                .FirstOrDefaultAsync(cancellationToken);

            var pmIds = await _db.WorkspaceMembers.AsNoTracking()
                .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);

            var who = user.DisplayName ?? user.Email ?? user.Id;
            foreach (var pmId in pmIds)
            {
                if (pmId == user.Id) continue;

                await _notifications.CreateAndPushAsync(
                    pmId,
                    NotificationType.InviteAccepted,
                    $"{who} đã chấp nhận lời mời vào workspace \"{wsName ?? ""}\".",
                    workspaceId: workspaceId,
                    redirectUrl: "/Invite/Sent",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invite accepted notification failed for workspace {Ws}", workspaceId);
        }

        var http = _httpContextAccessor.HttpContext;
        if (http is not null)
        {
            if (http.User?.Identity?.IsAuthenticated == true)
                await http.SignOutAsync(IdentityConstants.ApplicationScheme);

            var principal = await _signInManager.CreateUserPrincipalAsync(user);
            if (principal.Identity is ClaimsIdentity identity)
            {
                var wsId = workspaceId.ToString("D");
                if (!identity.HasClaim("CurrentWorkspaceId", wsId))
                    identity.AddClaim(new Claim("CurrentWorkspaceId", wsId));
                if (!identity.HasClaim("workspace_id", wsId))
                    identity.AddClaim(new Claim("workspace_id", wsId));
            }

            await http.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = false });
        }

        return new AcceptInviteResult { Success = true, WorkspaceId = workspaceId };
    }

    public async Task<RejectInviteResult> RejectInviteAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new RejectInviteResult { Success = false, ErrorMessage = "Token không hợp lệ." };

        var tokenHash = ComputeSha256Hex(token);

        var invitation = await _db.WorkspaceInviteTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (invitation is null)
            return new RejectInviteResult { Success = false, ErrorMessage = "Token không tồn tại." };

        if (invitation.UsedAtUtc is not null)
            return new RejectInviteResult { Success = false, ErrorMessage = "Lời mời này đã được xử lý." };

        if (invitation.ExpiresAtUtc <= DateTime.UtcNow)
            return new RejectInviteResult { Success = false, ErrorMessage = "Lời mời đã hết hạn." };

        invitation.Status = InviteStatus.Rejected;
        invitation.UsedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var wsName = await _db.Workspaces.AsNoTracking()
                .Where(w => w.Id == invitation.WorkspaceId)
                .Select(w => w.Name)
                .FirstOrDefaultAsync(cancellationToken);

            var pmIds = await _db.WorkspaceMembers.AsNoTracking()
                .Where(m => m.WorkspaceId == invitation.WorkspaceId && m.Role == WorkspaceMemberRole.PM)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);

            foreach (var pmId in pmIds)
            {
                await _notifications.CreateAndPushAsync(
                    pmId,
                    NotificationType.InviteRejected,
                    $"{invitation.Email} đã từ chối lời mời vào workspace \"{wsName ?? ""}\".",
                    workspaceId: invitation.WorkspaceId,
                    redirectUrl: "/Invite/Sent",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invite rejected notification failed for workspace {Ws}", invitation.WorkspaceId);
        }

        return new RejectInviteResult { Success = true };
    }

    private static bool IsAllowedRole(WorkspaceMemberRole role) =>
        role == WorkspaceMemberRole.Member || role == WorkspaceMemberRole.PM;

    private static bool IsAllowedSubRole(string subRole) =>
        AllowedSubRoles.Contains(subRole, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseEmails(string emailsRaw)
    {
        if (string.IsNullOrWhiteSpace(emailsRaw))
            return Array.Empty<string>();

        return emailsRaw
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email)
    {
        var validator = new EmailAddressAttribute();
        return validator.IsValid(email);
    }

    private static string GenerateTokenPlain()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateRandomInvitePassword()
    {
        var digits = RandomNumberGenerator.GetInt32(1000, 9999);
        var tail = Guid.NewGuid().ToString("N")[..10];
        return $"WpA{digits}{tail}";
    }

    private static string ComputeSha256Hex(string tokenPlain)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tokenPlain));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

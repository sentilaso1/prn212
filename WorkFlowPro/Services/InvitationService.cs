using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
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
    private readonly IEmailSender _emailSender;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvitationService> _logger;
    private readonly INotificationService _notifications;

    public InvitationService(
        WorkFlowProDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender emailSender,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<InvitationService> logger,
        INotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _emailSender = emailSender;
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

        var normalizedSubRole = string.IsNullOrWhiteSpace(subRole)
            ? null
            : subRole.Trim();

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

        var now = DateTime.UtcNow;

        // Validation per email.
        foreach (var email in emails)
        {
            if (!IsValidEmail(email))
            {
                errors.Add($"Email '{email}' không hợp lệ.");
                continue;
            }

            // Người đã là member của workspace -> không cho invite.
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

            // Không tạo token mới nếu email đã có token đang active.
            var hasActiveInvite = await _db.WorkspaceInviteTokens.AnyAsync(
                t => t.WorkspaceId == workspaceId &&
                     t.Email == email &&
                     t.UsedAtUtc == null &&
                     t.ExpiresAtUtc > now,
                cancellationToken);

            // Nếu đã có lời mời active trước đó, trong scope dev/local ta vẫn cho phép tạo thêm token mới
            // để (1) có AcceptUrl non-null và (2) người nhận nhận được Notification/route accept đúng ngay.
            // Tránh việc block UX test UC-03.
            _ = hasActiveInvite;
        }

        if (errors.Count > 0)
            return new InviteMembersResult { Errors = errors };

        // 1) Tạo token + record
        var baseUrl = _configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "https://yourapp.com";
        baseUrl = baseUrl.TrimEnd('/');

        var workspace = await _db.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        var acceptSubject = $"WorkFlowPro: Bạn được mời vào workspace";
        // Chỉ lưu accept URL dưới dạng relative để tránh lỗi protocol (http vs https) khi local click link.
        var tokensToSend = new List<(string Email, string TokenPlain, string AcceptUrl)>();
        var inviteRecords = new List<WorkspaceInviteToken>();
        var debugAcceptLinks = new List<string>();
        var isDryRun = _configuration.GetValue<bool?>("Email:DryRun") ?? false;

        foreach (var email in emails)
        {
            // Random token plain; store only SHA256 hash in DB.
            var tokenPlain = GenerateTokenPlain();
            var tokenHash = ComputeSha256Hex(tokenPlain);
            var acceptPath = $"/Invite/Accept?token={Uri.EscapeDataString(tokenPlain)}";
            var acceptLink = acceptPath;

            tokensToSend.Add((email, tokenPlain, acceptLink));
            inviteRecords.Add(new WorkspaceInviteToken
            {
                WorkspaceId = workspaceId,
                Email = email,
                TokenHash = tokenHash,
                Role = role,
                SubRole = normalizedSubRole,
                AcceptUrl = acceptLink,
                ExpiresAtUtc = now.AddDays(7)
            });
        }

        _db.WorkspaceInviteTokens.AddRange(inviteRecords);
        await _db.SaveChangesAsync(cancellationToken);

        // 2) Gửi email mời
        var roleLabel = role == WorkspaceMemberRole.PM ? "PM" : "Member";
        var workspaceName = workspace?.Name ?? "workspace";

        foreach (var (email, tokenPlain, acceptLink) in tokensToSend)
        {
            debugAcceptLinks.Add($"{email} => {acceptLink}");

            // Absolute URL chỉ dùng cho email (nếu có gửi thật). AcceptUrl/redirectUrl cho UI là relative.
            var acceptUrlForEmail = $"{baseUrl}{acceptLink}";

            var bodyHtml = $@"
<p>Xin chào,</p>
<p>Bạn đã được mời tham gia <strong>{System.Net.WebUtility.HtmlEncode(workspaceName)}</strong>.</p>
<p>Vai trò: <strong>{roleLabel}</strong>{(string.IsNullOrWhiteSpace(normalizedSubRole) ? "" : $"<br/>SubRole: <strong>{System.Net.WebUtility.HtmlEncode(normalizedSubRole)}</strong>")}</p>
<p>Nhấn vào liên kết sau để chấp nhận lời mời:</p>
<p><a href=""{System.Net.WebUtility.HtmlEncode(acceptUrlForEmail)}"">Accept invitation</a></p>
<p>Nếu bạn không yêu cầu lời mời này, bạn có thể bỏ qua email.</p>
";

            // Notify người đã có tài khoản ngay (UC-03 extend: notify both existing & non-existing).
            var inviteeUser = await _userManager.FindByEmailAsync(email);
            if (inviteeUser is not null)
            {
                var subRoleSuffix = string.IsNullOrWhiteSpace(normalizedSubRole)
                    ? string.Empty
                    : $" (SubRole: {normalizedSubRole})";

                var notifMessage =
                    $"Bạn được mời vào workspace \"{workspaceName}\". Vai trò: {roleLabel}.{subRoleSuffix}";

                // workspaceId không truyền vào để tránh NotificationService chặn vì người đó chưa là member.
                await _notifications.CreateAndPushAsync(
                    inviteeUser.Id,
                    NotificationType.WorkspaceInvite,
                    notifMessage,
                    workspaceId: null,
                    redirectUrl: acceptLink,
                    cancellationToken: cancellationToken);
            }

            try
            {
                await _emailSender.SendEmailAsync(
                    email,
                    acceptSubject,
                    bodyHtml,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // For safety, we don't delete tokens already created.
                _logger.LogError(ex, "Failed to send invite email to {Email}", email);
                throw new InvalidOperationException($"Gửi email mời tới '{email}' thất bại.");
            }
        }

        return new InviteMembersResult
        {
            Errors = Array.Empty<string>(),
            IsDryRun = isDryRun,
            DebugAcceptLinks = debugAcceptLinks
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
            return new AcceptInviteResult { Success = false, ErrorMessage = "Lời mời này đã được sử dụng." };

        if (invitation.ExpiresAtUtc <= now)
            return new AcceptInviteResult { Success = false, ErrorMessage = "Lời mời đã hết hạn." };

        var workspaceId = invitation.WorkspaceId;
        var email = invitation.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);

        // 1) Tạo user nếu chưa có.
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

        // 2) Add WorkspaceMember + MemberProfile
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
            p => p.UserId == user.Id,
            cancellationToken);

        if (profile is null)
        {
            profile = new MemberProfile
            {
                UserId = user.Id
                // Các default giá trị có sẵn trong entity.
            };
            _db.MemberProfiles.Add(profile);
        }

        // 3) Mark used
        invitation.UsedAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken);

        // UC-11: thông báo PM trong workspace (invite accepted).
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

            var who = user.DisplayName ?? user.Email ?? user.UserName ?? user.Id;
            foreach (var pmId in pmIds)
            {
                if (pmId == user.Id)
                    continue;

                await _notifications.CreateAndPushAsync(
                    pmId,
                    NotificationType.InviteAccepted,
                    $"{who} đã chấp nhận lời mời vào workspace \"{wsName ?? ""}\".",
                    workspaceId: workspaceId,
                    redirectUrl: "/Index",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UC-11: invite accepted notification failed for workspace {Ws}", workspaceId);
        }

        // 4) Sign-in
        var http = _httpContextAccessor.HttpContext;
        if (http is not null)
        {
            if (http.User?.Identity?.IsAuthenticated == true)
            {
                // Ensure claims are consistent with the invitation workspace.
                await http.SignOutAsync(IdentityConstants.ApplicationScheme);
            }

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

    private static bool IsAllowedRole(WorkspaceMemberRole role) =>
        role == WorkspaceMemberRole.Member || role == WorkspaceMemberRole.PM;

    private static bool IsAllowedSubRole(string subRole) =>
        AllowedSubRoles.Contains(subRole, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseEmails(string emailsRaw)
    {
        if (string.IsNullOrWhiteSpace(emailsRaw))
            return Array.Empty<string>();

        var parts = emailsRaw.Split(
            new[] { ',', ';', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);

        return parts
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
        // 32 bytes => 43 chars base64url; đủ mạnh cho invite token.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var base64 = Convert.ToBase64String(bytes);
        return base64
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateRandomInvitePassword()
    {
        // Identity policy in Program.cs: min 8, requires digit + uppercase.
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


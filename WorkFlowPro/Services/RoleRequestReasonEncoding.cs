using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

/// <summary>Gắn SubRole đề xuất vào Reason (UC-03 PM → Admin) để khi duyệt áp dụng vào WorkspaceMember.</summary>
public static class RoleRequestReasonEncoding
{
    private const string Prefix = "[ProposedSubRole:";

    public static string Encode(string? proposedSubRole, string? reason)
    {
        string? sr = null;
        if (WorkspacePolicies.IsAllowedMemberSubRole(proposedSubRole))
            sr = proposedSubRole!.Trim();

        var r = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (sr == null)
            return r ?? "";

        return $"{Prefix}{sr}]" + (r == null ? "" : " " + r);
    }

    public static bool TryDecodeProposedSubRole(string? storedReason, out string? subRole)
    {
        subRole = null;
        if (string.IsNullOrEmpty(storedReason) || !storedReason.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var close = storedReason.IndexOf(']', Prefix.Length);
        if (close <= Prefix.Length)
            return false;

        var val = storedReason.Substring(Prefix.Length, close - Prefix.Length).Trim();
        if (!WorkspacePolicies.IsAllowedMemberSubRole(val))
            return false;

        subRole = AllowedMemberSubRolesCanonical(val);
        return true;
    }

    private static string AllowedMemberSubRolesCanonical(string val)
    {
        foreach (var a in WorkspacePolicies.AllowedMemberSubRoles)
        {
            if (string.Equals(a, val, StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return val;
    }
}

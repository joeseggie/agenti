using System.Text.Json.Serialization;

namespace EastSeat.Agenti.Android.Models;

// ─── Auth ────────────────────────────────────────────────────────────────────

public class LoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public long? AgentId { get; set; }

    [JsonPropertyName("branchId")]
    public long? BranchId { get; set; }
}

// ─── API envelope ────────────────────────────────────────────────────────────

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

// ─── Dashboard ───────────────────────────────────────────────────────────────

public class DashboardViewModel
{
    [JsonPropertyName("wallets")]
    public List<WalletBalanceSummary> Wallets { get; set; } = [];

    [JsonPropertyName("agentSummaries")]
    public List<AgentWalletSummary> AgentSummaries { get; set; } = [];

    [JsonPropertyName("sessionStatus")]
    public SessionStatus SessionStatus { get; set; } = new();

    [JsonPropertyName("totalBalance")]
    public decimal TotalBalance { get; set; }

    [JsonPropertyName("totalAgentBalance")]
    public decimal TotalAgentBalance { get; set; }

    [JsonPropertyName("vaultBalance")]
    public decimal VaultBalance { get; set; }

    [JsonPropertyName("vaultAgentDifference")]
    public decimal VaultAgentDifference { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "UGX";

    [JsonPropertyName("hasWallets")]
    public bool HasWallets { get; set; }

    [JsonPropertyName("hasAgents")]
    public bool HasAgents { get; set; }
}

public class WalletBalanceSummary
{
    [JsonPropertyName("walletId")]
    public long WalletId { get; set; }

    [JsonPropertyName("walletName")]
    public string WalletName { get; set; } = string.Empty;

    [JsonPropertyName("walletTypeName")]
    public string WalletTypeName { get; set; } = string.Empty;

    [JsonPropertyName("balance")]
    public decimal Balance { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "UGX";
}

public class SessionStatus
{
    [JsonPropertyName("sessionId")]
    public long? SessionId { get; set; }

    [JsonPropertyName("sessionDate")]
    public DateOnly? SessionDate { get; set; }

    [JsonPropertyName("status")]
    public CashSessionStatus? Status { get; set; }

    [JsonPropertyName("openedAt")]
    public DateTimeOffset? OpenedAt { get; set; }

    [JsonPropertyName("hasActiveSession")]
    public bool HasActiveSession { get; set; }

    [JsonPropertyName("statusDisplay")]
    public string StatusDisplay { get; set; } = "No Session";

    [JsonPropertyName("statusColor")]
    public string StatusColor { get; set; } = "info";
}

public class AgentWalletSummary
{
    [JsonPropertyName("agentId")]
    public long AgentId { get; set; }

    [JsonPropertyName("agentCode")]
    public string AgentCode { get; set; } = string.Empty;

    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = string.Empty;

    [JsonPropertyName("totalBalance")]
    public decimal TotalBalance { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "UGX";
}

// ─── Agents ──────────────────────────────────────────────────────────────────

public class AgentListItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("walletCount")]
    public int WalletCount { get; set; }

    [JsonPropertyName("totalBalance")]
    public decimal TotalBalance { get; set; }
}

public class AgentDetail
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("wallets")]
    public List<AgentWallet> Wallets { get; set; } = [];
}

public class AgentWallet
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("walletTypeName")]
    public string WalletTypeName { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "UGX";

    [JsonPropertyName("balance")]
    public decimal Balance { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

// ─── Cash Sessions ───────────────────────────────────────────────────────────

public class CashSessionListItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("sessionDate")]
    public DateOnly SessionDate { get; set; }

    [JsonPropertyName("status")]
    public CashSessionStatus Status { get; set; }

    [JsonPropertyName("openedAt")]
    public DateTimeOffset OpenedAt { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTimeOffset? ClosedAt { get; set; }

    [JsonPropertyName("agentCount")]
    public int AgentCount { get; set; }

    [JsonPropertyName("approvedClosingCount")]
    public int ApprovedClosingCount { get; set; }

    [JsonPropertyName("totalOpeningAmount")]
    public decimal TotalOpeningAmount { get; set; }

    [JsonPropertyName("totalClosingAmount")]
    public decimal? TotalClosingAmount { get; set; }

    [JsonPropertyName("allClosingCountsApproved")]
    public bool AllClosingCountsApproved { get; set; }

    [JsonPropertyName("pendingApprovalCount")]
    public int PendingApprovalCount { get; set; }

    public string StatusDisplay => Status switch
    {
        CashSessionStatus.Open => "Open",
        CashSessionStatus.Closed => "Closed",
        CashSessionStatus.Pending => "Pending",
        CashSessionStatus.DiscrepancyUnderReview => "Under Review",
        CashSessionStatus.Completed => "Completed",
        CashSessionStatus.Blocked => "Blocked",
        _ => "Unknown"
    };
}

// ─── Cash Counts ─────────────────────────────────────────────────────────────

public class CurrentSessionInfo
{
    [JsonPropertyName("sessionId")]
    public long? SessionId { get; set; }

    [JsonPropertyName("sessionDate")]
    public DateOnly? SessionDate { get; set; }

    [JsonPropertyName("statusText")]
    public string StatusText { get; set; } = "No Active Session";

    [JsonPropertyName("statusColor")]
    public string StatusColor { get; set; } = "warning";

    [JsonPropertyName("canPerformOpeningCount")]
    public bool CanPerformOpeningCount { get; set; }

    [JsonPropertyName("canPerformClosingCount")]
    public bool CanPerformClosingCount { get; set; }

    [JsonPropertyName("hasOpeningCount")]
    public bool HasOpeningCount { get; set; }

    [JsonPropertyName("hasClosingCount")]
    public bool HasClosingCount { get; set; }
}

public class CashCountForm
{
    [JsonPropertyName("cashCountId")]
    public long? CashCountId { get; set; }

    [JsonPropertyName("cashSessionId")]
    public long? CashSessionId { get; set; }

    [JsonPropertyName("isOpening")]
    public bool IsOpening { get; set; }

    [JsonPropertyName("walletEntries")]
    public List<WalletCountEntry> WalletEntries { get; set; } = [];

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("totalExpected")]
    public decimal TotalExpected { get; set; }
}

public class WalletCountEntry
{
    [JsonPropertyName("walletId")]
    public long WalletId { get; set; }

    [JsonPropertyName("walletName")]
    public string WalletName { get; set; } = string.Empty;

    [JsonPropertyName("walletTypeName")]
    public string WalletTypeName { get; set; } = string.Empty;

    [JsonPropertyName("expectedBalance")]
    public decimal ExpectedBalance { get; set; }

    [JsonPropertyName("countedAmount")]
    public decimal CountedAmount { get; set; }
}

public class CashCountResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("cashCountId")]
    public long? CashCountId { get; set; }

    [JsonPropertyName("cashSessionId")]
    public long? CashSessionId { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ─── Vault ───────────────────────────────────────────────────────────────────

public class VaultInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("branchId")]
    public long BranchId { get; set; }

    [JsonPropertyName("branchName")]
    public string BranchName { get; set; } = string.Empty;

    [JsonPropertyName("currentBalance")]
    public decimal CurrentBalance { get; set; }
}

public class VaultTransactionItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("balanceAfter")]
    public decimal? BalanceAfter { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public enum CashSessionStatus
{
    Closed = 0,
    Open = 1,
    Pending = 2,
    DiscrepancyUnderReview = 3,
    Completed = 4,
    Blocked = 5
}

-- Apply with psql -v database_name=agenti_prod -v mcp_role=agenti_mcp_readonly -f mcp-readonly-grants.sql
-- Use a NEW dedicated non-superuser login, not the application or migration login.
GRANT CONNECT ON DATABASE :"database_name" TO :"mcp_role";
GRANT USAGE ON SCHEMA public TO :"mcp_role";
GRANT SELECT ON TABLE
    public."McpAccessTokens", public."Branches", public."Agents",
    public."Wallets", public."WalletTypes", public."CashSessions",
    public."CashCounts", public."Discrepancies", public."Transactions",
    public."Vaults", public."VaultTransactions"
TO :"mcp_role";
GRANT SELECT (
    "Id", "FirstName", "LastName", "SecurityStamp", "IsActive", "IsDeleted",
    "Role", "BranchId", "LockoutEnabled", "LockoutEnd"
) ON public."AspNetUsers" TO :"mcp_role";

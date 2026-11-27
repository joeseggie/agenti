# GitHub Copilot Instructions for Agenti

Agenti is a banking agency ERP for Uganda built with ASP.NET Core Blazor Server
(.NET 10), .NET MAUI Android, PostgreSQL 16, Entity Framework Core, MudBlazor,
and ASP.NET Core Identity. The solution also contains a read-only MCP server.

Use [`AGENTS.md`](../AGENTS.md) for detailed coding, architecture, security, and
testing rules. Use [`CLAUDE.md`](../CLAUDE.md) for project structure, setup,
commands, and deployment guidance. Keep this file as a concise summary of the
rules Copilot must not miss.

## Architecture

- Organize web features as vertical slices in
  `EastSeat.Agenti.Web/Features/{FeatureName}/`.
- Feature services combine business logic and data access; inject
  `ApplicationDbContext` directly. Do not add a repository layer.
- Register DbContext-backed services as `Scoped` in `Program.cs`.
- Return DTOs to UI and API callers; do not expose EF entities.
- Keep authorization and business rules in services/endpoints, not UI-only
  checks. Blazor pages use the appropriate authorization policy.
- The Android app uses the web backend's JWT-authenticated `/api` endpoints.
  Preserve unrelated Android banking APIs when changing web features.

## Data and banking rules

- Money uses `decimal` with database precision `18,2`; timestamps use
  `DateTimeOffset`. Persist enums as strings and use explicit foreign keys.
- Use `IBusinessClock.Today` for business dates; do not use server-local
  `DateTime.Today`.
- Use transactions for multi-entity financial changes and validate uniqueness
  before inserts. Use soft deletion (`IsActive` for domain entities and
  `IsDeleted`/`DeletedAt` for users) where applicable.
- Audit and financial history is append-only. Never update or delete
  `VaultTransaction`, `AuditLog`, `Transaction`, or `UserAuditLog`.
- Preserve vault controls: creator cannot approve their own adjustment, notes
  are required (minimum 10 characters), and vault operations retain their
  concurrency protections.

## Bank statements and reconciliation

- New statement imports use the shipped, versioned universal CSV schema.
  Do not restore bank-specific executable parsers or Admin-editable schemas.
- Admin uploads complete, non-empty single-day statements and reconciles all Bank
  wallets together once. Cash/Mobile Money require no statements. Admin may waive statements per Bank wallet type (audited; shown as "Not reconciled").
- Compare approved opening plus credits minus debits with submitted closing and
  statement anchors. This is daily arithmetic, not matching or exact-cutoff
  certification; never fabricate times.
- Preserve immutable revisions/results and read-only legacy archives. No
  matching/classification, baseline wizard, historical correction grant or rerun.
- Approval is separate. Supervisors may ordinarily approve Success; only Admin
  may exception-approve Failed with acknowledgement and a reason. Failed remains
  Failed. Missing prerequisites cannot be overridden.
- Outstanding review does not block next-day opening or replay settlement.
  Preserve session, opening-approval and vault safeguards. Do not add Android,
  API or MCP reconciliation workflows. See `docs/closing-reconciliation-business-process.md`.

## Security and MCP

- Apply the existing authorization policies: `VaultView`,
  `VaultAccess`/`VaultAdjust`, `VaultApprove`, and `UserManagement`; check
  `Program.cs` and the feature for the required roles and authentication schemes.
- `EastSeat.Agenti.Mcp` is HTTP-only and read-only. Use `ReadOnlyDbContext` and
  scoped `McpCaller.ResolveBranch`: Admin all branches, Supervisor assigned
  branch only, Agents denied. Cap lists at `McpServerConfig.MaxRows`. Token
  issuance/revocation belongs only in the cookie-authenticated Web feature.
- Never commit credentials, secrets, production data, or real bank statements.

## Testing and commands

- Add focused xUnit tests for service behavior, including success, validation,
  duplicate, and edge cases. Use in-memory SQLite for unit tests.
- Use integration tests for database, concurrency, and isolation behavior;
  use E2E tests for user workflows. Run the smallest relevant test project, then
  broaden validation when needed.
- Common commands: `dotnet build` and `dotnet test`. See `CLAUDE.md` for
  project-specific test commands and prerequisites.

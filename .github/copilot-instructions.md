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
- Import, matching/classification, and Admin responses are Web Admin workflows.
  Supervisors may read and flag; do not add Android statement-import pages or
  routes.
- Date-only statements support transaction-date daily control, not exact
  session-cutoff certification. Never invent statement times or bypass the
  evaluator's cutoff guard.
- Preserve import revisions and append-only baseline, match, classification,
  flag, and response decisions. Unknown or unexplained rows remain unresolved.
- An unresolved daily control warns but does not block the next session opening.
  Preserve existing session, opening-approval, and vault safeguards.

## Security and MCP

- Apply the existing authorization policies: `VaultView`,
  `VaultAccess`/`VaultAdjust`, `VaultApprove`, and `UserManagement`; check
  `Program.cs` and the feature for the required roles and authentication schemes.
- `EastSeat.Agenti.Mcp` is read-only. Use `ReadOnlyDbContext`, retain branch
  isolation unless `CanQueryAllBranches` permits otherwise, and cap results at
  `McpServerConfig.MaxRows`. Never add write operations.
- Never commit credentials, secrets, production data, or real bank statements.

## Testing and commands

- Add focused xUnit tests for service behavior, including success, validation,
  duplicate, and edge cases. Use in-memory SQLite for unit tests.
- Use integration tests for database, concurrency, and isolation behavior;
  use E2E tests for user workflows. Run the smallest relevant test project, then
  broaden validation when needed.
- Common commands: `dotnet build` and `dotnet test`. See `CLAUDE.md` for
  project-specific test commands and prerequisites.

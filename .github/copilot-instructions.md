# GitHub Copilot Instructions for Agenti

**Agenti** is a banking agency ERP system built with **ASP.NET Core Blazor Server** (.NET 10), using **Vertical Slice Architecture**, PostgreSQL 16, Entity Framework Core, MudBlazor, and ASP.NET Core Identity.

For full coding guidelines see [`AGENTS.md`](../AGENTS.md). For setup and CLI commands see [`CLAUDE.md`](../CLAUDE.md).

---

## Architecture

Each feature lives in `EastSeat.Agenti.Web/Features/{FeatureName}/`:

```
Features/{FeatureName}/
├── {Feature}Dtos.cs          # Request/response DTOs
├── I{Feature}Service.cs      # Service interface
└── {Feature}Service.cs       # Business logic + data access
```

- Services inject `ApplicationDbContext` directly — **no separate repository layer**.
- Register services as `Scoped` in `Program.cs`.
- Return DTOs from services, **never** expose domain entities to UI.

---

## Key Code Standards

- **Money fields:** `decimal` with `HasPrecision(18, 2)` in `OnModelCreating`.
- **Timestamps:** Always `DateTimeOffset`, never `DateTime`.
- **Enums:** Store as strings via `.HasConversion<string>()`.
- **Soft deletes:** Set `IsActive = false`; never hard-delete entities with audit history.
- **Immutable audit logs:** Never update or delete `VaultTransaction`, `AuditLog`, `Transaction`, or `UserAuditLog`.
- **Transactions:** Use `BeginTransactionAsync()` for multi-entity changes.
- **Unique constraints:** Validate with `AnyAsync()` before insert.

---

## Security & Business Rules

- **Dual approval:** Creator cannot approve their own vault adjustment.
- **Vault notes:** Required, minimum 10 characters.
- **Float conservation:** Opening total must equal previous day's closing total.
- **Authorization policies:** Apply `[Authorize(Policy = "...")]` to protected Blazor pages.
  - `VaultApprove` — Admin only
  - `VaultAccess` / `VaultAdjust` — Admin, Supervisor
  - `UserManagement` — Admin only

---

## Testing

- All service methods require unit tests (xUnit).
- Use in-memory SQLite: `UseSqlite("DataSource=:memory:")`.
- Cover: happy path, error cases, duplicate constraints, edge cases.
- Run tests: `dotnet test`

---

## Build & Run

```bash
dotnet build
cd EastSeat.Agenti.Web && dotnet run   # https://localhost:7001
```

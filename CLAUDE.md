# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Agenti** is a banking agency ERP system built with **ASP.NET Core Blazor Server** (.NET 10) using **Vertical Slice Architecture**. It manages cash operations, transactions, vault management, and discrepancy workflows for banking agencies in Uganda.

**Tech Stack:**
- Framework: ASP.NET Core Blazor Server (.NET 10)
- Database: PostgreSQL 16 (via Docker)
- ORM: Entity Framework Core 10 + Npgsql
- UI: MudBlazor 8.15
- Auth: ASP.NET Core Identity with role-based policies
- Real-time: SignalR (built into Blazor Server)

## Development Commands

### Database Operations

```bash
# Start PostgreSQL container
docker-compose up -d

# Verify database connection
docker-compose exec postgres psql -U agenti_user -d agenti_dev -c "SELECT 1"

# Apply migrations
cd EastSeat.Agenti.Web
dotnet ef database update

# Create new migration
dotnet ef migrations add MigrationName --output-dir Data/Migrations

# Drop and recreate database (warning: destructive)
docker-compose down -v
docker-compose up -d
cd EastSeat.Agenti.Web
dotnet ef database update
```

**Connection String:** `Server=localhost;Port=5432;Database=agenti_dev;User Id=<username>;Password=<password>;`

### Build and Run

```bash
# Build solution
dotnet build

# Run application (from EastSeat.Agenti.Web directory)
cd EastSeat.Agenti.Web
dotnet run

# Clean build
dotnet clean && dotnet build

# Run tests (if any exist)
dotnet test
```

Application URLs:
- HTTPS: `https://localhost:7001`
- HTTP: `http://localhost:5113` (if enabled)

### Docker

```bash
# Stop containers
docker-compose down

# View PostgreSQL logs
docker-compose logs -f postgres

# Check container status
docker ps | grep agenti-postgres
```

## Architecture

### Vertical Slice Architecture

Each feature is a self-contained vertical slice in `EastSeat.Agenti.Web/Features/{FeatureName}/`:

```
Features/{FeatureName}/
├── {Feature}Dtos.cs              # Request/response DTOs
├── I{Feature}Service.cs          # Service interface
└── {Feature}Service.cs           # Business logic + data access
```

**Current Features:**
- `Agents` - Agent (teller) management
- `CashCounts` - Opening/closing cash count recording
- `CashSessions` - Daily cash session lifecycle
- `Dashboard` - Summary metrics and real-time status
- `Setup` - First-time system setup wizard
- `Users` - User management with audit logs
- `Vault` / `Vaults` - Branch vault management with dual-approval workflow
- `WalletTypes` - Wallet type catalog (Cash, Mobile Money, Bank)

### Shared Layer Structure

```
Shared/Domain/
├── Entities/               # EF Core entities (15+ entities)
│   ├── Agent.cs           # 1:1 with ApplicationUser
│   ├── Branch.cs          # Tenant branches (1:1 with Vault)
│   ├── Vault.cs           # Central cash vault per branch
│   ├── VaultTransaction.cs # Immutable vault audit log
│   ├── CashSession.cs     # Daily session (1 per agent per day)
│   ├── CashCount.cs       # Opening/closing count snapshots
│   ├── CashCountDetail.cs # Per-wallet breakdown in count
│   ├── Wallet.cs          # Agent wallet instance
│   ├── WalletType.cs      # Wallet type catalog
│   ├── Transaction.cs     # Inter-wallet movements
│   ├── Discrepancy.cs     # Count mismatches requiring approval
│   └── AuditLog.cs / UserAuditLog.cs
└── Enums/
    ├── CashSessionStatus.cs
    ├── VaultTransactionType.cs
    ├── VaultTransactionStatus.cs
    ├── DiscrepancyStatus.cs
    ├── TransactionType.cs
    ├── UserRole.cs
    └── WalletType.cs (enum)
```

### Key Architectural Patterns

**1. Service Layer Pattern**
Services combine business logic and data access (no separate repository layer). All services use `ApplicationDbContext` directly via DI.

**2. DTO Pattern**
Each feature defines DTOs (suffixed with `Request`, `Response`, `Dto`, or `ViewModel`) to avoid exposing domain entities to UI.

**3. Authorization Policies** (defined in `Program.cs`)
- `VaultView` - Admin, Supervisor
- `VaultAccess` / `VaultAdjust` - Admin, Supervisor
- `VaultApprove` - Admin only
- `UserManagement` - Admin only

Use `@attribute [Authorize(Policy = "PolicyName")]` in Razor components.

**4. Background Services**
- `VaultExpirationService` - Expires pending vault transactions after 12 hours
- `UserAuditCleanupService` - Removes old audit logs

**5. Claims Transformation**
`BranchIdClaimsTransformer` adds `BranchId` claim from `Agent.BranchId` for multi-branch support.

## Database Context

**Location:** `EastSeat.Agenti.Web/Data/ApplicationDbContext.cs`

- Inherits from `IdentityDbContext<ApplicationUser>`
- Contains 15+ `DbSet<T>` properties for domain entities
- Overrides `SaveChangesAsync` to auto-create Vault when Branch is created
- Configures relationships, indexes, precision, and enums-as-strings in `OnModelCreating`
- Seeds default `WalletTypes` and `AppConfig` entries

**Important Relationships:**
- `Branch` 1:1 `Vault` (auto-created on branch insert)
- `ApplicationUser` 1:1 `Agent` (via `UserId` FK)
- `Agent` 1:N `Wallet` (unique constraint on `AgentId + WalletTypeId`)
- `CashSession` 1:N `CashCount`, `Transaction`, `Discrepancy`
- `Vault` 1:N `VaultTransaction` (immutable audit log)

## Critical Business Logic

### Vault Management (Features/Vaults)

**Concurrency Safety:** Uses PostgreSQL row-level locks (`FOR UPDATE`) + Serializable isolation to prevent race conditions.

**Workflow:**
1. **Opening Cash Session** → `VaultService.WithdrawForSessionAsync()` → Deducts from vault, populates agent wallets
2. **Closing Cash Session** → `VaultService.DepositForSessionAsync()` → Returns to vault, zeros out wallets
3. **Manual Adjustments** → Creates `VaultTransaction` with `Status=Pending`, `ExpiresAt=12h` → Requires Admin approval via `ApproveManualAdjustmentAsync()` → Enforces dual-approval (creator ≠ approver)

**Fraud Prevention:**
- Dual approval required (creator cannot approve own request)
- Admin-only approval policy
- Automatic 12-hour expiry on pending transactions
- Mandatory notes (≥10 chars) for audit trail
- Immutable transaction history (no updates/deletes)

### Cash Count Validation (Features/CashCounts)

**Opening Count Rules:**
- Today's opening total must equal previous day's closing total (float conservation)
- On submit: Vault withdrawal occurs → Wallets populated → Session opened

**Closing Count Rules:**
- Closing total should equal opening total (discrepancies trigger approval workflow)
- On submit: Wallets zeroed → Total returned to vault → Session closed

**Discrepancy Workflow:**
- If actual ≠ expected → Creates `Discrepancy` entity with `Status=Pending`
- Teller provides explanation (notes)
- Supervisor approval required to close session

### Setup Flow (Features/Setup)

On first run, app redirects to `/setup-prerequisites` (middleware in `Program.cs`):
1. Creates default branch
2. Creates admin user
3. Sets `AppConfig["SetupComplete"] = "true"`
4. All subsequent requests allowed

## UI Components (Blazor Server)

**Location:** `EastSeat.Agenti.Web/Components/`

**Pages:**
- `Pages/` - Routable page components (`@page "/route"`)
- `Layout/MainLayout.razor` - Main layout with MudBlazor components
- `Layout/NavMenu.razor` - Side navigation with role-based menu items
- `Account/` - Identity-related pages (login, register, etc.)

**Component Pattern:**
```razor
@page "/your-route"
@using EastSeat.Agenti.Web.Features.YourFeature
@inject IYourFeatureService Service
@inject NavigationManager Nav
@attribute [Authorize(Policy = "YourPolicy")]

<MudContainer>
    <MudCard>
        <MudCardHeader>
            <MudText Typo="Typo.h5">Title</MudText>
        </MudCardHeader>
        <MudCardContent>
            @* Component content *@
        </MudCardContent>
    </MudCard>
</MudContainer>

@code {
    // Code-behind logic
}
```

**MudBlazor Components:** Extensively used throughout (MudTable, MudButton, MudTextField, MudSelect, MudDialog, MudSnackbar, etc.)

## Dependency Injection Registration

**Location:** `Program.cs`

Pattern:
```csharp
builder.Services.AddScoped<IYourFeatureService, YourFeatureService>();
```

All feature services registered as `Scoped` (per HTTP request lifetime in Blazor Server).

## Common Development Tasks

### Git Branching Workflow (Required)

**ALWAYS create a new branch for feature implementations.** Do not make changes directly on `main`.

```bash
# Create and switch to a new feature branch
git checkout -b feature/{feature-name}

# For bug fixes
git checkout -b fix/{bug-description}

# For refactoring
git checkout -b refactor/{refactor-description}
```

**Workflow:**
1. Create a new branch from `main`
2. Make your changes with atomic commits
3. Ensure all tests pass (`dotnet test`)
4. Merge into `main` when feature is complete

**Merging to Main:**
```bash
# Ensure your branch is up to date with main
git pull origin main

# Switch to main and merge
git checkout main
git merge feature/{feature-name}

# Push and clean up
git push origin main
git branch -d feature/{feature-name}
```

### Adding a New Feature Slice

1. Create folder: `Features/{FeatureName}/`
2. Create DTOs: `{Feature}Dtos.cs`
3. Create interface: `I{Feature}Service.cs`
4. Implement service: `{Feature}Service.cs` (inject `ApplicationDbContext`)
5. Register in `Program.cs`: `builder.Services.AddScoped<I{Feature}Service, {Feature}Service>()`
6. Create Blazor component in `Components/Pages/{Feature}.razor`
7. Add navigation link in `NavMenu.razor` (if applicable)

### Adding a New Domain Entity

1. Create entity class in `Shared/Domain/Entities/{EntityName}.cs`
2. Add `DbSet<EntityName>` to `ApplicationDbContext.cs`
3. Configure in `OnModelCreating()` (relationships, indexes, precision)
4. Create migration: `dotnet ef migrations add Add{EntityName}`
5. Apply migration: `dotnet ef database update`

### Adding Authorization Policy

1. Define in `Program.cs`:
```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("PolicyName", policy => policy.RequireRole(UserRole.Admin.ToString()));
```
2. Apply to component: `@attribute [Authorize(Policy = "PolicyName")]`
3. Conditional UI: `<AuthorizeView Policy="PolicyName">...</AuthorizeView>`

## Important Notes

- **Project uses .NET 10** (not .NET 9 as mentioned in some docs - check `.csproj`)
- **All enums stored as strings** in database (via `.HasConversion<string>()`)
- **Decimal precision:** All money fields use `decimal(18,2)`
- **Timestamps:** Use `DateTimeOffset` (UTC) for all date/time fields
- **User IDs:** ASP.NET Identity uses `string` (max 450 chars) for user IDs
- **Namespace convention:** `EastSeat.Agenti.Web.Features.{FeatureName}` (note: `Vaults` not `Vault` to avoid class name collision)
- **Entity Framework logging:** Set `"Microsoft.EntityFrameworkCore": "Information"` in `appsettings.json` to debug queries

## Security Considerations

- Connection string loaded from `appsettings.json` → DefaultConnection (empty by default, populated from user secrets in dev)
- User secrets ID: `aspnet-EastSeat.Agenti.Web-f158fce3-8d06-4b04-9bf6-32e5e29eaf2a`
- Role-based authorization via ASP.NET Core Identity
- All vault adjustments require dual approval
- Audit logs track user actions on sensitive entities

## Troubleshooting

**PostgreSQL connection issues:**
- Ensure Docker container running: `docker ps`
- Check logs: `docker-compose logs postgres`
- Verify connection string in `appsettings.json` or user secrets

**Migration errors:**
- Build must succeed before migrations: `dotnet build`
- Ensure `dotnet ef` tool installed: `dotnet tool install --global dotnet-ef`
- Check Npgsql version matches .NET version in `.csproj`

**Blazor rendering issues:**
- Check browser console (F12) for client-side errors
- Check terminal for server-side exceptions
- Verify component has `@page` directive for routing
- Ensure service is registered in `Program.cs`

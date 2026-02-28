# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Agenti** is a banking agency ERP system built with **ASP.NET Core Blazor Server** (.NET 10) using **Vertical Slice Architecture**. It manages cash operations, transactions, vault management, and discrepancy workflows for banking agencies in Uganda. It also includes a companion **Android mobile app** built with .NET MAUI that consumes a REST API served by the web backend.

**Tech Stack:**

- Framework: ASP.NET Core Blazor Server (.NET 10)
- Mobile: .NET MAUI Android app (`EastSeat.Agenti.Android`)
- Database: PostgreSQL 16 (via Docker)
- ORM: Entity Framework Core 10 + Npgsql
- UI: MudBlazor 8.15
- Auth: ASP.NET Core Identity (cookie-based for web) + JWT Bearer (for Android API)
- Logging: Serilog with Application Insights sink
- Monitoring: Azure Application Insights
- API Docs: Swagger/OpenAPI via Swashbuckle (dev only, at `/api/docs`)
- Real-time: SignalR (built into Blazor Server)

## Solution Structure

The solution file is `Agenti.slnx` (XML solution format) containing:

```text
Agenti.slnx
├── EastSeat.Agenti.Web/           # Blazor Server web app + REST API backend
├── EastSeat.Agenti.Android/       # .NET MAUI Android mobile client
├── tests/
│   ├── EastSeat.Agenti.UnitTests/
│   ├── EastSeat.Agenti.IntegrationTests/
│   └── EastSeat.Agenti.E2ETests/
├── docs/                           # Project documentation
├── scripts/azure/                  # Azure infrastructure scripts
└── .github/workflows/              # CI/CD pipelines
```

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

**Note:** Migrations are auto-applied on startup via `db.Database.MigrateAsync()`, so `dotnet ef database update` is only needed for local development before running the app.

**Connection String:** `Server=localhost;Port=5432;Database=agenti_dev;User Id=<username>;Password=<password>;`

### Build and Run

```bash
# Build solution
dotnet build

# Run web application (from EastSeat.Agenti.Web directory)
cd EastSeat.Agenti.Web
dotnet run

# Clean build
dotnet clean && dotnet build

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/EastSeat.Agenti.UnitTests
dotnet test tests/EastSeat.Agenti.IntegrationTests
dotnet test tests/EastSeat.Agenti.E2ETests
```

Application URLs:

- HTTPS: `https://localhost:7001`
- HTTP: `http://localhost:5113` (if enabled)
- Swagger UI (dev only): `https://localhost:7001/api/docs`

### Docker

```bash
# Stop containers
docker-compose down

# View PostgreSQL logs
docker-compose logs -f postgres

# Check container status
docker ps | grep agenti-postgres

# Build production Docker image
docker build -f EastSeat.Agenti.Web/Dockerfile -t agenti .
```

## Architecture

### Vertical Slice Architecture

Each feature is a self-contained vertical slice in `EastSeat.Agenti.Web/Features/{FeatureName}/`:

```text
Features/{FeatureName}/
├── {Feature}Dtos.cs              # Request/response DTOs
├── I{Feature}Service.cs          # Service interface
└── {Feature}Service.cs           # Business logic + data access
```

**Current Features:**

- `Agents` - Agent (teller) management
- `Api` - REST API endpoints for the Android mobile app (JWT-authenticated minimal APIs)
- `CashCounts` - Opening/closing cash count recording
- `CashSessions` - Daily cash session lifecycle
- `Dashboard` - Summary metrics and real-time status
- `Setup` - First-time system setup wizard
- `Theme` - Dark/light mode theme management (`AppThemes`, `ThemeService`, `ThemePreferenceConstants`)
- `Users` - User management with audit logs
- `Vault` / `Vaults` - Branch vault management with dual-approval workflow
- `WalletTypes` - Wallet type catalog (Cash, Mobile Money, Bank)

### REST API (Features/Api)

Minimal API endpoints under `/api` serve the Android mobile app, authenticated via JWT Bearer tokens:

| Route Group | Endpoints File | Description |
| --- | --- | --- |
| `/api/auth` | `AuthEndpoints.cs` | JWT login (issues tokens) |
| `/api/dashboard` | `DashboardEndpoints.cs` | Dashboard metrics |
| `/api/agents` | `AgentEndpoints.cs` | Agent management |
| `/api/cash-counts` | `CashCountEndpoints.cs` | Cash count operations |
| `/api/cash-sessions` | `CashSessionEndpoints.cs` | Cash session lifecycle |
| `/api/vault` | `VaultEndpoints.cs` | Vault operations |
| `/api/wallet-types` | `WalletTypeEndpoints.cs` | Wallet type catalog |

Each endpoint file defines extension methods (e.g., `MapAuthApi()`) called from `Program.cs`.

### Shared Layer Structure

```text
Shared/Domain/
├── Entities/               # EF Core entities (15+ entities)
│   ├── Agent.cs           # 1:1 with ApplicationUser
│   ├── AppConfig.cs       # Key-value application config (e.g., SetupComplete flag)
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

All policies support dual authentication schemes: Identity cookies (Blazor web) and JWT Bearer (Android API).

Use `@attribute [Authorize(Policy = "PolicyName")]` in Razor components.

### Background Services

- `VaultExpirationService` - Expires pending vault transactions after 12 hours
- `UserAuditCleanupService` - Removes old audit logs

Both services accept an optional `TelemetryClient?` parameter for Application Insights integration.

**5. Claims Transformation**
`BranchIdClaimsTransformer` adds `BranchId` claim from `Agent.BranchId` for multi-branch support.

### Dual Authentication

- **Web (Blazor):** Identity cookies via `IdentityConstants.ApplicationScheme`
- **Android API:** JWT Bearer tokens via `JwtBearerDefaults.AuthenticationScheme`
- Both schemes are registered on every authorization policy so the same policies work for web and API requests.

## Database Context

**Location:** `EastSeat.Agenti.Web/Data/ApplicationDbContext.cs`

- Inherits from `IdentityDbContext<ApplicationUser>`
- Contains 15+ `DbSet<T>` properties for domain entities (including `DbSet<AppConfig> AppConfigs`)
- Overrides `SaveChangesAsync` to auto-create Vault when Branch is created
- Configures relationships, indexes, precision, and enums-as-strings in `OnModelCreating`
- Seeds default `WalletTypes` and `AppConfig` entries

**Registration:** Both `AddDbContext` and `AddDbContextFactory` are registered (both Scoped). The factory is used by services that need their own `DbContext` instance to avoid concurrency issues during Blazor prerendering.

**Important Relationships:**

- `Branch` 1:1 `Vault` (auto-created on branch insert)
- `ApplicationUser` 1:1 `Agent` (via `UserId` FK)
- `Agent` 1:N `Wallet` (unique constraint on `AgentId + WalletTypeId`)
- `CashSession` 1:N `CashCount`, `Transaction`, `Discrepancy`
- `Vault` 1:N `VaultTransaction` (immutable audit log)

## Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Jwt": {
    "Key": "",
    "Issuer": "EastSeat.Agenti",
    "Audience": "EastSeat.Agenti.Android",
    "ExpiryMinutes": 60
  },
  "ApplicationInsights": {
    "ConnectionString": "",
    "EnableAdaptiveSampling": false
  }
}
```

- **Jwt** - Required for the Android REST API. The `Key` must be set via user secrets or environment variable (`Jwt__Key`). App logs a warning at startup if missing.
- **ApplicationInsights** - Optional. If `ConnectionString` is empty, telemetry and Serilog AI sink are disabled.
- **ConnectionStrings:DefaultConnection** - PostgreSQL connection string. Loaded from user secrets in dev.

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

The setup middleware also excludes `/api/*` paths from the redirect so the REST API remains accessible during setup.

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

## Android Mobile App

**Location:** `EastSeat.Agenti.Android/`

A .NET MAUI Android app that consumes the REST API at `/api/*`:

- **Pages:** LoginPage, DashboardPage, AgentsPage, CashSessionsPage, CashCountPage
- **ViewModels:** LoginViewModel, DashboardViewModel, AgentsViewModel, CashSessionsViewModel, CashCountViewModel, VaultViewModel, BaseViewModel
- **Services:** `ApiService` (HTTP client), `AuthService` (JWT token management)
- Authenticates via `/api/auth` (JWT) and calls the same business logic as the web UI

## Testing

Three test projects under `tests/`:

```bash
# Run all tests
dotnet test

# Unit tests only (mocked services, no DB required)
dotnet test tests/EastSeat.Agenti.UnitTests

# Integration tests (requires PostgreSQL via DatabaseFixture)
dotnet test tests/EastSeat.Agenti.IntegrationTests

# End-to-end tests
dotnet test tests/EastSeat.Agenti.E2ETests
```

- **Unit tests** cover all feature services (AgentService, CashCountService, CashSessionService, DashboardService, SetupService, ThemeService, UserService, VaultService, WalletTypeService, ApiResponse, VaultExpirationService)
- **Integration tests** use a real PostgreSQL database via `DatabaseFixture` and `IntegrationTestBase`
- **E2E tests** cover end-to-end workflows

## CI/CD and Deployment

### GitHub Actions Workflows

- **`.github/workflows/ci-cd.yml`** - Build → Unit Tests → Integration Tests → E2E Tests → Deploy to Azure App Service (triggers on push to `main`)
- **`.github/workflows/infrastructure.yml`** - Provisions Azure infrastructure (PostgreSQL Flexible Server, App Service Plan B1 Linux, Web App) via `workflow_dispatch`

### Dockerfile

`EastSeat.Agenti.Web/Dockerfile` — Multi-stage build:

1. **Build stage:** `dotnet/sdk:10.0` → restore, build, publish
2. **Runtime stage:** `dotnet/aspnet:10.0` → runs as non-root `app` user on port 8080

### Azure Deployment

- **App Service:** B1 Linux, UAE North region
- **Database:** Azure Database for PostgreSQL Flexible Server (Burstable B1ms)
- **Infrastructure scripts:** `scripts/azure/setup-infrastructure.ps1`
- Forwarded headers middleware (`UseForwardedHeaders`) handles Azure reverse proxy headers

## Dependency Injection Registration

**Location:** `Program.cs`

Pattern:

```csharp
builder.Services.AddScoped<IYourFeatureService, YourFeatureService>();
```

All feature services registered as `Scoped` (per HTTP request lifetime in Blazor Server).

## Startup Behavior (Program.cs)

Key startup steps in order:

1. Configure Serilog (console + optional Application Insights sink)
2. Register services (MudBlazor, Auth, DbContext + DbContextFactory, feature services, background services, Swagger)
3. Configure JWT Bearer authentication alongside Identity cookies
4. Validate JWT key presence (logs warning if missing)
5. **Auto-apply pending EF migrations** (`db.Database.MigrateAsync()`)
6. Run setup check (clean up fresh databases if setup incomplete)
7. Configure middleware pipeline (forwarded headers, Swagger in dev, setup redirect, API endpoints)

## Documentation

The `docs/` directory contains detailed documentation:

- `AGENT_DOMAIN.md` - Agent domain model documentation
- `ANDROID_DEPLOYMENT.md` - Android app deployment guide
- `APPLICATION_INSIGHTS_SETUP.md` - Application Insights configuration
- `AZURE_DEPLOYMENT_SETUP.md` - Azure deployment setup guide
- `AZURE_LOGS_QUERYING.md` - Azure log querying reference
- `CI_CD_PIPELINE.md` - CI/CD pipeline documentation
- `DEVELOPMENT_GUIDE.md` - Development environment setup
- `IMPLEMENTATION_SUMMARY.md` - Implementation overview
- `VAULT_FEATURE_SUMMARY.md` - Vault feature details

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
8. If the feature needs API access, add endpoints in `Features/Api/{Feature}Endpoints.cs` and map in `Program.cs`

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
       .AddPolicy("PolicyName", policy =>
           policy.RequireRole(UserRole.Admin.ToString())
                 .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme));
   ```

2. Apply to component: `@attribute [Authorize(Policy = "PolicyName")]`
3. Conditional UI: `<AuthorizeView Policy="PolicyName">...</AuthorizeView>`

### Adding a New API Endpoint

1. Create `Features/Api/{Feature}Endpoints.cs`
2. Define a static extension method `Map{Feature}Api(this RouteGroupBuilder group)`
3. Register in `Program.cs`:

   ```csharp
   apiGroup.MapGroup("/{route}")
       .WithTags("Tag Name")
       .Map{Feature}Api();
   ```

4. Endpoints use JWT Bearer auth — apply `.RequireAuthorization("PolicyName")` as needed

## Important Notes

- **Project uses .NET 10** (not .NET 9 as mentioned in some docs - check `.csproj`)
- **Solution format:** Uses `.slnx` (XML solution), not `.sln`
- **All enums stored as strings** in database (via `.HasConversion<string>()`)
- **Decimal precision:** All money fields use `decimal(18,2)`
- **Timestamps:** Use `DateTimeOffset` (UTC) for all date/time fields
- **User IDs:** ASP.NET Identity uses `string` (max 450 chars) for user IDs
- **Namespace convention:** `EastSeat.Agenti.Web.Features.{FeatureName}` (note: `Vaults` not `Vault` to avoid class name collision)
- **Entity Framework logging:** Set `"Microsoft.EntityFrameworkCore": "Information"` in `appsettings.json` to debug queries
- **Auto-migration:** Pending migrations are applied automatically on app startup — no manual `dotnet ef database update` needed in production

## Security Considerations

- Connection string loaded from `appsettings.json` → DefaultConnection (empty by default, populated from user secrets in dev)
- JWT key loaded from `Jwt:Key` (empty by default, set via user secrets or `Jwt__Key` env var)
- User secrets ID: `aspnet-EastSeat.Agenti.Web-f158fce3-8d06-4b04-9bf6-32e5e29eaf2a`
- Role-based authorization via ASP.NET Core Identity
- Dual authentication: Identity cookies (web) + JWT Bearer (API)
- All vault adjustments require dual approval
- Audit logs track user actions on sensitive entities
- Docker container runs as non-root user

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

**JWT / API issues:**

- Verify `Jwt:Key` is configured (check startup logs for warning)
- Test with Swagger UI at `/api/docs` (dev only)
- Ensure authorization policies include both authentication schemes

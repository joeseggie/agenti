# Agenti - Banking Agency ERP

Agenti is a banking agency ERP system built with **ASP.NET Core Blazor Server** (.NET 10) using **Vertical Slice Architecture**. It manages daily cash operations, vault workflows, discrepancies, and user administration for banking agencies in Uganda. The solution also includes a **.NET MAUI Android app** that consumes the backend REST API.

## Tech Stack

- **Framework**: ASP.NET Core Blazor Server (.NET 10)
- **Mobile**: .NET MAUI Android (`EastSeat.Agenti.Android`)
- **Database**: PostgreSQL 16
- **ORM**: Entity Framework Core 10 + Npgsql
- **Authentication**: ASP.NET Identity (web) + JWT Bearer (API)
- **UI**: MudBlazor
- **Real-time**: SignalR
- **Observability**: Serilog + Azure Application Insights

## Solution Structure

```text
Agenti.slnx
├── EastSeat.Agenti.Web/           # Blazor Server web app + REST API backend
├── EastSeat.Agenti.Android/       # .NET MAUI Android mobile client
├── EastSeat.Agenti.Mcp/           # Read-only MCP server for AI assistant integration
├── tests/
│   ├── EastSeat.Agenti.UnitTests/
│   ├── EastSeat.Agenti.IntegrationTests/
│   └── EastSeat.Agenti.E2ETests/
├── docs/
├── scripts/azure/
└── .github/workflows/
```

## Current Feature Slices (Web)

Located under `EastSeat.Agenti.Web/Features/`:

- `Agents`
- `Api`
- `BankRuns`
- `CashCounts`
- `CashSessions`
- `Dashboard`
- `Notifications`
- `PendingTransactions`
- `Setup`
- `Theme`
- `Transactions`
- `Users`
- `Vault`, `Vaults`
- `WalletAdjustments`
- `WalletTypes`

## Local Development Setup

### Prerequisites

- .NET 10 SDK
- Docker + Docker Compose
- MAUI Android workload (required only when building Android project):
  ```bash
  dotnet workload install maui-android
  ```

### 1) Configure local environment

```bash
cp .env.example .env
# Edit .env and set POSTGRES_PASSWORD
```

### 2) Start PostgreSQL

```bash
docker-compose up -d
```

### 3) Verify database connection

```bash
docker-compose exec postgres psql -U ${POSTGRES_USER:-agenti_user} -d ${POSTGRES_DB:-agenti_dev} -c "SELECT 1"
```

### 4) Run the web app

```bash
cd EastSeat.Agenti.Web
dotnet run
```

Application URLs:

- Web UI: `https://localhost:7001`
- Swagger (dev): `https://localhost:7001/api/docs`

> Note: Migrations are auto-applied on app startup (`db.Database.MigrateAsync()`).

## Build and Test

From repository root:

```bash
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run specific test projects
dotnet test tests/EastSeat.Agenti.UnitTests
dotnet test tests/EastSeat.Agenti.IntegrationTests
dotnet test tests/EastSeat.Agenti.E2ETests
```

## CI/CD

GitHub Actions workflow (`.github/workflows/ci-cd.yml`) runs:

1. Build (including MAUI Android workload install)
2. Unit tests
3. Integration tests (PostgreSQL service container)
4. E2E tests (PostgreSQL service container)
5. Deploy to Azure App Service on `main` pushes

## Documentation

- Setup and contributor reference: [`CLAUDE.md`](CLAUDE.md)
- AI coding architecture rules: [`AGENTS.md`](AGENTS.md)
- Development setup guide: [`docs/DEVELOPMENT_GUIDE.md`](docs/DEVELOPMENT_GUIDE.md)
- CI/CD details: [`docs/CI_CD_PIPELINE.md`](docs/CI_CD_PIPELINE.md)
- Domain guide: [`docs/AGENT_DOMAIN.md`](docs/AGENT_DOMAIN.md)

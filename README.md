# Agenti - Banking Agency ERP

A vertical-slice architecture Blazor Server application for managing banking agency operations in Uganda.

## Tech Stack

- **Framework**: ASP.NET Core Blazor Server (.NET 9)
- **Database**: PostgreSQL
- **Authentication**: ASP.NET Identity
- **UI**: MudBlazor
- **Real-time**: SignalR
- **Architecture**: Vertical Slice Architecture

## Project Setup

### Prerequisites

- .NET 9.0 SDK
- Docker & Docker Compose (for PostgreSQL)
- Visual Studio Code (Recommended)

### Local Development Setup

1. **Start PostgreSQL Container**:
   ```bash
   cd C:\repos\Agenti
   docker-compose up -d
   ```

2. **Verify PostgreSQL Connection**:
   ```bash
   docker-compose exec postgres psql -U agenti_user -d agenti_dev -c "SELECT 1"
   ```

3. **Apply Database Migrations**:
   ```bash
   cd EastSeat.Agenti.Web
   dotnet ef database update
   ```

4. **Run the Application**:
   ```bash
   dotnet run
   ```

   The application will be available at: `https://localhost:7001`

### Database Configuration

- **Connection String**: `Server=localhost;Port=5432;Database=agenti_dev;User Id=agenti_user;Password=DevPassword123!;`
- **User**: agenti_user
- **Password**: DevPassword123!
- **Database**: agenti_dev

## Project Structure

```
Features/                          # Vertical slices (feature modules)
├── Authentication/               # Auth & Identity management
├── WalletCatalog/               # Wallet type management
├── DailyCashSession/            # Daily session opening/closing
├── CashCounts/                  # Cash count recording
├── Transactions/                # Movement between wallets
├── DiscrepancyWorkflow/         # Discrepancy explanation & approval
├── Notifications/               # SignalR notifications
└── Reporting/                   # Reports & analytics

Shared/                           # Cross-cutting concerns
├── Domain/                       # Domain entities & enums
├── Infrastructure/              # DbContext, migrations
├── Exceptions/                  # Custom exceptions
├── Middleware/                  # Middleware components
├── Security/                    # Auth/authorization
└── SignalR/                     # Real-time hubs

Components/                       # Global Blazor components
Pages/                            # Global pages
Layouts/                          # Global layouts
Data/                             # Database context & migrations
```

## Features

### Phase 1 (MVP)

- ✅ Daily Opening Cash Count
- ✅ Daily Closing Cash Count  
- ✅ Wallet Management (predefined + custom types)
- ✅ Transaction Recording
- ✅ Discrepancy Detection & Explanation
- ✅ Supervisor Approval Workflow
- ✅ ASP.NET Identity Authentication
- ✅ Basic Reporting
- ✅ Real-time Notifications (SignalR)

## Key Business Rules

1. **Opening Count Validation**: Today's opening total = Previous day's closing total
2. **Closing Count Validation**: Closing total = Opening total (float conservation)
3. **Discrepancy Workflow**: Mismatches require teller explanation + supervisor approval
4. **Transaction Integrity**: Movements between wallets don't change total float

## Development Notes

- Each slice is independent with its own Models, Services, Components, and Validators
- Shared domain entities live in `Shared/Domain/Entities/`
- Database configuration is in `Data/ApplicationDbContext.cs`
- Cross-slice communication via querier interfaces

## Future Enhancements

- Phase 2: Multi-branch support, advanced analytics
- Phase 3: Mobile app, offline support (PWA)
- Phase 4: API layer, third-party integrations

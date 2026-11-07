# Development Guide - Agenti ERP

## Quick Start

### 1. Start PostgreSQL Database
```bash
cd C:\repos\Agenti
docker-compose up -d
```

Verify connection:
```bash
docker-compose exec postgres psql -U agenti_user -d agenti_dev -c "SELECT 1"
```

### 2. Build the Solution
```bash
cd C:\repos\Agenti
dotnet build
```

### 3. Apply Database Migrations
```bash
cd EastSeat.Agenti.Web
dotnet ef database update
```

### 4. Run the Application
```bash
dotnet run
```

The app will be available at:
- **HTTPS**: `https://localhost:7001`
- **HTTP**: `http://localhost:5113` (if unencrypted is enabled)

## Project Navigation

- **Solution Root**: `C:\repos\Agenti\`
- **Main Project**: `C:\repos\Agenti\EastSeat.Agenti.Web\`
- **Features**: `C:\repos\Agenti\EastSeat.Agenti.Web\Features\`
- **Domain Entities**: `C:\repos\Agenti\EastSeat.Agenti.Web\Shared\Domain\Entities\`
- **DbContext**: `C:\repos\Agenti\EastSeat.Agenti.Web\Data\ApplicationDbContext.cs`

## Adding a New Feature Slice

### Template Structure

```
Features/YourFeature/
├── Models/
│   ├── YourFeatureRequest.cs         # Input DTO
│   ├── YourFeatureResponse.cs        # Output DTO
│   └── YourFeatureViewModel.cs       # Blazor component state
├── Components/
│   ├── YourFeatureForm.razor         # Main UI component
│   └── YourFeatureForm.razor.cs      # Code-behind
├── Services/
│   └── YourFeatureService.cs         # Business logic orchestration
├── Validators/
│   └── YourFeatureValidator.cs       # FluentValidation (if added)
└── Repositories/
    ├── IYourFeatureRepository.cs     # Data access interface
    └── YourFeatureRepository.cs      # EF Core implementation
```

### Steps

1. **Create the feature folder** under `Features/`
2. **Define domain logic** in Models
3. **Create a service** to orchestrate business logic
4. **Build the Blazor component** for UI
5. **Register in Program.cs** via dependency injection
6. **Add route** in Components/Pages or routing config

## Database Operations

### Add a Migration
```bash
cd EastSeat.Agenti.Web
dotnet ef migrations add MigrationName --output-dir Data/Migrations
dotnet ef database update
```

### View Current Schema
```bash
docker-compose exec postgres psql -U agenti_user -d agenti_dev -c "\dt"
```

### Drop & Recreate Database
```bash
docker-compose down -v
docker-compose up -d
dotnet ef database update
```

## Blazor Server Components

### Basic Component Template

```razor
@page "/your-page"
@using EastSeat.Agenti.Web.Features.YourFeature.Models
@using EastSeat.Agenti.Web.Features.YourFeature
@inject YourFeatureService Service
@inject NavigationManager Nav

<MudContainer>
    <MudCard>
        <MudCardHeader>
            <MudText Typo="Typo.h5">Your Feature</MudText>
        </MudCardHeader>
        <MudCardContent>
            <EditForm Model="@Model" OnValidSubmit="@HandleSubmit">
                <DataAnnotationsValidator />
                <MudTextField @bind-Value="Model.Name" Label="Name" />
                <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary">
                    Submit
                </MudButton>
            </EditForm>
        </MudCardContent>
    </MudCard>
</MudContainer>

@code {
    private YourFeatureRequest Model = new();

    private async Task HandleSubmit()
    {
        try
        {
            var response = await Service.CreateAsync(Model);
            Nav.NavigateTo("/success");
        }
        catch (Exception ex)
        {
            // Error handling
        }
    }
}
```

## DI Registration Pattern

In `Program.cs`:

```csharp
// Add your feature services
builder.Services.AddScoped<YourFeatureService>();
builder.Services.AddScoped<IYourFeatureRepository, YourFeatureRepository>();
```

## Important Class Namespaces

- **Domain Entities**: `EastSeat.Agenti.Shared.Domain.Entities`
- **Enums**: `EastSeat.Agenti.Shared.Domain.Enums`
- **DbContext**: `EastSeat.Agenti.Web.Data`
- **Feature Slices**: `EastSeat.Agenti.Web.Features.{FeatureName}`

## MudBlazor Components Usage

Common MudBlazor components already available:

```razor
<MudContainer>        <!-- Layout container -->
<MudCard>             <!-- Card component -->
<MudCardHeader>       <!-- Card header -->
<MudCardContent>      <!-- Card content -->
<MudButton>           <!-- Button -->
<MudTextField>        <!-- Text input -->
<MudDatePicker>       <!-- Date picker -->
<MudNumericField>     <!-- Number input -->
<MudSelect>           <!-- Dropdown -->
<MudTable>            <!-- Data table -->
<MudDialog>           <!-- Modal -->
<MudSnackbar>         <!-- Toast notifications -->
```

## Debugging

### Enable Entity Framework Logging
Add to `appsettings.json`:

```json
"Logging": {
  "LogLevel": {
    "Microsoft.EntityFrameworkCore": "Debug"
  }
}
```

### Debug Blazor Server
1. Use browser DevTools (F12)
2. Blazor debugging available via .NET debugging extension in VS Code
3. Server logs in terminal where `dotnet run` is executed

## Testing the Setup

1. **Check PostgreSQL is running**:
   ```bash
   docker ps | grep agenti-postgres
   ```

2. **Test database connection**:
   ```bash
   docker-compose exec postgres pg_isready -U agenti_user
   ```

3. **Verify application build**:
   ```bash
   dotnet build
   ```

4. **Run application**:
   ```bash
   dotnet run
   ```

## Common Commands

| Task | Command |
|------|---------|
| Clean build | `dotnet clean && dotnet build` |
| Run tests | `dotnet test` |
| Create migration | `dotnet ef migrations add Name` |
| Update database | `dotnet ef database update` |
| View migrations | `dotnet ef migrations list` |
| Drop database | `dotnet ef database drop` |
| Stop containers | `docker-compose down` |
| View logs | `docker-compose logs -f postgres` |

## Troubleshooting

### Bank reconciliation verification and rollout gate

The banking workflow separates closing submission (one provisional internal settlement)
from later administrator financial approval. Legacy sessions retain their original
rules. Statement intake is a Web Admin workflow that uses one shipped, versioned
universal CSV contract (`agenti-bank-statement-v1`); there is no per-bank parser
verification or sample-fixture gate, and Admins cannot upload or edit a schema. A
date-only CSV supports a transaction-date daily control and does not certify exact
session cutoffs. A synthetic JSON profile exists only in the test assemblies so the
browser journey can exercise the upload flow.

Use the .NET 10 SDK. PostgreSQL integration and browser tests start their own
PostgreSQL 16 Testcontainers with generated credentials and random ports. They
never use `ConnectionStrings__DefaultConnection` for a development or production
database. Docker must be running; no shared PostgreSQL service or test password
secret is required in CI.

```powershell
dotnet test tests\EastSeat.Agenti.UnitTests\EastSeat.Agenti.UnitTests.csproj
dotnet test tests\EastSeat.Agenti.IntegrationTests\EastSeat.Agenti.IntegrationTests.csproj --filter "FullyQualifiedName~Reconciliation"
dotnet build tests\EastSeat.Agenti.E2ETests\EastSeat.Agenti.E2ETests.csproj
& .\tests\EastSeat.Agenti.E2ETests\bin\Debug\net10.0\playwright.ps1 install chromium
dotnet test tests\EastSeat.Agenti.E2ETests\EastSeat.Agenti.E2ETests.csproj --no-build --filter "Category!=AndroidDevice"
```

The browser journey captures a receipt, submits a locked closing, opens a later
session, uploads a synthetic statement for the statement date, matches it, and
approves the old closing without changing current balances.

The native journey exercises encrypted draft recovery after process termination,
receipt capture and the agent's closing submission. Statement upload and
reconciliation are Web Admin workflows and are not part of the Android app.
Create a **separate disposable AVD** whose name starts with
`agenti-reconciliation-`; never use a physical device or an existing personal AVD.
Start it, then use its exact emulator serial:

```powershell
dotnet build EastSeat.Agenti.Android\EastSeat.Agenti.Android.csproj -f net10.0-android -p:ApplicationId=com.eastseat.agenti.reconciliationtest -p:BankingTestApiUrl=https://127.0.0.1:58731/ -p:EmbedAssembliesIntoApk=true
$env:AGENTI_ANDROID_TEST_APK = (Resolve-Path EastSeat.Agenti.Android\bin\Debug\net10.0-android\com.eastseat.agenti.reconciliationtest-Signed.apk).Path
$env:AGENTI_ADB_PATH = '<Android SDK>\platform-tools\adb.exe'
$env:AGENTI_ANDROID_SERIAL = '<isolated emulator serial>'
# Optional for a dedicated ADB server: $env:AGENTI_ADB_SERVER_PORT = '5039'
dotnet test tests\EastSeat.Agenti.E2ETests\EastSeat.Agenti.E2ETests.csproj --filter "Category=AndroidDevice"
```

The test starts an isolated HTTPS backend on loopback port 58731 and a scoped
ADB reverse tunnel, installs only
the test application ID, and removes that application afterward. The backend
override is compiled into Debug builds only and rejects non-loopback hosts.
Native tests are explicitly skipped without the test APK setting. Rebuild the
normal application without these test properties before distribution.

Before production enrollment: confirm with Finance the bank balance basis
(ledger or available) and the source of the first opening baseline for each
account, approve evidence retention/access/backup arrangements, rehearse
migrations on a dedicated copy, and distribute the compatible Android client.
Pausing enrollment must not downgrade existing modern sessions or replay their
settlements.

### PostgreSQL Connection Issues
- Check if container is running: `docker ps`
- View logs: `docker-compose logs postgres`
- Verify credentials in `appsettings.json`

### EF Migration Issues
- Ensure project builds first: `dotnet build`
- Check connection string is correct
- Verify Npgsql package version matches .NET version

### Blazor Rendering Issues
- Check browser console (F12) for client errors
- Review terminal output for server errors
- Verify component is declared with correct `@page` directive

---

**Last Updated**: January 7, 2026

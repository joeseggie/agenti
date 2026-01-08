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

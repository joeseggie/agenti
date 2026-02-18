# AGENTS.md

## Introduction

This file provides behavioral rules, code generation guidelines, and architectural constraints for AI assistants (Claude, GitHub Copilot, Cursor, etc.) working with the **Agenti** banking ERP codebase.

**Purpose:** Guide AI assistants on how to write code correctly within the Agenti architecture.

**Relationship to CLAUDE.md:** This file focuses on *how to write code*, while CLAUDE.md focuses on *setup and commands*.

**When to Reference:** When generating or modifying code, implementing features, or making architectural decisions.

For domain-specific documentation about the Agent entity (tellers), see [docs/AGENT_DOMAIN.md](docs/AGENT_DOMAIN.md).

---

## Vertical Slice Architecture Rules

**Agenti uses Vertical Slice Architecture** where each feature is self-contained.

### Required Structure

Each feature must be organized in `Features/{FeatureName}/`:

```
Features/{FeatureName}/
├── {Feature}Dtos.cs              # Request/response DTOs
├── I{Feature}Service.cs          # Service interface
└── {Feature}Service.cs           # Business logic + data access
```

### Core Principles

✅ **DO:**
- Keep features self-contained in their own folder
- Service layer combines business logic + data access (no separate repository layer)
- Services inject `ApplicationDbContext` directly via constructor
- Register all services as `Scoped` in `Program.cs`
- Clear separation: Domain entities → DTOs → UI components

❌ **DO NOT:**
- Create separate repository layers
- Share business logic across feature boundaries (use shared services if needed)
- Expose domain entities directly to UI components

### Service Registration

```csharp
// In Program.cs
builder.Services.AddScoped<IYourFeatureService, YourFeatureService>();
```

---

## Code Generation Guidelines

### Service Layer Pattern

**Service Structure:**
```csharp
public class AgentService : IAgentService
{
    private readonly ApplicationDbContext _dbContext;

    public AgentService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AgentDetailDto> GetAgentAsync(long id)
    {
        var agent = await _dbContext.Agents
            .Include(a => a.User)
            .Include(a => a.Wallets)
            .FirstOrDefaultAsync(a => a.Id == id);

        return agent == null ? null : MapToDto(agent);
    }
}
```

**Service Best Practices:**
- Always inject `ApplicationDbContext` via constructor
- Use async/await for all database operations
- Return DTOs, never domain entities
- Use transactions for multi-entity changes

**Multi-Entity Transaction Example:**
```csharp
// Creating Agent requires updating both Agent and User
using var transaction = await _dbContext.Database.BeginTransactionAsync();
try
{
    var agent = new Agent { UserId = model.UserId, Code = code };
    _dbContext.Agents.Add(agent);
    await _dbContext.SaveChangesAsync();

    var user = await _dbContext.Users.FindAsync(model.UserId);
    user.AgentId = agent.Id;
    user.BranchId = model.BranchId;
    await _dbContext.SaveChangesAsync();

    await transaction.CommitAsync();
    return MapToDto(agent);
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### DTO Pattern

**Separate DTOs by Purpose:**

```csharp
// For list views (minimal data)
public class AgentListItemDto
{
    public long Id { get; set; }
    public string Code { get; set; }
    public string FullName { get; set; }
    public int WalletCount { get; set; }
    public decimal TotalBalance { get; set; }
}

// For detail views (full data)
public class AgentDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public List<AgentWalletDto> Wallets { get; set; }
}

// For forms (create/edit)
public class AgentFormModel
{
    public long? Id { get; set; }
    public string? UserId { get; set; }
    public string Code { get; set; }
    public bool IsActive { get; set; } = true;
}
```

**DTO Rules:**
- ✅ List DTOs: Minimal fields for table displays
- ✅ Detail DTOs: Full fields with nested collections
- ✅ Form Models: Only editable fields
- ❌ Never expose navigation properties or EF Core proxies to UI

### Entity Design Standards

**Money Fields:**
```csharp
public decimal Balance { get; set; }  // Always configure as decimal(18,2) in OnModelCreating
```

**Timestamps:**
```csharp
public DateTimeOffset CreatedAt { get; set; }  // Always use DateTimeOffset, never DateTime
public DateTimeOffset? UpdatedAt { get; set; }
```

**Enums:**
```csharp
public UserRole Role { get; set; }  // Store as string in database

// In ApplicationDbContext.OnModelCreating:
entity.Property(e => e.Role).HasConversion<string>();
```

**Foreign Keys:**
```csharp
// Always define explicit FK properties
public string UserId { get; set; }
public ApplicationUser? User { get; set; }
```

**Unique Constraints:**
```csharp
// In ApplicationDbContext.OnModelCreating:
entity.HasIndex(e => e.Code).IsUnique();
entity.HasIndex(e => new { e.AgentId, e.WalletTypeId }).IsUnique();
```

### Blazor Component Pattern

**Basic Component Structure:**
```razor
@page "/agents"
@using EastSeat.Agenti.Web.Features.Agents
@inject IAgentService Service
@inject NavigationManager Nav
@inject ISnackbar Snackbar
@attribute [Authorize(Policy = "YourPolicy")]

<MudContainer MaxWidth="MaxWidth.Large">
    <MudCard>
        <MudCardHeader>
            <MudText Typo="Typo.h5">Agents</MudText>
        </MudCardHeader>
        <MudCardContent>
            <MudTable Items="@agents" />
        </MudCardContent>
    </MudCard>
</MudContainer>

@code {
    private List<AgentListItemDto> agents = new();

    protected override async Task OnInitializedAsync()
    {
        agents = await Service.GetAgentsAsync();
    }
}
```

**Component Best Practices:**
- Use `@page` directive for routable pages
- Inject services via `@inject I{Feature}Service Service`
- Use `@attribute [Authorize(Policy = "PolicyName")]` for protected pages
- Extensive use of MudBlazor components (MudTable, MudButton, MudDialog, etc.)
- No business logic in components (call service methods)

---

## Security & Compliance Rules

### Banking-Specific Constraints

**Dual Approval (Vault Operations):**
```csharp
// Creator cannot approve their own adjustment
if (adjustment.CreatedBy == currentUserId)
    throw new InvalidOperationException("Cannot approve own adjustment");
```

**Audit Immutability:**
- ❌ Never update or delete: `VaultTransaction`, `AuditLog`, `Transaction`, `UserAuditLog`
- ✅ All changes are append-only (new records)

**Soft Deletes:**
```csharp
// Use IsActive flags, not hard deletes
public bool IsActive { get; set; } = true;

// Deactivate instead of delete
agent.IsActive = false;
await _dbContext.SaveChangesAsync();
```

**Authorization Policies:**
- Apply policies before sensitive operations:
  - `VaultApprove` - Admin only (vault transaction approvals)
  - `VaultAccess` / `VaultAdjust` - Admin, Supervisor
  - `UserManagement` - Admin only

**Mandatory Notes:**
```csharp
// Vault adjustments require notes ≥10 characters
if (string.IsNullOrWhiteSpace(model.Notes) || model.Notes.Length < 10)
    throw new InvalidOperationException("Notes required (min 10 characters)");
```

### Database Security

**Critical Relationships:**
```csharp
// Use Restrict for critical relationships (prevent accidental cascading deletes)
entity.HasOne(a => a.User)
    .WithOne(u => u.Agent)
    .HasForeignKey<Agent>(a => a.UserId)
    .OnDelete(DeleteBehavior.Restrict);
```

**Concurrency Control (Vault Operations):**
```csharp
// PostgreSQL row-level locks
await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

var vault = await _dbContext.Vaults
    .FromSqlRaw("SELECT * FROM \"Vaults\" WHERE \"Id\" = {0} FOR UPDATE", vaultId)
    .FirstOrDefaultAsync();
```

---

## Business Logic Constraints

### Agent Management Rules

See [docs/AGENT_DOMAIN.md](docs/AGENT_DOMAIN.md) for comprehensive Agent entity documentation.

**Key Constraints:**
- Agent code: 4 letters, uppercase, unique (auto-generated from user name)
- One agent per user (1:1, enforced by unique index on `UserId`)
- One wallet per type per agent (unique index on `AgentId + WalletTypeId`)
- Cannot delete wallets with non-zero balance or transaction history
- `Agent.BranchId` must sync with `User.BranchId` on updates

**Agent Code Generation:**
```csharp
// Format: First 2 chars of FirstName + First 2 chars of LastName
// "John Doe" → "JODO"
// "José García" → "JOGA" (strips diacritics)
// Collision handling: JOD1, JOD2, ..., JODA, JODB, ...
```

### Vault Operations Workflow

**Opening Cash Session:**
```
1. Vault withdrawal → 2. Populate agent wallets → 3. Open session
```

**Closing Cash Session:**
```
1. Zero agent wallets → 2. Deposit to vault → 3. Close session
```

**Manual Vault Adjustments:**
```
1. Create pending transaction → 2. 12-hour expiry → 3. Require approval
```

**Float Conservation:**
- Today's opening total MUST equal yesterday's closing total

### Discrepancy Workflow

**When Actual ≠ Expected:**
```csharp
if (actualTotal != expectedTotal)
{
    var discrepancy = new Discrepancy
    {
        CashSessionId = sessionId,
        ExpectedAmount = expectedTotal,
        ActualAmount = actualTotal,
        Difference = actualTotal - expectedTotal,
        Status = DiscrepancyStatus.Pending,
        Notes = tellerExplanation
    };
    _dbContext.Discrepancies.Add(discrepancy);
}
```

- Requires supervisor approval to close session
- Teller must provide notes (explanation)

---

## Testing Requirements

### Unit Tests (Required)

**All service methods must have unit tests.**

**Test Structure:**
```csharp
[Fact]
public async Task CreateAgentAsync_ValidUser_CreatesAgentAndUpdatesUser()
{
    // Arrange
    var user = new ApplicationUser { Id = "user1", FirstName = "John", LastName = "Doe" };
    await _dbContext.Users.AddAsync(user);
    await _dbContext.SaveChangesAsync();

    var model = new AgentFormModel { UserId = "user1", BranchId = 1 };

    // Act
    var result = await _service.CreateAgentAsync(model);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("JODO", result.Code);
}
```

**Test Coverage Requirements:**
- ✅ Success paths (happy path)
- ✅ Error cases (null handling, validation failures)
- ✅ Duplicate constraint violations
- ✅ Edge cases (short names, special characters, collisions)
- ✅ Constraint violations (e.g., wallet deletion with balance)

**Use In-Memory SQLite:**
```csharp
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlite("DataSource=:memory:")
    .Options;

using var dbContext = new ApplicationDbContext(options);
await dbContext.Database.OpenConnectionAsync();
await dbContext.Database.EnsureCreatedAsync();
```

### Integration Tests (Critical Paths)

**Required for:**
- Vault concurrency scenarios (race conditions, row locking)
- Cash count validation (float conservation)
- Multi-branch data isolation

---

## Anti-Patterns & Common Mistakes

### ❌ DO NOT

**Expose Domain Entities to UI:**
```csharp
// ❌ WRONG
public async Task<Agent> GetAgentAsync(long id)

// ✅ CORRECT
public async Task<AgentDetailDto> GetAgentAsync(long id)
```

**Create Repository Layers:**
```csharp
// ❌ WRONG - Don't create repositories
public class AgentRepository : IAgentRepository { }

// ✅ CORRECT - Services access DbContext directly
public class AgentService : IAgentService
{
    private readonly ApplicationDbContext _dbContext;
}
```

**Use Magic Strings:**
```csharp
// ❌ WRONG
if (user.Role == "Admin")

// ✅ CORRECT
if (user.Role == UserRole.Admin)
```

**Hard Delete Entities with History:**
```csharp
// ❌ WRONG
_dbContext.Agents.Remove(agent);

// ✅ CORRECT
agent.IsActive = false;
await _dbContext.SaveChangesAsync();
```

**Allow Self-Approval:**
```csharp
// ❌ WRONG - Creator approving own adjustment
await ApproveAsync(adjustmentId, creatorUserId);

// ✅ CORRECT - Validate approver ≠ creator
if (adjustment.CreatedBy == approverId)
    throw new InvalidOperationException("Cannot approve own adjustment");
```

**Update Immutable Audit Logs:**
```csharp
// ❌ WRONG
vaultTransaction.Amount = newAmount;
await _dbContext.SaveChangesAsync();

// ✅ CORRECT - Create new record
_dbContext.VaultTransactions.Add(new VaultTransaction { ... });
```

**Use Decimal Without Precision:**
```csharp
// ❌ WRONG
entity.Property(e => e.Balance);

// ✅ CORRECT
entity.Property(e => e.Balance).HasPrecision(18, 2);
```

**Use DateTime Instead of DateTimeOffset:**
```csharp
// ❌ WRONG
public DateTime CreatedAt { get; set; }

// ✅ CORRECT
public DateTimeOffset CreatedAt { get; set; }
```

### ✅ DO

**Use Transactions for Multi-Entity Changes:**
```csharp
using var transaction = await _dbContext.Database.BeginTransactionAsync();
try
{
    // Multiple changes
    await _dbContext.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

**Validate Unique Constraints Before Insert:**
```csharp
var existingCode = await _dbContext.Agents
    .AnyAsync(a => a.Code == code);
if (existingCode)
    throw new InvalidOperationException($"Agent code '{code}' already exists");
```

**Return Meaningful Error Messages:**
```csharp
// ✅ GOOD
throw new InvalidOperationException("Cannot delete wallet with non-zero balance");

// ❌ BAD
throw new Exception("Error");
```

**Sync Related Entities:**
```csharp
// When updating Agent.BranchId, sync User.BranchId
agent.BranchId = model.BranchId;
var user = await _dbContext.Users.FindAsync(agent.UserId);
user.BranchId = model.BranchId;
await _dbContext.SaveChangesAsync();
```

**Order Lists by Meaningful Fields:**
```csharp
var agents = await _dbContext.Agents
    .OrderBy(a => a.Code)  // Not by Id
    .ToListAsync();
```

---

## Common Development Workflows

### Git Branching Workflow (Required)

**ALWAYS create a new branch for feature implementations.** Do not make changes directly on `main`.

**Branch Creation:**
```bash
# Create and switch to a new feature branch
git checkout -b feature/{feature-name}

# For bug fixes
git checkout -b fix/{bug-description}

# For refactoring
git checkout -b refactor/{refactor-description}
```

**Branch Naming Conventions:**
- `feature/{feature-name}` - New features (e.g., `feature/agent-wallet-export`)
- `fix/{bug-description}` - Bug fixes (e.g., `fix/vault-balance-calculation`)
- `refactor/{description}` - Code refactoring (e.g., `refactor/cash-session-service`)

**Workflow:**
1. Create a new branch from `main`
2. Make your changes with atomic commits
3. Ensure all tests pass
4. Merge into `main` when feature is complete

**Merging to Main:**
```bash
# Ensure you're on your feature branch and it's up to date
git checkout feature/{feature-name}
git pull origin main

# Switch to main and merge
git checkout main
git merge feature/{feature-name}

# Push to remote
git push origin main

# Delete the feature branch (optional)
git branch -d feature/{feature-name}
```

**Commit Message Format:**
```
type: short description

Optional longer description explaining what and why.
```

Types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`

**Example:**
```
feat: add wallet export functionality

Adds CSV export for agent wallets with date range filtering.
```

---

### Adding a New Feature Slice

1. **Create Feature Folder:**
   ```
   Features/{FeatureName}/
   ```

2. **Define DTOs:**
   ```csharp
   // Features/{FeatureName}/{Feature}Dtos.cs
   public class {Feature}ListItemDto { }
   public class {Feature}DetailDto { }
   public class {Feature}FormModel { }
   ```

3. **Create Service Interface:**
   ```csharp
   // Features/{FeatureName}/I{Feature}Service.cs
   public interface I{Feature}Service
   {
       Task<List<{Feature}ListItemDto>> GetAllAsync();
   }
   ```

4. **Implement Service:**
   ```csharp
   // Features/{FeatureName}/{Feature}Service.cs
   public class {Feature}Service : I{Feature}Service
   {
       private readonly ApplicationDbContext _dbContext;

       public {Feature}Service(ApplicationDbContext dbContext)
       {
           _dbContext = dbContext;
       }
   }
   ```

5. **Register in Program.cs:**
   ```csharp
   builder.Services.AddScoped<I{Feature}Service, {Feature}Service>();
   ```

6. **Create Blazor Component:**
   ```
   Components/Pages/{Feature}.razor
   ```

7. **Add Navigation Link (if applicable):**
   ```razor
   <!-- NavMenu.razor -->
   <MudNavLink Href="/feature" Icon="@Icons.Material.Filled.Icon">
       Feature Name
   </MudNavLink>
   ```

8. **Write Unit Tests:**
   ```
   tests/.../Services/{Feature}ServiceTests.cs
   ```

### Adding a Domain Entity

1. **Create Entity:**
   ```csharp
   // Shared/Domain/Entities/{Entity}.cs
   public class {Entity}
   {
       public long Id { get; set; }
       public DateTimeOffset CreatedAt { get; set; }
   }
   ```

2. **Add DbSet to ApplicationDbContext:**
   ```csharp
   public DbSet<{Entity}> {Entities} { get; set; }
   ```

3. **Configure in OnModelCreating:**
   ```csharp
   modelBuilder.Entity<{Entity}>(entity =>
   {
       entity.HasKey(e => e.Id);
       entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
       entity.HasIndex(e => e.Name).IsUnique();
   });
   ```

4. **Create Migration:**
   ```bash
   cd EastSeat.Agenti.Web
   dotnet ef migrations add Add{Entity} --output-dir Data/Migrations
   ```

5. **Apply Migration:**
   ```bash
   dotnet ef database update
   ```

6. **Add Related DTOs and Service Methods:**
   - Update relevant feature DTOs
   - Add service methods for CRUD operations

### Modifying Existing Business Logic

1. **Read Existing Tests:**
   - Understand current behavior from unit tests
   - Identify what will change

2. **Update Service Method:**
   - Modify business logic
   - Ensure backward compatibility or plan migration

3. **Update/Add Unit Tests:**
   - Modify existing tests for new behavior
   - Add new tests for new scenarios

4. **Update DTOs (if needed):**
   - Add new properties
   - Update mapping logic

5. **Manual Testing:**
   ```bash
   cd EastSeat.Agenti.Web
   dotnet run
   # Navigate to https://localhost:7001
   ```

---

## Database Patterns

### EF Core Query Patterns

**Include Navigation Properties:**
```csharp
var agents = await _dbContext.Agents
    .Include(a => a.User)
    .Include(a => a.Wallets)
        .ThenInclude(w => w.WalletType)
    .OrderBy(a => a.Code)
    .ToListAsync();
```

**Validate Unique Constraints:**
```csharp
var exists = await _dbContext.Agents
    .AnyAsync(a => a.Code == code);
if (exists)
    throw new InvalidOperationException("Code already exists");
```

**Transactional Updates:**
```csharp
using var transaction = await _dbContext.Database.BeginTransactionAsync();
try
{
    // Multiple operations
    await _dbContext.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

**Projection to DTOs:**
```csharp
var agents = await _dbContext.Agents
    .Select(a => new AgentListItemDto
    {
        Id = a.Id,
        Code = a.Code,
        FullName = $"{a.User.FirstName} {a.User.LastName}",
        WalletCount = a.Wallets.Count,
        TotalBalance = a.Wallets.Sum(w => w.Balance)
    })
    .ToListAsync();
```

### PostgreSQL-Specific Patterns

**Row-Level Locking:**
```csharp
// FOR UPDATE lock
var vault = await _dbContext.Vaults
    .FromSqlRaw("SELECT * FROM \"Vaults\" WHERE \"Id\" = {0} FOR UPDATE", id)
    .FirstOrDefaultAsync();
```

**Serializable Isolation:**
```csharp
await _dbContext.Database.BeginTransactionAsync(
    System.Data.IsolationLevel.Serializable);
```

**Enum as String:**
```csharp
entity.Property(e => e.Status).HasConversion<string>();
```

---

## Dependency Injection Patterns

### Service Registration

**Standard Service:**
```csharp
builder.Services.AddScoped<IAgentService, AgentService>();
```

**Service with Dependencies:**
```csharp
public class VaultService : IVaultService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAgentService _agentService;  // Service can depend on other services

    public VaultService(ApplicationDbContext dbContext, IAgentService agentService)
    {
        _dbContext = dbContext;
        _agentService = agentService;
    }
}
```

**Injection Rules:**
- ✅ Always inject interfaces, not concrete implementations
- ✅ Use constructor injection exclusively
- ✅ Scoped lifetime for services accessing DbContext
- ❌ Never use service locator pattern

---

## Migration Guidelines

### Creating Migrations

```bash
cd EastSeat.Agenti.Web
dotnet ef migrations add MigrationName --output-dir Data/Migrations
dotnet ef database update
```

### Migration Best Practices

**One Logical Change Per Migration:**
```
✅ GOOD: AddWalletExpiration, AddVaultApprovalWorkflow
❌ BAD: UpdateDatabase, Changes, Fixes
```

**Test Migrations:**
```bash
# Test on fresh database
docker-compose down -v
docker-compose up -d
dotnet ef database update
```

**Never Modify Applied Migrations:**
- ❌ Don't edit migration files after applying
- ✅ Create new migration to correct issues

**Seed Critical Data in OnModelCreating:**
```csharp
// Seed in ApplicationDbContext.OnModelCreating, not in migrations
modelBuilder.Entity<WalletType>().HasData(
    new WalletType { Id = 1, Name = "Cash", IsActive = true }
);
```

---

## Error Handling & Validation

### Service-Level Validation

**Input Validation:**
```csharp
if (model.UserId == null)
    throw new ArgumentNullException(nameof(model.UserId));

if (string.IsNullOrWhiteSpace(model.Code))
    throw new ArgumentException("Code is required");
```

**Business Rule Violations:**
```csharp
throw new InvalidOperationException("Cannot delete wallet with non-zero balance");
```

**Authorization Failures:**
```csharp
throw new UnauthorizedAccessException("Insufficient permissions");
```

**Not Found Scenarios:**
```csharp
// Return null, let caller decide error handling
var agent = await _dbContext.Agents.FindAsync(id);
return agent == null ? null : MapToDto(agent);
```

### Component-Level Error Handling

```csharp
@code {
    private async Task CreateAgentAsync()
    {
        try
        {
            await Service.CreateAgentAsync(model);
            Snackbar.Add("Agent created successfully", Severity.Success);
            NavigationManager.NavigateTo("/agents");
        }
        catch (InvalidOperationException ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
        catch (Exception ex)
        {
            Snackbar.Add("An unexpected error occurred", Severity.Error);
            // Log exception
        }
    }
}
```

---

## Summary Checklist

When writing code for Agenti, ensure you:

- [ ] Follow Vertical Slice Architecture (feature folder structure)
- [ ] Services inject `ApplicationDbContext` directly (no repositories)
- [ ] Return DTOs from services, never domain entities
- [ ] Use `decimal(18,2)` for money, `DateTimeOffset` for timestamps
- [ ] Store enums as strings in database
- [ ] Use `DeleteBehavior.Restrict` for critical relationships
- [ ] Soft delete with `IsActive` flags, not hard deletes
- [ ] Validate unique constraints before insert
- [ ] Use transactions for multi-entity changes
- [ ] Sync related entities (e.g., `Agent.BranchId` ↔ `User.BranchId`)
- [ ] Write unit tests for all service methods
- [ ] Never update immutable audit logs
- [ ] Enforce dual approval for vault adjustments
- [ ] Register services as `Scoped` in `Program.cs`
- [ ] Use MudBlazor components in Blazor pages
- [ ] Apply authorization policies to protected pages

---

## Additional Resources

- **Setup & Commands:** [CLAUDE.md](CLAUDE.md)
- **Agent Entity Documentation:** [docs/AGENT_DOMAIN.md](docs/AGENT_DOMAIN.md)
- **MudBlazor Documentation:** https://mudblazor.com/
- **EF Core Documentation:** https://learn.microsoft.com/en-us/ef/core/

# Agent Domain Documentation

## Overview

The **Agent** entity represents a teller or banking agent in the Agenti ERP system. Agents are responsible for daily cash operations, handling transactions, managing multiple wallets, and interacting with the central branch vault.

**Core Purpose:** Enable secure, auditable cash handling operations for banking agencies in Uganda.

**Key Responsibilities:**
- Opening and closing daily cash sessions
- Managing multiple wallet types (Cash, Mobile Money, Bank)
- Processing customer transactions (deposits, withdrawals, transfers)
- Reconciling cash counts with vault operations
- Handling discrepancies and approval workflows

**Target Audience:** Developers, architects, business analysts, and operations teams.

---

## Entity Structure

### Agent Entity Definition

**Location:** `Shared/Domain/Entities/Agent.cs`

```csharp
public class Agent
{
    public long Id { get; set; }
    public string UserId { get; set; }           // FK to ApplicationUser (1:1)
    public string Code { get; set; }             // Unique 4-char code
    public long? BranchId { get; set; }          // Optional branch assignment
    public bool IsActive { get; set; }           // Active/Inactive status
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public ApplicationUser? User { get; set; }
    public ICollection<Wallet> Wallets { get; set; }
    public ICollection<CashSession> CashSessions { get; set; }
}
```

### Field Descriptions

| Field | Type | Description | Constraints |
|-------|------|-------------|-------------|
| `Id` | `long` | Primary key | Auto-increment |
| `UserId` | `string` | Link to ApplicationUser for authentication | Required, Unique index |
| `Code` | `string` | 4-letter unique identifier (e.g., "JODO") | Required, Unique index, Max 10 chars |
| `BranchId` | `long?` | Branch assignment for multi-branch support | Nullable, FK to Branch |
| `IsActive` | `bool` | Soft delete flag (true = active, false = deactivated) | Required, Default: true |
| `CreatedAt` | `DateTimeOffset` | Record creation timestamp (UTC) | Required |
| `UpdatedAt` | `DateTimeOffset?` | Last modification timestamp (UTC) | Nullable |

### Navigation Properties

- **User** (`ApplicationUser`): 1:1 relationship, provides authentication and profile info
- **Wallets** (`ICollection<Wallet>`): 1:N relationship, one wallet per type maximum
- **CashSessions** (`ICollection<CashSession>`): 1:N relationship, one session per day

---

## Relationships

### Agent ↔ ApplicationUser (1:1, Required)

**Configuration:** `ApplicationDbContext.cs:138-145`

```csharp
entity.HasOne(a => a.User)
    .WithOne(u => u.Agent)
    .HasForeignKey<Agent>(a => a.UserId)
    .OnDelete(DeleteBehavior.Restrict);

entity.HasIndex(e => e.UserId).IsUnique();
```

**Characteristics:**
- Every agent must be linked to exactly one user account
- User provides authentication (ASP.NET Identity)
- User stores profile: FirstName, LastName, Email, PhoneNumber, Role, ThemePreference
- `DeleteBehavior.Restrict`: Cannot delete user if agent exists
- Unique index on `UserId` enforces 1:1 relationship

**ApplicationUser Fields:**
```csharp
public long? AgentId { get; set; }      // Link back to Agent (nullable)
public long? BranchId { get; set; }     // Synced from Agent.BranchId
public UserRole Role { get; set; }      // Admin, Supervisor, Agent
public bool IsActive { get; set; }
```

### Agent → Wallet (1:N)

**Configuration:** `ApplicationDbContext.cs:159-162`

```csharp
entity.HasOne(e => e.Agent)
    .WithMany(a => a.Wallets)
    .HasForeignKey(e => e.AgentId)
    .OnDelete(DeleteBehavior.Restrict);

entity.HasIndex(e => new { e.AgentId, e.WalletTypeId }).IsUnique();
```

**Characteristics:**
- Each agent has multiple wallets (one per wallet type maximum)
- Unique constraint: `(AgentId, WalletTypeId)` ensures one wallet per type per agent
- Wallets store balances for: Cash, Mobile Money (MTN, Airtel), Bank accounts
- `DeleteBehavior.Restrict`: Cannot delete agent with wallets

**Wallet Types:**
- Cash (with denominations support)
- MTN Mobile Money
- Airtel Mobile Money
- Bank Transfer

### Agent → CashSession (1:N)

**Configuration:** `ApplicationDbContext.cs:170-173`

```csharp
entity.HasOne(e => e.Agent)
    .WithMany(a => a.CashSessions)
    .HasForeignKey(e => e.AgentId)
    .OnDelete(DeleteBehavior.Restrict);

entity.HasIndex(e => new { e.AgentId, e.SessionDate }).IsUnique();
```

**Characteristics:**
- Each agent opens one cash session per day
- Unique constraint: `(AgentId, SessionDate)` enforces one session per agent per day
- Cash sessions track opening/closing counts, transactions, and discrepancies
- `DeleteBehavior.Restrict`: Cannot delete agent with session history

**CashSession Statuses:**
- `Open`: Session in progress
- `Closed`: Session completed successfully
- `PendingApproval`: Session has discrepancy, awaiting supervisor approval

### Agent → Branch (N:1, Optional)

**Characteristics:**
- Multiple agents can belong to the same branch
- Each branch has exactly one Vault for central cash management
- BranchId is nullable (optional assignment)
- `Agent.BranchId` synced with `User.BranchId` for claims-based authorization

---

## Agent Code Generation Algorithm

### Overview

Agent codes are **auto-generated** from the user's name to ensure uniqueness and consistency.

**Format:** 4 uppercase letters (First 2 chars of FirstName + First 2 chars of LastName)

**Implementation:** `AgentService.cs:156-204`

### Generation Rules

#### 1. Base Code Generation

```csharp
// Format: First 2 letters of FirstName + First 2 letters of LastName
"John Doe"        → "JODO"
"Alice Smith"     → "ALSM"
"José García"     → "JOGA"  // Strips diacritics
"Michael O'Brien" → "MIOB"  // Strips non-letters
```

**Process:**
1. Take first 2 letters of `FirstName`
2. Take first 2 letters of `LastName`
3. Remove non-letter characters (apostrophes, hyphens, etc.)
4. Strip diacritics (José → Jose)
5. Convert to uppercase
6. Pad with 'X' if name is too short

#### 2. Short Name Handling

```csharp
"A B"    → "AXBX"  // Single-letter names padded with 'X'
"X"      → "XXXX"  // Empty last name → all 'X'
```

#### 3. Collision Handling

If base code already exists, system tries suffixes:

**Numeric Suffixes (1-9):**
```
JODO exists → Try JOD1
JOD1 exists → Try JOD2
...
JOD9 exists → Try alphabetic
```

**Alphabetic Suffixes (A-Z):**
```
JOD9 exists → Try JODA
JODA exists → Try JODB
...
JODZ exists → Use timestamp-based suffix
```

**Last Resort:**
```csharp
// Timestamp-based unique suffix
JODO + DateTime.UtcNow.Ticks.ToString().Substring(0, 2)
```

### Test Cases

**Source:** `tests/.../Services/AgentServiceTests.cs:156-204`

```csharp
[Theory]
[InlineData("John", "Doe", "JODO")]
[InlineData("Alice", "Smith", "ALSM")]
[InlineData("A", "B", "AXBX")]
[InlineData("X", "", "XXXX")]
[InlineData("Michael", "O'Brien", "MIOB")]
[InlineData("José", "García", "JOGA")]
```

---

## Business Rules & Validations

### Agent Creation

**Service Method:** `AgentService.CreateAgentAsync()`

**Validation Rules:**

| Rule | Check | Error |
|------|-------|-------|
| UserId provided | `model.UserId != null` | `ArgumentNullException` |
| User exists | `await _dbContext.Users.FindAsync(userId)` | `InvalidOperationException` |
| User not already linked | `user.AgentId == null` | `InvalidOperationException` |
| Code uniqueness | Auto-handled via collision algorithm | N/A |

**Success Flow:**
1. Validate UserId exists and user is not already linked
2. Generate unique 4-letter code from user's name
3. Create Agent entity
4. Update User entity with `AgentId` and `BranchId`
5. Save both entities in transaction
6. Return `AgentDetailDto`

**Transaction Example:**
```csharp
using var transaction = await _dbContext.Database.BeginTransactionAsync();
try
{
    // Create Agent
    var agent = new Agent { UserId = model.UserId, Code = code, ... };
    _dbContext.Agents.Add(agent);
    await _dbContext.SaveChangesAsync();

    // Update User
    user.AgentId = agent.Id;
    user.BranchId = model.BranchId;
    await _dbContext.SaveChangesAsync();

    await transaction.CommitAsync();
}
catch { await transaction.RollbackAsync(); throw; }
```

### Agent Update

**Service Method:** `AgentService.UpdateAgentAsync()`

**Editable Fields:**
- ✅ `Code` (with duplicate check)
- ✅ `BranchId` (synced to User.BranchId)
- ✅ `IsActive`

**Immutable Fields:**
- ❌ `UserId` (cannot change user-agent relationship)
- ❌ `Id` (primary key)

**Validation Rules:**
- Code uniqueness checked if code is changed
- User.BranchId synced on Agent.BranchId change

### Agent Deletion

**Constraints:**
- ❌ Cannot delete agent with wallets (Restrict constraint)
- ❌ Cannot delete agent with cash session history (Restrict constraint)
- ✅ Should deactivate instead (set `IsActive = false`)

**Recommended Approach:**
```csharp
// Instead of deleting
agent.IsActive = false;
await _dbContext.SaveChangesAsync();
```

### Agent Deactivation

**Service Method:** `AgentService.ToggleAgentStatusAsync()`

**Effects:**
- Deactivated agents (`IsActive = false`) cannot open new cash sessions
- Cannot create new wallets
- Historical data remains accessible
- Can be reactivated later

---

## Wallet Management

### Wallet Creation

**Service Method:** `AgentService.AddWalletAsync()`

**Validation Rules:**

| Rule | Check | Error |
|------|-------|-------|
| Agent exists | `await _dbContext.Agents.FindAsync(agentId)` | `InvalidOperationException` |
| Wallet type exists | `await _dbContext.WalletTypes.FindAsync(typeId)` | `InvalidOperationException` |
| Wallet type active | `walletType.IsActive == true` | `InvalidOperationException` |
| No duplicate type | Check `(AgentId, WalletTypeId)` unique | `InvalidOperationException` |

**Success Flow:**
1. Validate agent and wallet type exist and are active
2. Check for duplicate wallet type (unique constraint)
3. Create Wallet entity with initial balance
4. Return `AgentWalletDto`

### Wallet Update

**Service Method:** `AgentService.UpdateWalletAsync()`

**Editable Fields:**
- ✅ `Name` (display name for wallet)
- ✅ `Currency` (UGX, USD, etc.)
- ✅ `IsActive` (soft delete)

**Immutable Fields:**
- ❌ `Balance` (only changes via cash counts/transactions)
- ❌ `WalletTypeId` (cannot change wallet type)
- ❌ `AgentId` (cannot reassign wallet to different agent)

### Wallet Deletion

**Service Method:** `AgentService.DeleteWalletAsync()`

**Pre-Deletion Checks:**

```csharp
// Must have zero balance
if (wallet.Balance != 0)
    throw new InvalidOperationException("Cannot delete wallet with non-zero balance");

// Must have no transaction history
if (wallet.TransactionsFrom.Any() || wallet.TransactionsTo.Any())
    throw new InvalidOperationException("Cannot delete wallet with transaction history");

// Must have no cash count details
if (wallet.CashCountDetails.Any())
    throw new InvalidOperationException("Cannot delete wallet with cash count history");
```

**Recommended Alternative:**
```csharp
// Deactivate instead of delete
await ToggleWalletStatusAsync(walletId);
```

### Wallet Deactivation

**Service Method:** `AgentService.ToggleWalletStatusAsync()`

**Effects:**
- Deactivated wallets (`IsActive = false`) cannot be used in new transactions
- Historical data preserved
- Can be reactivated later

---

## Agent Lifecycle

### 1. User Creation

**Feature:** Users management

**Process:**
1. Admin creates `ApplicationUser` account
2. Assigns `UserRole` (Admin, Supervisor, Agent)
3. Sets profile: FirstName, LastName, Email, PhoneNumber
4. Marks `IsActive = true`
5. User available for agent assignment (if role = Agent)

### 2. Agent Assignment

**Feature:** Agents management → Create Agent

**Process:**
1. Admin navigates to Agents page
2. Clicks "Create Agent" button
3. Selects available user from dropdown (users without `AgentId`)
4. System auto-generates code preview (e.g., "JODO" for "John Doe")
5. Admin assigns branch (optional)
6. Clicks "Create" button
7. Transaction creates Agent + updates User (`AgentId`, `BranchId`)
8. Success message + redirect to agent detail page

**UI Component:** `Components/Pages/Agents.razor`

### 3. Wallet Configuration

**Feature:** Agent detail → Manage Wallets

**Process:**
1. Admin/Supervisor navigates to agent detail page
2. Clicks "Add Wallet" button
3. Selects wallet type (Cash, MTN, Airtel, Bank)
4. Enters wallet name (e.g., "Main Cash Drawer")
5. Enters currency (UGX, USD)
6. Enters initial balance (if applicable)
7. Clicks "Save"
8. Wallet created and displayed in agent's wallet list

**Available Wallet Types:**
- 💵 Cash (supports denominations)
- 📱 MTN Mobile Money
- 📱 Airtel Money
- 🏦 Bank Transfer

**UI Component:** `Components/Pages/AgentDetail.razor`

### 4. Daily Operations

#### Opening Cash Session

**Feature:** Cash Counts → Opening Count

**Workflow:**
```
1. Agent performs opening cash count
2. System validates: Today's opening = Yesterday's closing (float conservation)
3. Vault withdrawal occurs (VaultService.WithdrawForSessionAsync)
4. Agent wallets populated with opening balances
5. Session opened (Status = Open)
```

**Validation:**
```csharp
// Float conservation check
if (todayOpeningTotal != yesterdayClosingTotal)
{
    throw new InvalidOperationException(
        $"Opening total ({todayOpeningTotal}) must equal previous closing ({yesterdayClosingTotal})"
    );
}
```

#### During Session

**Operations:**
- Process customer transactions (deposits, withdrawals, transfers)
- Wallets updated in real-time
- All transactions logged immutably in Transaction table
- Agent can view current wallet balances

#### Closing Cash Session

**Feature:** Cash Counts → Closing Count

**Workflow:**
```
1. Agent performs closing cash count
2. System compares actual vs. expected totals
3. If match: Wallets zeroed → Total returned to vault → Session closed
4. If mismatch: Discrepancy created → Requires supervisor approval
```

**Discrepancy Handling:**
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
        Notes = tellerExplanation  // Required
    };
    _dbContext.Discrepancies.Add(discrepancy);
    // Session remains open until supervisor approves
}
```

### 5. Deactivation

**Process:**
1. Admin toggles `IsActive = false`
2. Agent cannot open new cash sessions
3. Cannot create new wallets
4. Historical data remains accessible for audit
5. Can be reactivated by toggling `IsActive = true`

**Effects:**
- Agent code preserved (cannot be reused)
- All wallets remain in database
- All transaction history intact
- User account unaffected (can still login if user is active)

---

## Multi-Branch Support

### Claims-Based Authorization

**Implementation:** `Components/Account/BranchIdClaimsTransformer.cs`

**Process:**
1. User authenticates (ASP.NET Identity)
2. `BranchIdClaimsTransformer` adds "BranchId" claim to `ClaimsPrincipal`
3. Claim value read from `ApplicationUser.BranchId`
4. Claim available throughout user's session

**Usage:**
```csharp
// In services or components
var branchId = User.FindFirst("BranchId")?.Value;
var branchIdLong = long.Parse(branchId);

// Filter data by branch
var agents = await _dbContext.Agents
    .Where(a => a.BranchId == branchIdLong)
    .ToListAsync();
```

### Branch Assignment

**Characteristics:**
- `Agent.BranchId` is nullable (optional assignment)
- `User.BranchId` synced with `Agent.BranchId` on create/update
- Each branch has exactly one `Vault` for central cash management
- Branch assignment enables multi-branch deployments

**Synchronization:**
```csharp
// When updating Agent.BranchId
agent.BranchId = newBranchId;
var user = await _dbContext.Users.FindAsync(agent.UserId);
user.BranchId = newBranchId;
await _dbContext.SaveChangesAsync();
```

---

## Service Layer API

### Agent Management Methods

**Interface:** `Features/Agents/IAgentService.cs`

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetAgentsAsync()` | `Task<List<AgentListItemDto>>` | List all agents with wallet counts and total balance |
| `GetAgentAsync(long id)` | `Task<AgentDetailDto?>` | Get agent details including all wallets |
| `GetAvailableUsersAsync()` | `Task<List<AvailableUserDto>>` | Get users without agent assignments (active only) |
| `CreateAgentAsync(model)` | `Task<AgentDetailDto>` | Create agent from user + auto-generate code |
| `UpdateAgentAsync(model)` | `Task<AgentDetailDto>` | Update agent code, branch, status |
| `ToggleAgentStatusAsync(long id)` | `Task` | Toggle IsActive flag |

### Wallet Management Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddWalletAsync(model)` | `Task<AgentWalletDto>` | Create wallet for agent (only 1 per type) |
| `UpdateWalletAsync(model)` | `Task<AgentWalletDto>` | Update wallet metadata (NOT balance) |
| `ToggleWalletStatusAsync(long id)` | `Task` | Deactivate/activate wallet |
| `DeleteWalletAsync(long id)` | `Task` | Remove wallet (only if zero balance + no history) |
| `GetAgentWalletsAsync(long id)` | `Task<List<AgentWalletDto>>` | Get ordered wallets by type and name |
| `GetAvailableWalletTypesForAgentAsync(long id)` | `Task<List<WalletTypeDto>>` | Get unassigned wallet types |

### Lookup Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetWalletTypesAsync()` | `Task<List<WalletTypeDto>>` | Get all active wallet types |
| `GetBranchesAsync()` | `Task<List<BranchDto>>` | Get all branches for assignment |

---

## Data Transfer Objects (DTOs)

**Location:** `Features/Agents/AgentDtos.cs`

### AgentListItemDto

**Purpose:** List view display (Agents.razor table)

```csharp
public class AgentListItemDto
{
    public long Id { get; set; }
    public string Code { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public long? BranchId { get; set; }
    public bool IsActive { get; set; }
    public int WalletCount { get; set; }
    public decimal TotalBalance { get; set; }
}
```

### AgentDetailDto

**Purpose:** Detail view (AgentDetail.razor)

```csharp
public class AgentDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public long? BranchId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<AgentWalletDto> Wallets { get; set; }
}
```

### AgentFormModel

**Purpose:** Create/edit forms

```csharp
public class AgentFormModel
{
    public long? Id { get; set; }           // Null for new, populated for edit
    public string? UserId { get; set; }     // Required for new agent
    public string Code { get; set; }        // Auto-generated for new, editable for existing
    public long? BranchId { get; set; }
    public bool IsActive { get; set; } = true;
}
```

### AgentWalletDto

**Purpose:** Wallet display in agent detail

```csharp
public class AgentWalletDto
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string WalletTypeName { get; set; }
    public string Icon { get; set; }        // 💵, 📱, 🏦
    public string Currency { get; set; }
    public decimal Balance { get; set; }
    public bool IsActive { get; set; }
}
```

### AvailableUserDto

**Purpose:** User selection in create agent dialog

```csharp
public class AvailableUserDto
{
    public string Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string FullName { get; set; }
    public string DisplayName { get; set; }  // "{FullName} ({Email})"
}
```

---

## UI Components

### Agents.razor

**Route:** `/agents`

**Location:** `Components/Pages/Agents.razor` (629 lines)

**Features:**
- MudTable with agent list
- Displays: Code, Name, Email, Phone, Wallet count, Total balance, Status
- Actions: View, Manage Wallets, Edit, Toggle status
- Create agent dialog with user selection and code preview
- Wallet management dialog (add/edit/delete/toggle)

**Key Components:**
- `MudTable<AgentListItemDto>` - Agent list table
- `MudDialog` - Create agent form
- `MudDialog` - Wallet management form
- `MudChip` - Status indicators (Active/Inactive)
- `MudSnackbar` - Success/error notifications

### AgentDetail.razor

**Route:** `/agents/{AgentId:long}`

**Location:** `Components/Pages/AgentDetail.razor` (383 lines)

**Layout:**
- **Left Panel:** Agent profile card
  - Avatar (initials)
  - Full name
  - Agent code
  - Email, Phone
  - Created date
  - Total balance across all wallets

- **Right Panel:** Wallet details
  - Wallet cards with icons (💵 Cash, 📱 Mobile, 🏦 Bank)
  - Balance display
  - Add/Edit/Delete/Toggle actions

**Key Components:**
- `MudCard` - Agent profile card
- `MudGrid` - Wallet grid layout
- `MudDialog` - Wallet form
- `MudIcon` - Wallet type icons

---

## Database Configuration

**Location:** `Data/ApplicationDbContext.cs`

### Agent Entity Configuration

```csharp
modelBuilder.Entity<Agent>(entity =>
{
    entity.HasKey(e => e.Id);

    // 1:1 with ApplicationUser
    entity.HasOne(a => a.User)
        .WithOne(u => u.Agent)
        .HasForeignKey<Agent>(a => a.UserId)
        .OnDelete(DeleteBehavior.Restrict);

    // Unique constraints
    entity.HasIndex(e => e.Code).IsUnique();
    entity.HasIndex(e => e.UserId).IsUnique();

    // String lengths
    entity.Property(e => e.Code).HasMaxLength(10).IsRequired();
});
```

### Wallet Unique Constraint

```csharp
modelBuilder.Entity<Wallet>(entity =>
{
    entity.HasIndex(e => new { e.AgentId, e.WalletTypeId }).IsUnique();

    entity.Property(e => e.Balance).HasPrecision(18, 2);
});
```

### CashSession Unique Constraint

```csharp
modelBuilder.Entity<CashSession>(entity =>
{
    entity.HasIndex(e => new { e.AgentId, e.SessionDate }).IsUnique();
});
```

---

## Testing Strategy

### Unit Test Coverage

**Test File:** `tests/EastSeat.Agenti.UnitTests/Services/AgentServiceTests.cs` (1265 lines)

**Test Categories:**

#### Agent Creation Tests (12 tests)
- Valid agent creation with auto-code generation
- Code generation from various name formats
- Duplicate code collision handling (numeric/alphabetic suffixes)
- Error cases: missing UserId, non-existent user, user already has agent

#### Code Generation Tests (8 tests)
- Normal names → "JODO", "ALSM"
- Short names → "AXBX", "XXXX"
- Special characters → "MIOB" (O'Brien)
- Diacritics → "JOGA" (García)
- Collision handling → JOD1, JOD2, JODA, JODB

#### Agent Update Tests (5 tests)
- Update code with duplicate check
- Update branch (syncs User.BranchId)
- Update status
- Error handling for missing/non-existent agents

#### Wallet Management Tests (10 tests)
- Add wallet (valid, duplicate type prevention, inactive type rejection)
- Update wallet metadata (not balance)
- Delete wallet (zero balance constraint, no history constraint)
- Toggle wallet status
- Get available wallet types (unassigned, active only)

#### Test Pattern

```csharp
[Fact]
public async Task CreateAgentAsync_ValidUser_CreatesAgentAndUpdatesUser()
{
    // Arrange
    var user = new ApplicationUser
    {
        Id = "user1",
        FirstName = "John",
        LastName = "Doe"
    };
    await _dbContext.Users.AddAsync(user);
    await _dbContext.SaveChangesAsync();

    var model = new AgentFormModel { UserId = "user1", BranchId = 1 };

    // Act
    var result = await _service.CreateAgentAsync(model);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("JODO", result.Code);
    var updatedUser = await _dbContext.Users.FindAsync("user1");
    Assert.NotNull(updatedUser.AgentId);
    Assert.Equal(1, updatedUser.BranchId);
}
```

---

## Common Scenarios & Solutions

### Scenario 1: Create Agent for New User

**Steps:**
1. Admin creates ApplicationUser via Users feature
2. Admin navigates to Agents page → Click "Create Agent"
3. Select user from dropdown → System previews code (e.g., "JODO")
4. Assign branch (optional) → Click "Create"

**Result:**
- Agent created with auto-generated code
- User linked to agent (`User.AgentId = agent.Id`)
- User.BranchId synced with Agent.BranchId
- Success message displayed

### Scenario 2: Handle Duplicate Agent Code

**Problem:**
- User "John Doe" exists with code "JODO"
- New user "Jonathan Doherty" would generate "JODO"

**Solution:**
- System automatically tries: JOD1, JOD2, ..., JOD9
- Then alphabetic: JODA, JODB, ..., JODZ
- Last resort: Timestamp-based unique suffix
- Code uniqueness guaranteed by database constraint + service validation

### Scenario 3: Delete Wallet with Balance

**Attempt:**
```csharp
await _service.DeleteWalletAsync(walletId);
```

**Result:**
```
InvalidOperationException: Cannot delete wallet with non-zero balance
```

**Solutions:**
1. **Zero out balance:** Close cash session to return balance to vault
2. **Deactivate instead:** `await _service.ToggleWalletStatusAsync(walletId)`

### Scenario 4: Agent Code Collision

**Example:**
- Existing: "John Doe" → "JODO"
- New: "Jonathan Doherty" → "JODO" (collision)

**Resolution:**
```
System auto-resolves:
1. Try "JOD1" (available) → Use "JOD1"
2. If "JOD1" exists → Try "JOD2"
3. Continue until unique code found
```

### Scenario 5: Sync Agent and User BranchId

**Problem:**
- Agent.BranchId updated but User.BranchId not synced

**Solution:**
```csharp
// AgentService.UpdateAgentAsync handles sync automatically
agent.BranchId = model.BranchId;
var user = await _dbContext.Users.FindAsync(agent.UserId);
user.BranchId = model.BranchId;
await _dbContext.SaveChangesAsync();
```

---

## File Inventory

### Core Implementation

| File | Lines | Description |
|------|-------|-------------|
| `Shared/Domain/Entities/Agent.cs` | ~30 | Entity definition |
| `Features/Agents/IAgentService.cs` | ~40 | Service interface |
| `Features/Agents/AgentService.cs` | 465 | Service implementation |
| `Features/Agents/AgentDtos.cs` | 149 | Data transfer objects |

### UI Components

| File | Lines | Description |
|------|-------|-------------|
| `Components/Pages/Agents.razor` | 629 | List & management UI |
| `Components/Pages/AgentDetail.razor` | 383 | Detail view UI |

### Related Entities

| File | Description |
|------|-------------|
| `Data/ApplicationUser.cs` | User entity with AgentId FK |
| `Shared/Domain/Entities/Wallet.cs` | Wallet entity |
| `Shared/Domain/Entities/CashSession.cs` | Cash session entity |
| `Shared/Domain/Entities/Branch.cs` | Branch entity |

### Infrastructure

| File | Description |
|------|-------------|
| `Data/ApplicationDbContext.cs` | Database context with relationships |
| `Components/Account/BranchIdClaimsTransformer.cs` | Claims transformer for multi-branch |
| `Program.cs:65` | Service registration |

### Tests

| File | Lines | Description |
|------|-------|-------------|
| `tests/.../Services/AgentServiceTests.cs` | 1265 | Comprehensive unit tests |

---

## Future Enhancements

### Potential Improvements

**Performance Metrics:**
- Transactions per day per agent
- Average transaction time
- Accuracy rate (discrepancy frequency)
- Cash handling efficiency score

**Commission Calculation:**
- Transaction volume-based commissions
- Tiered commission structures
- Monthly commission reports

**Shift Scheduling:**
- Morning/afternoon shift assignments
- Shift handover workflows
- Shift-based cash count tracking

**Agent Delegation:**
- Temporary handoff to another agent
- Delegation approval workflow
- Audit trail for delegated operations

**Training & Certification:**
- Training status tracking
- Certification expiration dates
- Required training modules per role

**Multi-Agent Collaboration:**
- Shared transactions between agents
- Team-based wallet management
- Collaborative cash counts

**Agent Photos:**
- Photo upload for profile
- Display in agent list and detail
- Facial recognition for security (future)

---

## Related Documentation

- **AI Assistant Guidelines:** [AGENTS.md](../AGENTS.md)
- **Setup & Commands:** [CLAUDE.md](../CLAUDE.md)
- **Architecture Overview:** [CLAUDE.md#architecture](../CLAUDE.md#architecture)

---

## Glossary

| Term | Definition |
|------|------------|
| **Agent** | A teller or banking agent responsible for cash operations |
| **Agent Code** | 4-letter unique identifier (e.g., "JODO") |
| **Wallet** | A container for a specific type of cash/money (Cash, Mobile Money, Bank) |
| **Cash Session** | A daily work session for an agent (one per day) |
| **Float Conservation** | Today's opening total = Yesterday's closing total |
| **Discrepancy** | Mismatch between expected and actual cash count |
| **Vault** | Central cash repository for a branch |
| **Multi-Branch** | Support for multiple bank branches in single deployment |
| **Soft Delete** | Deactivation using IsActive flag instead of physical deletion |
| **Claims Transformer** | Service that adds custom claims (BranchId) to authenticated user |

---

**Document Version:** 1.0
**Last Updated:** 2026-02-07
**Author:** Development Team

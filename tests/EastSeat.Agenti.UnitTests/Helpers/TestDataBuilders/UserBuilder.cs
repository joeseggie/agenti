using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Web.Data;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating ApplicationUser test data.
/// </summary>
public class UserBuilder
{
    private string _id = Guid.NewGuid().ToString();
    private string _firstName = "Test";
    private string _lastName = "User";
    private string _email = "test@test.com";
    private string _userName = "test@test.com";
    private string? _phoneNumber = null;
    private UserRole _role = UserRole.Agent;
    private bool _isActive = true;
    private long? _branchId = 1;
    private long? _agentId = null;
    private DateTime _createdAt = DateTime.UtcNow;
    private string? _themePreference = null;
    private bool _isDeleted = false;
    private DateTime? _deletedAt = null;

    public UserBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public UserBuilder WithFirstName(string firstName)
    {
        _firstName = firstName;
        return this;
    }

    public UserBuilder WithLastName(string lastName)
    {
        _lastName = lastName;
        return this;
    }

    public UserBuilder WithEmail(string email)
    {
        _email = email;
        _userName = email;
        return this;
    }

    public UserBuilder WithPhoneNumber(string phoneNumber)
    {
        _phoneNumber = phoneNumber;
        return this;
    }

    public UserBuilder WithRole(UserRole role)
    {
        _role = role;
        return this;
    }

    public UserBuilder WithBranchId(long? branchId)
    {
        _branchId = branchId;
        return this;
    }

    public UserBuilder WithAgentId(long? agentId)
    {
        _agentId = agentId;
        return this;
    }

    public UserBuilder IsInactive()
    {
        _isActive = false;
        return this;
    }

    public UserBuilder WithCreatedAt(DateTime createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public UserBuilder WithThemePreference(string? themePreference)
    {
        _themePreference = themePreference;
        return this;
    }

    public UserBuilder AsDeleted()
    {
        _isDeleted = true;
        _isActive = false;
        _deletedAt = DateTime.UtcNow;
        _firstName = string.Empty;
        _lastName = string.Empty;
        _phoneNumber = null;
        _branchId = null;
        _themePreference = null;
        return this;
    }

    public ApplicationUser Build()
    {
        return new ApplicationUser
        {
            Id = _id,
            FirstName = _firstName,
            LastName = _lastName,
            Email = _email,
            UserName = _userName,
            PhoneNumber = _phoneNumber,
            Role = _role,
            IsActive = _isActive,
            BranchId = _branchId,
            AgentId = _agentId,
            CreatedAt = _createdAt,
            ThemePreference = _themePreference,
            IsDeleted = _isDeleted,
            DeletedAt = _deletedAt,
            NormalizedEmail = _email.ToUpperInvariant(),
            NormalizedUserName = _userName.ToUpperInvariant()
        };
    }

    public static UserBuilder Default() => new();
}

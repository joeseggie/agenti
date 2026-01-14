using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Features.Users;

public interface IUserService
{
    Task<List<UserListItemDto>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default);
    Task<UserDetailDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<ServiceResult> UpdateProfileAsync(UserFormModel model, string performedByUserId, CancellationToken cancellationToken = default);
    Task<ServiceResult> ChangeRoleAsync(string userId, UserRole newRole, string performedByUserId, CancellationToken cancellationToken = default);
    Task<ServiceResult> DeactivateAsync(string userId, string performedByUserId, CancellationToken cancellationToken = default);
    Task<ServiceResult> ReactivateAsync(string userId, string performedByUserId, CancellationToken cancellationToken = default);
    Task<ServiceResult> DeleteAsync(string userId, string performedByUserId, CancellationToken cancellationToken = default);
}

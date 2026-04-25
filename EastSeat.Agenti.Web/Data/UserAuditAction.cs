namespace EastSeat.Agenti.Web.Data;

public enum UserAuditAction
{
    Created = 0,
    RoleChanged = 1,
    Deactivated = 2,
    Reactivated = 3,
    Deleted = 4,
    PasswordReset = 5,
    LoginSuccess = 6,
    LoginFailed = 7,
    PasswordChanged = 8
}

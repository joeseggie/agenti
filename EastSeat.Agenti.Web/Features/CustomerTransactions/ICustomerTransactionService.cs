namespace EastSeat.Agenti.Web.Features.CustomerTransactions;

public interface ICustomerTransactionService
{
    Task<CustomerCaptureOptionsDto> GetCaptureOptionsAsync(string userId, long sessionId, CancellationToken cancellationToken = default);
    Task<CustomerCaptureOptionsDto> GetCorrectionOptionsAsync(string userId, long correctionRequestId, CancellationToken cancellationToken = default);
    Task<List<CustomerTransactionDto>> GetForSessionAsync(string userId, long sessionId, CancellationToken cancellationToken = default);
    Task<List<CustomerTransactionDto>> GetOpenCasesAsync(string userId, CancellationToken cancellationToken = default);
    Task<CustomerTransactionDto> GetAsync(string userId, long transactionId, CancellationToken cancellationToken = default);
    Task<CustomerTransactionDto> CaptureAsync(string userId, CustomerTransactionForm form, CancellationToken cancellationToken = default);
    Task<CustomerTransactionDto> RecordFollowUpAsync(string userId, long transactionId, CustomerFollowUpForm form, CancellationToken cancellationToken = default);
    Task<CustomerTransactionDto> CorrectAsync(string userId, CustomerMovementCorrectionForm form, CancellationToken cancellationToken = default);
    Task ResubmitCorrectionsAsync(string userId, long correctionRequestId, int expectedVersion, CancellationToken cancellationToken = default);
}

namespace EastSeat.Agenti.Web.Features.Api;

public sealed record ReviewReasonRequest(int ExpectedVersion, string Reason);
public sealed record BankingCapabilitiesDto(int ContractVersion, long MaximumEvidenceBytes, string BusinessTimeZone);

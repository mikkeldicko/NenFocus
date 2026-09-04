using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.SaveReflection;

public sealed record SaveNenContractReflectionCommand : ICommand<SaveNenContractReflectionResult>
{
    public Guid ContractId { get; init; }
    public string? Reflection { get; init; }
}

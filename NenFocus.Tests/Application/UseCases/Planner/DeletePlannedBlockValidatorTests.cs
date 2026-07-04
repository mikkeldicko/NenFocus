using FluentAssertions;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class DeletePlannedBlockValidatorTests
{
    private readonly DeletePlannedBlockValidator _validator = new();

    [Fact]
    public async Task Validate_ValidBlockId_IsValid()
    {
        var result = await _validator.ValidateAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyBlockId_IsInvalid()
    {
        var result = await _validator.ValidateAsync(new DeletePlannedBlockCommand { BlockId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }
}

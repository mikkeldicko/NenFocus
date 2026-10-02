using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllContractPresets;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetAllContractPresetsQueryHandlerTests
{
    private readonly IContractPresetRepository _presets = Substitute.For<IContractPresetRepository>();

    private GetAllContractPresetsQueryHandler CreateHandler() => new(_presets);

    [Fact]
    public async Task HandleAsync_NoPresets_ReturnsEmpty()
    {
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ContractPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetAllContractPresetsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_MapsEntitiesToDtos()
    {
        var preset = ContractPreset.Create(Guid.NewGuid(), "Study Month", TestRules.Of(fixedDailyFloor: new DailyFloor(4)), null);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ContractPreset>)[preset]);

        var result = await CreateHandler().HandleAsync(new GetAllContractPresetsQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Study Month");
        result[0].DailyFloorHours.Should().Be(4);
    }
}

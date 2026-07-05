namespace NenFocus.Domain.ValueObjects;

public record NenContractGoal
{
    public string Value { get; }

    public NenContractGoal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Contract goal cannot be empty.");

        Value = value.Trim();
    }

    public override string ToString() => Value;
}

namespace NenFocus.Domain.ValueObjects;

public record SessionGoal
{
    public string Value { get; }

    public SessionGoal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Session goal cannot be empty.");

        Value = value.Trim();
    }

    public override string ToString() => Value;
}
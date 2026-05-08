namespace NenFocus.Domain.ValueObjects;

public record BlockedApp
{
    public string ProcessName { get; }

    public BlockedApp(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Process name cannot be empty.");

        ProcessName = processName.Trim();
    }

    public override string ToString() => ProcessName;
}
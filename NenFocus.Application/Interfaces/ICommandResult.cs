namespace NenFocus.Application.Interfaces;

/// <summary>A command's outcome as the pipeline sees it: did it succeed? A failed command saves nothing (Gap 110).</summary>
public interface ICommandResult
{
    bool Success { get; }
}

public interface ICommandResult<TSelf> : ICommandResult where TSelf : ICommandResult<TSelf>
{
    static abstract TSelf Fail(string message);
}

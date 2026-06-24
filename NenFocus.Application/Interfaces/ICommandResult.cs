namespace NenFocus.Application.Interfaces;

public interface ICommandResult<TSelf> where TSelf : ICommandResult<TSelf>
{
    static abstract TSelf Fail(string message);
    bool Success { get; }
}

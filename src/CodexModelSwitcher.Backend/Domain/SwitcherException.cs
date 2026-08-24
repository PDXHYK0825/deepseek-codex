namespace CodexModelSwitcher.Domain;

public sealed class SwitcherException : Exception
{
    public SwitcherException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public SwitcherException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

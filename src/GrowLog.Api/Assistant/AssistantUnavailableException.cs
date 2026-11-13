namespace GrowLog.Api.Assistant;

/// <summary>OpenRouter refused or failed a question, with the status it gave.</summary>
internal sealed class AssistantUnavailableException : Exception
{
    public AssistantUnavailableException()
    {
    }

    public AssistantUnavailableException(string message)
        : base(message)
    {
    }

    public AssistantUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AssistantUnavailableException(int status)
        : base($"OpenRouter answered {status}.") => Status = status;

    public int Status { get; }
}

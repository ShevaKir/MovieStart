using MovieStart.Client;

namespace MovieStart.Web.State;

/// <summary>Short messages shown over the page, e.g. why a command failed.</summary>
public sealed class Notifications
{
    public event Action? Changed;

    public string? Message { get; private set; }

    public void Show(string message)
    {
        Message = message;
        Changed?.Invoke();
    }

    public void Dismiss()
    {
        Message = null;
        Changed?.Invoke();
    }

    /// <summary>Shows the error of a failed result; returns whether it succeeded.</summary>
    public bool Report(AgentResult result)
    {
        if (!result.IsSuccess)
            Show(result.Error ?? "Something went wrong.");
        return result.IsSuccess;
    }
}

namespace MovieStart.Agent.Tests;

public static class Eventually
{
    public static async Task AssertAsync(Func<bool> condition, string because, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting until {because}.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}

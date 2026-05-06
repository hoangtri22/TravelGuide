namespace TravelGuide;

internal static class DemoDiagnostics
{
    private static readonly object Sync = new();
    private static string _lastHttpError = "N/A";
    private static DateTimeOffset? _lastHttpErrorAt;

    internal static void RecordHttpError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (Sync)
        {
            _lastHttpError = message.Trim();
            _lastHttpErrorAt = DateTimeOffset.UtcNow;
        }
    }

    internal static (string Message, DateTimeOffset? AtUtc) GetLastHttpError()
    {
        lock (Sync)
            return (_lastHttpError, _lastHttpErrorAt);
    }
}

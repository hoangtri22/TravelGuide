namespace TravelGuide;

/// <summary>
/// Nếu chưa có token trong Preferences/env, đọc dòng đầu tiên bắt đầu bằng <c>pk.</c> từ
/// <c>Resources/Raw/mapbox_token.secret.txt</c> (một dòng token <c>pk....</c>; không commit — có trong .gitignore).
/// </summary>
internal static class MapboxTokenBootstrap
{
    internal static void TryLoadFromBundledSecretFile()
    {
        if (!string.IsNullOrWhiteSpace(Preferences.Get(MapboxConfig.PreferencesKey, string.Empty)))
            return;

        Stream stream;
        try
        {
            stream = FileSystem.OpenAppPackageFileAsync("mapbox_token.secret.txt").GetAwaiter().GetResult();
        }
        catch
        {
            return;
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                var t = line.Trim();
                if (t.StartsWith("pk.", StringComparison.Ordinal) && t.Length > 20)
                {
                    Preferences.Set(MapboxConfig.PreferencesKey, t);
                    return;
                }
            }
        }
    }
}

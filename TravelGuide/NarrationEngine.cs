using Microsoft.Maui.Media;
using Microsoft.Maui.Devices;
using Plugin.Maui.Audio;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Hàng đợi thuyết minh: nếu <see cref="TouristPlace.AudioUrl"/> là http(s) hợp lệ thì tải và phát qua <see cref="IAudioManager"/>;
/// lỗi hoặc không có URL thì dùng TTS.
/// </summary>
public class NarrationEngine
{
    private const string AdminWebBaseLoopback = "http://127.0.0.1:5280";
    private const string AdminWebBaseAndroid = "http://10.0.2.2:5280";
    private const string AdminWebBaseLoopbackAlt = "http://127.0.0.1:5090";
    private const string AdminWebBaseAndroidAlt = "http://10.0.2.2:5090";
    private readonly HttpClient _http;
    private readonly IAudioManager _audioManager;

    private bool _isSpeaking;
    private CancellationTokenSource? _cts;
    private IEnumerable<Locale>? _cachedLocales;
    private readonly Queue<TouristPlace> _queue = new();
    private IAudioPlayer? _audioPlayer;

    public bool IsSpeaking => _isSpeaking;

    public TouristPlace? CurrentPlace { get; private set; }

    public event Action<TouristPlace>? OnStartedPlaying;
    public event Action? OnStoppedPlaying;

    public NarrationEngine(HttpClient http, IAudioManager audioManager)
    {
        _http = http;
        _audioManager = audioManager;
    }

    public async Task SpeakAsync(TouristPlace place)
    {
        _queue.Enqueue(place);
        if (!_isSpeaking)
            await ProcessQueueAsync();
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        DisposeActiveAudio();
        _queue.Clear();
        _isSpeaking = false;
        CurrentPlace = null;
        OnStoppedPlaying?.Invoke();
        return Task.CompletedTask;
    }

    public void SkipNext() => _cts?.Cancel();

    private void DisposeActiveAudio()
    {
        try
        {
            _audioPlayer?.Stop();
            _audioPlayer?.Dispose();
        }
        catch { /* ignore */ }
        finally
        {
            _audioPlayer = null;
        }
    }

    private async Task ProcessQueueAsync()
    {
        _isSpeaking = true;

        try
        {
            try
            {
                var localeTask = TextToSpeech.Default.GetLocalesAsync();
                var completed = await Task.WhenAny(localeTask, Task.Delay(TimeSpan.FromSeconds(2)));
                _cachedLocales = completed == localeTask
                    ? await localeTask
                    : Array.Empty<Locale>();
            }
            catch (Exception ex)
            {
                // If locale discovery fails, still allow narration with default TTS settings.
                System.Diagnostics.Debug.WriteLine($"[TTS] GetLocales failed: {ex.Message}");
                _cachedLocales = Array.Empty<Locale>();
            }

            while (_queue.Count > 0)
            {
                _cts = new CancellationTokenSource();
                var place = _queue.Dequeue();
                CurrentPlace = place;
                OnStartedPlaying?.Invoke(place);

                try
                {
                    var usedAudio = await TryPlayRemoteAudioAsync(place, _cts.Token).ConfigureAwait(false);
                    if (!usedAudio)
                    {
                        var text = BuildNarrationText(place);
                        var opts = BuildSpeechOptions(_cachedLocales!);
                        await TextToSpeech.Default.SpeakAsync(text, opts, _cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // bỏ qua / dừng
                }
                catch (Exception ex)
                {
                    // Keep the queue alive even when one place fails to narrate.
                    System.Diagnostics.Debug.WriteLine($"[Narration] {place.Name}: {ex.Message}");
                }
                finally
                {
                    DisposeActiveAudio();
                }

                if (_queue.Count > 0)
                    await Task.Delay(600).ConfigureAwait(false);
            }
        }
        finally
        {
            DisposeActiveAudio();
            _isSpeaking = false;
            CurrentPlace = null;
            OnStoppedPlaying?.Invoke();
        }
    }

    private async Task<bool> TryPlayRemoteAudioAsync(TouristPlace place, CancellationToken ct)
    {
        var candidates = ResolveAudioSources(place.AudioUrl);
        foreach (var url in candidates)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                continue;

            MemoryStream? buffer = null;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                await using (var network = await _http.GetStreamAsync(uri, timeoutCts.Token).ConfigureAwait(false))
                {
                    buffer = new MemoryStream();
                    await network.CopyToAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
                }

                buffer!.Position = 0;
                DisposeActiveAudio();
                _audioPlayer = _audioManager.CreatePlayer(buffer);
                _audioPlayer.Play();

                while (_audioPlayer.IsPlaying)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Audio] {url} failed: {ex.Message} → thử nguồn khác/TTS");
            }
            finally
            {
                DisposeActiveAudio();
                try
                {
                    buffer?.Dispose();
                }
                catch { /* ignore */ }
            }
        }

        return false;
    }

    private static string BuildNarrationText(TouristPlace place) =>
        string.IsNullOrWhiteSpace(place.Description) ? place.Name : place.Description;

    private static IReadOnlyList<string> ResolveAudioSources(string? rawAudioUrl)
    {
        var raw = (rawAudioUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return [raw];

        var normalized = raw.TrimStart('.', '/');
        if (normalized.StartsWith("WEB/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..];
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        var (primaryBase, altBase) = GetAdminWebBaseUrls();
        return
        [
            $"{primaryBase.TrimEnd('/')}/{normalized}",
            $"{altBase.TrimEnd('/')}/{normalized}"
        ];
    }

    private static (string Primary, string Alt) GetAdminWebBaseUrls() =>
        DeviceInfo.Platform == DevicePlatform.Android
            ? (AdminWebBaseAndroid, AdminWebBaseAndroidAlt)
            : (AdminWebBaseLoopback, AdminWebBaseLoopbackAlt);

    private static SpeechOptions BuildSpeechOptions(IEnumerable<Locale> available)
    {
        var (lang, country) = AppLanguage.Current switch
        {
            "vi" => ("vi", "VN"),
            "en" => ("en", "US"),
            "ja" => ("ja", "JP"),
            "ko" => ("ko", "KR"),
            "zh" => ("zh", "CN"),
            _ => ("vi", "VN")
        };

        var list = (available ?? Array.Empty<Locale>()).ToList();
        var matched =
            list.FirstOrDefault(l =>
                string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.Country, country, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(l =>
                string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase));

        return new SpeechOptions { Volume = 1f, Pitch = 1f, Locale = matched };
    }
}

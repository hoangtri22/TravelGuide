using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

public class NarrationEngine
{
    private bool _isSpeaking = false;
    private CancellationTokenSource? _cts;
    private IEnumerable<Locale>? _cachedLocales;
    private readonly Queue<TouristPlace> _queue = new();

    public bool IsSpeaking => _isSpeaking;
    public TouristPlace? CurrentPlace { get; private set; }

    // ✅ Events cho MiniPlayer
    public event Action<TouristPlace>? OnStartedPlaying;
    public event Action? OnStoppedPlaying;

    public async Task SpeakAsync(TouristPlace place)
    {
        _queue.Enqueue(place);
        if (!_isSpeaking)
            await ProcessQueueAsync();
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _queue.Clear();
        _isSpeaking = false;
        CurrentPlace = null;
        OnStoppedPlaying?.Invoke();
        await TextToSpeech.Default.SpeakAsync("",
            new SpeechOptions { Volume = 0 });
    }

    // ✅ Skip sang địa điểm tiếp
    public void SkipNext() => _cts?.Cancel();

    private async Task ProcessQueueAsync()
    {
        _isSpeaking = true;
        _cachedLocales ??= await TextToSpeech.Default.GetLocalesAsync();

        try
        {
            while (_queue.Count > 0)
            {
                _cts = new CancellationTokenSource();

                var place = _queue.Dequeue();
                CurrentPlace = place;

                // ✅ Thông báo MiniPlayer
                OnStartedPlaying?.Invoke(place);

                var text = BuildNarrationText(place);
                var opts = BuildSpeechOptions(_cachedLocales);

                System.Diagnostics.Debug.WriteLine(
                    $"[TTS] Speaking [{AppLanguage.Current}]: {place.Name}");

                try
                {
                    await TextToSpeech.Default.SpeakAsync(text, opts, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Skip — tiếp tục item tiếp theo nếu còn
                }

                if (_queue.Count > 0)
                    await Task.Delay(600)
                          .ContinueWith(_ => { });
            }
        }
        finally
        {
            _isSpeaking = false;
            CurrentPlace = null;
            OnStoppedPlaying?.Invoke();
        }
    }

    private static string BuildNarrationText(TouristPlace place) =>
        AppLanguage.Current switch
        {
            "vi" => $"Bạn đang đến gần {place.Name}. {place.Description}",
            "en" => $"You are approaching {place.Name}. {place.Description}",
            "ja" => $"{place.Name}に近づいています。{place.Description}",
            "ko" => $"{place.Name}에 가까워지고 있습니다. {place.Description}",
            "zh" => $"您正在接近{place.Name}。{place.Description}",
            _ => $"{place.Name}. {place.Description}"
        };

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
        var list = available.ToList();
        var matched = list.FirstOrDefault(l =>
            string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.Country, country, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(l =>
                string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(l =>
                string.Equals(l.Language, "en", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.Country, "US", StringComparison.OrdinalIgnoreCase));

        return new SpeechOptions { Volume = 1.0f, Pitch = 1.0f, Locale = matched };
    }
}
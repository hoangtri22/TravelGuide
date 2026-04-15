using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Hàng đợi thuyết minh: luôn dùng TTS theo <see cref="AppLanguage.Current"/> (tên/mô tả POI đã theo ngôn ngữ).
/// </summary>
public class NarrationEngine
{
    private Task? _processTask;
    private bool _isSpeaking;
    private CancellationTokenSource? _cts;
    private IEnumerable<Locale>? _cachedLocales;
    private readonly Queue<TouristPlace> _queue = new();

    public bool IsSpeaking => _isSpeaking;

    public TouristPlace? CurrentPlace { get; private set; }

    public event Action<TouristPlace>? OnStartedPlaying;
    public event Action? OnStoppedPlaying;

    public Task SpeakAsync(TouristPlace place)
    {
        _queue.Enqueue(place);
        if (_processTask is null || _processTask.IsCompleted)
            _processTask = ProcessQueueAsync();
        return Task.CompletedTask;
    }

    /// <summary>Dừng mọi bài đang phát, xóa hàng đợi, chỉ phát đúng một POI (màn chi tiết / map / audio).</summary>
    public async Task SpeakExclusiveAsync(TouristPlace place)
    {
        _cts?.Cancel();
        _queue.Clear();
        if (_processTask != null)
        {
            try
            {
                await _processTask.ConfigureAwait(false);
            }
            catch
            {
                /* hủy / lỗi phiên trước */
            }

            _processTask = null;
        }

        _queue.Enqueue(place);
        _processTask = ProcessQueueAsync();
        await _processTask.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _queue.Clear();
        if (_processTask != null)
        {
            try
            {
                await _processTask.ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }

            _processTask = null;
        }

        CurrentPlace = null;
        OnStoppedPlaying?.Invoke();
    }

    public void SkipNext() => _cts?.Cancel();

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
                    var text = BuildNarrationText(place);
                    var opts = BuildSpeechOptions(_cachedLocales!);
                    await RunSpeechOnMainThreadAsync(() =>
                        TextToSpeech.Default.SpeakAsync(text, opts, _cts.Token)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // bỏ qua / dừng
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Narration] {place.Name}: {ex.Message}");
                }

                if (_queue.Count > 0)
                    await Task.Delay(600).ConfigureAwait(false);
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
        string.IsNullOrWhiteSpace(place.Description) ? place.Name : place.Description;

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

    /// <summary>Android: TTS thường cần main thread; nếu không có thể im lặng.</summary>
    private static Task RunSpeechOnMainThreadAsync(Func<Task> speak)
    {
        if (MainThread.IsMainThread)
            return speak();
        return MainThread.InvokeOnMainThreadAsync(speak);
    }
}

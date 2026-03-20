using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Narration Engine: Quản lý hàng chờ audio, tránh phát đè.
/// Chọn đúng ngôn ngữ TTS theo AppLanguage.Current.
/// </summary>
public class NarrationEngine
{
    private bool _isSpeaking = false;
    private CancellationTokenSource? _cts;

    // FIX: Cache locales ở field level, load 1 lần duy nhất bằng await (tránh .Result deadlock)
    private IEnumerable<Locale>? _cachedLocales;

    private readonly Queue<TouristPlace> _queue = new();

    public bool IsSpeaking => _isSpeaking;

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
        await TextToSpeech.Default.SpeakAsync("", new SpeechOptions { Volume = 0 });
    }

    private async Task ProcessQueueAsync()
    {
        _isSpeaking = true;
        _cts = new CancellationTokenSource();

        // FIX: await thay vì .Result — tránh deadlock trên UI thread
        _cachedLocales ??= await TextToSpeech.Default.GetLocalesAsync();

        try
        {
            while (_queue.Count > 0)
            {
                if (_cts.Token.IsCancellationRequested) break;

                var place = _queue.Dequeue();
                var text = BuildNarrationText(place);
                var opts = BuildSpeechOptions(_cachedLocales);

                System.Diagnostics.Debug.WriteLine(
                    $"[TTS] Speaking [{AppLanguage.Current}]: {place.Name}");

                await TextToSpeech.Default.SpeakAsync(text, opts, _cts.Token);

                if (_queue.Count > 0)
                    await Task.Delay(800, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[TTS] Cancelled");
        }
        finally
        {
            _isSpeaking = false;
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

    // FIX: Nhận locales đã await sẵn thay vì gọi .Result, fallback 3 tầng
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

        // 1. Khớp chính xác language + country (vd: vi-VN)
        var matched = list.FirstOrDefault(l =>
            string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.Country, country, StringComparison.OrdinalIgnoreCase));

        // 2. Fallback: chỉ khớp language (vd: "ja" bất kể region)
        matched ??= list.FirstOrDefault(l =>
            string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase));

        // 3. Fallback cuối: en-US (hầu hết thiết bị đều có)
        matched ??= list.FirstOrDefault(l =>
            string.Equals(l.Language, "en", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.Country, "US", StringComparison.OrdinalIgnoreCase));

        System.Diagnostics.Debug.WriteLine(
            $"[TTS] Locale: {matched?.Language}-{matched?.Country} (lang={AppLanguage.Current})");

        return new SpeechOptions { Volume = 1.0f, Pitch = 1.0f, Locale = matched };
    }
}
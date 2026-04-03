using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

public class AudioQueueManager
{
    private readonly Queue<TouristPlace> _queue = new();
    private bool _isPlaying = false;
    private bool _isPaused = false;
    private CancellationTokenSource? _cts;
    private readonly NarrationEngine _narrationEngine;
    private TouristPlace? _currentPlace = null;
    private readonly Dictionary<int, DateTime> _lastPlayed = new();

    public bool IsPaused => _isPaused;
    public bool IsPlaying => _isPlaying;

    public event Action<TouristPlace>? OnStarted;
    public event Action? OnStopped;
    public event Action<bool>? OnPausedChanged;

    public AudioQueueManager(NarrationEngine narrationEngine)
    {
        _narrationEngine = narrationEngine;
        _narrationEngine.OnStoppedPlaying += () =>
        {
            OnStopped?.Invoke();
            _isPlaying = false;
            _currentPlace = null;
        };
    }

    public void Enqueue(TouristPlace place, bool highPriority = false)
    {
        if (IsInCooldown(place)) return;

        if (highPriority)
        {
            var newQueue = new Queue<TouristPlace>();
            newQueue.Enqueue(place);
            foreach (var item in _queue.Where(p => p.Id != place.Id))
                newQueue.Enqueue(item);
            _queue.Clear();
            foreach (var item in newQueue) _queue.Enqueue(item);
        }
        else
        {
            _queue.Enqueue(place);
        }

        if (!_isPlaying && !_isPaused)
            _ = ProcessQueueAsync();
    }

    private bool IsInCooldown(TouristPlace place)
    {
        if (_lastPlayed.TryGetValue(place.Id, out var last))
            return (DateTime.Now - last).TotalMinutes < 5;
        return false;
    }

    public async Task PauseAsync()
    {
        if (!_isPlaying || _isPaused) return;
        _isPaused = true;
        _cts?.Cancel();
        OnPausedChanged?.Invoke(true);
        await _narrationEngine.StopAsync();
    }

    public async Task ResumeAsync()
    {
        if (!_isPaused) return;
        _isPaused = false;
        OnPausedChanged?.Invoke(false);

        if (_currentPlace != null)
        {
            await _narrationEngine.SpeakAsync(_currentPlace);
        }
        else if (_queue.Count > 0)
        {
            await ProcessQueueAsync();
        }
    }

    public async Task StopAsync()
    {
        _queue.Clear();
        _cts?.Cancel();
        _isPlaying = false;
        _isPaused = false;
        _currentPlace = null;
        OnPausedChanged?.Invoke(false);
        await _narrationEngine.StopAsync();
        OnStopped?.Invoke();
    }

    public void SkipNext()
    {
        _cts?.Cancel();
    }

    private async Task ProcessQueueAsync()
    {
        _isPlaying = true;

        var locales = await TextToSpeech.Default.GetLocalesAsync();

        try
        {
            while (_queue.Count > 0 && !_isPaused)
            {
                _cts = new CancellationTokenSource();
                _currentPlace = _queue.Dequeue();
                _lastPlayed[_currentPlace.Id] = DateTime.Now;
                OnStarted?.Invoke(_currentPlace);

                var text = $"{_currentPlace.Name}. {_currentPlace.Description}";
                var opts = new SpeechOptions
                {
                    Volume = 1.0f,
                    Pitch = 1.0f,
                    Locale = locales.FirstOrDefault()
                };
                try
                {
                    await TextToSpeech.Default.SpeakAsync(text, opts, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Skip sang item kế tiếp
                }
                if (_queue.Count > 0)
                    await Task.Delay(600);
            }
        }
        finally
        {
            _isPlaying = false;
            _currentPlace = null;
            OnStopped?.Invoke();
        }
    }
}
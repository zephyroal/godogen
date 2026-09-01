using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>Reusable audio manager: caches streams, plays one-shots and loops.
/// Gracefully no-ops when audio files are absent (missing files → silent skip, no error).</summary>
public partial class AudioPlayer : Node
{
    private readonly AudioStreamPlayer _sfx = new();
    private readonly AudioStreamPlayer _loop = new();
    private readonly Dictionary<string, AudioStream> _cache = new();

    public override void _Ready()
    {
        AddChild(_sfx);
        AddChild(_loop);
    }

    private AudioStream Load(string name)
    {
        if (_cache.TryGetValue(name, out var s)) return s;
        var path = $"res://assets/audio/{name}.wav";
        s = GD.Load<AudioStream>(path);
        if (s == null)
        {
            path = $"res://assets/audio/{name}.ogg";
            s = GD.Load<AudioStream>(path);
        }
        if (s != null) _cache[name] = s;
        return s;
    }

    /// <summary>Play a one-shot sound effect. If already playing, restarts it.</summary>
    public void Play(string name)
    {
        var s = Load(name);
        if (s == null) return;
        _sfx.Stream = s;
        _sfx.Play();
    }

    /// <summary>Start a looping ambient sound (footsteps, AI thinking, etc.).</summary>
    public void StartLoop(string name)
    {
        var s = Load(name);
        if (s == null) return;
        if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
        _loop.Stream = s;
        _loop.Play();
    }

    public void StopLoop()
    {
        _loop.Stop();
    }
}

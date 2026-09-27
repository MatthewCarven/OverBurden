using System;
using System.Diagnostics;
using System.IO;
using Godot;
using Overburden.Core.Structure;

namespace Overburden.Game.Audio;

/// <summary>
/// The collapse's phonk (COLLAPSE-PLAN part e): his track, played while a collapse is live and sped
/// up with its pace. The rules are <see cref="CollapseMusicCue"/>'s; this holds the player.
/// </summary>
/// <remarks>
/// <b>Heard everywhere, not positional</b> (decision 5): it is a warning, and a collapse behind a
/// wall is the one to hear. <b>The track is his and stays out of the repo</b> (decision 11):
/// <c>media/</c> is gitignored, and the MP3 is read from its bytes at start-up, so it needs no
/// import and no <c>res://</c> path.
/// </remarks>
public sealed class CollapseMusic
{
    private readonly AudioStreamPlayer _player;
    private readonly CollapseMusicCue _cue;
    private readonly float _volumeDb;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastSeconds;
    private double _printedPitch = 1.0;
    private double _printedAt = double.NegativeInfinity;

    private CollapseMusic(Node parent, AudioStreamMP3 stream, CollapseMusicCue cue, float volumeDb)
    {
        _cue = cue;
        _volumeDb = volumeDb;
        _player = new AudioStreamPlayer { Stream = stream, VolumeDb = volumeDb, Name = "CollapseMusic" };
        parent.AddChild(_player);
    }

    /// <summary>The rules this plays by, for the <c>M9:</c> line.</summary>
    public CollapseMusicCue Cue => _cue;

    /// <summary>The file it plays.</summary>
    public string Path { get; private init; } = string.Empty;

    /// <summary>The track's length, seconds.</summary>
    public double LengthSeconds { get; private init; }

    /// <summary>
    /// Reads the track and adds its player under <paramref name="parent"/>; null, with the reason,
    /// when the file is missing or unreadable — the game then plays silent.
    /// </summary>
    public static CollapseMusic? Load(Node parent, string path, CollapseMusicCue cue, float volumeDb, out string why)
    {
        if (!File.Exists(path))
        {
            why = $"no file at {path}";
            return null;
        }

        AudioStreamMP3 stream;
        try
        {
            stream = new AudioStreamMP3 { Data = File.ReadAllBytes(path), Loop = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = $"{path} could not be read ({e.Message})";
            return null;
        }

        double length = stream.GetLength();
        if (!(length > 0))
        {
            why = $"{path} is not an MP3 Godot can play";
            return null;
        }

        why = string.Empty;
        return new CollapseMusic(parent, stream, cue, volumeDb) { Path = path, LengthSeconds = length };
    }

    /// <summary>
    /// A frame: <paramref name="fastest"/> is the fastest live collapse's pace multiplier, or null
    /// with none live. Prints an <c>M9: music</c> line when it starts, stops, or changes speed by a
    /// step — at most once a second for the speed.
    /// </summary>
    public void Update(double? fastest)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double seconds = now - _lastSeconds;
        _lastSeconds = now;

        bool wasPlaying = _cue.Playing;
        if (_cue.Step(fastest, seconds))
        {
            _player.PitchScale = (float)_cue.Pitch;
            _player.VolumeDb = _volumeDb;
            _player.Play();
            _printedPitch = _cue.Pitch;
            _printedAt = now;
            GD.Print($"M9: music — a collapse started, the track from the top at x{_cue.Pitch:0.###} speed");
            return;
        }

        if (!_cue.Playing)
        {
            if (wasPlaying)
            {
                _player.Stop();
                GD.Print($"M9: music — the last collapse ended, faded out over {_cue.FadeSeconds:0.#} s");
            }

            return;
        }

        _player.PitchScale = (float)_cue.Pitch;
        _player.VolumeDb = _volumeDb + (float)Mathf.LinearToDb(_cue.Gain);
        if (Math.Abs(_cue.Pitch - _printedPitch) >= 0.01 && now - _printedAt >= 1.0)
        {
            _printedPitch = _cue.Pitch;
            _printedAt = now;
            GD.Print($"M9: music — x{fastest ?? 0:0.#} pace, the track at x{_cue.Pitch:0.###} speed");
        }
    }
}

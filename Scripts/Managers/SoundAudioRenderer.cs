using Godot;

// =================================================================================================
// FILE: SoundAudioRenderer.cs (GODOT VERSION)
// PURPOSE: Optional runtime audio playback for sound events (paired with sound glyph visuals).
// ATTACH TO: Do not attach (spawned at runtime by SoundSystem).
// =================================================================================================
public static class SoundAudioRenderer
{
    private static SoundAudioRuntime runtime;

    public static void TryPlay(Vector3 worldPosition, string audioStreamPath, float volumeDb = 0f, float pitchScale = 1f)
    {
        if (string.IsNullOrWhiteSpace(audioStreamPath)) return;

        var tree = Engine.GetMainLoop() as SceneTree;
        var scene = tree?.CurrentScene;
        if (scene == null) return;

        if (!GodotObject.IsInstanceValid(runtime))
        {
            runtime = new SoundAudioRuntime
            {
                Name = "SoundAudioRuntime"
            };
            scene.AddChild(runtime);
        }

        runtime.PlayAt(worldPosition, audioStreamPath, volumeDb, pitchScale);
    }
}

public partial class SoundAudioRuntime : Node3D
{
    public void PlayAt(Vector3 worldPosition, string audioStreamPath, float volumeDb, float pitchScale)
    {
        var stream = GD.Load<AudioStream>(audioStreamPath);
        if (stream == null)
        {
            GD.PrintErr($"[SoundAudioRenderer] Could not load audio stream at path: {audioStreamPath}");
            return;
        }

        var player = new AudioStreamPlayer3D
        {
            Stream = stream,
            GlobalPosition = worldPosition,
            VolumeDb = volumeDb,
            PitchScale = Mathf.Clamp(pitchScale, 0.25f, 4f),
            UnitSize = 10f,
            MaxDistance = 90f,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            Autoplay = false
        };

        AddChild(player);
        player.Finished += () =>
        {
            if (GodotObject.IsInstanceValid(player))
            {
                player.QueueFree();
            }
        };
        player.Play();
    }
}
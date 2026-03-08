using Godot;

// =================================================================================================
// FILE: TimeManager.cs (GODOT VERSION)
// PURPOSE: Manages time of day, moon phases, and global illumination levels.
// ATTACH TO: A persistent "GameManager" Node or Autoload.
// =================================================================================================

public partial class TimeManager : Godot.Node
{
    public static TimeManager Instance { get; private set; }

    [Export]
    [ExportGroup("Time Settings")]
    public TimeOfDay CurrentTimeOfDay = TimeOfDay.Day;
    
    [Export]
    public MoonPhase CurrentMoonPhase = MoonPhase.FullMoon;

    [ExportGroup("Global Light Levels")]
    // 0 = Darkness, 1 = Dim Light, 2 = Normal Light, 3 = Bright/Direct Sunlight
    public int GlobalLightLevel { get; private set; }

    public override void _Ready()
    {
        if (Instance != null && Instance != this) 
        {
            QueueFree();
        }
        else 
        {
            Instance = this;
        }
        
        CalculateGlobalLight();
    }

    public void SetTime(TimeOfDay time)
    {
        CurrentTimeOfDay = time;
        CalculateGlobalLight();
        GD.Print($"[Time] It is now {CurrentTimeOfDay}. Global Light: {GlobalLightLevel}");
    }

    public void SetMoonPhase(MoonPhase phase)
    {
        CurrentMoonPhase = phase;
        CalculateGlobalLight();
    }

    private void CalculateGlobalLight()
    {
        switch (CurrentTimeOfDay)
        {
            case TimeOfDay.Day:
                GlobalLightLevel = 2; // Normal Light
                break;
            
            case TimeOfDay.Dawn:
            case TimeOfDay.Dusk:
                GlobalLightLevel = 1; // Dim Light
                break;
            
            case TimeOfDay.Night:
                // Rule: "Outdoors on a moonlit night"
                // Full Moon provides Dim Light (1) globally.
                // Anything less than Full Moon is usually Darkness (0) for mechanical purposes.
                if (CurrentMoonPhase == MoonPhase.FullMoon || CurrentMoonPhase == MoonPhase.Gibbous)
                {
                    GlobalLightLevel = 1; // Dim Light
                }
                else
                {
                    GlobalLightLevel = 0; // Darkness
                }
                break;
        }
    }

    /// <summary>
    /// Returns true if the global condition counts as "Moonlit Night".
    /// Used for Low-Light Vision checks.
    /// </summary>
    public bool IsMoonlitNight()
    {
        return CurrentTimeOfDay == TimeOfDay.Night && GlobalLightLevel == 1;
    }
}
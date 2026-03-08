using Godot;
using System.Collections.Generic;
using System.Linq;
// =================================================================================================
// FILE: WeatherManager.cs (GODOT VERSION)
// PURPOSE: Manages the current weather state, its visual effects, and its impact on the grid.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================
public partial class WeatherManager : Godot.Node
{
public static WeatherManager Instance { get; private set; }

[ExportGroup("Weather Settings")]
[Export]
[Tooltip("A list of all possible weather conditions that can occur in this scene.")]
public Godot.Collections.Array<Weather_SO> PossibleWeathers = new();

[Export(PropertyHint.Range, "0,1")]
public float WeatherChangeChancePerRound = 0.1f;

[ExportGroup("Scene References")]
[Export]
[Tooltip("A Node3D in the scene to act as a parent for weather VFX.")]
public Node3D WeatherVFXParent;

public Weather_SO CurrentWeather { get; private set; }
public Vector3 CurrentWindDirection { get; private set; }

private class PendingWeather
{
    public Weather_SO Weather;
    public double DelaySeconds;
    public double DurationSeconds;
    public Vector3 WindDir;
}
private List<PendingWeather> pendingWeathers = new List<PendingWeather>();
private double activeOverrideDurationSeconds = 0d;

private Node3D currentVFXInstance;
private DirectionalLight3D sunLight;

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
    
    // Find sun (DirectionalLight3D)
    sunLight = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;

    if (PossibleWeathers != null && PossibleWeathers.Count > 0)
    {
        // LINQ FirstOrDefault
        var initial = PossibleWeathers.FirstOrDefault(w => w.WeatherName == "Clear Skies") ?? PossibleWeathers[0];
        SetWeather(initial);
    }
}

public override void _Process(double delta)
{
    // Simple visual feedback for Time of Day
    if (TimeManager.Instance != null && sunLight != null)
    {
        float targetIntensity = 1.0f;
        Color targetColor = Colors.White;

        switch (TimeManager.Instance.CurrentTimeOfDay)
        {
            case TimeOfDay.Day:
                targetIntensity = 1.0f;
                targetColor = new Color(1f, 0.95f, 0.9f); 
                break;
            case TimeOfDay.Dawn:
            case TimeOfDay.Dusk:
                targetIntensity = 0.5f;
                targetColor = new Color(1f, 0.6f, 0.4f); 
                break;
            case TimeOfDay.Night:
                targetIntensity = 0.2f; 
                targetColor = new Color(0.4f, 0.4f, 0.7f); 
                break;
        }

        // Lerp intensity
        sunLight.LightEnergy = Mathf.Lerp(sunLight.LightEnergy, targetIntensity, (float)delta);
        sunLight.LightColor = sunLight.LightColor.Lerp(targetColor, (float)delta);
    }

    // Process queued weather changes (Control Weather manifestation delays)
    for (int i = pendingWeathers.Count - 1; i >= 0; i--)
    {
        pendingWeathers[i].DelaySeconds -= delta;
        if (pendingWeathers[i].DelaySeconds <= 0)
        {
            SetWeather(pendingWeathers[i].Weather, pendingWeathers[i].WindDir);
            activeOverrideDurationSeconds = pendingWeathers[i].DurationSeconds;
            GD.PrintRich($"[color=orange][Weather] Controlled weather '{pendingWeathers[i].Weather.WeatherName}' has fully manifested![/color]");
            pendingWeathers.RemoveAt(i);
        }
    }

    if (activeOverrideDurationSeconds > 0)
    {
        activeOverrideDurationSeconds -= delta;
        if (activeOverrideDurationSeconds <= 0)
        {
            GD.Print("[Weather] Controlled weather has expired. Returning to natural patterns.");
            activeOverrideDurationSeconds = 0;
            // Force an immediate natural weather shift
            OnNewRound(forceNaturalShift: true);
        }
    }
}

public void QueueWeatherChange(Weather_SO newWeather, float delaySeconds, float durationSeconds, Vector3 windDirection)
{
    pendingWeathers.Add(new PendingWeather 
    { 
        Weather = newWeather, 
        DelaySeconds = delaySeconds, 
        DurationSeconds = durationSeconds, 
        WindDir = windDirection 
    });
    GD.Print($"[Weather] A weather change to {newWeather.WeatherName} is brewing and will manifest in {delaySeconds / 60f:F1} minutes.");
}

public void OnNewRound(bool forceNaturalShift = false)
{
    if (activeOverrideDurationSeconds > 0 && !forceNaturalShift) return; // Do not randomly change controlled weather
    if (PossibleWeathers == null || PossibleWeathers.Count <= 1) return;

    if (forceNaturalShift || GD.Randf() <= WeatherChangeChancePerRound)
    {
        Weather_SO newWeather;
        do
        {
            newWeather = PossibleWeathers[GD.RandRange(0, PossibleWeathers.Count - 1)];
        } while (newWeather == CurrentWeather);
        
        SetWeather(newWeather);
    }
}

private void SetWeather(Weather_SO newWeather, Vector3? forcedWindDir = null)
{
    if (newWeather == null) return;

    CurrentWeather = newWeather;
    GD.PrintRich($"<color=orange>[Weather] The weather has changed to: {CurrentWeather.WeatherName}</color>");

    if (CurrentWeather.WindStrength == WindStrength.None)
    {
        CurrentWindDirection = Vector3.Zero;
    }
    else
    {
        if (forcedWindDir.HasValue && forcedWindDir.Value != Vector3.Zero)
        {
            CurrentWindDirection = forcedWindDir.Value.Normalized();
        }
        else
        {
            CurrentWindDirection = new Vector3(GD.Randf() * 2 - 1, 0, GD.Randf() * 2 - 1).Normalized();
        }
        GD.Print($"[Weather] Wind Strength: {CurrentWeather.WindStrength}. Direction: {CurrentWindDirection}");
    }

    // Update Visuals
    if (currentVFXInstance != null) currentVFXInstance.QueueFree();
    
    if (CurrentWeather.VisualEffectPrefab != null && WeatherVFXParent != null)
    {
        currentVFXInstance = CurrentWeather.VisualEffectPrefab.Instantiate<Node3D>();
        WeatherVFXParent.AddChild(currentVFXInstance);
        currentVFXInstance.GlobalPosition = WeatherVFXParent.GlobalPosition;
        
        // Align forward (Negative Z in Godot) to wind direction? 
        if (CurrentWindDirection != Vector3.Zero)
        {
            // LookAt logic: Target = Position + Direction
            currentVFXInstance.LookAt(currentVFXInstance.GlobalPosition + CurrentWindDirection, Vector3.Up);
        }
    }

    // Update Grid
    GridManager.Instance?.UpdateGridForWeather(CurrentWeather);
}

public void UpdatePlayerWeatherVisibility(CreatureStats playerCreature)
{
    if (currentVFXInstance == null || CurrentWeather == null) return;

    bool clearVision = false;
    string wName = CurrentWeather.WeatherName.ToLower();

    if (wName.Contains("snow") || wName.Contains("blizzard"))
    {
        if (playerCreature.Template.HasSnowsight) clearVision = true;
    }
    else if (wName.Contains("rain") || wName.Contains("mist") || wName.Contains("fog"))
    {
        if (playerCreature.Template.HasMistsight) clearVision = true;
    }
	 else if (wName.Contains("smoke") || wName.Contains("ash") || wName.Contains("dust"))
    {
        if (playerCreature.Template.HasSmokeVision) clearVision = true;
    }

    // Apply visual change (Toggle visibility)
    currentVFXInstance.Visible = !clearVision;
    
    if (clearVision) 
        GD.PrintRich($"<color=cyan>Weather visuals disabled due to {playerCreature.Name}'s senses.</color>");
}
}
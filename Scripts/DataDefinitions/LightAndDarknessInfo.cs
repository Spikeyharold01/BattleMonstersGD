using Godot;

[GlobalClass]
public partial class LightAndDarknessInfo : Resource
{
    [Export] public float Radius = 60f;
    [Export] public float OuterRadius = 0f;
    [Export] public int OuterIntensityChange = 0;
	[Export] public bool IsMagical = true; // Default true for spell assets

    [Export(PropertyHint.None, "Steps to change light level. +1 Daylight, -2 Deeper Darkness.")]
    public int IntensityChange = 1;
    
    [Export] public bool IsSupernaturalDarkness = false;
    
    [Export] public float DurationInMinutes = 10f;
}
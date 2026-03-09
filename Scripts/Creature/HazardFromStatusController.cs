using Godot;
// =================================================================================================
// FILE: HazardFromStatusController.cs (GODOT VERSION)
// PURPOSE: Manages hazard controllers that are applied dynamically via status effects.
// ATTACH TO: All creature prefabs (Child Node).
// =================================================================================================
public partial class HazardFromStatusController : Node
{
// Called by ActionManager.OnTurnStart
// (Needs integration into ActionManager similar to others)
public void OnTurnStart()
{
var myEffects = GetParent().GetNodeOrNull<StatusEffectController>("StatusEffectController");
if (myEffects == null) return;

// Check for Cold Aura
    if (myEffects.HasEffect("In Cold Aura"))
    {
        if (GetParent().GetNodeOrNull<ColdHazardController>("ColdHazardController") == null)
        {
            var controller = new ColdHazardController();
            controller.Name = "ColdHazardController";
            GetParent().AddChild(controller);
            controller.Initialize(ColdSeverity.Severe); 
        }
    }
    else
    {
        // If not in a cold aura, remove a controller that isn't from a biome.
        bool isBiomeCold = false;
        if (EnvironmentManager.Instance != null && EnvironmentManager.Instance.CurrentSceneProperties != null)
        {
            isBiomeCold = EnvironmentManager.Instance.CurrentSceneProperties.Contains(EnvironmentProperty.Cold) || 
                          EnvironmentManager.Instance.CurrentSceneProperties.Contains(EnvironmentProperty.Arctic);
        }

        var controller = GetParent().GetNodeOrNull<ColdHazardController>("ColdHazardController");
        if (controller != null && !isBiomeCold)
        {
            // Note: ColdHazardController might have a duration or other state logic for removal.
            // This override logic assumes Status Effect is the only source if Biome isn't.
            controller.QueueFree();
        }
    }
}
}
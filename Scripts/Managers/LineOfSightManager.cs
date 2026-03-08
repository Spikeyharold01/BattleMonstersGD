// =================================================================================================
// FILE: LineOfSightManager.cs (Godot C# Version)
// =================================================================================================
using Godot;
using System.Collections.Generic;
using System.Linq;

public struct VisibilityResult
{
    public bool HasLineOfEffect; 
    public bool HasLineOfSight;
    public int ConcealmentMissChance;
    public int CoverBonusToAC;
    public string Reason;
}

public enum VisibilityState { Hidden, Outlined, Visible, VisibleWithCover }

public static class LineOfSightManager
{
    public const float SCENT_MULTIPLIER_UPWIND = 2.0f;
    public const float SCENT_MULTIPLIER_DOWNWIND = 0.5f;

    public static VisibilityResult GetVisibility(CreatureStats observer, CreatureStats target)
    {
        var result = new VisibilityResult();
        if (observer == null || target == null) return result;
		var banishmentController = target.GetNodeOrNull<TemporaryBanishmentController>("TemporaryBanishmentController");
        if (banishmentController != null && banishmentController.IsBanished)
        {
            result.Reason = "Target is banished";
            return result;
        }


        // --- 1. STANDARD CHECKS THAT APPLY REGARDLESS OF VIEWPOINT ---
        if (target.MyEffects.HasCondition(Condition.Invisible))
        {
            bool seeInvis = observer.MyEffects.HasCondition(Condition.SeeInvisibility) || observer.MyEffects.HasCondition(Condition.TrueSeeing);
            if (!seeInvis)
            {
                result.Reason = "Target is Invisible";
                return result;
            }
        }

    // --- 2. CHECK BASE POSITION + ANY ACTIVE CLAIRVOYANCE SENSORS ---
        var viewOrigins = ScryingSensorRegistry.GetActiveVisionOrigins(observer);
        foreach (var origin in viewOrigins)
        {
            var viewResult = CalculateCoverAndConcealmentFromOrigin(observer, target, origin);
            if (!viewResult.HasLineOfEffect) continue;

            // Keep the first successful origin. This allows the spell sensor to act as a remote, fixed viewer.
            result = viewResult;
            result.HasLineOfSight = true;
            result.Reason = "Clear Line of Sight";
            break;
        }

        if (!result.HasLineOfSight)
        {
            result.HasLineOfEffect = false;
            result.Reason = "Total Cover";
            return result;
        }

        // Check for Status Effects granting Concealment (Blur, Displacement)
        // Rule: Multiple concealment sources don't stack; take the highest.
        if (target.MyEffects != null)
        {
            foreach (var effect in target.MyEffects.ActiveEffects)
            {
                if (!effect.IsSuppressed && effect.EffectData.ConcealmentMissChance > result.ConcealmentMissChance)
                {

                    bool trueSeeing = observer.MyEffects.HasCondition(Condition.TrueSeeing);
                    if (!trueSeeing)
                    {
                        result.ConcealmentMissChance = effect.EffectData.ConcealmentMissChance;
                        result.Reason = $"{effect.EffectData.EffectName} (Concealment)";
                    }
                }
            }
        }
        
        return result;
    }

    public static bool HasLineOfEffect(Node3D context, Vector3 start, Vector3 end)
    {
        // Godot Raycast
        var spaceState = context.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(start, end);
        // Set collision mask for walls (assuming Layer 1)
        query.CollisionMask = 1; 
        
        var hit = spaceState.IntersectRay(query);
        return hit.Count == 0;
    }
	 public static bool HasVisualLineOfSight(Node3D context, Vector3 start, Vector3 end)
    {
        var spaceState = context.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(start, end);
        query.CollisionMask = 1; // Walls

        // We loop raycasts to ignore transparent walls (StructureTraits.IsTransparent)
        var from = start;
        for (int i = 0; i < 5; i++) // Max penetrations
        {
            var hit = spaceState.IntersectRay(query);
            if (hit.Count == 0) return true; // Clear path

            GridNode collider = (GridNode)hit["collider"];
            
            // Check for StructureTraits component on the collider or its parent
            var structure = collider.GetNodeOrNull<StructureTraits>("StructureTraits") ?? 
                            collider.GetParent()?.GetNodeOrNull<StructureTraits>("StructureTraits");
                            
            if (structure != null && structure.IsTransparent)
            {
                // Move origin past the wall and retry
                from = (Vector3)hit["position"] + (end - start).Normalized() * 0.1f;
                query.From = from;
                continue;
            }
            return false; // Hit opaque wall
        }
        return false;
    }

    private static VisibilityResult CalculateCoverAndConcealmentFromOrigin(CreatureStats viewer, CreatureStats target, Vector3 origin)
    {
        var result = new VisibilityResult();
        result.HasLineOfEffect = true; // Default

        Vector3 start = origin + Vector3.Up * 1.5f; // Eye level (can be viewer or remote sensor).
        Vector3 end = target.GlobalPosition + Vector3.Up * 1.0f; // Center mass

        // Check Wall Cover (UPDATED)
        if (HasLineOfEffect(viewer, start, end))
        {
            result.HasLineOfEffect = true;
            result.HasLineOfSight = true; // Implied if physical path is clear
        }
        else
        {
            // LOE Blocked, check if it's just a transparent wall (Gaze works)
            result.HasLineOfEffect = false;
            
            if (HasVisualLineOfSight(viewer, start, end))
            {
                result.HasLineOfSight = true;
                result.Reason = "Blocked by Transparent Barrier"; // e.g. Wall of Force
            }
            else
            {
                result.HasLineOfSight = false;
                result.Reason = "Blocked by Terrain";
            }
            // If physical path is blocked, we return early regardless of visual status
            // because LoE determines if you can *act* on them directly.
            // But for GetVisibility logic, we might want to return the result so the AI knows "I see them but can't hit them".
            // The method continues to Fog check? Usually Fog applies to Visuals.
            // If LoE is blocked but LoS is clear, we should continue.
            
            if (!result.HasLineOfSight) return result; 
        }

        // Check Fog/Smoke via GridManager
        GridNode targetNode = GridManager.Instance.NodeFromWorldPoint(target.GlobalPosition);
   if (targetNode.environmentalTags.Contains("Fog"))
        {
            if (!viewer.MyEffects.HasCondition(Condition.Mistsight))
            {
                result.ConcealmentMissChance = 20;
                result.Reason = "Fog";
            }
        }

        if (targetNode.environmentalTags.Contains("Snow"))
        {
            if (!viewer.Template.HasSnowsight && !viewer.MyEffects.HasCondition(Condition.Snowsight))
            {
                if (result.ConcealmentMissChance < 20)
                {
                    result.ConcealmentMissChance = 20;
                    result.Reason = "Heavy Snow";
                }
            }
        }

        return result;
    }
}
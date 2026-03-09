using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Central place for deciding combat map dimensions across Travel and Arena flows.
///
/// Expected output:
/// - One consistent sizing formula used in both modes.
/// - Travel combat never grows beyond the fixed 1,200 ft tactical window.
/// - Arena combat can use a higher configurable cap without inheriting travel constraints.
/// - Grid cells remain 6 ft so movement and range interpretation are consistent.
/// </summary>
public static class CombatMapSizingService
{
    // Arena uses the same sizing math as Travel, but can have a larger cap.
    // Designers can tune this later from one place without touching formula behavior.
    public const float DefaultArenaMaxSizeFeet = 2400f;

    /// <summary>
    /// Compact telemetry bundle kept intentionally explicit so logs/readers can verify
    /// each part of the formula without hunting through different systems.
    /// </summary>
    public readonly struct CombatSizingTelemetry
    {
        public readonly float MaxSpeedPerRound;
        public readonly float MaxReach;
        public readonly float MaxBodyDiameter;
        public readonly float MaxRelevantAttackRange;
        public readonly float RequiredRadius;
        public readonly float CombatMapSizeFeet;

        public CombatSizingTelemetry(float maxSpeedPerRound, float maxReach, float maxBodyDiameter, float maxRelevantAttackRange, float requiredRadius, float combatMapSizeFeet)
        {
            MaxSpeedPerRound = maxSpeedPerRound;
            MaxReach = maxReach;
            MaxBodyDiameter = maxBodyDiameter;
            MaxRelevantAttackRange = maxRelevantAttackRange;
            RequiredRadius = requiredRadius;
            CombatMapSizeFeet = combatMapSizeFeet;
        }
    }

    /// <summary>
    /// Applies calculated combat dimensions to GridManager.
    ///
    /// Expected output:
    /// - X/Z world size equals the chosen combat map span in feet.
    /// - nodeRadius is set so each grid square is exactly 6 ft.
    /// - Existing vertical world size is preserved to avoid unintended vertical clipping changes.
    /// </summary>
    public static CombatSizingTelemetry ApplySizingToGrid(bool isTravelCombat, float arenaMaxSizeFeet = DefaultArenaMaxSizeFeet)
    {
        List<CreatureStats> combatants = ResolveRelevantCombatants();
        CombatSizingTelemetry telemetry = ComputeSizing(combatants, isTravelCombat, arenaMaxSizeFeet);

        GridManager gridManager = GridManager.Instance;
        if (gridManager == null)
        {
            GD.PrintErr("[CombatMapSizing] GridManager.Instance not found. Combat sizing telemetry computed but not applied.");
            return telemetry;
        }

        float mapSize = telemetry.CombatMapSizeFeet;
        gridManager.nodeRadius = TravelScaleDefinitions.CombatSquareFeet * 0.5f;
        gridManager.gridWorldSize = new Vector3(mapSize, gridManager.gridWorldSize.Y, mapSize);

        // Refreshes only grid-node sampling inside the already-loaded world representation.
        // This does not regenerate strategic/tactical terrain assets.
        gridManager.CreateGrid();

        GD.Print($"[CombatMapSizing] Mode={(isTravelCombat ? "Travel" : "Arena")}, " +
                 $"Speed={telemetry.MaxSpeedPerRound:0.##}, Reach={telemetry.MaxReach:0.##}, " +
                 $"Body={telemetry.MaxBodyDiameter:0.##}, AttackRange={telemetry.MaxRelevantAttackRange:0.##}, " +
                 $"Radius={telemetry.RequiredRadius:0.##}, MapSize={telemetry.CombatMapSizeFeet:0.##}, " +
                 $"GridSquaresPerSide={(int)Mathf.Round(mapSize / TravelScaleDefinitions.CombatSquareFeet)}");

        return telemetry;
    }

    /// <summary>
    /// Computes the exact required map size from encounter metrics.
    ///
    /// Formula (exact):
    /// RequiredRadius = max((MaxSpeedPerRound * 3) + MaxReach + MaxBodyDiameter, MaxRelevantAttackRange)
    /// CombatMapSize = RequiredRadius * 2
    /// Then clamp by mode.
    /// </summary>
    public static CombatSizingTelemetry ComputeSizing(List<CreatureStats> combatants, bool isTravelCombat, float arenaMaxSizeFeet = DefaultArenaMaxSizeFeet)
    {
        float maxSpeedPerRound = 0f;
        float maxReach = 0f;
        float maxBodyDiameter = 0f;
        float maxRelevantAttackRange = 0f;

        foreach (CreatureStats creature in combatants)
        {
            if (creature == null || creature.Template == null)
            {
                continue;
            }

            // Highest movement option available to the creature in one 6-second turn.
            float creatureSpeed = Mathf.Max(creature.Template.Speed_Land,
                creature.Template.Speed_Fly,
                creature.Template.Speed_Swim,
                creature.Template.Speed_Burrow,
                creature.Template.Speed_Climb);
            maxSpeedPerRound = Mathf.Max(maxSpeedPerRound, creatureSpeed);

            // Largest close-quarters engagement distance.
            maxReach = Mathf.Max(maxReach, creature.GetMaxReach());

            // Occupied diameter in feet. Falls back to tactical square size if missing.
            float bodyDiameter = creature.Template.Space > 0f
                ? creature.Template.Space
                : TravelScaleDefinitions.CombatSquareFeet;
            maxBodyDiameter = Mathf.Max(maxBodyDiameter, bodyDiameter);

            // Longest relevant attack line for this creature.
            maxRelevantAttackRange = Mathf.Max(maxRelevantAttackRange, ResolveMaxRelevantAttackRange(creature));
        }

        float maneuverRadius = (maxSpeedPerRound * 3f) + maxReach + maxBodyDiameter;
        float requiredRadius = Mathf.Max(maneuverRadius, maxRelevantAttackRange);
        float mapSize = requiredRadius * 2f;

        float maxSize = isTravelCombat
            ? TravelScaleDefinitions.MaximumTravelCombatMapSizeFeet
            : Mathf.Max(TravelScaleDefinitions.MinimumCombatMapSizeFeet, arenaMaxSizeFeet);

        float clampedMapSize = Mathf.Clamp(mapSize, TravelScaleDefinitions.MinimumCombatMapSizeFeet, maxSize);

        return new CombatSizingTelemetry(
            maxSpeedPerRound,
            maxReach,
            maxBodyDiameter,
            maxRelevantAttackRange,
            requiredRadius,
            clampedMapSize);
    }

    private static List<CreatureStats> ResolveRelevantCombatants()
    {
        // Prefer already-known combat roster. If combat has not been initialized yet,
        // fall back to living scene creatures so map sizing still works on phase entry.
        List<CreatureStats> fromTurnManager = TurnManager.Instance?.GetAllCombatants();
        if (fromTurnManager != null && fromTurnManager.Count > 0)
        {
            return fromTurnManager
                .Where(IsValidCombatant)
                .ToList();
        }

        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
        {
            return new List<CreatureStats>();
        }

var creatures = new List<CreatureStats>();
        foreach (Node node in tree.GetNodesInGroup("Creature"))
        {
            if (node is CreatureStats creature && IsValidCombatant(creature))
            {
                creatures.Add(creature);
            }
        }

        return creatures;
    }

    private static bool IsValidCombatant(CreatureStats creature)
    {
        return creature != null
            && GodotObject.IsInstanceValid(creature)
            && creature.Template != null
            && !creature.IsDead
            && creature.CurrentHP > -creature.Template.Constitution;
    }

    private static float ResolveMaxRelevantAttackRange(CreatureStats creature)
    {
        float maxRange = 0f;

        if (creature.MyInventory != null)
        {
            Item_SO mainHand = creature.MyInventory.GetEquippedItem(EquipmentSlot.MainHand);
            Item_SO offHand = creature.MyInventory.GetEquippedItem(EquipmentSlot.OffHand);

            if (mainHand != null)
            {
                maxRange = Mathf.Max(maxRange, mainHand.RangeIncrement);
            }

            if (offHand != null)
            {
                maxRange = Mathf.Max(maxRange, offHand.RangeIncrement);
            }

            foreach (ItemInstance backpackWeapon in creature.MyInventory.GetBackpackWeapons())
            {
                if (backpackWeapon?.ItemData == null)
                {
                    continue;
                }

                maxRange = Mathf.Max(maxRange, backpackWeapon.ItemData.RangeIncrement);
            }
        }

        if (creature.Template.KnownAbilities != null)
        {
            foreach (Ability_SO ability in creature.Template.KnownAbilities)
            {
                if (ability?.Range == null)
                {
                    continue;
                }

                maxRange = Mathf.Max(maxRange, ability.Range.GetRange(creature));
            }
        }

        if (creature.Template.SpecialAttacks != null)
        {
            foreach (Ability_SO ability in creature.Template.SpecialAttacks)
            {
                if (ability?.Range == null)
                {
                    continue;
                }

                maxRange = Mathf.Max(maxRange, ability.Range.GetRange(creature));
            }
        }

        return maxRange;
    }
}

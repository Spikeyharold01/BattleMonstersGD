using Godot;
using System.Collections.Generic;
using System.Linq;

// =================================================================================================
// FILE: ActionManager.cs (GODOT VERSION - SANITIZED)
// PURPOSE: Manages action economy state (Standard/Move/Swift availability).
// RULES: Enforces hard stops based on conditions (Stunned = No Actions).
//        Does NOT calculate numeric penalties (handled by Stats/Mover).
// =================================================================================================

public partial class ActionManager : Godot.Node
{
    // --- STATE TRACKERS FOR THE CURRENT TURN ---
    [ExportGroup("Current Turn Action States")]
    [Export] private bool hasUsedStandard;
    [Export] private bool hasUsedMove;
    [Export] private bool hasUsedSwift;
    [Export] private bool hasTaken5FootStep;
    [Export] private bool isPerformingFullRound;
    private bool hasPerformedFlybyAttack = false;
    
    public bool IsFightingDefensively { get; set; } 
    public bool IsUsingTotalDefense { get; set; }   
    public bool IsRunning { get; set; } = false;

    // --- STATE TRACKERS ACROSS TURNS ---
    [ExportGroup("Cross-Turn State")]
    [Export]
    [Tooltip("Set to true if an Immediate Action was used when it was not this creature's turn.")]
    private bool hasUsedImmediateOffTurn;
    public bool HasTakenCombatAction { get; private set; }
        
    // --- Cached Components ---
    private CreatureStats myStats;
    private StatusEffectController myEffects;
    private bool isUsingImageSenses = false;
    private StatusEffect_SO blindedEffect;
    private StatusEffect_SO deafenedEffect;
    public CombatOptionsController MyOptions { get; private set; }
	private bool isCursedTurnSkipped = false;

    // --- TURN RESTRICTIONS ---
    [ExportGroup("Turn Restrictions")]
    [Tooltip("Is the creature limited to only a single Standard or Move action this turn? (e.g., Surprise Round)")]
    public bool IsRestrictedToSingleAction { get; private set; }
    
    public float MoveActionDistanceUsed { get; private set; } 

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        myEffects = GetParent().GetNodeOrNull<StatusEffectController>("StatusEffectController");
        MyOptions = GetParent().GetNodeOrNull<CombatOptionsController>("CombatOptionsController");

        blindedEffect = new StatusEffect_SO();
        blindedEffect.EffectName = "Sensory Deprivation (Blinded)";
        blindedEffect.ConditionApplied = Condition.Blinded;
        blindedEffect.DurationInRounds = 0; 

        deafenedEffect = new StatusEffect_SO();
        deafenedEffect.EffectName = "Sensory Deprivation (Deafened)";
        deafenedEffect.ConditionApplied = Condition.Deafened;
        deafenedEffect.DurationInRounds = 0; 
    }

    public void OnTurnStart()
    {
        hasUsedStandard = false;
        hasUsedMove = false;
        hasTaken5FootStep = false;
        isPerformingFullRound = false;
        hasPerformedFlybyAttack = false;
        IsFightingDefensively = false;
        IsUsingTotalDefense = false;
        IsRestrictedToSingleAction = false; 
        MoveActionDistanceUsed = 0f;
        HasTakenCombatAction = false;
        IsRunning = false;
		
		isCursedTurnSkipped = false;
if (myEffects != null && myEffects.HasCondition(Condition.Cursed_Inaction))
{
    if (Dice.Roll(1, 100) <= 50)
    {
        isCursedTurnSkipped = true;
        GD.PrintRich($"[color=red]{GetParent().Name} is cursed and does nothing this turn![/color]");
    }
}

        if (hasUsedImmediateOffTurn)
        {
            hasUsedSwift = true; 
            hasUsedImmediateOffTurn = false; 
        }
        else
        {
            hasUsedSwift = false; 
        }

        if (myEffects != null && (myEffects.HasCondition(Condition.Staggered) || myEffects.HasCondition(Condition.Disabled)))
        {
            IsRestrictedToSingleAction = true;
        }

        myEffects?.OnTurnStart_ResolveRecurringSaves();
        myEffects?.OnTurnStart_EmitOngoingStatusSounds();

        if (TurnManager.Instance.GetCurrentRound() == 0)
        {
            IsRestrictedToSingleAction = true;
        }

        GetParent().GetNodeOrNull<CreatureMover>("CreatureMover")?.OnTurnStart();
        GetParent().GetNodeOrNull<AbilityCooldownController>("AbilityCooldownController")?.TickDownCooldowns();
        GetParent().GetNodeOrNull<MountedCombatController>("MountedCombatController")?.OnTurnStart();
        GetParent().GetNodeOrNull<SwimController>("SwimController")?.OnTurnStart();
        GetParent().GetNodeOrNull<TelekinesisController>("TelekinesisController")?.OnTurnStart();
		GetParent().GetNodeOrNull<GaseousFormController>("GaseousFormController")?.OnTurnStart();
		GetParent().GetNodeOrNull<PassiveEnvironmentBuffController>("PassiveEnvironmentBuffController")?.OnTurnStart();
		GetParent().GetNodeOrNull<ColdHazardController>("ColdHazardController")?.OnTurnStart_ColdCheck();
		GetParent().GetNodeOrNull<PassiveAdhesiveController>("PassiveAdhesiveController")?.OnTurnStart();
		GetParent().GetNodeOrNull<SoulEngineController>("SoulEngineController")?.OnTurnStart();
		GetParent().GetNodeOrNull<HeatHazardController>("HeatHazardController")?.OnTurnStart_HeatCheck();
		GetParent().GetNodeOrNull<HazardFromStatusController>("HazardFromStatusController")?.OnTurnStart();
		 GetParent().GetNodeOrNull<AnimatedObjectController>("AnimatedObjectController")?.OnTurnStart();
        
        MyOptions?.OnTurnStart();
    }

    public void CommitToFlybyAttack(float moveDistanceUsed)
    {
        if (CanPerformAction(ActionType.Standard) && CanPerformAction(ActionType.Move))
        {
            UseAction(ActionType.Standard);
            UseAction(ActionType.Move, moveDistanceUsed);
            hasPerformedFlybyAttack = true;
        }
    }

    public bool CanPerformAction(ActionType type)
    {
		if (isCursedTurnSkipped && type != ActionType.Free && type != ActionType.NotAnAction) return false;
        // --- HARD STOP CONDITIONS ---
        if (myEffects != null)
        {
            if (myEffects.HasCondition(Condition.Dazed)) return false;
            if (myEffects.HasCondition(Condition.Cowering)) return false;
            if (myEffects.HasCondition(Condition.Fascinated)) return false;
            if (myEffects.HasCondition(Condition.Paralyzed)) return false;
            if (myEffects.HasCondition(Condition.Stunned)) return false;
            
            // Nauseated: Move actions only
            if (myEffects.HasCondition(Condition.Nauseated))
            {
                return type == ActionType.Move && !hasUsedMove;
            }
            
            // Pinned: Only escape attempts (Standard/Free usually)
            if (myEffects.HasCondition(Condition.Pinned))
            {
                return type == ActionType.Standard || type == ActionType.Free;
            }

            // Panicked/Frightened: No Aggressive Actions (Standard usually implies attack/cast)
            // Note: This blocks Standard Actions to prevent attacking. 
            // It assumes Fleeing uses Move or FullRound (Run).
            if (myEffects.HasCondition(Condition.Panicked) || myEffects.HasCondition(Condition.Frightened))
            {
                if (type == ActionType.Standard) return false;
            }

            // Generic "calming" style behavior: no violent actions while the effect is active.
            if (myEffects.ActiveEffects.Any(e => !e.IsSuppressed && e.EffectData.BlocksViolentActions))
            {
                if (type == ActionType.Standard || type == ActionType.FullRound) return false;
            }
        }

        if (type == ActionType.Immediate && myStats.IsFlatFooted) return false;

        if ((type == ActionType.Move || type == ActionType.FullRound) && hasPerformedFlybyAttack)
        {
            return false;
        }

        if (isPerformingFullRound)
        {
            return type == ActionType.Free || type == ActionType.Swift || type == ActionType.FiveFootStep;
        }
        
        if (myEffects != null && myEffects.HasCondition(Condition.Grappled))
        {
            if (type == ActionType.Move || type == ActionType.FiveFootStep) return false;
        }

        // --- SURPRISE ROUND / STAGGERED ---
        if (IsRestrictedToSingleAction)
        {
            if (hasUsedStandard || hasUsedMove)
            {
                return type == ActionType.Swift || type == ActionType.Free;
            }
        }
        
        // --- RESOURCE CHECK ---
        switch (type)
        {
            case ActionType.Standard:
                return !hasUsedStandard;

            case ActionType.Move:
                return !hasUsedMove || !hasUsedStandard;

            case ActionType.FullRound:
                if (myEffects != null && myEffects.HasCondition(Condition.Entangled))
                {
                    return false; 
                }
                return !hasUsedStandard && !hasUsedMove;
                
            case ActionType.Swift:
                return !hasUsedSwift;
            
            case ActionType.Immediate:
                return !hasUsedSwift;

            case ActionType.Free:
                return true; 

            case ActionType.FiveFootStep:
                bool canMoveFreely = myStats.Template.Speed_Land > 5f && (myEffects == null || !myEffects.HasCondition(Condition.Entangled));
                return canMoveFreely && !hasUsedMove && !hasTaken5FootStep;

            case ActionType.NotAnAction:
                return true; 

            default:
                return false; 
        }
    }

    public void UseAction(ActionType type, float distanceMoved = 0f)
    {
        if (!CanPerformAction(type))
        {
            GD.PrintErr($"{GetParent().Name} tried to use action '{type}' but it was not available!");
            return;
        }

        if (type != ActionType.Move && type != ActionType.FiveFootStep && type != ActionType.Free && type != ActionType.NotAnAction)
        {
            HasTakenCombatAction = true;
            GetParent().GetNodeOrNull<SwimController>("SwimController")?.OnTakeStrenuousAction();
        }

        switch (type)
        {
            case ActionType.Standard:
                hasUsedStandard = true;
                break;

            case ActionType.Move:
                MoveActionDistanceUsed += distanceMoved;
                if (!hasUsedMove)
                {
                    hasUsedMove = true;
                }
                else 
                {
                    hasUsedStandard = true;
                }
                break;

            case ActionType.FullRound:
                MoveActionDistanceUsed += distanceMoved;
                isPerformingFullRound = true; 
                hasUsedStandard = true;
                hasUsedMove = true;
                break;

            case ActionType.Swift:
                hasUsedSwift = true;
                break;

            case ActionType.Immediate:
                if (TurnManager.Instance.GetCurrentCombatant() == myStats)
                {
                    hasUsedSwift = true;
                }
                else
                {
                    hasUsedImmediateOffTurn = true;
                }
                break;

            case ActionType.Free:
            case ActionType.NotAnAction:
                break;
            
            case ActionType.FiveFootStep:
                MoveActionDistanceUsed += 5f;
                hasTaken5FootStep = true;
                break;
        }

        if (myStats.CurrentHP < 0 && myStats.HasFeat("Diehard"))
        {
            if (type == ActionType.Standard || type == ActionType.FullRound)
            {
                GD.Print($"{myStats.Name} (Diehard) pushes through the pain and takes 1 damage.");
                myStats.TakeDamage(1, "Exertion");
            }
        }
    }
    
    public bool CanPerformAction(Ability_SO ability)
    {
        if (!CanPerformAction(ability.ActionCost))
        {
            return false;
        }

        if (myEffects != null && myEffects.HasCondition(Condition.Grappled))
        {
            if (ability.Components.HasSomatic)
            {
                GD.Print($"Action blocked: Cannot cast {ability.AbilityName} while grappled because it requires a Somatic component.");
                return false;
            }

            if (ability.Components.HasMaterial || ability.Components.HasFocus || ability.Components.HasDivineFocus)
            {
                var mainHandItem = myStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
                var offHandItem = myStats.MyInventory?.GetEquippedItem(EquipmentSlot.OffHand);
                var shield = myStats.MyInventory?.GetEquippedItem(EquipmentSlot.Shield);

                if (mainHandItem != null && (offHandItem != null || shield != null))
                {
                    GD.Print($"Action blocked: Cannot cast {ability.AbilityName} while grappled; both hands are full.");
                    return false;
                }
            }
        }

        var gaseous = GetParent().GetNodeOrNull<GaseousFormController>("GaseousFormController");
        if (gaseous != null && gaseous.IsActionRestricted(ability))
        {
            return false;
        }

        var polymorph = GetParent().GetNodeOrNull<PolymorphController>("PolymorphController");
        if (polymorph != null && polymorph.IsActionRestricted(ability))
        {
            return false;
        }

        return true;
    }

    public void ToggleImageSenses()
    {
        isUsingImageSenses = !isUsingImageSenses;
        if (isUsingImageSenses)
        {
            GD.Print($"{myStats.Name} is now using their projected image's senses and is blinded/deafened.");
            myEffects.AddEffect(blindedEffect, myStats);
            myEffects.AddEffect(deafenedEffect, myStats);
        }
        else
        {
            GD.Print($"{myStats.Name} has switched back to their own senses.");
            myEffects.RemoveEffect(blindedEffect.EffectName);
            myEffects.RemoveEffect(deafenedEffect.EffectName);
        }
    }
}
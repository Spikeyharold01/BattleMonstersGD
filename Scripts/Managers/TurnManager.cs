using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// =================================================================================================
// FILE: TurnManager.cs (GODOT VERSION)
// PURPOSE: The central coordinator for combat turns, UI, and AI activation.
// ATTACH TO: A persistent "GameManager" Node.
// =================================================================================================


/// <summary>
/// Travel-only trigger categories used by TurnManager to switch travel resolution.
/// </summary>
public enum TravelResolutionEvent
{
    PerceptionEvent,
    NoiseDetected,
    SpellCast,
    HostileProximityThresholdCrossed,
    TrapTriggered,
    EnvironmentalTriggerActivated,
    AttackRollInitiated,
    HostileActionDeclared,
    AggressionStateTriggered,
    CombatEnded
}

/// <summary>
/// Manages the turn-based combat sequence, including initiative, turn order,
/// surprise rounds, and determining victory or defeat.
/// It acts as the central state machine for combat.
/// </summary>
public partial class TurnManager : Node
{
    public static TurnManager Instance { get; private set; }

    /// <summary>
    /// Fired when combat victory creates a valid recruitment opportunity for the last defeated opponent.
    /// </summary>
    public event Action<CreatureStats, CreatureStats, RecruitmentEvaluation> RecruitmentDecisionRequested;

    /// <summary>
    /// A private helper class to store a creature's initiative entry.
    /// It holds the creature, their roll, and a flag to indicate if it's a secondary turn from Dual Initiative.
    /// </summary>
    public class CombatantInitiative
    {
        public CreatureStats Stats; // Reference to the creature's stats component.
        public int InitiativeRoll;  // The calculated initiative score for this creature.
        public bool IsSecondaryTurn = false; // Flag to know if this is the "-20" turn.
    }

    [Export]
    [Tooltip("The button the player clicks to end their turn.")]
    public Button EndTurnButton;

    // The master list of all turn entries in the combat, sorted by initiative.
    private List<CombatantInitiative> combatantsInOrder = new List<CombatantInitiative>();
    // An index that points to the current turn entry in the `combatantsInOrder` list.
    private int currentTurnIndex = 0;
    // Tracks the current round number. Starts at 0 for a surprise round, then 1 for normal combat.
    private int currentRound = 1;
    private List<CreatureTemplate_SO> defeatedEnemiesThisCombat = new List<CreatureTemplate_SO>();
    private CreatureStats lastDefeatedEnemyCandidate;
    private CreatureStats pendingRecruitCandidate;
    private CreatureStats pendingRecruitLeader;
    private bool recruitmentDecisionPending;

    // A flag to prevent turn progression while an interrupt (like a readied action or AoO) is resolving.
    private bool isResolvingInterrupt = false;
    private Dictionary<CreatureStats, Ability_SO> counteredSpells = new Dictionary<CreatureStats, Ability_SO>();
    
    // --- REACTION SYSTEM STATE ---
    private bool isAwaitingReaction = false;
    private System.Action<bool> onReactionDecision;
    // Note: Godot C# doesn't use Coroutine type for tracking async tasks easily, using flag logic.
    private bool isTurnInProgress = false; 

    /// <summary>
    /// High-level phase mode currently informing authoritative turn length.
    /// </summary>
    public GamePhaseType CurrentGameMode { get; private set; } = GamePhaseType.Arena;

    /// <summary>
    /// Active travel resolution used only while CurrentGameMode is Travel.
    /// </summary>
    public TravelResolutionState CurrentTravelResolutionState { get; private set; } = TravelResolutionState.StrategicHour;

    /// <summary>
    /// Sole authoritative turn length in seconds for all turn-driven systems.
    /// </summary>
    public int CurrentTurnLengthSeconds { get; private set; } = TravelScaleDefinitions.CombatTurnSeconds;

    private bool _travelThreatFlag;
    private bool _travelPerceptionFlag;
    private bool _travelSpellFlag;
    private int _quietTacticalTurnCount;

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
        
        // Keep authoritative timing aligned from startup.
        SetGameMode(GamePhaseType.Arena);

        // Setup UI Signal
        if(EndTurnButton != null)
        {
            EndTurnButton.Pressed += OnEndTurnButtonPressed;
            EndTurnButton.Visible = false; // Hidden by default
        }
    }

    public override void _EnterTree()
    {
        // Event listeners (assuming CreatureStats has a static C# event for death)
        // CreatureStats.OnCreatureDied += HandleCreatureDeath;
    }

    public override void _ExitTree()
    {
        // Cleanup
        // CreatureStats.OnCreatureDied -= HandleCreatureDeath;
    }

    // Helper to register listeners if C# events are used, usually called by CreatureStats _Ready
    public void RegisterCreatureDeathListener(CreatureStats creature)
    {
        creature.Connect("OnDeath", new Callable(this, nameof(HandleCreatureDeath)));
    }

    /// <summary>
    /// The public entry point to begin a combat encounter.
    /// </summary>
    public void StartCombat()
    {
        _ = StartCombatAsync();
    }

    /// <summary>
    /// Async method that orchestrates the entire combat setup and execution.
    /// </summary>
    private async Task StartCombatAsync()
    {
        CombatMemory.ResetMemory();
		 SoundSystem.Reset();
		 ScentSystem.Reset();
        ReadyActionManager.Instance?.OnCombatStart();
        defeatedEnemiesThisCombat.Clear();
        lastDefeatedEnemyCandidate = null;
        pendingRecruitCandidate = null;
        pendingRecruitLeader = null;
        recruitmentDecisionPending = false;

        // Find all creatures in the group "Creatures" or by type
        // In Godot, GetTree().GetNodesInGroup("Creatures") returns Node array.
        // Assuming CreatureStats is the root node of creatures or easily accessible.
        var allNodes = GetTree().GetNodesInGroup("Creature");
        List<CreatureStats> allCreatures = new List<CreatureStats>();
        foreach(var node in allNodes)
        {
            if(node is CreatureStats stats) allCreatures.Add(stats);
        }

        if (allCreatures.Count == 0)
        {
            GD.PrintRich("[color=yellow]StartCombat called, but no combatants were found![/color]");
            return;
        }

        var awarenessMap = StealthManager.ResolvePreCombatDetection(allCreatures);
        List<CreatureStats> surpriseRoundParticipants = new List<CreatureStats>();
        foreach (var creature in allCreatures)
        {
            // Check if this creature detects any enemies
            bool detectsEnemy = false;
            if (awarenessMap.ContainsKey(creature))
            {
                foreach (var detected in awarenessMap[creature])
                {
                    // Assuming "IsInGroup" check for factions
                    if (detected.IsInGroup("Player") != creature.IsInGroup("Player"))
                    {
                        detectsEnemy = true;
                        break;
                    }
                }
            }

            if (detectsEnemy)
            {
                surpriseRoundParticipants.Add(creature);
            }
            else
            {
                creature.IsFlatFooted = true;
            }
        }

        // Roll initiative for everyone and create the full turn order for the entire combat.
        List<CombatantInitiative> initiativeEntries = RollAndCreateInitiativeList(allCreatures);
        var sortedInitiative = initiativeEntries
            .OrderByDescending(c => c.InitiativeRoll)
            .ThenByDescending(c => c.Stats.DexModifier)
            .ToList();
        combatantsInOrder = sortedInitiative;

        if (surpriseRoundParticipants.Any())
        {
            GD.PrintRich("[color=red]--- SURPRISE ROUND BEGINS ---[/color]");
            currentRound = 0;

            foreach (var combatantEntry in combatantsInOrder)
            {
                if (combatantEntry.IsSecondaryTurn) continue;

                CreatureStats combatant = combatantEntry.Stats;
                if (surpriseRoundParticipants.Contains(combatant))
                {
                    GD.Print($"[Surprise] {combatant.Name} gets a surprise round action.");
                    combatant.IsFlatFooted = false;
                    combatant.GetNode<ActionManager>("ActionManager")?.OnTurnStart();

                    if (combatant.IsInGroup("Player"))
                    {
                        var playerController = combatant.GetNode<PlayerActionController>("PlayerActionController");
                        playerController?.BeginPlayerTurn();
                        // Wait until player turn is done
                        await ToSignal(playerController, "TurnEnded");
                    }
                    else
                    {
                        var aiController = combatant.GetNode<AIController>("AIController");
                        await aiController.ExecuteSurpriseRoundTurn();
                    }
                }
            }
            GD.PrintRich("[color=red]--- SURPRISE ROUND ENDS ---[/color]");
        }
        else
        {
            GD.PrintRich("[color=green]--- NO SURPRISE, COMBAT BEGINS NORMALLY ---[/color]");
        }

        foreach (var combatant in allCreatures)
        {
            combatant.IsFlatFooted = !surpriseRoundParticipants.Contains(combatant);
        }

        GD.Print("--- BEGINNING NORMAL COMBAT ROUNDS ---");
        
        foreach (var combatant in allCreatures)
        {
            combatant.GetNodeOrNull<AIController>("AIController")?.OnCombatStart();
            combatant.GetNodeOrNull<CombatStateController>("CombatStateController")?.OnCombatStart(allCreatures);
        }

        currentRound = 1;
        currentTurnIndex = 0;
        StartTurn();
    }

    /// <summary>
    /// Begins the turn for the current combatant in the initiative order.
    /// </summary>
    private void StartTurn()
    {
        if (CheckForCombatEnd()) return;

        CombatantInitiative currentEntry = GetCurrentCombatantEntry();
        if (currentEntry == null || currentEntry.Stats == null)
        {
            EndTurn();
            return;
        }

        // Fire and forget the async turn logic
        _ = StartTurnAsync(currentEntry);
    }
    
    /// <summary>
    /// Async task to handle all start-of-turn events before the creature can act.
    /// </summary>
    private async Task StartTurnAsync(CombatantInitiative currentEntry)
    {
        isTurnInProgress = true;
        CreatureStats currentCombatant = currentEntry.Stats;
		
        var banishmentController = currentCombatant.GetNodeOrNull<TemporaryBanishmentController>("TemporaryBanishmentController");
        if (banishmentController != null && banishmentController.IsBanished)
        {
            banishmentController.AdvanceBanishmentRound();
            EndTurn();
            return;
        }
        
        // --- PRIMARY TURN ONLY EVENTS ---
        if (!currentEntry.IsSecondaryTurn)
        {
            if (currentCombatant.Template.Speed_Fly > 0)
            {
                var mover = currentCombatant.GetNode<CreatureMover>("CreatureMover");
                await mover.OnTurnStart_WindCheck();
                if (!GodotObject.IsInstanceValid(currentCombatant) || currentCombatant.CurrentHP <= 0) { EndTurn(); return; }
            }
            
            if (currentCombatant.CurrentHP < 0)
            {
                GD.Print($"{currentCombatant.Name} is dying and will make a stabilization check instead of acting.");
                currentCombatant.OnTurnStart_DyingCheck();
                EndTurn();
                return;
            }
			// Whirlpool Logic: Check if caught at start of turn
            var whirlpools = GetTree().GetNodesInGroup("Whirlpool");
            foreach(GridNode n in whirlpools)
            {
                if (n is PersistentEffect_Whirlpool pool) pool.ApplyTurnEffects(currentCombatant);
            }

            // Resolve recurring saves logic here if implemented
            if (!GodotObject.IsInstanceValid(currentCombatant) || currentCombatant.CurrentHP <= 0) { EndTurn(); return; }
        }

        // --- Events for EVERY turn (Primary and Secondary) ---
		// --- ADDED: GAZE CHECK ---
		var gazeControllers = GetTree().GetNodesInGroup("GazeControllers"); // Assuming added to group in editor or Ready
    foreach(GridNode node in gazeControllers)
    {
        if (node is PassiveGazeController gaze) gaze.HandleGazeCheck(currentCombatant);
    }
// Generic Aura Check (Fear, Stench, Fire)
        var auraControllers = GetTree().GetNodesInGroup("PersistentAura");
        foreach(GridNode node in auraControllers)
        {
            if (node is PersistentAuraController aura) aura.CheckAuraExposure(currentCombatant);
        }

        // Frightful Presence Check (Aura Mode)
        var fpControllers = GetTree().GetNodesInGroup("FrightfulPresenceControllers");
        foreach(GridNode node in fpControllers)
        {
            if (node is FrightfulPresenceController fp) fp.CheckExposure(currentCombatant);
        }

         // TRIGGER AUTONOMOUS SPELL ENTITIES OWNED BY CURRENT COMBATANT
        var autonomousEntities = GetTree().GetNodesInGroup("AutonomousEntities");
        foreach (GridNode node in autonomousEntities)
        {
            if (node is AutonomousEntityController entity && entity.Caster == currentCombatant)
            {
                _ = entity.OnCasterTurnStart();
            }
        }
        if (ReadyActionManager.Instance.IsDelaying(currentCombatant))
        {
            GD.Print($"{currentCombatant.Name}'s turn is skipped because they are delaying.");
            EndTurn();
            return;
        }

        if (currentCombatant.IsFlatFooted)
        {
            currentCombatant.IsFlatFooted = false;
        }

        currentCombatant.GetNode<ActionManager>("ActionManager")?.OnTurnStart();

        // --- Wait Logic for Reaction Interruption (if needed) ---
        if (isAwaitingReaction)
        {
            // Simple polling wait
            while (isAwaitingReaction)
            {
                await ToSignal(GetTree().CreateTimer(0.1f), "timeout");
            }
            if (!GodotObject.IsInstanceValid(currentCombatant) || currentCombatant.CurrentHP <= 0) { EndTurn(); return; }
        }

        // --- Activate Controller ---
        if (currentCombatant.IsInGroup("Player"))
        {
            if(EndTurnButton != null) EndTurnButton.Visible = true;
            currentCombatant.GetNode<PlayerActionController>("PlayerActionController")?.BeginPlayerTurn();
        }
        else
        {
            if(EndTurnButton != null) EndTurnButton.Visible = false;
            await currentCombatant.GetNode<AIController>("AIController").DecideAndExecuteBestTurnPlan();
        }
    }

    /// <summary>
    /// Finalizes the current turn and proceeds to the next one.
    /// </summary>
    public void EndTurn()
    {
        if (isAwaitingReaction) return; // Don't end if paused for reaction

        CombatantInitiative activeCombatantEntry = GetCurrentCombatantEntry();
        if (activeCombatantEntry != null && activeCombatantEntry.Stats != null)
        {
            CreatureStats activeCombatant = activeCombatantEntry.Stats;

            // Only tick down effects and handle most end-of-turn events on the PRIMARY turn.
            if (!activeCombatantEntry.IsSecondaryTurn)
            {
                var actionManager = activeCombatant.GetNode<ActionManager>("ActionManager");
                if (actionManager != null)
                {
                    if (!actionManager.HasTakenCombatAction) activeCombatant.ProcessInactionTurn();
                    else activeCombatant.ResetConsecutiveInaction();
                }
				 VisibilityManager.Instance?.ClearTemporaryReveals(activeCombatant);

               if (activeCombatant.Template.Speed_Fly > 0)
                {
                    activeCombatant.GetNode<CreatureMover>("CreatureMover").OnTurnEnd_FlyCheck();
                }
                
                var effectsCtrl = activeCombatant.GetNodeOrNull<StatusEffectController>("StatusEffectController");
                effectsCtrl?.TickDownEffects();
                effectsCtrl?.AdvanceTime(CurrentTurnLengthSeconds); // Duration model always uses authoritative TurnManager turn seconds

				activeCombatant.GetNodeOrNull<PassiveSwarmController>("PassiveSwarmController")?.OnTurnEnd();
				// Whirlpool Logic: Check if they swam
                var endWhirlpools = GetTree().GetNodesInGroup("Whirlpool");
                foreach(GridNode n in endWhirlpools)
                {
                    if (n is PersistentEffect_Whirlpool pool) pool.OnCreatureTurnEnd(activeCombatant);
                }
            }
            // Ready Action triggers can happen on any turn.
            ReadyActionManager.Instance?.CheckTriggers(ReadyTriggerType.CreatureMoves, activeCombatant);
        }

        isTurnInProgress = false;
        currentTurnIndex++;
        
        if (currentTurnIndex >= combatantsInOrder.Count)
        {
            currentTurnIndex = 0;
            currentRound++;
            
            // On a new round, notify all relevant managers.
            WeatherManager.Instance?.OnNewRound();
            CombatMemory.DecayStyleMemory();
            ReadyActionManager.Instance?.OnNewRound();
            GD.Print("--- NEW ROUND ---");

  var summons = GetTree().GetNodesInGroup("SummonedCreatures");
            foreach (GridNode node in summons)
            {
                if (node is SummonedCreatureController summonController)
                {
                    summonController.OnRoundEnd();
                }
            }

            foreach (var combatant in GetAllCombatants())
            {
                combatant?.ResetAoOsForNewRound();
            }
        }
        StartTurn();
    }

    /// <summary>
    /// Rolls initiative for all creatures and creates their turn entries, including secondary turns for Dual Initiative.
    /// </summary>
    private List<CombatantInitiative> RollAndCreateInitiativeList(List<CreatureStats> allCreatures)
    {
        var initiativeList = new List<CombatantInitiative>();
        GD.Print("--- INITIATIVE ROLLS ---");
        foreach (var creature in allCreatures)
        {
            int roll = Dice.Roll(1, 20);
            int initiative = roll + creature.Template.TotalInitiativeModifier;
            
            initiativeList.Add(new CombatantInitiative { Stats = creature, InitiativeRoll = initiative, IsSecondaryTurn = false });
            GD.Print($"{creature.Name} (Primary) rolls a {roll} + {creature.Template.TotalInitiativeModifier} (mod) = {initiative}");

            if (creature.Template.HasDualInitiative)
            {
                int secondaryInitiative = initiative - 20;
                initiativeList.Add(new CombatantInitiative { Stats = creature, InitiativeRoll = secondaryInitiative, IsSecondaryTurn = true });
                GD.Print($"{creature.Name} (Secondary) gets a turn at initiative {secondaryInitiative}");
            }
        }
        return initiativeList;
    }

    /// <summary>
    /// Event handler that removes a dead creature's turns from the combat order.
    /// Called via Signal connection.
    /// </summary>
    public void HandleCreatureDeath(CreatureStats deadCreature)
    {
        if (deadCreature.IsInGroup("Enemy"))
        {
            defeatedEnemiesThisCombat.Add(deadCreature.Template);
            lastDefeatedEnemyCandidate = deadCreature;
        }

        if (deadCreature.IsInGroup("Player") && deadCreature.IsInGroup("Ally"))
        {
            PartyRosterRuntime.ActiveManager?.RemoveOnDeath(deadCreature);
        }

        // Use a reverse loop to safely remove all entries for the dead creature.
        for (int i = combatantsInOrder.Count - 1; i >= 0; i--)
        {
            if (combatantsInOrder[i].Stats == deadCreature)
            {
                if (i < currentTurnIndex)
                {
                    currentTurnIndex--;
                }
                combatantsInOrder.RemoveAt(i);
            }
        }
        CheckForCombatEnd();
    }
	/// <summary>
    /// Adds a revived creature back into the turn order.
    /// </summary>
    public void ReviveCombatant(CreatureStats creature)
    {
        // Check if already in order to avoid duplicates
        if (combatantsInOrder.Any(c => c.Stats == creature)) return;

        // Roll new initiative
        int roll = Dice.Roll(1, 20);
        int initiative = roll + creature.Template.TotalInitiativeModifier;
        
        // Find insert index based on initiative value
        int insertIndex = combatantsInOrder.Count;
        for(int i=0; i<combatantsInOrder.Count; i++)
        {
            if (combatantsInOrder[i].InitiativeRoll < initiative)
            {
                insertIndex = i;
                break;
            }
        }
        
        combatantsInOrder.Insert(insertIndex, new CombatantInitiative { Stats = creature, InitiativeRoll = initiative });
        
        // Adjust current index if insertion happened before current turn
        if (insertIndex <= currentTurnIndex)
        {
            currentTurnIndex++;
        }

        GD.Print($"{creature.Name} has been revived and rolls initiative {initiative}.");
    }

    /// <summary>
    /// Checks if a victory or defeat condition has been met.
    /// </summary>
    private bool CheckForCombatEnd()
    {
        var all = GetAllCombatants();
        int playerCount = all.Count(c => c.IsInGroup("Player") && c.CurrentHP > -c.Template.Constitution);
        int enemyCount = all.Count(c => c.IsInGroup("Enemy") && c.CurrentHP > -c.Template.Constitution);

        if (playerCount > 0 && enemyCount == 0)
        {
            ClearEncounterCorruptionFromCombatants(all);
            GD.PrintRich("[color=green]VICTORY![/color]");
            if(EndTurnButton != null) EndTurnButton.Visible = false;
            PartyRosterRuntime.ActiveManager?.ApplyVictoryMoraleEvent();
            TryRaiseRecruitmentOpportunity();
            return true;
        }
        else if (playerCount == 0)
        {
            ClearEncounterCorruptionFromCombatants(all);
            GD.PrintRich("[color=red]DEFEAT![/color]");
            if(EndTurnButton != null) EndTurnButton.Visible = false;
            PartyRosterRuntime.ActiveManager?.ApplyLossMoraleEvent();
            return true;
        }
        return false;
    }

    private void ClearEncounterCorruptionFromCombatants(List<CreatureStats> combatants)
    {
        foreach (CreatureStats combatant in combatants)
        {
            combatant?.MyEffects?.ClearEncounterCorruption();
        }
    }

    /// <summary>
    /// Inserts a creature that was Delaying back into the turn order to act.
    /// </summary>
    public void InsertAndAct(CreatureStats creature)
    {
        if (isResolvingInterrupt) return;

        CombatantInitiative currentTurnEntry = GetCurrentCombatantEntry();
        if(currentTurnEntry == null) return;
        
        int currentInitiative = currentTurnEntry.InitiativeRoll;
        combatantsInOrder.RemoveAll(c => c.Stats == creature);

        // Find the correct insertion point to maintain sorted order.
        int primaryIndex = combatantsInOrder.FindIndex(c => c.InitiativeRoll < currentInitiative);
        if (primaryIndex == -1) primaryIndex = combatantsInOrder.Count;
        
        combatantsInOrder.Insert(primaryIndex, new CombatantInitiative { Stats = creature, InitiativeRoll = currentInitiative, IsSecondaryTurn = false });

        // If the creature has Dual Initiative, its secondary turn must also be re-inserted.
        if (creature.Template.HasDualInitiative)
        {
            int secondaryInitiative = currentInitiative - 20;
            int secondaryIndex = combatantsInOrder.FindIndex(c => c.InitiativeRoll < secondaryInitiative);
            if (secondaryIndex == -1) secondaryIndex = combatantsInOrder.Count;
            combatantsInOrder.Insert(secondaryIndex, new CombatantInitiative { Stats = creature, InitiativeRoll = secondaryInitiative, IsSecondaryTurn = true });
        }
        
        currentTurnIndex = combatantsInOrder.FindIndex(c => c.Stats == creature && !c.IsSecondaryTurn);
        StartTurn();
    }

    /// <summary>
    /// Changes a creature's permanent initiative position after a Readied Action.
    /// </summary>
    public void ChangeInitiative(CreatureStats creature, int newIndex)
    {
        int oldIndex = combatantsInOrder.FindIndex(c => c.Stats == creature && !c.IsSecondaryTurn);
        combatantsInOrder.RemoveAll(c => c.Stats == creature);
        
        // Clamp index to prevent errors
        if (newIndex >= combatantsInOrder.Count) newIndex = combatantsInOrder.Count > 0 ? combatantsInOrder.Count - 1 : 0;
        if (combatantsInOrder.Count == 0) {
             combatantsInOrder.Add(new CombatantInitiative { Stats = creature, InitiativeRoll = 10, IsSecondaryTurn = false });
             return;
        }

        // The creature's new initiative score is now tied to the spot they acted in.
        int newInitiativeScore = combatantsInOrder[newIndex].InitiativeRoll;
        
        int primaryIndex = combatantsInOrder.FindIndex(c => c.InitiativeRoll < newInitiativeScore);
        if (primaryIndex == -1) primaryIndex = combatantsInOrder.Count;
        combatantsInOrder.Insert(primaryIndex, new CombatantInitiative { Stats = creature, InitiativeRoll = newInitiativeScore, IsSecondaryTurn = false });

        if (creature.Template.HasDualInitiative)
        {
            int secondaryInitiative = newInitiativeScore - 20;
            int secondaryIndex = combatantsInOrder.FindIndex(c => c.InitiativeRoll < secondaryInitiative);
            if (secondaryIndex == -1) secondaryIndex = combatantsInOrder.Count;
            combatantsInOrder.Insert(secondaryIndex, new CombatantInitiative { Stats = creature, InitiativeRoll = secondaryInitiative, IsSecondaryTurn = true });
        }

        // Adjust the currentTurnIndex if the turn order shifted before the current actor.
        if (oldIndex != -1 && newIndex <= currentTurnIndex)
        {
            currentTurnIndex++;
        }
    }

    private void TryRaiseRecruitmentOpportunity()
    {
        if (recruitmentDecisionPending || lastDefeatedEnemyCandidate == null)
        {
            return;
        }

        CreatureStats leader = GetPlayerLeader();
        if (leader == null)
        {
            return;
        }

        RecruitmentEvaluation evaluation = PartyRosterRuntime.ActiveManager?.EvaluateRecruitment(leader, lastDefeatedEnemyCandidate);
        if (evaluation == null)
        {
            return;
        }

        recruitmentDecisionPending = true;
        pendingRecruitCandidate = lastDefeatedEnemyCandidate;
        pendingRecruitLeader = leader;

        if (RecruitmentDecisionRequested == null)
        {
            ResolveRecruitmentDecision(RecruitmentDecision.Release);
            return;
        }

        RecruitmentDecisionRequested.Invoke(leader, lastDefeatedEnemyCandidate, evaluation);
    }

    public bool ResolveRecruitmentDecision(RecruitmentDecision decision)
    {
        if (!recruitmentDecisionPending || pendingRecruitCandidate == null || pendingRecruitLeader == null)
        {
            return false;
        }

        bool success = true;
        if (decision == RecruitmentDecision.Recruit)
        {
            Vector3 spawnPosition = pendingRecruitLeader.GlobalPosition + new Vector3(1.5f, 0f, 1.5f);
            GridNode spawnParent = pendingRecruitLeader.GetParent();
            success = PartyRosterRuntime.ActiveManager?.RecruitCreature(pendingRecruitLeader, pendingRecruitCandidate, spawnParent, spawnPosition, CreaturePersistenceService.Active, out string failureReason) == true;
            if (!success)
            {
                GD.PrintErr($"Recruitment failed: {failureReason}");
            }
        }
        else if (decision == RecruitmentDecision.Study)
        {
            RecruitmentEvaluation studyEvaluation = PartyRosterRuntime.ActiveManager?.EvaluateRecruitment(pendingRecruitLeader, pendingRecruitCandidate);
            bool hadMeaningfulChoice = studyEvaluation?.CanRecruit == true;
            IntelligenceStudyResult studyResult = IntelligenceGrowthRuntime.Service.ApplyStudy(pendingRecruitLeader, pendingRecruitCandidate, studyEvaluation, hadMeaningfulChoice);
            success = studyResult.Applied;
            if (!studyResult.Applied)
            {
                GD.PrintErr($"Study failed: {studyResult.FailureReason}");
            }
            else
            {
                string learnedLanguagesText = studyResult.NewlyLearnedLanguages.Count == 0
                    ? "No new languages learned."
                    : $"Learned languages: {string.Join(", ", studyResult.NewlyLearnedLanguages)}.";

                GD.Print($"Study resolved. IQ {studyResult.IntelligenceBefore} -> {studyResult.IntelligenceAfter}, exposure +{studyResult.ExposureGained:F2}. {learnedLanguagesText}");
            }
        }
        else if (decision == RecruitmentDecision.Release)
        {
            RecruitmentRuntime.ActiveManager?.RecordReleasedKnowledge(pendingRecruitCandidate.Template);
        }

        recruitmentDecisionPending = false;
        pendingRecruitCandidate = null;
        pendingRecruitLeader = null;
        lastDefeatedEnemyCandidate = null;
        return success;
    }


    /// <summary>
    /// Updates the authoritative game mode for timing.
    /// Arena always remains fixed at 6-second turns and ignores travel resolution logic.
    /// </summary>
    public void SetGameMode(GamePhaseType mode)
    {
        CurrentGameMode = mode;

        if (mode == GamePhaseType.Arena)
        {
            CurrentTurnLengthSeconds = TravelScaleDefinitions.CombatTurnSeconds;
            return;
        }

        CurrentTurnLengthSeconds = TravelScaleDefinitions.SecondsFor(CurrentTravelResolutionState);
    }

    /// <summary>
    /// Sets travel resolution using locked conversion constants.
    /// </summary>
    public void SetTravelResolutionState(TravelResolutionState state)
    {
        CurrentTravelResolutionState = state;
        _quietTacticalTurnCount = 0;

        if (CurrentGameMode == GamePhaseType.Travel)
        {
            CurrentTurnLengthSeconds = TravelScaleDefinitions.SecondsFor(CurrentTravelResolutionState);
        }
    }

    /// <summary>
    /// Travel systems call this to report tactical pressure and trigger legal resolution transitions.
    /// </summary>
    public void RegisterTravelResolutionEvent(TravelResolutionEvent resolutionEvent)
    {
        if (CurrentGameMode != GamePhaseType.Travel)
        {
            return;
        }

        switch (resolutionEvent)
        {
            case TravelResolutionEvent.PerceptionEvent:
            case TravelResolutionEvent.NoiseDetected:
            case TravelResolutionEvent.SpellCast:
            case TravelResolutionEvent.HostileProximityThresholdCrossed:
            case TravelResolutionEvent.TrapTriggered:
            case TravelResolutionEvent.EnvironmentalTriggerActivated:
                _travelPerceptionFlag = true;
                if (resolutionEvent == TravelResolutionEvent.SpellCast)
                {
                    _travelSpellFlag = true;
                }
                if (CurrentTravelResolutionState == TravelResolutionState.StrategicHour)
                {
                    SetTravelResolutionState(TravelResolutionState.TacticalMinute);
                }
                break;
            case TravelResolutionEvent.AttackRollInitiated:
            case TravelResolutionEvent.HostileActionDeclared:
            case TravelResolutionEvent.AggressionStateTriggered:
                _travelThreatFlag = true;
                if (CurrentTravelResolutionState == TravelResolutionState.TacticalMinute)
                {
                    SetTravelResolutionState(TravelResolutionState.CombatSixSeconds);
                }
                break;
            case TravelResolutionEvent.CombatEnded:
                _travelThreatFlag = false;
                if (CurrentTravelResolutionState == TravelResolutionState.CombatSixSeconds)
                {
                    SetTravelResolutionState(TravelResolutionState.TacticalMinute);
                }
                break;
        }
    }

    /// <summary>
    /// Advances one travel turn worth of duration logic using authoritative turn seconds.
    /// </summary>
    public void AdvanceTravelTurn(IEnumerable<CreatureStats> creatures, bool hasHostiles, bool hasPerceptionEvents, bool hasThreatState)
    {
        if (CurrentGameMode != GamePhaseType.Travel)
        {
            return;
        }

        int turnSeconds = CurrentTurnLengthSeconds;
        if (creatures != null)
        {
            foreach (CreatureStats creature in creatures)
            {
                if (creature == null || !GodotObject.IsInstanceValid(creature))
                {
                    continue;
                }

                creature.GetNodeOrNull<StatusEffectController>("StatusEffectController")?.AdvanceTime(turnSeconds);
            }
        }

        bool tacticalQuietTurn = CurrentTravelResolutionState == TravelResolutionState.TacticalMinute
            && !hasHostiles
            && !hasPerceptionEvents
            && !_travelSpellFlag
            && !hasThreatState
            && !_travelThreatFlag;

        if (tacticalQuietTurn)
        {
            _quietTacticalTurnCount++;
            if (_quietTacticalTurnCount >= 10)
            {
                SetTravelResolutionState(TravelResolutionState.StrategicHour);
            }
        }
        else
        {
            _quietTacticalTurnCount = 0;
        }

        _travelPerceptionFlag = false;
        _travelSpellFlag = false;
    }

    // --- ACCESSORS AND OTHER METHODS ---

    public CombatantInitiative GetCurrentCombatantEntry()
    {
        if (combatantsInOrder == null || combatantsInOrder.Count == 0 || currentTurnIndex >= combatantsInOrder.Count) return null;
        return combatantsInOrder[currentTurnIndex];
    }
    
    public CreatureStats GetCurrentCombatant() => GetCurrentCombatantEntry()?.Stats;

    public List<CreatureStats> GetAllCombatants() => combatantsInOrder?.Select(c => c.Stats).Distinct().ToList() ?? new List<CreatureStats>();
    
    public int GetCurrentRound() => currentRound;

    public int GetCurrentTurnIndex() => currentTurnIndex;

    public CreatureStats GetPlayerLeader() => GetAllCombatants()?.FirstOrDefault(c => c.IsInGroup("Player"));
    
    public void SetInterruptState(bool isInterrupting) => this.isResolvingInterrupt = isInterrupting;

    public void OnEndTurnButtonPressed() { if (GetCurrentCombatant()?.IsInGroup("Player") == true) EndTurn(); }
    
    // --- Reaction System ---
    public void SetSpellAsCountered(CreatureStats caster, Ability_SO spell) { counteredSpells[caster] = spell; }
    
    public bool WasSpellCountered(CreatureStats caster, Ability_SO spell) { return counteredSpells.Remove(caster, out Ability_SO removed) && removed == spell; }
    
    public async Task WaitForReaction(CreatureStats reactingCreature, float timeout, System.Action<bool> decisionCallback) 
    { 
        isAwaitingReaction = true; 
        onReactionDecision = decisionCallback; 
        
        // Wait loop using Godot timer
        float timer = 0;
        while(isAwaitingReaction && timer < timeout)
        {
            await ToSignal(GetTree().CreateTimer(0.1f), "timeout");
            timer += 0.1f;
        }
        
        if(isAwaitingReaction) MakeReactionDecision(false); // Default to no reaction on timeout
    }
    
    public void MakeReactionDecision(bool choseToReact) 
    { 
        if(isAwaitingReaction) 
        { 
            isAwaitingReaction = false; 
            onReactionDecision?.Invoke(choseToReact); 
        } 
    }
}

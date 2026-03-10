using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: AIController.cs (GODOT VERSION)
// PURPOSE: The "brain" component for an individual AI-controlled creature.
// ATTACH TO: AI-controlled creature scenes (Child Node).
// =================================================================================================
[GlobalClass]
public partial class AIPersonalityProfile : Resource
{
[Export(PropertyHint.Range, "0,200")] public float W_Aggressive = 100f;
[Export(PropertyHint.Range, "0,200")] public float W_Defensive = 100f;
[Export(PropertyHint.Range, "0,200")] public float W_Strategic = 100f;
[Export] public float B_ExploitWeakness = 25f;
}

public struct PendingSoundLearning
{
public bool IsActive;
public int TriggerRound;
public int IntelligenceAtDecision;
public float ThreatAtDecision;
public int HPAtDecision;
public int DamageTakenAfterDecision;
public bool DiedAfterDecision;
}

public partial class AIController : Node
{
// --- Cached Component References ---
public CreatureStats MyStats { get; private set; }
public ActionManager MyActionManager { get; private set; }
public CreatureMover MyMover { get; private set; }

[ExportGroup("AI Personality")]
[Export] public AIPersonalityProfile Personality;
[Export(PropertyHint.Range, "0,100")]
public float LoyaltyToPlayer = 50f;

private TacticalData myTactics;
private PendingSoundLearning pendingSoundLearning;
private MoraleDecisionModifiers _moraleModifiers;
private readonly RandomNumberGenerator _instabilityRng = new RandomNumberGenerator();

public override void _Ready()
{
	// Cache references from parent (Creature Root)
	MyStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
	MyActionManager = GetParent().GetNodeOrNull<ActionManager>("ActionManager");
	MyMover = GetParent().GetNodeOrNull<CreatureMover>("CreatureMover");
	
	if (Personality == null) Personality = new AIPersonalityProfile();
	_instabilityRng.Seed = (ulong)Mathf.Abs(GetParent().GetInstanceId() * 7919);
	_moraleModifiers = MoraleLoyaltyResolver.BuildDecisionModifiers(0.7f, LoyaltyToPlayer / 100f);
	
	CreatureStats.OnAnyCreatureDamaged += OnAnyCreatureDamagedForSoundLearning;
	CreatureStats.OnCreatureDied += OnAnyCreatureDiedForSoundLearning;
}

public override void _ExitTree()
{
	ResolvePendingSoundLearningWindow();
	CreatureStats.OnAnyCreatureDamaged -= OnAnyCreatureDamagedForSoundLearning;
	CreatureStats.OnCreatureDied -= OnAnyCreatureDiedForSoundLearning;
}

public void OnCombatStart()
{
	myTactics = AITacticalMatrix.GetPerfectTactics();
	AttemptToIdentifyEnemies();
}

#region AI Turn Logic

public async Task DecideAndExecuteBestTurnPlan(TurnPlan playerSuggestion = null)
{
   ResolvePendingSoundLearningWindow();
	UpdateSensesData(); 
	if (EvaluateArenaDesertionAndBetrayal())
	{
		TurnManager.Instance?.EndTurn();
		return;
	}

	TurnPlan bestPlan = DecideBestTurnPlan(playerSuggestion);

	if (bestPlan != null && bestPlan.Score > 0)
	{
		string source = bestPlan.IsPlayerSuggestion ? "Player Suggestion" : "AI Decision";
		GD.Print($"<color=yellow>{GetParent().Name} decides on plan: {bestPlan.Name} (Source: {source}, Score: {bestPlan.Score:F1})</color>");
		if (!string.IsNullOrWhiteSpace(bestPlan.DecisionNarrative))
		{
			GD.PrintRich($"[color=light_blue][Ally Reasoning][/color] {bestPlan.DecisionNarrative}");
		}
		
		foreach (var action in bestPlan.Actions)
		{
			await action.Execute();
			if (!GodotObject.IsInstanceValid(this) || MyStats.CurrentHP <= 0) break;
		}
	}
	else
	{
		GD.Print($"<color=yellow>{GetParent().Name} can't find a good action and ends its turn.</color>");
		await ToSignal(GetTree().CreateTimer(1f), "timeout");
	}

	TurnManager.Instance?.EndTurn();
}

public async Task ExecuteSurpriseRoundTurn()
{
	var allPossibleActions = GeneratePossibleSingleActions();
	
	// Filter actions
	var attackActions = allPossibleActions.OfType<AIAction_Attack>()
		.Concat<AIAction>(allPossibleActions.OfType<AIAction_SingleNaturalAttack>())
		.ToList();
	
	if (attackActions.Any())
	{
		foreach(var a in attackActions) a.CalculateScore();
		var bestAttack = attackActions.OrderByDescending(a => a.Score).First();
		GD.PrintRich($"[color=red][Surprise] {GetParent().Name} decides to: {bestAttack.Name}[/color]");
		await bestAttack.Execute();
	}
	else
	{
		var moveActions = allPossibleActions.OfType<AIAction_MoveToPosition>().ToList();
		if (moveActions.Any())
		{
			foreach(var a in moveActions) a.CalculateScore();
			var bestMove = moveActions.OrderByDescending(a => a.Score).First();
			GD.PrintRich($"[color=red][Surprise] {GetParent().Name} decides to: {bestMove.Name}[/color]");
			await bestMove.Execute();
		}
		else
		{
			GD.PrintRich($"[color=red][Surprise] {GetParent().Name} has no available actions.[/color]");
		}
	}
}

public async Task ExecuteDominatedTurn()
{
	// Delegate to the static logic class
	await AIDominationLogic.ExecuteDominatedTurn(MyStats, MyMover);
}

private void OnAnyCreatureDamagedForSoundLearning(CreatureStats victim, CreatureStats attacker)
{
	if (!pendingSoundLearning.IsActive || victim != MyStats) return;

	int delta = Mathf.Max(0, pendingSoundLearning.HPAtDecision - MyStats.CurrentHP);
	pendingSoundLearning.DamageTakenAfterDecision = Mathf.Max(pendingSoundLearning.DamageTakenAfterDecision, delta);
}

private void OnAnyCreatureDiedForSoundLearning(CreatureStats victim)
{
	if (!pendingSoundLearning.IsActive) return;
	if (victim == MyStats)
	{
		pendingSoundLearning.DiedAfterDecision = true;
	}
}

private void BeginSoundLearningWindow(float threatEstimate)
{
	if (pendingSoundLearning.IsActive) return;

	pendingSoundLearning = new PendingSoundLearning
	{
		IsActive = true,
		TriggerRound = TurnManager.Instance?.GetCurrentRound() ?? 0,
		IntelligenceAtDecision = MyStats.Template.Intelligence,
		ThreatAtDecision = threatEstimate,
		HPAtDecision = MyStats.CurrentHP,
		DamageTakenAfterDecision = 0,
		DiedAfterDecision = false
	};
}

private void ResolvePendingSoundLearningWindow()
{
	if (!pendingSoundLearning.IsActive) return;

	int currentRound = TurnManager.Instance?.GetCurrentRound() ?? pendingSoundLearning.TriggerRound;
	bool expired = currentRound > pendingSoundLearning.TriggerRound;
	bool resolvedByDeath = pendingSoundLearning.DiedAfterDecision;
	if (!expired && !resolvedByDeath) return;

	float maxHp = Mathf.Max(1f, MyStats.Template.MaxHP);
	float damageRatio = pendingSoundLearning.DamageTakenAfterDecision / maxHp;

	// Positive delta means become more cautious, negative means confidence in current response.
	float cautionDelta = 0f;
	if (pendingSoundLearning.DiedAfterDecision || damageRatio >= 0.45f) cautionDelta = 2.2f;
	else if (damageRatio >= 0.25f) cautionDelta = 1.4f;
	else if (damageRatio >= 0.1f) cautionDelta = 0.8f;
	else cautionDelta = -0.25f;

	AITacticalMatrix.RecordSoundOutcome(
		pendingSoundLearning.IntelligenceAtDecision,
		pendingSoundLearning.ThreatAtDecision,
		cautionDelta);

	pendingSoundLearning = default;
}


#endregion

#region Sensing & Identification

private void UpdateSensesData()
{
	// Know Alignment (Su): Instantly know exact alignment of visible targets.
	bool hasKnowAlignment = MyStats.Template.SpecialQualities.Contains("Know Alignment") || 
							(MyStats.Template.VisionQualities != null && MyStats.Template.VisionQualities.Contains("Know Alignment"));

	var creaturesToScan = FindVisibleTargets();

	if (hasKnowAlignment)
	{
		foreach (var target in creaturesToScan)
		{
			// Instantly record the full alignment string (e.g. "Chaotic Neutral")
			CombatMemory.RecordAlignment(target, target.Template.Alignment);
		}
	}
	 // --- INSERT HERE (Replaces previous Detect Chaos logic) ---
	// Refresh scan list
	creaturesToScan = FindVisibleTargets();
	
	// Helper to perform the check for a specific alignment type
	void CheckAlignmentSense(bool hasSense, int range, string alignKey1, string alignKey2, string conditionName)
	{
		// Check Template OR Status Effect
		bool isActive = hasSense || (MyStats.MyEffects != null && MyStats.MyEffects.HasConditionStr(conditionName));
		if (!isActive) return;

		float effectiveRange = hasSense ? range : 60f; // Default 60 for spell version

		foreach (var target in creaturesToScan)
		{
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= effectiveRange)
			{
				string targetAlign = target.Template.Alignment;
				// Check for word (e.g. "Evil") or abbreviation letter (e.g. "E" in "LE")
				if (targetAlign.Contains(alignKey1) || targetAlign.Contains(alignKey2))
				{
					CombatMemory.RecordAlignment(target, targetAlign);
				}
			}
		}
	}

	// Run checks for all 4
	CheckAlignmentSense(MyStats.Template.HasDetectChaos, MyStats.Template.DetectChaosRange, "Chaos", "C", "SensingChaos");
	CheckAlignmentSense(MyStats.Template.HasDetectEvil,  MyStats.Template.DetectEvilRange,  "Evil",  "E", "SensingEvil");
	CheckAlignmentSense(MyStats.Template.HasDetectGood,  MyStats.Template.DetectGoodRange,  "Good",  "G", "SensingGood");
	CheckAlignmentSense(MyStats.Template.HasDetectLaw,   MyStats.Template.DetectLawRange,   "Law",   "L", "SensingLaw");
	
	// Scent Logic (additive perception channel, parallel to hearing)
	foreach (var combatant in TurnManager.Instance.GetAllCombatants())
	{
		ScentSystem.EmitCreatureScent(combatant, isTrail: true);
	}

	var smelledContacts = AISpatialAnalysis.FindSmelledContacts(MyStats);
	foreach (var contact in smelledContacts)
	{
		if (contact.Source == null) continue;

		bool canSee = LineOfSightManager.GetVisibility(MyStats, contact.Source).HasLineOfSight;
		if (canSee && (contact.Source.MyEffects == null || !contact.Source.MyEffects.HasCondition(Condition.Invisible)))
		{
			continue;
		}

		CombatMemory.RecordSmelledEnemy(MyStats, contact.Source);

		var locationStatus = contact.IsPinpointed
			? LocationStatus.Pinpointed
			: (contact.Confidence >= 0.55f ? LocationStatus.KnownDirection : LocationStatus.DetectedPresence);

		GetParent().GetNode<CombatStateController>("CombatStateController")?.UpdateEnemyLocation(contact.Source, locationStatus);
	}
	
	// Hearing Logic (additive perception channel)
	// Heard contacts are intentionally unreliable and treated as uncertain direction data.
	var heardContacts = AISpatialAnalysis.FindHeardContacts(MyStats);
	foreach (var contact in heardContacts)
	{
		if (contact.Source != null)
		{
			GetParent().GetNode<CombatStateController>("CombatStateController")?.UpdateEnemyLocation(contact.Source, LocationStatus.KnownDirection);
		}
	}

	// Keen Scent Special: Detect Tiefling/Half-Fiend
	if (MyStats.Template.SpecialQualities.Contains("Keen Scent"))
	{
		foreach (var target in creaturesToScan)
		{
			// Only check if within scent range (60ft base)
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.Template.ScentRange)
			{
				bool isFiendBlood = target.Template.Race.Contains("Tiefling") || target.Template.SubTypes.Contains("Half-Fiend");
				if (isFiendBlood)
				{
					// Make DC 15 Wisdom Check
					int wisCheck = Dice.Roll(1, 20) + MyStats.WisModifier;
					if (wisCheck >= 15)
					{
						GD.PrintRich($"[color=green]{GetParent().Name} sniffs out the fiendish blood in {target.Name}![/color]");
						CombatMemory.RecordIdentifiedTrait(target, "Race: Fiend-Blooded");
					}
				}
			}
		}
	}
	// Detect Magic Logic (Passive)
	if (MyStats.Template.HasDetectMagic)
	{
		float range = (MyStats.Template.DetectMagicRange > 0) ? MyStats.Template.DetectMagicRange : 60f;
		foreach (var target in creaturesToScan)
		{
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= range)
			{
				// Check if the target has an AuraController child node (since components are children)
				// Note: In Godot, AuraController is likely a Child Node of the Creature.
				// We iterate children or check known path.
				// Or check Group "AuraControllers" on children.
				// For simplicity, GetNodeOrNull.
				var auraController = target.GetNodeOrNull<AuraController>("AuraController");
				
				if (auraController != null && auraController.Auras.Count > 0)
				{
					// Record the strongest aura found
					CombatMemory.RecordMagicalAura(target, auraController.GetMostPotentAuraStrength());
				}
			}
		}
	}
	
	if (MyStats.MyEffects.HasCondition(Condition.SensingDeathwatch))
	{
		creaturesToScan = FindVisibleTargets();
		foreach (var creature in creaturesToScan)
		{
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(creature.GlobalPosition) <= 30f)
			{
				HealthStatus status;
				if (creature.CurrentHP <= 0) status = HealthStatus.Dead;
				else if (creature.Template.Type == CreatureType.Undead) status = HealthStatus.Undead;
				else if (creature.Template.Type == CreatureType.Construct) status = HealthStatus.Construct;
				else if (creature.CurrentHP <= 3) status = HealthStatus.Fragile;
				else if (creature.CurrentHP < creature.Template.MaxHP) status = HealthStatus.Wounded;
				else status = HealthStatus.Healthy;
				
				CombatMemory.RecordHealthStatus(creature, status);
			}
		}
	}
}

private void AttemptToIdentifyEnemies()
{
	if (MyStats.Template.Intelligence < 12) return;

	foreach (var enemy in FindVisibleTargets())
	{
		if (enemy.Template.MythicRank > 0)
		{
			int dc = 15 + enemy.Template.ChallengeRating + enemy.Template.MythicRank;
			int knowledgeBonus = MyStats.GetSkillBonus(enemy.Template.AssociatedKnowledgeSkill);
			int roll = Dice.Roll(1, 20) + knowledgeBonus;

			if (roll >= dc)
			{
				CombatMemory.RecordMythicStatus(enemy);
			}
		}
	}
}

#endregion

#region Planning & Scoring

private TurnPlan DecideBestTurnPlan(TurnPlan playerSuggestion = null)
{
	 // --- NEW CODE START: Soul Bound Bodyguard Logic ---
	if (MyStats.CurrentGrappleState != null && MyStats.CurrentGrappleState.Controller == MyStats)
	{
		var victim = MyStats.CurrentGrappleState.Target;
		if (victim != null && victim.MyEffects.HasCondition(Condition.SoulBound))
		{
			Personality.W_Defensive += 50f; 
			GD.Print($"{GetParent().Name} shifts to Bodyguard Mode (Soul Bound victim held). Defensive weight increased.");
		}
	}
	
	List<TurnPlan> possiblePlans = GeneratePossibleTurnPlans();
	List<AIAction> singleActions = GeneratePossibleSingleActions();
	
	foreach (var action in singleActions)
	{
		possiblePlans.Add(new TurnPlan(action));
	}

	foreach (var plan in possiblePlans)
	{
		plan.Score = ScoreTurnPlan(plan);
	}

	ResolveDynamicMoraleModifiers();

	float playerSuggestionRawScore = 0f;
	float influenceMultiplier = 1f;
	bool hasSuggestion = playerSuggestion != null && playerSuggestion.Actions.Any();
	ObedienceOutcome obedienceOutcome = ObedienceOutcome.FullCompliance;
	if (hasSuggestion)
	{
		obedienceOutcome = ResolveObedienceOutcome();
		playerSuggestionRawScore = ScoreTurnPlan(playerSuggestion, isPlayerSuggestionContext: true);
		influenceMultiplier = CalculatePlayerInfluenceMultiplier() * _moraleModifiers.ObedienceModifier;

		if (obedienceOutcome == ObedienceOutcome.PartialCompliance)
		{
			playerSuggestionRawScore *= 0.75f;
			playerSuggestionRawScore += 20f;
		}
		else if (obedienceOutcome == ObedienceOutcome.Refusal)
		{
			playerSuggestionRawScore *= 0.3f;
		}
		else if (obedienceOutcome == ObedienceOutcome.ExtremeInstability)
		{
			playerSuggestionRawScore = -10f;
		}

		playerSuggestion.Score = playerSuggestionRawScore * influenceMultiplier;
		possiblePlans.Add(playerSuggestion);
		GD.Print($"Player suggestion scored at {playerSuggestion.Score / Mathf.Max(0.01f, influenceMultiplier):F1}, boosted by {influenceMultiplier:F2} to {playerSuggestion.Score:F1} ({obedienceOutcome})");
	}

	if (!possiblePlans.Any())
	{
		GD.Print($"{GetParent().Name} has no possible plans and will do nothing.");
		return new TurnPlan(); 
	}

	foreach (var plan in possiblePlans)
	{
		var actionWithTarget = plan.Actions.FirstOrDefault(a => a.GetTarget() != null);
		if(actionWithTarget != null)
		{
			var target = actionWithTarget.GetTarget();
			AuraStrength strength = CombatMemory.GetKnownAuraStrength(target);
			if (strength >= AuraStrength.Strong)
			{
				float multiplier = (strength == AuraStrength.Overwhelming) ? 1.5f : 1.25f;
				plan.Score *= multiplier;
				plan.UpdateName(); 
			}
		}
	}

	possiblePlans = possiblePlans.OrderByDescending(p => p.Score).ToList();

	GD.Print($"--- {GetParent().Name}'s Plan Deliberation ---");
	for (int i = 0; i < Mathf.Min(5, possiblePlans.Count); i++)
	{
		if (possiblePlans[i].Score > 0)
		{
			string tag = possiblePlans[i].IsPlayerSuggestion ? " [PLAYER]" : " [AI]";
			GD.Print($"Option {i + 1}: {possiblePlans[i].Name}{tag} (Score: {possiblePlans[i].Score:F1})");
		}
	}
	
var best = possiblePlans.First();
	if (hasSuggestion && obedienceOutcome == ObedienceOutcome.ExtremeInstability)
	{
		TurnPlan instabilityPlan = BuildExtremeInstabilityPlan();
		if (instabilityPlan != null)
		{
			best = instabilityPlan;
		}
	}
	if (hasSuggestion)
	{
		bool followsSuggestion = best.IsPlayerSuggestion;
		string reasonA = followsSuggestion
			? $"your charisma and their loyalty align ({influenceMultiplier:F2}x influence)"
			: BuildCounterReason(best, playerSuggestionRawScore);
		string reasonB = followsSuggestion
			? BuildSupportReason(best)
			: BuildSupportReason(best);

		// We pass 'this' as the controller and a dummy SuggestedAction since the original data is packed in the plan context here.
		best.DecisionNarrative = AllyDecisionNarrativeGenerator.BuildNarrative(this, new SuggestedAction(), best, followsSuggestion);
			}

	return best;
}

private List<TurnPlan> GeneratePossibleTurnPlans()
{
	var possiblePlans = new List<TurnPlan>();
	var visibleTargets = FindVisibleTargets();

	// --- GENERATE MULTI-ACTION "MOVE THEN ATTACK" PLANS ---
	var equippedWeapon = MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
	var attacksToConsider = new List<(NaturalAttack naturalAttack, Item_SO weapon)>();
	if (equippedWeapon != null) attacksToConsider.Add((null, equippedWeapon));
	if (MyStats.Template.MeleeAttacks != null) 
	{
		foreach(var na in MyStats.Template.MeleeAttacks) attacksToConsider.Add((na, null));
	}
	if (!attacksToConsider.Any()) attacksToConsider.Add((null, null)); // Unarmed strike

	foreach (var target in visibleTargets)
	{
		foreach (var attack in attacksToConsider.Distinct())
		{
			var (minReach, maxReach) = attack.naturalAttack != null ? MyStats.GetEffectiveReach(attack.naturalAttack) : MyStats.GetEffectiveReach(attack.weapon);
			float dist = GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition);

			if ((dist > maxReach || dist < minReach) && MyActionManager.CanPerformAction(ActionType.Move))
			{
				GridNode bestPosForThisAttack = AISpatialAnalysis.FindPositionToUseAttack(MyStats, MyMover, target, minReach, maxReach);
				if (bestPosForThisAttack != null)
				{
					var path = Pathfinding.Instance.FindPath(MyStats, GetParent<Node3D>().GlobalPosition, bestPosForThisAttack.worldPosition);
					if (path != null)
					{
						var moveAction = new AIAction_MoveToPosition(this, bestPosForThisAttack.worldPosition, target);
						// HasStandardAction checks internal flags
						// We can use CanPerformAction here instead of internal flag access for safety
						if (MyActionManager.CanPerformAction(ActionType.Standard)) 
						{
							AIAction attackAction = null;
							if (attack.weapon != null)
							{
								var dummyAbility = new Ability_SO { AbilityName = attack.weapon.ItemName };
								attackAction = new AIAction_Attack(this, target, dummyAbility, null);
							}
							else if (attack.naturalAttack != null)
							{
								attackAction = new AIAction_SingleNaturalAttack(this, target, attack.naturalAttack);
							}
							if(attackAction != null)
								possiblePlans.Add(new TurnPlan(new List<AIAction> { moveAction, attackAction }));
						}
					}
				}
			}
		}
	}
	return possiblePlans;
}

private float ScoreTurnPlan(TurnPlan plan, bool isPlayerSuggestionContext = false)
{
	float totalScore = 0;
	foreach (var action in plan.Actions)
	{
		action.CalculateScore(); 
		totalScore += action.Score;
	}

 if (isPlayerSuggestionContext)
	{
		bool includesHide = plan.Actions.Any(a => a is AIAction_Hide);
		if (includesHide)
		{
			totalScore += 60f;
		}
	}

	if (plan.Actions.Count > 1) totalScore *= 1.1f;

	bool isAggressivePlan = plan.Actions.Any(a => a is AIAction_Attack || a is AIAction_FullAttack || a is AIAction_SingleNaturalAttack || a is AIAction_CastGenericAbility);
	bool isDefensivePlan = plan.Actions.Any(a => a is AIAction_Hide || a is AIAction_MoveToPosition);

	if (isAggressivePlan)
	{
		totalScore *= _moraleModifiers.AggressionModifier;
		totalScore *= _moraleModifiers.RiskBiasModifier;
	}

	if (isDefensivePlan)
	{
		totalScore *= Mathf.Lerp(1.25f, 0.85f, Mathf.Clamp(_moraleModifiers.RiskBiasModifier, 0f, 1.5f));
	}

	// --- SOUL ENGINE PRESERVATION LOGIC ---
	if (MyStats.HasSpecialRule("Soul Engine"))
	{
		bool enemiesInMelee = FindVisibleTargets().Any(e => e.GlobalPosition.DistanceTo(MyStats.GlobalPosition) <= 10f);
		if (enemiesInMelee)
		{
			// If surrounded, moving away or attacking the closest threat is massively prioritized.
			if (plan.Actions.Any(a => a is AIAction_MoveToPosition || a is AIAction_Withdraw || a is AIAction_Attack))
			{
				totalScore *= 2.0f; 
				plan.UpdateName();
			}
		}
	}

	var harmlessEffect = MyStats.MyEffects.ActiveEffects.FirstOrDefault(e => e.EffectData.EffectName == "Perceived as Harmless");
	if (harmlessEffect != null && harmlessEffect.SourceCreature != null)
	{
		var harmlessSource = harmlessEffect.SourceCreature;
				bool isHarmlessTargetedPlan = plan.Actions.Any(action => 
			action.GetTarget() == harmlessSource && 
			(action is AIAction_Attack || action is AIAction_FullAttack || (action is AIAction_CastGenericAbility cast && cast.Score > 0))
		);

		if (isHarmlessTargetedPlan)
		{
			totalScore = -1f; 
			plan.UpdateName(); 
		}
	}
	
	return totalScore;
}

private float GetVisibleEnemyCorruptionPressure()
{
	float pressure = 0f;
	var visibleEnemies = AISpatialAnalysis.FindVisibleTargets(MyStats);
	foreach (var enemy in visibleEnemies)
	{
		pressure += enemy.GetCorruptionMetrics().CorruptionSeverityScore;
	}

	return pressure;
}

private void ResolveDynamicMoraleModifiers()
{
	float morale = 0.7f;
	float loyalty = Mathf.Clamp(LoyaltyToPlayer / 100f, 0f, 1f);

	PartyRosterManager roster = PartyRosterRuntime.ActiveManager;
	if (roster != null && roster.TryGetMemberState(MyStats, out PartyMemberState state))
	{
		morale = state.CurrentMorale;
		loyalty = state.Loyalty;
	}

	_moraleModifiers = MoraleLoyaltyResolver.BuildDecisionModifiers(morale, loyalty);
}

private ObedienceOutcome ResolveObedienceOutcome()
{
	float morale = 0.7f;
	float loyalty = Mathf.Clamp(LoyaltyToPlayer / 100f, 0f, 1f);
	float friction = 0f;

	PartyRosterManager roster = PartyRosterRuntime.ActiveManager;
	CreatureStats leader = TurnManager.Instance?.GetPlayerLeader();
	if (roster != null && roster.TryGetMemberState(MyStats, out PartyMemberState state))
	{
		morale = state.CurrentMorale;
		loyalty = state.Loyalty;
		if (leader?.Template != null && state.Template != null)
		{
			friction = Mathf.Max(0f, MoraleLoyaltyResolver.ComputeAlignmentFriction(PartyRosterManager.ParseAlignment(leader.Template.Alignment), PartyRosterManager.ParseAlignment(state.Template.Alignment)));
		}
	}

	CorruptionMetrics selfCorruption = MyStats.GetCorruptionMetrics();
	float hp = Mathf.Clamp((float)MyStats.CurrentHP / Mathf.Max(1f, MyStats.GetEffectiveMaxHP()), 0f, 1f);
	CreatureStats highestThreat = CombatMemory.GetHighestThreat();
	float threat = highestThreat != null && highestThreat == MyStats ? 1f : 0.45f;
	bool afraid = MyStats.MyEffects.HasCondition(Condition.Frightened) || MyStats.MyEffects.HasCondition(Condition.Panicked) || selfCorruption.CorruptionSeverityScore >= 0.45f;
	hp *= Mathf.Clamp(1f - selfCorruption.CorruptionSeverityScore, 0.1f, 1f);

	float authorityModifier = IntelligenceGrowthRuntime.Service.ComputeAuthorityModifierPercent(leader, MyStats);

	ObedienceCheckContext ctx = new ObedienceCheckContext
	{
		Morale = morale,
		Loyalty = loyalty,
		HealthPercent = hp,
		RelativeThreat = threat,
		IsAfraid = afraid,
		AlignmentFriction = friction,
		AuthorityModifier = authorityModifier
	};

	return MoraleLoyaltyResolver.RollObedienceOutcome(ctx, _instabilityRng.Randf());
}

private TurnPlan BuildExtremeInstabilityPlan()
{
	List<AIAction> actions = GeneratePossibleSingleActions();
	AIAction defensive = actions.FirstOrDefault(a => a is AIAction_Hide || a is AIAction_MoveToPosition);
	if (defensive != null)
	{
		TurnPlan retreatPlan = new TurnPlan(defensive);
		retreatPlan.IsPlayerSuggestion = false;
		retreatPlan.DecisionNarrative = "Instability spike: the unit abandons direct orders and prioritizes immediate survival.";
		retreatPlan.Score = 999f;
		return retreatPlan;
	}

	return null;
}

private bool EvaluateArenaDesertionAndBetrayal()
{
	float morale = 0.7f;
	float loyalty = Mathf.Clamp(LoyaltyToPlayer / 100f, 0f, 1f);
	float friction = 0f;

	PartyRosterManager roster = PartyRosterRuntime.ActiveManager;
	CreatureStats leader = TurnManager.Instance?.GetPlayerLeader();
	if (roster != null && roster.TryGetMemberState(MyStats, out PartyMemberState state))
	{
		morale = state.CurrentMorale;
		loyalty = state.Loyalty;
		if (leader?.Template != null && state.Template != null)
		{
			friction = Mathf.Max(0f, MoraleLoyaltyResolver.ComputeAlignmentFriction(PartyRosterManager.ParseAlignment(leader.Template.Alignment), PartyRosterManager.ParseAlignment(state.Template.Alignment)));
		}
	}

	CorruptionMetrics selfCorruption = MyStats.GetCorruptionMetrics();
	float hp = Mathf.Clamp((float)MyStats.CurrentHP / Mathf.Max(1f, MyStats.GetEffectiveMaxHP()), 0f, 1f);
	float leaderCorruption = leader != null ? leader.GetCorruptionMetrics().CorruptionSeverityScore : 0f;
	float leaderHp = leader != null ? Mathf.Clamp((float)leader.CurrentHP / Mathf.Max(1f, leader.GetEffectiveMaxHP()), 0f, 1f) : 1f;
	leaderHp *= Mathf.Clamp(1f - leaderCorruption, 0.2f, 1f);
	hp *= Mathf.Clamp(1f - selfCorruption.CorruptionSeverityScore, 0.2f, 1f);
	List<CreatureStats> combatants = TurnManager.Instance?.GetAllCombatants() ?? new List<CreatureStats>();
	int allyCount = combatants.Count(c => c != null && GodotObject.IsInstanceValid(c) && c.IsInGroup("PlayerTeam") == MyStats.IsInGroup("PlayerTeam"));
	int enemyCount = Mathf.Max(0, combatants.Count - allyCount);
	float outnumbered = Mathf.Clamp((enemyCount - allyCount) / 6f, 0f, 1f);

	float authorityModifier = IntelligenceGrowthRuntime.Service.ComputeAuthorityModifierPercent(leader, MyStats);

	DesertionCheckContext ctx = new DesertionCheckContext
	{
		Morale = morale,
		Loyalty = loyalty,
		SelfHpPercent = hp,
		LeaderHpPercent = leaderHp,
		OutnumberedPressure = outnumbered,
		AlignmentFriction = friction,
		AuthorityModifier = authorityModifier
	};

	if (_instabilityRng.Randf() <= MoraleLoyaltyResolver.ComputeDesertionProbability(ctx))
	{
		GD.PrintRich($"[color=orange]{GetParent().Name} deserts the battlefield due to morale collapse.[/color]");
		if (roster != null)
		{
			roster.RemoveOnDeath(MyStats);
		}

		GetParent().QueueFree();
		return true;
	}

	float enemyCorruptionPressure = GetVisibleEnemyCorruptionPressure();
	bool imminentDefeat = outnumbered > 0.6f && leaderHp < 0.35f && enemyCorruptionPressure < 0.8f;
	float betrayal = MoraleLoyaltyResolver.ComputeBetrayalProbability(morale, loyalty, friction, imminentDefeat);
	if (_instabilityRng.Randf() <= betrayal)
	{
		GD.PrintRich($"[color=red]{GetParent().Name} betrays the team in a rare collapse event.[/color]");
		MyStats.RemoveFromGroup("PlayerTeam");
		MyStats.AddToGroup("Enemy");
	}

	return false;
}

private float CalculatePlayerInfluenceMultiplier()
{
	int allyIQ = MyStats.Template.Intelligence;
	CreatureStats playerLeader = TurnManager.Instance.GetPlayerLeader();
	if (playerLeader == null) return 1.0f; 

	int playerChaMod = playerLeader.ChaModifier;
	float baseInfluence;
	if (allyIQ <= 1) baseInfluence = 0.0f;
	else if (allyIQ <= 7) baseInfluence = (allyIQ - 1.0f) / 6.0f; 
	else if (allyIQ < 21) baseInfluence = 1.0f - ((allyIQ - 7.0f) / 14.0f); 
	else baseInfluence = 0.01f; 

	float iqSensitivity = (allyIQ > 7) ? Mathf.Clamp((allyIQ - 7.0f) / 14.0f, 0f, 1f) : 0f;
	float charismaBonus = (playerChaMod * 0.05f) * iqSensitivity; 

	float loyaltyBonus = Mathf.Clamp(LoyaltyToPlayer / 100f, 0f, 1f) * 0.35f;
	float finalInfluence = Mathf.Clamp(baseInfluence + charismaBonus + loyaltyBonus, 0f, 1f);
	return 1.0f + finalInfluence;
}

private string BuildCounterReason(TurnPlan chosenPlan, float suggestionRawScore)
{
	bool chosenHide = chosenPlan.Actions.Any(a => a is AIAction_Hide);
	bool suggestionWasWeak = suggestionRawScore < 25f;

	if (chosenHide)
		return "it reads immediate danger and pivots to concealment before committing again";
	if (suggestionWasWeak)
		return "the suggested move looked tactically weak against current threats";

	return "its tactical instincts found a higher-value action at this moment";
}

private string BuildSupportReason(TurnPlan chosenPlan)
{
	bool hasHide = chosenPlan.Actions.Any(a => a is AIAction_Hide);
	bool hasAttack = chosenPlan.Actions.Any(a => a is AIAction_Attack || a is AIAction_FullAttack || a is AIAction_SingleNaturalAttack);
	bool hasSpell = chosenPlan.Actions.Any(a => a is AIAction_CastGenericAbility);

	if (hasHide) return "survival takes priority, and hiding preserves pressure without abandoning the fight";
	if (hasAttack) return "it sees a clear opening to maintain tempo and threaten the enemy line";
	if (hasSpell) return "its battlefield read favors utility and control over brute force";
	return "the current board state rewards this action chain more than alternatives";
}

#endregion

#region Action Generation (Helpers)

private List<AIAction> GeneratePossibleSingleActions()
{
	List<AIAction> possibleActions = new List<AIAction>();
	List<CreatureStats> visibleTargets = FindVisibleTargets();

	var lights = GetTree().GetNodesInGroup("DancingLights");
	foreach(Node n in lights)
	{
		if (n is PersistentEffect_DancingLights dl && dl.Caster == MyStats) 
		{
			var enemies = FindVisibleTargets(); 
			foreach(var e in enemies)
			{
				var node = GridManager.Instance.NodeFromWorldPoint(e.GlobalPosition);
				if (GridManager.Instance.GetEffectiveLightLevel(node) == 0)
				{
					// Found a target in darkness!
				}
			}
		}
	} // <-- THIS BRACKET WAS MISSING!

	if (MyStats.MyEffects.HasCondition(Condition.Concentrating))
	{
		bool isEffective = false;
		foreach(var enemy in visibleTargets)
		{
			if (enemy.MyEffects.ActiveEffects.Any(e => 
				e.EffectData.ConditionApplied == Condition.Fascinated && 
				e.SourceCreature == MyStats))
			{
				isEffective = true;
				break;
			}
		}

		if (isEffective)
		{
			var concentrate = new AIAction_Concentrate(this);
			concentrate.BoostScore(100f, " (Maintain Control)");
			possibleActions.Add(concentrate);
			return possibleActions; 
		}
	}

	var helplessTarget = visibleTargets.FirstOrDefault(t => t.MyEffects.HasCondition(Condition.Helpless));
	if (helplessTarget != null && MyActionManager.CanPerformAction(ActionType.FullRound) && GetParent<Node3D>().GlobalPosition.DistanceTo(helplessTarget.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
	{
		var coupDeGraceAbility = GD.Load<Ability_SO>("res://Data/Abilities/SkillActions/Action_CoupDeGrace.tres");
		if (coupDeGraceAbility != null)
		{
			possibleActions.Add(new AIAction_CoupDeGrace(this, helplessTarget, coupDeGraceAbility));
		}
	}

	if (MyStats.MyEffects.HasCondition(Condition.Panicked) || MyStats.MyEffects.HasCondition(Condition.Frightened))
	{
		possibleActions.Add(new AIAction_Flee(this));
		return possibleActions;
	}
	
	 if (MyStats.MyEffects.HasCondition(Condition.Pinned))
	{
		var escapePinAbility = MyStats.AvailableSkillActions.FirstOrDefault(a => a.AbilityName == "Escape Pin");
		if (escapePinAbility != null && MyActionManager.CanPerformAction(escapePinAbility.ActionCost))
		{
			possibleActions.Add(new AIAction_CastGenericAbility(this, escapePinAbility, MyStats, GetParent<Node3D>().GlobalPosition));
		}
		possibleActions.Add(new AIAction_BreakGrapple(this));
		return possibleActions; 
	}
	
	if (MyStats.CurrentGrappleState != null)
	{
		if (MyStats.CurrentGrappleState.Controller == MyStats) 
		{
			possibleActions.Add(new AIAction_MaintainGrapple(this));
		}
		else if (MyStats.MyEffects.HasCondition(Condition.Grappled))
		{
			possibleActions.Add(new AIAction_BreakGrapple(this));
			var escapeGrappleAbility = MyStats.AvailableSkillActions.FirstOrDefault(a => a.AbilityName == "Escape Grapple");
			if (escapeGrappleAbility != null && MyActionManager.CanPerformAction(escapeGrappleAbility.ActionCost))
			{
				possibleActions.Add(new AIAction_CastGenericAbility(this, escapeGrappleAbility, MyStats, GetParent<Node3D>().GlobalPosition));
			}
		}

		var allAbilities = MyStats.Template.KnownAbilities.Concat(MyStats.AvailableSkillActions).ToList();
		foreach(var ability in allAbilities)
		{
			if (MyActionManager.CanPerformAction(ability))
			{
				if(ability.TargetType == TargetType.Self)
				{
					possibleActions.Add(new AIAction_CastGenericAbility(this, ability, MyStats, GetParent<Node3D>().GlobalPosition));
				}
			}
		}

		var target = MyStats.CurrentGrappleState?.Target == MyStats ? MyStats.CurrentGrappleState?.Controller : MyStats.CurrentGrappleState?.Target;
		if (target != null)
		{
			var weapon = MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
			if (weapon != null && weapon.Handedness != WeaponHandedness.TwoHanded)
			{
				var attackAbility = new Ability_SO();
				attackAbility.AbilityName = weapon.ItemName;
				possibleActions.Add(new AIAction_Attack(this, target, attackAbility, null));
			}
			if (MyStats.Template.MeleeAttacks.Any())
			{
				foreach (var naturalAttack in MyStats.Template.MeleeAttacks)
				{
					possibleActions.Add(new AIAction_SingleNaturalAttack(this, target, naturalAttack));
				}
			}
		}
		return possibleActions; 
	}

	var detectMagicController = GetParent().GetNodeOrNull<DetectMagicController>("DetectMagicController");
	if (detectMagicController != null && MyActionManager.CanPerformAction(ActionType.Standard))
	{
		possibleActions.Add(new AIAction_Concentrate(this));
	}

	var waterBreathingSpell = MyStats.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName == "Water Breathing");
	if (waterBreathingSpell != null && MyActionManager.CanPerformAction(waterBreathingSpell.ActionCost))
	{
		var primaryTarget = GetPerceivedHighestThreat();
		if (primaryTarget != null)
		{
			var path = Pathfinding.Instance.FindPath(MyStats, GetParent<Node3D>().GlobalPosition, primaryTarget.GlobalPosition);
			if (path != null && path.Any(p => GridManager.Instance.NodeFromWorldPoint(p).terrainType == TerrainType.Water))
			{
				if (!MyStats.MyEffects.HasEffect("Water Breathing"))
				{
					possibleActions.Add(new AIAction_CastGenericAbility(this, waterBreathingSpell, MyStats, GetParent<Node3D>().GlobalPosition));
				}
			}
		}
	}

	var allPossibleAbilities2 = MyStats.Template.KnownAbilities.Concat(MyStats.AvailableSkillActions).ToList();
	if (allPossibleAbilities2 != null)
	{
		foreach (var ability in allPossibleAbilities2)
		{
			if (!MyActionManager.CanPerformAction(ability) || !MyStats.MyUsage.HasUsesRemaining(ability)) continue;

			var weatherEffect = ability.EffectComponents.OfType<Effect_ControlWeather>().FirstOrDefault();
			if (weatherEffect != null && weatherEffect.AllowedWeathers.Count > 0)
			{
				foreach (var weatherOption in weatherEffect.AllowedWeathers)
				{
					var action = new AIAction_CastGenericAbility(this, ability, MyStats, GetParent<Node3D>().GlobalPosition, CommandWord.None, false, weatherOption);
					action.Name = $"Cast {ability.AbilityName} ({weatherOption.WeatherName})";
					possibleActions.Add(action);

					if (ability.IsMythicCapable && MyStats.HasMythicPower())
					{
						var mythicAction = new AIAction_CastGenericAbility(this, ability, MyStats, GetParent<Node3D>().GlobalPosition, CommandWord.None, true, weatherOption);
						mythicAction.Name = $"Cast {ability.AbilityName} (Mythic {weatherOption.WeatherName})";
						possibleActions.Add(mythicAction);
					}
				}
				continue; 
			}

			var beastShapeEffect = ability.EffectComponents.OfType<Effect_BeastShape>().FirstOrDefault();
			if (beastShapeEffect != null && beastShapeEffect.AllowedForms.Count > 0)
			{
				foreach (var formOption in beastShapeEffect.AllowedForms)
				{
					var action = new AIAction_CastGenericAbility(this, ability, MyStats, GetParent<Node3D>().GlobalPosition, CommandWord.None, false, formOption);
					action.Name = $"Cast {ability.AbilityName} ({formOption.CreatureName})";
					possibleActions.Add(action);

					if (ability.IsMythicCapable && MyStats.HasMythicPower())
					{
						var mythicAction = new AIAction_CastGenericAbility(this, ability, MyStats, GetParent<Node3D>().GlobalPosition, CommandWord.None, true, formOption);
						mythicAction.Name = $"Cast {ability.AbilityName} (Mythic {formOption.CreatureName})";
						possibleActions.Add(mythicAction);
					}
				}
				continue;
			}

			 if (ability.AvailableCommands != null && ability.AvailableCommands.Any())
			{
				foreach (var command in ability.AvailableCommands)
				{
					if (ability.TargetType == TargetType.SingleEnemy)
					{
						foreach (var target in visibleTargets)
						{
							if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= ability.Range.GetRange(MyStats))
							{
								possibleActions.Add(new AIAction_CastGenericAbility(this, ability, target, target.GlobalPosition, command));
							}
						}
					}
				}
				continue; 
			}
			
			 var myProjectedImage = TurnManager.Instance.GetAllCombatants().FirstOrDefault(c => c.IsProjectedImage && c.Caster == MyStats);
			if (myProjectedImage != null && ability.Range.GetRange(MyStats) > 0) 
			{
				Vector3 imagePosition = myProjectedImage.GlobalPosition;
				if (ability.TargetType == TargetType.SingleEnemy)
				{
					foreach (var target in visibleTargets)
					{
						if (imagePosition.DistanceTo(target.GlobalPosition) <= ability.Range.GetRange(MyStats))
						{
							var castAction = new AIAction_CastGenericAbility(this, ability, target, target.GlobalPosition);
							castAction.Name = $"[From Image] {castAction.Name}";
							possibleActions.Add(castAction);
						}
					}
				}
			}

			switch (ability.TargetType)
			{
			   case TargetType.Self:
				 if (ability.AbilityName == "Dimension Door")
					{
						var doorDestinations = GetDimensionDoorDestinations(ability, visibleTargets);
						foreach (var destination in doorDestinations)
						{
							possibleActions.Add(new AIAction_CastGenericAbility(this, ability, MyStats, destination));
						}
						break;
					}

					possibleActions.Add(new AIAction_CastGenericAbility(this, ability, MyStats, GetParent<Node3D>().GlobalPosition));
					if (ability.IsMythicCapable && MyStats.HasMythicPower())
					{
						possibleActions.Add(new AIAction_CastGenericAbility(this, ability, MyStats, GetParent<Node3D>().GlobalPosition, CommandWord.None, true));
					}
					break;
				case TargetType.SingleAlly:
					var allAllies = FindAllies();
					allAllies.Add(MyStats);
					foreach (var ally in allAllies)
					{
						if (GetParent<Node3D>().GlobalPosition.DistanceTo(ally.GlobalPosition) <= ability.Range.GetRange(MyStats))
						{
							possibleActions.Add(new AIAction_CastGenericAbility(this, ability, ally, ally.GlobalPosition));
							if (ability.IsMythicCapable && MyStats.HasMythicPower())
							{
								possibleActions.Add(new AIAction_CastGenericAbility(this, ability, ally, ally.GlobalPosition, CommandWord.None, true));
							}
						}
					}
					break;
				case TargetType.SingleEnemy:
					var candidateTargets = visibleTargets;
					if (ability.AbilityName == "Discern Location")
					{
						candidateTargets = GetDiscernLocationCandidates();
					}

					foreach (var target in candidateTargets)
					{
						if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= ability.Range.GetRange(MyStats))
						{
							possibleActions.Add(new AIAction_CastGenericAbility(this, ability, target, target.GlobalPosition));
							if (ability.IsMythicCapable && MyStats.HasMythicPower())
							{
								possibleActions.Add(new AIAction_CastGenericAbility(this, ability, target, target.GlobalPosition, CommandWord.None, true));
							}
						}
					}
					 if (ability.EntityType == TargetableEntityType.ObjectsOnly || ability.EntityType == TargetableEntityType.CreaturesAndObjects)
					{
						var nearbyObjects = GetTree().GetNodesInGroup("ObjectDurability").Cast<ObjectDurability>()
							.Where(o => GetParent<Node3D>().GlobalPosition.DistanceTo(o.GlobalPosition) <= ability.Range.GetRange(MyStats))
							.ToList();
						foreach (var obj in nearbyObjects)
						{
							possibleActions.Add(new AIAction_CastGenericAbility(this, ability, null, obj.GlobalPosition, CommandWord.None, false, null, obj));
						}
					}
					break;
			   case TargetType.Area_AlliesOnly:
				case TargetType.Area_EnemiesOnly:
				case TargetType.Area_FriendOrFoe:
					if (ability.AreaOfEffect.Shape == AoEShape.Burst || 
						ability.AreaOfEffect.Shape == AoEShape.Emanation || 
						ability.AreaOfEffect.Shape == AoEShape.Cylinder)
					{
						 possibleActions.Add(new AIAction_CastGenericAbility(this, ability, null, GetParent<Node3D>().GlobalPosition));
						 if (ability.IsMythicCapable && MyStats.HasMythicPower())
						 {
							possibleActions.Add(new AIAction_CastGenericAbility(this, ability, null, GetParent<Node3D>().GlobalPosition, CommandWord.None, true));
						 }
					}
					else 
					{
						var outcome = AISpatialAnalysis.FindBestPlacementForAreaEffect(MyStats, ability);
						if (outcome.EnemiesHit.Any() || outcome.AlliesHit.Any())
						{
							possibleActions.Add(new AIAction_CastGenericAbility(this, ability, null, outcome.AimPoint));
							if (ability.IsMythicCapable && MyStats.HasMythicPower())
							{
								possibleActions.Add(new AIAction_CastGenericAbility(this, ability, null, outcome.AimPoint, CommandWord.None, true));
							}
						}
					}
					break;
			}
		}
	}
	
	if (MyActionManager.CanPerformAction(ActionType.FiveFootStep))
	{
		GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(GetParent<Node3D>().GlobalPosition);
		List<GridNode> adjacentNodes = GridManager.Instance.GetNeighbours(currentNode);
		foreach(var neighbor in adjacentNodes)
		{
			if (neighbor.movementCost > neighbor.baseMovementCost || (neighbor.terrainType != TerrainType.Ground && neighbor.terrainType != TerrainType.Water))
			{
				continue;
			}
			possibleActions.Add(new AIAction_FiveFootStep(this, neighbor.worldPosition));
		}
	}
	
	bool hasMeleeCapability = (MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand) != null) || (MyStats.Template.MeleeAttacks.Any());
	
	if (MyActionManager.CanPerformAction(ActionType.FullRound) && hasMeleeCapability)
	{
		bool canFlurry = MyStats.Template.Classes.Any(c => c.ToLower().Contains("monk"));
		var weapon = MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);

		foreach (var target in visibleTargets)
		{
			 if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
			{
				if (canFlurry && (weapon == null || weapon.IsMonkWeapon))
				{
					possibleActions.Add(new AIAction_FlurryOfBlows(this, target));
				}

				possibleActions.Add(new AIAction_FullAttack(this, target, null));
				var powerAttackFeat = MyStats.Template.Feats.FirstOrDefault(f => f.Feat.FeatName == "Power Attack");
				if (powerAttackFeat != null)
				{
					possibleActions.Add(new AIAction_FullAttack(this, target, powerAttackFeat.Feat));
				}
			}
		}
	}
	
	if (MyActionManager.CanPerformAction(ActionType.Standard))
	{
		var weapon = MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
		if (weapon != null)
		{
			var weaponAttackAbility = new Ability_SO();
			weaponAttackAbility.AbilityName = weapon.ItemName;
			weaponAttackAbility.ActionCost = ActionType.Standard;
			foreach (var target in visibleTargets)
			{
				if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
				{
					possibleActions.Add(new AIAction_Attack(this, target, weaponAttackAbility, null));
					var powerAttackFeat = MyStats.Template.Feats.FirstOrDefault(f => f.Feat.FeatName == "Power Attack");
					if (powerAttackFeat != null)
					{
						possibleActions.Add(new AIAction_Attack(this, target, weaponAttackAbility, powerAttackFeat.Feat));
					}
				}
			}
			 if (MyStats.HasFeat("Vital Strike"))
			{
				foreach (var target in visibleTargets)
				{
					if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
					{
						possibleActions.Add(new AIAction_VitalStrike(this, target));
					}
				}
			}
		}
		else if (MyStats.Template.MeleeAttacks.Any())
		{
			foreach (var naturalAttack in MyStats.Template.MeleeAttacks.Where(na => na.IsPrimary))
			{
				 foreach (var target in visibleTargets)
				 {
					if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((NaturalAttack)null).max)
					{
						possibleActions.Add(new AIAction_SingleNaturalAttack(this, target, naturalAttack));
					}
				 }
			}
		}
		var rangedWeapon = MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
		if (rangedWeapon != null && rangedWeapon.WeaponType != WeaponType.Melee)
		{
			foreach (var target in visibleTargets)
			{
				if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= rangedWeapon.RangeIncrement * 10)
				{
					possibleActions.Add(new AIAction_RangedAttack(this, target, rangedWeapon, null));
					var deadlyAimFeat = MyStats.Template.Feats.FirstOrDefault(f => f.Feat.FeatName == "Deadly Aim");
					if (deadlyAimFeat != null)
					{
						possibleActions.Add(new AIAction_RangedAttack(this, target, rangedWeapon, deadlyAimFeat.Feat));
					}
				}
			}
		}
		  else if (rangedWeapon != null && rangedWeapon.WeaponType == WeaponType.Melee && rangedWeapon.RangeIncrement <= 0)
		{
			var throwImprovisedAbility = GD.Load<Ability_SO>("res://Data/Abilities/SkillActions/Action_ThrowImprovised.tres");
			if (throwImprovisedAbility != null)
			{
				foreach (var target in visibleTargets)
				{
					possibleActions.Add(new AIAction_ThrowImprovised(this, target, rangedWeapon, throwImprovisedAbility));
				}
			}
		}
		
		var awesomeBlowFeat = MyStats.Template.Feats.FirstOrDefault(f => f.Feat.FeatName == "Awesome Blow");
		 if (awesomeBlowFeat != null && awesomeBlowFeat.Feat.AssociatedAbility != null)
		{
			foreach (var target in visibleTargets)
			{
				if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max && target.Template.Size < MyStats.Template.Size)
				{
					possibleActions.Add(new AIAction_AwesomeBlow(this, target, awesomeBlowFeat.Feat.AssociatedAbility));
				}
			}
		}
		
		var allCombatants = TurnManager.Instance?.GetAllCombatants() ?? new System.Collections.Generic.List<CreatureStats>();
		foreach (var potentialVictim in allCombatants)
		{
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(potentialVictim.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
			{
				bool isHelpless = potentialVictim.MyEffects.HasCondition(Condition.Helpless) || potentialVictim.MyEffects.HasCondition(Condition.Unconscious);
				bool isAlly = potentialVictim.IsInGroup("Player") == MyStats.IsInGroup("Player");
				if (isHelpless || isAlly) possibleActions.Add(new AIAction_BindSoulEngine(this, potentialVictim));
			}
		}

		foreach (var target in visibleTargets)
		{
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
			{
				var soulEngine = target.GetNodeOrNull<SoulEngineController>("SoulEngineController");
				if (soulEngine != null && soulEngine.IsBodyAttached)
				{
					possibleActions.Add(new AIAction_PrySoulEngine(this, target));
				}
			}
		}
	}
	
	 if (MyActionManager.CanPerformAction(ActionType.Standard))
	{
		possibleActions.Add(new AIAction_TotalDefense(this));
	}

	var attackActionsToWrap = possibleActions.Where(a => a is AIAction_Attack || a is AIAction_FullAttack || a is AIAction_SingleNaturalAttack).ToList();
	foreach (var attack in attackActionsToWrap)
	{
		possibleActions.Add(new AIAction_FightDefensively(this, attack));
	}

	if (MyActionManager.CanPerformAction(ActionType.Standard))
	{
		var bestStandardAttack = possibleActions
			.Where(a => a is AIAction_Attack || a is AIAction_SingleNaturalAttack)
			.OrderByDescending(a => a.Score)
			.FirstOrDefault();

		if (bestStandardAttack != null && bestStandardAttack.GetTarget() != null)
		{
			possibleActions.Add(new AIAction_Feint(this, bestStandardAttack.GetTarget(), bestStandardAttack));
		}
	}

	if (MyActionManager.CanPerformAction(ActionType.FullRound))
	{
		var feignAbility = MyStats.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName == "Feign Harmlessness");
		if (feignAbility != null)
		{
			var highestThreat = GetPerceivedHighestThreat();
			if (highestThreat != null)
			{
				possibleActions.Add(new AIAction_CastGenericAbility(this, feignAbility, highestThreat, highestThreat.GlobalPosition));
			}
		}
	}

	if (MyStats.IsMounted)
	{
		if (MyStats.AvailableSkillActions.Any(a => a.AbilityName == "Spur Mount") && MyActionManager.CanPerformAction(ActionType.Move))
			possibleActions.Add(new AIAction_SpurMount(this));
		if (MyStats.AvailableSkillActions.Any(a => a.AbilityName == "Use Mount as Cover") && MyActionManager.CanPerformAction(ActionType.Immediate))
			possibleActions.Add(new AIAction_UseMountAsCover(this));
		if (MyStats.AvailableSkillActions.Any(a => a.AbilityName == "Recover from Cover") && MyActionManager.CanPerformAction(ActionType.Move))
			possibleActions.Add(new AIAction_RecoverFromCover(this));
		if (MyStats.AvailableSkillActions.Any(a => a.AbilityName == "Dismount") && MyActionManager.CanPerformAction(ActionType.Move))
			possibleActions.Add(new AIAction_Dismount(this));
	}
	else
	{
		if (MyStats.AvailableSkillActions.Any(a => a.AbilityName == "Mount") && MyActionManager.CanPerformAction(ActionType.Move))
		{
			var potentialMounts = FindAllies().Where(ally => MyStats.Mount(ally, isCheckOnly: true)).ToList();
			foreach (var mount in potentialMounts)
			{
				if (GetParent<Node3D>().GlobalPosition.DistanceTo(mount.GlobalPosition) <= MyMover.GetEffectiveMovementSpeed() + 5f)
				{
					possibleActions.Add(new AIAction_Mount(this, mount));
				}
			}
		}
	}
	
	if (MyActionManager.CanPerformAction(ActionType.Standard))
	{
		if (MyStats.MyInventory.GetEquippedItem(EquipmentSlot.MainHand) == null)
		{
			var adherers = visibleTargets.Where(t => t.GetNodeOrNull<PassiveAdhesiveController>("PassiveAdhesiveController") != null).ToList();
			foreach (var adherer in adherers)
			{
				var stuckItems = ItemManager.Instance.GetItemsInRadius(adherer.GlobalPosition, 1.0f);
				foreach (var item in stuckItems)
				{
					if (item.ItemData.ItemType == ItemType.Weapon)
					{
						if (GetParent<Node3D>().GlobalPosition.DistanceTo(adherer.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
						{
							possibleActions.Add(new AIAction_RetrieveStuckWeapon(this, item, adherer));
						}
					}
				}
			}
		}
	}
	
	if (MyActionManager.CanPerformAction(ActionType.Move))
	{
		possibleActions.Add(new AIAction_Search(this));
		
		var autonomousEntities = GetTree().GetNodesInGroup("AutonomousEntities");
		foreach (Node node in autonomousEntities)
		{
			if (node is AutonomousEntityController entity && entity.Caster == MyStats)
			{
				var highestThreat = GetPerceivedHighestThreat();
				if (highestThreat != null)
				{
					possibleActions.Add(new AIAction_RedirectAutonomousEntity(this, entity, highestThreat));
				}
			}
		}
		if (MyStats.Template.HasScent && !CombatMemory.HasCheckedScentDirection(MyStats))
		{
			var smelled = CombatMemory.GetSmelledEnemies(MyStats);
			var unknownLocationEnemies = smelled.Where(e => 
				GetParent().GetNode<CombatStateController>("CombatStateController").GetLocationStatus(e) == LocationStatus.DetectedPresence
			).ToList();

			if (unknownLocationEnemies.Any())
			{
				possibleActions.Add(new AIAction_IdentifyScentDirection(this, unknownLocationEnemies));
			}
		}
		if (visibleTargets.Any())
		{
			GridNode bestHidingSpot = AISpatialAnalysis.FindBestHidingSpot(MyStats, MyMover, GetPerceivedHighestThreat());
			if (bestHidingSpot != null)
			{
				float pathDist = GetParent<Node3D>().GlobalPosition.DistanceTo(bestHidingSpot.worldPosition);
				float halfSpeed = MyMover.GetEffectiveMovementSpeed() / 2f;
				float fullSpeed = MyMover.GetEffectiveMovementSpeed();
				if (pathDist <= halfSpeed)
					possibleActions.Add(new AIAction_Hide(this, bestHidingSpot.worldPosition, 0));
				if (pathDist > halfSpeed && pathDist <= fullSpeed)
					possibleActions.Add(new AIAction_Hide(this, bestHidingSpot.worldPosition, -5));
			}
			
			CreatureStats primaryTarget = GetPerceivedHighestThreat() ?? visibleTargets.First();
			Vector3 idealPosition = AISpatialAnalysis.FindBestPosition(MyStats, MyMover, primaryTarget);
			
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(idealPosition) > 1f)
			{
				possibleActions.Add(new AIAction_MoveToPosition(this, idealPosition, primaryTarget));
				if (AoOManager.Instance.IsThreatened(MyStats))
				{
					possibleActions.Add(new AIAction_MoveToPosition(this, idealPosition, primaryTarget, useAcrobatics: true, fullSpeedAcrobatics: false));
					possibleActions.Add(new AIAction_MoveToPosition(this, idealPosition, primaryTarget, useAcrobatics: true, fullSpeedAcrobatics: true));
				}
			}
		}
		else
		{
			var heardContacts = AISpatialAnalysis.FindHeardContacts(MyStats);
			if (heardContacts.Any())
			{
				AddSoundContactReactionActions(heardContacts, possibleActions);
			}
			else if (MyStats.Template.Speed_Fly > 0)
			{
				possibleActions.Add(new AIAction_Hover(this));
			}
		}
		
		if (MyStats.Template.Intelligence >= 6)
		{
			foreach (var target in visibleTargets)
			{
				var flankingOpportunities = AISpatialAnalysis.FindPotentialFlankingPositions(MyStats, MyMover, target);
				foreach(var opportunity in flankingOpportunities)
				{
					possibleActions.Add(new AIAction_MoveToFlank(this, opportunity.Position, target, opportunity.Ally));
					if (AoOManager.Instance.IsThreatened(MyStats))
					{
						possibleActions.Add(new AIAction_MoveToFlank(this, opportunity.Position, target, opportunity.Ally, useAcrobatics: true, fullSpeedAcrobatics: false));
						possibleActions.Add(new AIAction_MoveToFlank(this, opportunity.Position, target, opportunity.Ally, useAcrobatics: true, fullSpeedAcrobatics: true));
					}
				}
			}
		}
	}
	
	if (MyActionManager.CanPerformAction(ActionType.Move))
	{
		GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(GetParent<Node3D>().GlobalPosition);
		foreach (var neighbor in GridManager.Instance.GetNeighbours(currentNode))
		{
			if (neighbor.gridY > currentNode.gridY && MyStats.Template.Speed_Climb > 0)
			{
				possibleActions.Add(new AIAction_Climb(this, neighbor.worldPosition));
			}
			
			GridNode landNode = AISpatialAnalysis.FindLandingSpot(GetParent<Node3D>().GlobalPosition, neighbor);
			if (landNode != null)
			{
				float dist = GetParent<Node3D>().GlobalPosition.DistanceTo(landNode.worldPosition);
				if (dist <= MyMover.GetEffectiveMovementSpeed())
					possibleActions.Add(new AIAction_Jump(this, landNode.worldPosition, dist, isHighJump: false));
			}

			if(neighbor.gridY > currentNode.gridY && neighbor.terrainType == TerrainType.Ground)
			{
				float height = (neighbor.gridY - currentNode.gridY) * GridManager.Instance.nodeDiameter;
				if(height * 5f <= MyMover.GetEffectiveMovementSpeed())
				{
					possibleActions.Add(new AIAction_Jump(this, neighbor.worldPosition, height, isHighJump: true));
				}
			}
		}
	}
	
	if (MyActionManager.CanPerformAction(ActionType.FullRound))
	{
		if (!MyStats.MyEffects.HasCondition(Condition.Blinded))
		{
			Vector3? safeDestination = AISpatialAnalysis.FindBestFleePosition(MyStats, MyMover);
			if (safeDestination.HasValue)
			{
				possibleActions.Add(new AIAction_Withdraw(this, safeDestination.Value));
			}
		}
	}

	if (MyActionManager.CanPerformAction(ActionType.Move) && MyStats.MyEffects.HasCondition(Condition.Prone))
	{
		possibleActions.Add(new AIAction_StandUp(this));
	}

	if (MyActionManager.CanPerformAction(ActionType.Move))
	{
		var nearbyItems = ItemManager.Instance?.GetItemsInRadius(GetParent<Node3D>().GlobalPosition, MyMover.GetEffectiveMovementSpeed() * 2f);
		if (nearbyItems != null && nearbyItems.Any())
		{
			var primaryTarget = GetPerceivedHighestThreat() ?? visibleTargets.FirstOrDefault();
			foreach (var worldItem in nearbyItems)
			{
				float itemUpgradeScore = GetItemUpgradeScore(worldItem.ItemData, primaryTarget);
				if (itemUpgradeScore > 20)
					possibleActions.Add(new AIAction_PickupItem(this, worldItem, itemUpgradeScore));
			}
		}
		var nearbyObjects = GetTree().GetNodesInGroup("WorldObject").Cast<WorldObject>()
			.Where(o => GetParent<Node3D>().GlobalPosition.DistanceTo(o.GlobalPosition) <= MyMover.GetEffectiveMovementSpeed() * 2f)
			.ToList();
		
		foreach (var worldObject in nearbyObjects)
		{
			float itemUpgradeScore = GetItemUpgradeScore(worldObject.BecomesItemOnPickup, MyStats);
			if (itemUpgradeScore > 50) 
			{
				possibleActions.Add(new AIAction_PickupItem(this, worldObject, itemUpgradeScore));
			}
		}
	}

	if (MyActionManager.CanPerformAction(ActionType.Move))
	{
		var currentWeapon = MyStats.MyInventory?.GetEquippedItemInstance(EquipmentSlot.MainHand);
		var backpackWeapons = MyStats.MyInventory?.GetBackpackWeapons();
		var primaryTarget = GetPerceivedHighestThreat() ?? visibleTargets.FirstOrDefault();

		if (primaryTarget != null && backpackWeapons != null && backpackWeapons.Any())
		{
			foreach (var backpackWeapon in backpackWeapons)
			{
				string reason = IsWeaponSuperior(backpackWeapon, currentWeapon, primaryTarget);
				if (!string.IsNullOrEmpty(reason))
					possibleActions.Add(new AIAction_SwitchWeapon(this, backpackWeapon, primaryTarget, reason));
			}
		}
	}

	if (MyStats.Template.Intelligence >= 12 && MyActionManager.CanPerformAction(ActionType.Standard))
	{
		possibleActions.Add(new AIAction_Delay(this));
	}
	
	if (MyActionManager.CanPerformAction(ActionType.Standard))
	{
		var gaseous = GetParent().GetNodeOrNull<GaseousFormController>("GaseousFormController");
		if (gaseous != null && gaseous.IsWindWalk && !gaseous.InFastWindMode)
		{
			possibleActions.Add(new AIAction_ToggleWindWalk(this));
		}
	}
	
	if (MyActionManager.CanPerformAction(ActionType.Standard))
	{
		if (MyStats.HasFeat("Flyby Attack") && 
			MyStats.Template.Speed_Fly > 0 && 
			MyActionManager.CanPerformAction(ActionType.Move) && 
			MyActionManager.CanPerformAction(ActionType.Standard))
		{
			List<AIAction> potentialStandardActions = new List<AIAction>();
			var weapon = MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
			if (weapon != null)
			{
				var weaponAttackAbility = new Ability_SO { AbilityName = weapon.ItemName, ActionCost = ActionType.Standard };
				foreach(var t in visibleTargets) potentialStandardActions.Add(new AIAction_Attack(this, t, weaponAttackAbility, null));
			}
			
			foreach (var standardAction in potentialStandardActions)
			{
				CreatureStats target = standardAction.GetTarget();
				if (target == null) continue;

				Vector3 direction = (target.GlobalPosition - GetParent<Node3D>().GlobalPosition).Normalized();
				if (direction == Vector3.Zero) direction = -GetParent<Node3D>().GlobalTransform.Basis.Z;
				Vector3 endPoint = target.GlobalPosition + direction * 30f;
				
				List<Vector3> flybyPath = Pathfinding.Instance.FindPath(MyStats, GetParent<Node3D>().GlobalPosition, endPoint);

				if (flybyPath != null && (flybyPath.Count * 5f) <= MyMover.GetEffectiveMovementSpeed())
				{
					bool canPerformActionOnPath = false;
					if (standardAction is AIAction_Attack || standardAction is AIAction_SingleNaturalAttack)
					{
						canPerformActionOnPath = flybyPath.Any(p => p.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max);
					}
					
					if (canPerformActionOnPath)
					{
						possibleActions.Add(new AIAction_FlybyAttack(this, standardAction, endPoint, flybyPath));
					}
				}
			}
		}
		
		var nearbyObjects = GetTree().GetNodesInGroup("ObjectDurability").Cast<ObjectDurability>()
			.Where(o => GetParent<Node3D>().GlobalPosition.DistanceTo(o.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
			.ToList();

		var primaryTarget = GetPerceivedHighestThreat();

		foreach (var obj in nearbyObjects)
		{
			if (primaryTarget != null)
			{
				var visibility = LineOfSightManager.GetVisibility(MyStats, primaryTarget);
				if (!visibility.HasLineOfSight && visibility.Reason.Contains("Cover"))
				{
					possibleActions.Add(new AIAction_AttackObject(this, obj, "Clear LoS"));
				}
			}
			
			if (MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand) == null && obj.DebrisLootTable != null)
			{
				possibleActions.Add(new AIAction_AttackObject(this, obj, "Create Weapon"));
			}
			
			if (obj.IsFlammable && MyStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand)?.DamageInfo.Any(d => d.DamageType == "Fire") == true)
			{
				possibleActions.Add(new AIAction_AttackObject(this, obj, "Burn Hazard"));
			}
		}
	}

	if (MyStats.HasFeat("Improved Sunder")) 
	{
		foreach (var target in visibleTargets)
		{
			if (GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition) <= MyStats.GetEffectiveReach((Item_SO)null).max)
			{
				var targetInv = target.GetNodeOrNull<InventoryController>("InventoryController");
				if (targetInv?.GetEquippedItem(EquipmentSlot.MainHand) != null || targetInv?.GetEquippedItem(EquipmentSlot.Shield) != null)
				{
					possibleActions.Add(new AIAction_Sunder(this, target));
				}
			}
		}
	}
	return possibleActions;
}

private void AddSoundContactReactionActions(List<HeardSoundContact> heardContacts, List<AIAction> possibleActions)
{
	if (heardContacts == null || heardContacts.Count == 0) return;

	int intelligence = MyStats.Template.Intelligence;
	var strongest = heardContacts.OrderByDescending(c => c.ThreatEstimate * c.Confidence).First();
	var closest = heardContacts.OrderBy(c => MyStats.GlobalPosition.DistanceTo(c.Position)).First();
	bool multipleSounds = heardContacts.Count >= 2;
	bool loudAndSudden = strongest.ThreatEstimate >= 6f;
	bool mediumVolume = strongest.ThreatEstimate >= 2.2f;
	float learnedCaution = AITacticalMatrix.GetSoundCautionBias(intelligence, strongest.ThreatEstimate);

	if (intelligence <= 2)
	{
		if (loudAndSudden)
		{
			Vector3 fleePoint = GetRepositionPointAwayFrom(strongest.Position);
			var fleeAction = new AIAction_MoveToPosition(this, fleePoint, strongest.Source);
			fleeAction.Name = "Sound panic: flee from loud noise";
			fleeAction.BoostScore(1.35f, " (animal fear response)");
			possibleActions.Add(fleeAction);
			GD.PrintRich($"[color=orange][Sound Intel][/color] {MyStats.Name} treats the noise as unconfirmed danger and flees.");
			return;
		}

		if (mediumVolume)
		{
			if (learnedCaution >= 1.1f)
			{
				Vector3 cautiousPoint = GetLateralRepositionPoint(strongest.Position);
				var cautiousAnimal = new AIAction_MoveToPosition(this, cautiousPoint, strongest.Source);
				cautiousAnimal.Name = "Animal caution: circle sound source";
				cautiousAnimal.BoostScore(1.08f, " (learned caution)");
				possibleActions.Add(cautiousAnimal);
				BeginSoundLearningWindow(strongest.ThreatEstimate);
				GD.PrintRich($"[color=orange][Sound Intel][/color] {MyStats.Name} hesitates and circles after prior punishment from loud noises.");
				return;
			}

			var chargeAction = new AIAction_MoveToPosition(this, closest.Position, closest.Source);
			chargeAction.Name = "Animal charge toward sound";
			chargeAction.BoostScore(1.2f, " (instinct investigation)");
			possibleActions.Add(chargeAction);
			BeginSoundLearningWindow(strongest.ThreatEstimate);
			GD.PrintRich($"[color=orange][Sound Intel][/color] {MyStats.Name} rushes toward the sound instinctively.");
		}
		return;
	}

	if (intelligence <= 6)
	{
		if (multipleSounds)
		{
			var chaseClosest = new AIAction_MoveToPosition(this, closest.Position, closest.Source);
			chaseClosest.Name = "Chase nearest heard contact";
			chaseClosest.BoostScore(1.18f, " (low-int nearest threat)");
			possibleActions.Add(chaseClosest);
			BeginSoundLearningWindow(strongest.ThreatEstimate);
			GD.PrintRich($"[color=orange][Sound Intel][/color] {MyStats.Name} chases the nearest unconfirmed hostile sound.");
			return;
		}

		bool forcedCaution = learnedCaution >= 0.9f;
		var lowIntApproach = new AIAction_MoveToPosition(this, strongest.Position, strongest.Source);
		lowIntApproach.Name = (loudAndSudden && !forcedCaution) ? "Charge loud sound blindly" : "Approach sound cautiously";
		lowIntApproach.BoostScore((loudAndSudden && !forcedCaution) ? 1.22f : 1.08f, (loudAndSudden && !forcedCaution) ? " (aggressive low-int response)" : " (careful low-int response)");
		possibleActions.Add(lowIntApproach);
		BeginSoundLearningWindow(strongest.ThreatEstimate);
		GD.PrintRich($"[color=orange][Sound Intel][/color] {MyStats.Name} treats heard noise as an unconfirmed hostile contact and advances.");
		return;
	}

	bool approachLikely = IsSoundApproaching(strongest);
	bool repeatedNoise = IsRepeatedNoisePattern();

	if (intelligence <= 12)
	{
		bool flankRisk = HasLikelyFlankRisk(strongest);
		if (flankRisk)
		{
			var holdAction = new AIAction_Search(this);
			holdAction.Name = "Hold for flank risk from sound";
			holdAction.BoostScore(1.15f, " (tactical sound triangulation)");
			possibleActions.Add(holdAction);
			GD.PrintRich($"[color=yellow][Sound Intel][/color] {MyStats.Name} suspects a flank from unseen noise and holds position.");
			return;
		}

		if (loudAndSudden || approachLikely || learnedCaution >= 0.75f)
		{
			GridNode hideSpot = FindHearingReactionHidingSpot(strongest.Position);
			if (hideSpot != null)
			{
				var hideAction = new AIAction_Hide(this, hideSpot.worldPosition, 0);
				hideAction.Name = "Hide from unconfirmed hostile sound";
				hideAction.BoostScore(1.18f, " (ambush suspicion)");
				possibleActions.Add(hideAction);
				BeginSoundLearningWindow(strongest.ThreatEstimate);
				GD.PrintRich($"[color=yellow][Sound Intel][/color] {MyStats.Name} suspects an ambush and hides before committing.");
				return;
			}
		}

		Vector3 repositionPoint = GetLateralRepositionPoint(strongest.Position);
		var reposition = new AIAction_MoveToPosition(this, repositionPoint, strongest.Source);
		reposition.Name = "Reposition vs approaching noise";
		reposition.BoostScore(1.12f, " (tactical angle change)");
		possibleActions.Add(reposition);
		BeginSoundLearningWindow(strongest.ThreatEstimate);
		GD.PrintRich($"[color=yellow][Sound Intel][/color] {MyStats.Name} repositions while treating sound as unconfirmed hostile contact.");
		return;
	}

	bool allyDeathsNearby = TurnManager.Instance.GetAllCombatants()
		.Any(c => c != null && c.IsInGroup("Player") == MyStats.IsInGroup("Player") && c.CurrentHP <= 0 && c.GlobalPosition.DistanceTo(MyStats.GlobalPosition) <= 35f);

	if (allyDeathsNearby || repeatedNoise || learnedCaution >= 0.6f)
	{
		GridNode hideSpot = FindHearingReactionHidingSpot(strongest.Position);
		if (hideSpot != null)
		{
			var regroupHide = new AIAction_Hide(this, hideSpot.worldPosition, 0);
			regroupHide.Name = "Strategic regroup from suspect sound";
			regroupHide.BoostScore(1.25f, " (trap probability rising)");
			possibleActions.Add(regroupHide);
			BeginSoundLearningWindow(strongest.ThreatEstimate);
			GD.PrintRich($"[color=cyan][Sound Intel][/color] {MyStats.Name} suspects deception and regroups defensively.");
			return;
		}
	}

	Vector3 cautiousAdvancePoint = GetLateralRepositionPoint(strongest.Position);
	var cautiousAdvance = new AIAction_MoveToPosition(this, cautiousAdvancePoint, strongest.Source);
	cautiousAdvance.Name = "Strategic cautious advance toward sound";
	cautiousAdvance.BoostScore(1.1f, " (sound treated as unreliable)");
	possibleActions.Add(cautiousAdvance);
	BeginSoundLearningWindow(strongest.ThreatEstimate);
	GD.PrintRich($"[color=cyan][Sound Intel][/color] {MyStats.Name} advances cautiously and does not fully trust the sound source.");
}

private Vector3 GetRepositionPointAwayFrom(Vector3 dangerPoint)
{
	Vector3 origin = MyStats.GlobalPosition;
	Vector3 away = (origin - dangerPoint).Normalized();
	if (away == Vector3.Zero) away = Vector3.Back;
	float desiredDistance = Mathf.Max(5f, MyMover.GetEffectiveMovementSpeed() * 0.9f);
	Vector3 desired = origin + (away * desiredDistance);
	return GridManager.Instance.NodeFromWorldPoint(desired).worldPosition;
}

private Vector3 GetLateralRepositionPoint(Vector3 soundPoint)
{
	Vector3 origin = MyStats.GlobalPosition;
	Vector3 toSound = (soundPoint - origin).Normalized();
	if (toSound == Vector3.Zero) toSound = Vector3.Forward;

	Vector3 lateral = new Vector3(-toSound.Z, 0f, toSound.X).Normalized();
	float distance = Mathf.Max(5f, MyMover.GetEffectiveMovementSpeed() * 0.65f);
	Vector3 optionA = origin + lateral * distance;
	Vector3 optionB = origin - lateral * distance;

	float threatA = optionA.DistanceTo(soundPoint);
	float threatB = optionB.DistanceTo(soundPoint);
	Vector3 chosen = threatA >= threatB ? optionA : optionB;
	return GridManager.Instance.NodeFromWorldPoint(chosen).worldPosition;
}

private bool IsSoundApproaching(HeardSoundContact contact)
{
	var heard = CombatMemory.GetHeardSounds(MyStats);
	if (heard.Count < 2) return false;

	var matching = heard
		.Where(h => (contact.Source != null && h.Source == contact.Source) || h.Position.DistanceTo(contact.Position) <= 8f)
		.ToList();
	if (matching.Count > 2)
	{
		matching = matching.Skip(matching.Count - 2).ToList();
	}

	if (matching.Count < 2) return false;

	float olderDistance = matching[0].Position.DistanceTo(MyStats.GlobalPosition);
	float newerDistance = matching[1].Position.DistanceTo(MyStats.GlobalPosition);
	return newerDistance + 2f < olderDistance;
}

private bool IsRepeatedNoisePattern()
{
	var heard = CombatMemory.GetHeardSounds(MyStats);
	if (heard.Count < 3) return false;

	var recent = heard.Count > 5 ? heard.Skip(heard.Count - 5).ToList() : heard.ToList();
	int repeatedClusters = recent
		.GroupBy(h => h.Source)
		.Count(g => g.Count() >= 2 && g.Key != null);

	bool repeatedPositions = recent
		.GroupBy(h => new Vector2(Mathf.Round(h.Position.X / 5f), Mathf.Round(h.Position.Z / 5f)))
		.Any(g => g.Count() >= 3);

	return repeatedClusters > 0 || repeatedPositions;
}

private bool HasLikelyFlankRisk(HeardSoundContact strongestContact)
{
	Vector3 myPos = MyStats.GlobalPosition;
	Vector3 soundDir = (strongestContact.Position - myPos).Normalized();
	if (soundDir == Vector3.Zero) return false;

	// If allies are concentrated on one side while hostile sound appears from the opposite arc,
	// treat this as a possible flanking attempt.
	var allies = AISpatialAnalysis.FindAllies(MyStats);
	foreach (var ally in allies)
	{
		if (ally == null || ally.CurrentHP <= 0) continue;
		Vector3 allyDir = (ally.GlobalPosition - myPos).Normalized();
		if (allyDir == Vector3.Zero) continue;
		if (soundDir.Dot(allyDir) <= -0.2f)
		{
			return true;
		}
	}

	return false;
}

private GridNode FindHearingReactionHidingSpot(Vector3 soundPoint)
{
	GridNode originNode = GridManager.Instance.NodeFromWorldPoint(MyStats.GlobalPosition);
	float moveBudget = Mathf.Max(5f, MyMover.GetEffectiveMovementSpeed());
	int radius = Mathf.Clamp(Mathf.CeilToInt(moveBudget / GridManager.Instance.nodeDiameter), 1, 8);
	GridNode bestNode = null;
	float bestScore = float.MinValue;

	for (int x = -radius; x <= radius; x++)
	for (int z = -radius; z <= radius; z++)
	{
		Vector3 samplePos = originNode.worldPosition + new Vector3(x * GridManager.Instance.nodeDiameter, 0f, z * GridManager.Instance.nodeDiameter);
		GridNode candidate = GridManager.Instance.NodeFromWorldPoint(samplePos);
		if (candidate == null || candidate.terrainType == TerrainType.Solid) continue;
		if (!Pathfinding.IsNodeWalkable(MyStats, originNode, candidate)) continue;

		float distance = candidate.worldPosition.DistanceTo(MyStats.GlobalPosition);
		if (distance > moveBudget + 0.5f) continue;

		var incomingVisibility = CombatCalculations.GetVisibilityFromPoint(soundPoint, candidate.worldPosition);
		float score = (incomingVisibility.CoverBonusToAC * 10f) + incomingVisibility.ConcealmentMissChance - distance;
		if (score > bestScore)
		{
			bestScore = score;
			bestNode = candidate;
		}
	}

	return bestNode;
}

#endregion

#region AI Utilities

public void OnRequestReaction(CreatureStats spellCaster, Ability_SO incomingSpell, string skillName)
{
	bool shouldReact = false;
	
	if (CombatMemory.IsSpellKnown(spellCaster, incomingSpell))
	{
		shouldReact = false;
	}
	else if (spellCaster == GetPerceivedHighestThreat())
	{
		shouldReact = true;
	}
	else if (incomingSpell.SpellLevel >= 3)
	{
		shouldReact = true;
	}
	
	GD.Print($"<color=yellow>AI REACTION:</color> {GetParent().Name} considers using {skillName} to identify '{incomingSpell.AbilityName}' from {spellCaster.Name}. Decision: {shouldReact}.");
	
	TurnManager.Instance.MakeReactionDecision(shouldReact);
}

// REFACTORED: Use AISpatialAnalysis
public List<CreatureStats> FindVisibleTargets() => AISpatialAnalysis.FindVisibleTargets(MyStats);
public List<CreatureStats> FindAllies() => AISpatialAnalysis.FindAllies(MyStats);

public float GetItemUpgradeScore(Item_SO foundItem, CreatureStats recipient)
{
	if (foundItem == null || recipient == null) return 0f;

	var recipientInv = recipient.GetNodeOrNull<InventoryController>("InventoryController");
	if (recipientInv == null) return 0f;
	 // --- APPRAISING SIGHT LOGIC ---
	
	if (MyStats.Template.HasAppraisingSight)
	{
		// Detect Magic Property
		bool isMagic = foundItem.Modifications.Any(m => m.BonusType == BonusType.Enhancement && m.ModifierValue > 0);
		if (isMagic) { /* Logic handled by multiplier later */ }
	}

	var currentItem = recipientInv.GetEquippedItem(foundItem.EquipSlot);

	if (currentItem == null) return 50f; 

	if (foundItem.ItemType == ItemType.Weapon)
	{
		float currentDPR = (currentItem.DamageInfo.FirstOrDefault()?.DiceCount ?? 0) * ((currentItem.DamageInfo.FirstOrDefault()?.DieSides ?? 0) / 2f + 0.5f);
		float newDPR = (foundItem.DamageInfo.FirstOrDefault()?.DiceCount ?? 0) * ((foundItem.DamageInfo.FirstOrDefault()?.DieSides ?? 0) / 2f + 0.5f);
		return (newDPR - currentDPR) * 10f; 
	}
	
	if (foundItem.ItemType == ItemType.Armor || foundItem.ItemType == ItemType.Shield)
	{
		int currentAC = currentItem.Modifications.Where(m => m.StatToModify == StatToModify.ArmorClass).Sum(m => m.ModifierValue);
		int newAC = foundItem.Modifications.Where(m => m.StatToModify == StatToModify.ArmorClass).Sum(m => m.ModifierValue);
		return (newAC - currentAC) * 25f; 
	}
	
	if (foundItem.ItemType == ItemType.Potion)
	{
		 return (recipient.Template.MaxHP - recipient.CurrentHP) * 2f; 
	}

	 float score = 0; // Default
	if (MyStats.Template.HasAppraisingSight && score > 0) score *= 2f; 
	return score;
}

// Note: This overload for WorldItem uses the logic above for Item_SO
private float GetWeaponUpgradeScore(Item_SO foundItem, CreatureStats target)
{
	if (foundItem.ItemType != ItemType.Weapon) return 0;

	var allOwnedWeapons = MyStats.MyInventory.GetBackpackWeapons();
	var currentWeapon = MyStats.MyInventory.GetEquippedItemInstance(EquipmentSlot.MainHand);
	if (currentWeapon != null) allOwnedWeapons.Add(currentWeapon);

	float newItemScore = GetLearnedWeaponScore(new ItemInstance(foundItem), target);
	float bestOwnedWeaponScore = 0;
	if (allOwnedWeapons.Any())
	{
		bestOwnedWeaponScore = allOwnedWeapons.Max(w => GetLearnedWeaponScore(w, target));
	}

	return newItemScore - bestOwnedWeaponScore;
}

private float GetLearnedWeaponScore(ItemInstance weaponInstance, CreatureStats target, NaturalAttack naturalAttack = null)
{
	if (target == null) return 0;
	if (weaponInstance == null && naturalAttack == null) return 0;
	
	var learnedTactics = GetTactics();
	float totalScore = 0;
	List<string> targetTraits = new List<string> { $"Type_{target.Template.Type}" };
	// SubTypes in Godot is Array<string>, convert for LINQ
	if (target.Template.SubTypes != null)
		targetTraits.AddRange(target.Template.SubTypes.Select(s => $"SubType_{s}"));
		
	List<string> weaponProperties = new List<string>();

	if (weaponInstance != null)
	{
		var weapon = weaponInstance.ItemData;
		weaponProperties.Add($"Material_{weapon.Material}");
		weaponProperties.AddRange(weapon.DamageInfo.Select(d => $"DamageType_{d.DamageType}"));
	}
	else
	{
		weaponProperties.AddRange(naturalAttack.DamageInfo.Select(d => $"DamageType_{d.DamageType}"));
	}

	foreach (var trait in targetTraits)
	{
		foreach (var prop in weaponProperties)
		{
			string key = $"{trait}_{prop}";
			if (learnedTactics.W_WeaponEffectiveness.TryGetValue(key, out float score))
			{
				totalScore += score;
			}
		}
	}
	return totalScore;
}

private string IsWeaponSuperior(ItemInstance newWeaponInstance, ItemInstance currentWeaponInstance, CreatureStats target)
{
	float currentWeaponScore = GetWeaponCombatEffectivenessScore(currentWeaponInstance, target);
	float newWeaponScore = GetWeaponCombatEffectivenessScore(newWeaponInstance, target);

	if (newWeaponScore > currentWeaponScore + 15)
	{
		return $"Better Effectiveness ({newWeaponScore:F0} vs {currentWeaponScore:F0})";
	}
	return null;
}

private float GetWeaponCombatEffectivenessScore(ItemInstance weaponInstance, CreatureStats target)
{
	if (target == null) return 0;
	
	if (weaponInstance == null)
	{
		var unarmedAttack = new NaturalAttack { AttackName = "Unarmed Strike", DamageInfo = new Godot.Collections.Array<DamageInfo> { new DamageInfo { DiceCount = 1, DieSides = MyStats.GetUnarmedDamageDieSides(), DamageType = "Bludgeoning" } } };
		return GetLearnedWeaponScore(null, target, unarmedAttack);
	}

	var weapon = weaponInstance.ItemData;
	var learnedTactics = GetTactics();
	float score = 0;
	float averageDamage = weapon.DamageInfo.Sum(d => (d.DiceCount * (d.DieSides / 2f + 0.5f)) + d.FlatBonus);
	score += averageDamage * 5; 
	float threatChance = (21 - weapon.CriticalThreatRange) / 20f;
	score += (threatChance * (weapon.CriticalMultiplier - 1) * averageDamage) * 10;
	float learnedScore = GetLearnedWeaponScore(weaponInstance, target);
	score += learnedScore;

	if (Mathf.IsEqualApprox(learnedScore, 0))
	{
		float experimentationUrgency = 1.0f;
		var currentWeapon = MyStats.MyInventory.GetEquippedItemInstance(EquipmentSlot.MainHand);
		float currentWeaponLearnedScore = GetLearnedWeaponScore(currentWeapon, target);

		if (currentWeaponLearnedScore < 0)
		{
			experimentationUrgency = 2.5f;
		}
		score += learnedTactics.W_Experimentation * experimentationUrgency;
	}

	float distance = GetParent<Node3D>().GlobalPosition.DistanceTo(target.GlobalPosition);
	if (weapon.WeaponType == WeaponType.Melee)
	{
		if (distance > weapon.WeaponReach) score -= 200;
		else if (distance <= weapon.WeaponReach && distance > 5f) score += 30;
	}
	else
	{
		if (distance <= weapon.RangeIncrement * 10) score += 40;
		if(AoOManager.Instance.IsThreatened(MyStats)) score -= 50;
	}
	
	if (target.Template.DamageReductions != null)
	{
		foreach (var dr in target.Template.DamageReductions)
		{
			if (MyStats.CheckDrBypass(dr.Bypass, MyStats, weapon, null))
			{
				score += dr.Amount * 10;
			}
		}
	}
	return score;
}

public TacticalData GetTactics() => myTactics;
public AIPersonalityProfile GetProfile() => Personality;

public float PredictSuccessChanceVsSR(CreatureStats target, Ability_SO ability)
{
	if (!ability.AllowsSpellResistance) return 1.0f; 

	if (CombatMemory.IsKnownToHaveSR(target))
	{
		return 0.05f;
	}

	int predictedSR = GlobalKnowledgeDB.PredictSRFromTraits(target.Template);
	if (predictedSR <= 0) return 1.0f;

	int casterLevel = MyStats.Template.CasterLevel;
	int rollNeeded = predictedSR - casterLevel;
	float successChance = Mathf.Clamp((21f - rollNeeded) / 20f, 0f, 1f);
	return successChance;
}

public CreatureStats GetPerceivedHighestThreat()
{
	return CombatMemory.GetHighestThreat();
}

public CreatureTemplate_SO GetPerceivedTemplate(CreatureStats target)
{
	if (target.MyVeil != null && !CombatMemory.HasDisbelievedIllusion(MyStats, target))
	{
		return target.MyVeil.ApparentTemplate;
	}
	return target.Template;
}

public float GetPredictedDamageMultiplier(CreatureStats target, string damageType)
{
	if (string.IsNullOrEmpty(damageType) || damageType == "Untyped") return 1.0f;

	if (CombatMemory.IsTraitIdentified(target, $"Vulnerability:{damageType}")) return 1.5f;
	if (CombatMemory.IsTraitIdentified(target, $"Immunity:{damageType}")) return 0f;
	if (CombatMemory.IsTraitIdentified(target, $"Resistance:{damageType}")) return 0.5f;

	float intelligenceFactor = Mathf.Clamp(MyStats.Template.Intelligence / 15f, 0f, 1f);
	if (intelligenceFactor < 0.2f) return 1.0f; 
	return GlobalKnowledgeDB.PredictMultiplierFromTraits(target.Template, damageType);
}

private List<Vector3> GetDimensionDoorDestinations(Ability_SO ability, List<CreatureStats> visibleTargets)
{
	var candidates = new List<Vector3>();
	float maxRange = ability.Range.GetRange(MyStats);

	Vector3? fleePosition = AISpatialAnalysis.FindBestFleePosition(MyStats, MyMover);
	if (fleePosition.HasValue)
	{
		candidates.Add(fleePosition.Value);
	}

	var highestThreat = GetPerceivedHighestThreat();
	if (highestThreat != null)
	{
		candidates.Add(AISpatialAnalysis.FindBestPosition(MyStats, MyMover, highestThreat));
	}

	foreach (var enemy in visibleTargets)
	{
		Vector3 away = (enemy.GlobalPosition - MyStats.GlobalPosition).Normalized();
		if (away != Vector3.Zero)
		{
			candidates.Add(MyStats.GlobalPosition - away * Mathf.Min(maxRange, 60f));
		}
	}

	var uniqueValid = new List<Vector3>();
	foreach (var point in candidates)
	{
		if (MyStats.GlobalPosition.DistanceTo(point) > maxRange) continue;

		GridNode node = GridManager.Instance.NodeFromWorldPoint(point);
		if (node == null || node.terrainType == TerrainType.Solid) continue;

		bool isKnown = LineOfSightManager.HasLineOfEffect(MyStats, MyStats.GlobalPosition + Vector3.Up, node.worldPosition + Vector3.Up * 0.5f);
		if (!isKnown) continue;

		bool occupiedByEnemy = TurnManager.Instance.GetAllCombatants().Any(c =>
			c != MyStats &&
			c.IsInGroup("Player") != MyStats.IsInGroup("Player") &&
			GridManager.Instance.NodeFromWorldPoint(c.GlobalPosition) == node);

		if (occupiedByEnemy) continue;

		if (!uniqueValid.Any(existing => existing.DistanceTo(node.worldPosition) < 0.5f))
		{
			uniqueValid.Add(node.worldPosition);
		}
	}

	if (!uniqueValid.Any())
	{
		uniqueValid.Add(MyStats.GlobalPosition);
	}

	return uniqueValid;
}
// Kept for external callers like Summon logic
public void ExecuteMoveTo(Vector3 destination)
{
	MyActionManager.UseAction(ActionType.Move);
	MyMover.MoveTo(destination);
}
public void ExecuteAttack(CreatureStats target)
{
	MyActionManager.UseAction(ActionType.Standard);
	CombatManager.ResolveMeleeAttack(MyStats, target);
}

// Domination command issuance is still a "Brain" function
public void IssueCommandToDominatedCreature(CreatureStats minion)
{
	if (MyActionManager.CanPerformAction(ActionType.Free))
	{
		var highestThreat = GetPerceivedHighestThreat();
		Vector3 strategicPoint;
		
		if (highestThreat != null)
		{
			// REFACTORED: Use AISpatialAnalysis
			strategicPoint = AISpatialAnalysis.FindBestPosition(minion, minion.GetNode<CreatureMover>("CreatureMover"), highestThreat);
		}
		else
		{
			// Forward is -Z in Godot
			strategicPoint = GetParent<Node3D>().GlobalPosition + (-GetParent<Node3D>().GlobalTransform.Basis.Z * 30f); 
		}
		
		var path = Pathfinding.Instance.FindPath(minion, minion.GlobalPosition, strategicPoint);

		var command = new DominateCommand 
		{ 
			CommandType = DominateCommandType.MoveAndAttack, 
			TargetPosition = strategicPoint,
			PathToDestination = path 
		};
		minion.MyDomination.SetNewCommand(command);
	}
}

#endregion


	private List<CreatureStats> GetDiscernLocationCandidates()
	{
		var stateController = GetParent().GetNodeOrNull<CombatStateController>("CombatStateController");
		if (stateController == null) return new List<CreatureStats>();

		var candidates = new List<CreatureStats>();
		foreach (var seen in stateController.SeenCreaturesThisCombat)
		{
			if (seen == null || seen.CurrentHP <= 0) continue;
			if (seen.IsInGroup("Player") == MyStats.IsInGroup("Player")) continue;

			if (stateController.GetLocationStatus(seen) < LocationStatus.Pinpointed)
			{
				candidates.Add(seen);
			}
		}

		return candidates;
	}
}

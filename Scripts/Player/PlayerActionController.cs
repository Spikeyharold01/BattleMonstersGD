using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// =================================================================================================
// FILE: PlayerActionController.cs (GODOT VERSION)
// PURPOSE: Handles player input during their turn to select and execute actions.
// ATTACH TO: All player-controlled creature prefabs (Child Node).
// =================================================================================================

public partial class PlayerActionController : Node
{
// --- STATE ---
private PlayerTurnState currentState = PlayerTurnState.AwaitingInput;
private Ability_SO selectedAbility;
private CommandWord chosenCommand;
private bool isControllingImage = false;
private ProjectedImageController myImageController;
private CreatureStats flybyTarget;
private Ability_SO flybyStandardAction;
private bool isControllingAlly = false;
private TurnPlan currentSuggestedPlan;
private ActionManager allyActionManager;

// --- CACHED COMPONENTS ---
private CreatureStats myStats;
private ActionManager myActionManager;
private CreatureMover myMover;
private Camera3D mainCamera;

private Ability_SO throwImprovisedAbility; 

[Export] public PackedScene PathPreviewPrefab; // LineRenderer equivalent? Godot uses Line2D (Screen) or ImmediateMesh/MeshInstance3D (World)
private Node3D pathPreviewInstance; // Assuming it's a Node3D with visual logic

[Export] public PackedScene AoeTemplatePrefab;
private Node3D currentAoeTemplate;
private Node3D illusionPreviewInstance;
private const float GhostSoundVolumeStep = 1f;

[Export(PropertyHint.Layers3DPhysics)] public uint GroundLayer = 1;
[Export(PropertyHint.Layers3DPhysics)] public uint CreatureLayer = 2;
[Export(PropertyHint.Layers3DPhysics)] public uint ObjectLayer = 4; // Hazard/Object

// Signal for UI
[Signal] public delegate void TurnEndedEventHandler();

public override void _Ready()
{
	myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
	myActionManager = GetParent().GetNodeOrNull<ActionManager>("ActionManager");
	myMover = GetParent().GetNodeOrNull<CreatureMover>("CreatureMover");
	mainCamera = GetViewport().GetCamera3D();
	
	if (ResourceLoader.Exists("res://Data/Abilities/SkillActions/Action_ThrowImprovised.tres"))
		throwImprovisedAbility = GD.Load<Ability_SO>("res://Data/Abilities/SkillActions/Action_ThrowImprovised.tres");
		
	SetProcess(false); // Disable Update equivalent
	SetProcessInput(false);
}

public void BeginPlayerTurn()
{
	
		// Survival-first rule: frightened allies may flee or hide/reposition based on AI judgment.
		if (myStats.MyEffects.HasCondition(Condition.Panicked) || myStats.MyEffects.HasCondition(Condition.Frightened))
		{
			GD.PrintRich($"<color=orange>{GetParent().Name} is terrified and will prioritize survival choices while evaluating your suggestion.</color>");
		}
	SetProcess(true);
	SetProcessInput(true);
	isControllingAlly = !myStats.IsInGroup("Player"); // Assuming player controlled units are in "Player" group

	if (isControllingAlly)
	{
		currentState = PlayerTurnState.SuggestingAllyTurn;
		currentSuggestedPlan = new TurnPlan { IsPlayerSuggestion = true };
		// Create a temporary clone of the action manager logic?
		// In Godot, adding component dynamically is complex if it relies on Ready.
		// We'll assume allyActionManager references the existing one but we track usage separately, or duplicate.
		// Duplicating Node is easier.
		// However, ActionManager modifies CreatureStats state. A clone operating on same stats is tricky.
		// For simplicity in this port, we'll use the real manager but reset it if cancelled? 
		// Or just allow simulation. The original code adds a component.
		// I will create a new ActionManager node instance detached from stats logic if possible, but ActionManager relies on `GetParent`.
		// So we add it as child.
		allyActionManager = new ActionManager();
		allyActionManager.Name = "AllyActionManager_Temp";
		GetParent().AddChild(allyActionManager);
		allyActionManager.OnTurnStart(); 
		
		GD.PrintRich($"<color=cyan>Suggesting turn for ally: {GetParent().Name}. Awaiting input.</color>");
	}
	else
	{
		currentState = PlayerTurnState.AwaitingInput;
		GD.PrintRich($"<color=cyan>{GetParent().Name}'s turn. Awaiting input.</color>");
	}
	
	// FindObjectsOfType equivalent
	var images = GetTree().GetNodesInGroup("ProjectedImages"); // Assuming group
	// Or manual search if not grouped. Original used FindObjectsOfType.
	// I will assume ProjectedImageController adds itself to group.
	foreach(Node n in images)
	{
		if (n is ProjectedImageController pic && pic.Caster == myStats)
		{
			myImageController = pic;
			break;
		}
	}
	
	isControllingImage = false; 
}

private void EndPlayerTurn()
{
	SetProcess(false);
	SetProcessInput(false);
	if (isControllingAlly && allyActionManager != null)
	{
		allyActionManager.QueueFree();
		allyActionManager = null;
	}
	CancelAction(); 
	EmitSignal(SignalName.TurnEnded);
}

public override void _Input(InputEvent @event)
{
	// Handle input events (Clicks)
	if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
	{
		if (mouseEvent.ButtonIndex == MouseButton.Right)
		{
			if (currentState != PlayerTurnState.AwaitingInput)
			{
				CancelAction();
				return;
			}
		}
		
		if (mouseEvent.ButtonIndex == MouseButton.Left)
		{
			HandleLeftClick(mouseEvent.Position);
		}

		if (currentState == PlayerTurnState.PlacingIllusion && IsGhostSoundSelected())
		{
			if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
			{
				AdjustGhostSoundPreviewVolume(GhostSoundVolumeStep);
			}
			else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
			{
				AdjustGhostSoundPreviewVolume(-GhostSoundVolumeStep);
			}
		}
	}
	else if (@event is InputEventKey keyEvent && keyEvent.Pressed)
	{
		if (keyEvent.Keycode == Key.Escape)
		{
			if (currentState != PlayerTurnState.AwaitingInput) CancelAction();
		}
		if (keyEvent.Keycode == Key.R && currentState == PlayerTurnState.PlacingIllusion)
		{
			if (illusionPreviewInstance != null) illusionPreviewInstance.RotateY(Mathf.DegToRad(90));
		}
	}
}

public override void _Process(double delta)
{
	if (myStats.CurrentHP <= 0) return;
	
	// Raycast update for previews
	UpdatePreviews();
}

private void UpdatePreviews()
{
	// Handle visual updates for AOE / Path lines based on mouse position
	// Similar to Unity Update loop logic for visuals
	if (mainCamera == null) return;
	
	Vector2 mousePos = GetViewport().GetMousePosition();
	var spaceState = GetWorld3D().DirectSpaceState;
	var from = mainCamera.ProjectRayOrigin(mousePos);
	var to = from + mainCamera.ProjectRayNormal(mousePos) * 100f;
	
	var query = PhysicsRayQueryParameters3D.Create(from, to, GroundLayer | CreatureLayer | ObjectLayer);
	var result = spaceState.IntersectRay(query);
	
	if (result.Count > 0)
	{
		Vector3 hitPoint = (Vector3)result["position"];
		
		// Logic from Update() moved here for clarity
		if (currentState == PlayerTurnState.PlacingIllusion && illusionPreviewInstance != null)
		{
			illusionPreviewInstance.GlobalPosition = hitPoint;
		}
		
		if (currentAoeTemplate != null)
		{
			// Update AOE visual position/rotation
			// ... (Logic same as Unity) ...
			// Replicating AOE positioning logic:
			if (selectedAbility != null)
			{
				var shape = selectedAbility.AreaOfEffect.Shape;
				if (shape == AoEShape.Line || shape == AoEShape.Cone)
				{
					Vector3 origin = isControllingImage && myImageController != null ? myImageController.GlobalPosition : GetParent<Node3D>().GlobalPosition;
					currentAoeTemplate.GlobalPosition = origin;
					
					Vector3 dir = (hitPoint - origin).Normalized();
					if (dir != Vector3.Zero)
					{
						// LookAt requires up vector.
						currentAoeTemplate.LookAt(origin + dir, Vector3.Up);
						
						if (shape == AoEShape.Line)
						{
							float len = selectedAbility.AreaOfEffect.Range;
							// Move forward by half length if scaling box
							currentAoeTemplate.Translate(Vector3.Forward * (len / 2f)); // Z is forward? Check Godot basis. -Z forward.
							// Godot LookAt makes -Z point to target.
							// Translate(Vector3.Forward) moves in local -Z.
						}
					}
				}
				else
				{
					currentAoeTemplate.GlobalPosition = hitPoint;
					currentAoeTemplate.GlobalRotation = Vector3.Zero;
				}
			}
		}
		
		// Path Preview Logic
		if (currentState.ToString().Contains("MoveTarget"))
		{
			// Re-calc path and update LineRenderer
			// (Omitted for brevity, assumes pathPreviewInstance has update logic or handled here)
		}
	}
}

private void HandleLeftClick(Vector2 mousePos)
{
	var spaceState = GetWorld3D().DirectSpaceState;
	var from = mainCamera.ProjectRayOrigin(mousePos);
	var to = from + mainCamera.ProjectRayNormal(mousePos) * 100f;
	
	// Mask depends on state
	uint mask = GroundLayer | CreatureLayer | ObjectLayer; 
	
	var query = PhysicsRayQueryParameters3D.Create(from, to, mask);
	var result = spaceState.IntersectRay(query);
	
	if (result.Count > 0)
	{
		Vector3 hitPoint = (Vector3)result["position"];
		Node collider = (Node)result["collider"]; // Godot collider is the body node

		if (currentState == PlayerTurnState.AwaitingInput)
		{
			// Context click logic (Adherer weapon retrieval)
			var creature = collider as CreatureStats ?? collider.GetNodeOrNull<CreatureStats>("CreatureStats");
			if (creature != null)
			{
				var adhesive = creature.GetNodeOrNull<PassiveAdhesiveController>("PassiveAdhesiveController");
				if (adhesive != null && GetParent<Node3D>().GlobalPosition.DistanceTo(creature.GlobalPosition) <= 6f)
				{
					var stuckItems = ItemManager.Instance.GetItemsInRadius(creature.GlobalPosition, 1.0f);
					if (stuckItems.Any())
					{
						AttemptRetrieveWeapon(creature, stuckItems[0]);
					}
				}
				// --- SOUL ENGINE CLICK (PRY) ---
				var soulEngine = creature.GetNodeOrNull<SoulEngineController>("SoulEngineController");
				if (soulEngine != null && soulEngine.IsBodyAttached && GetParent<Node3D>().GlobalPosition.DistanceTo(creature.GlobalPosition) <= myStats.GetEffectiveReach((Item_SO)null).max)
				{
					AttemptPrySoulEngine(creature);
				}

				// --- SOUL ENGINE CLICK (BIND) ---
				bool isHelpless = creature.MyEffects.HasCondition(Condition.Helpless) || creature.MyEffects.HasCondition(Condition.Unconscious);
				if (isHelpless && GetParent<Node3D>().GlobalPosition.DistanceTo(creature.GlobalPosition) <= myStats.GetEffectiveReach((Item_SO)null).max)
				{
					AttemptBindSoulEngine(creature);
				}
			}
	   
		 // State Handlers
		switch (currentState)
		{
			case PlayerTurnState.SelectingNormalMoveTarget:
			case PlayerTurnState.SelectingAcrobaticMoveHalfSpeedTarget:
			case PlayerTurnState.SelectingAcrobaticMoveFullSpeedTarget:
				HandleMoveTargetingClick(hitPoint);
				break;
			case PlayerTurnState.SelectingFlybyAttackTarget:
				// Check if creature
				var target = collider as CreatureStats ?? collider.GetNodeOrNull<CreatureStats>("CreatureStats");
				if (target != null) HandleFlybyTargetSelection(target);
				break;
			case PlayerTurnState.SelectingFlybyAttackDestination:
				HandleFlybyDestinationSelection(hitPoint);
				break;
			case PlayerTurnState.SelectingAbilityTarget:
				HandleAbilityTargetingClick(collider, hitPoint);
				break;
			case PlayerTurnState.PlacingIllusion:
				ResolveAbility(null, illusionPreviewInstance, hitPoint); // Use preview as object
				break;
		}
	}
}
}

private void HandleMoveTargetingClick(Vector3 point)
{
	List<Vector3> path = Pathfinding.Instance.FindPath(myStats, GetParent<Node3D>().GlobalPosition, point);
	if (path != null)
	{
		float speedBudget = myMover.GetEffectiveMovementSpeed();
		if (currentState == PlayerTurnState.SelectingAcrobaticMoveHalfSpeedTarget) speedBudget /= 2f;
		
		float pathCost = path.Count * 5f;

		if (pathCost <= speedBudget)
		{
			myActionManager.UseAction(ActionType.Move, pathCost);

			switch (currentState)
			{
				case PlayerTurnState.SelectingAcrobaticMoveHalfSpeedTarget:
					_ = myMover.AcrobaticMoveAsync(point, false);
					break;
				case PlayerTurnState.SelectingAcrobaticMoveFullSpeedTarget:
					_ = myMover.AcrobaticMoveAsync(point, true);
					break;
				default: 
					bool isFlying = myStats.Template.Speed_Fly > 0 && GridManager.Instance.NodeFromWorldPoint(GetParent<Node3D>().GlobalPosition).terrainType != TerrainType.Ground;
					_ = myMover.MoveAlongPathAsync(path, isFlying);
					break;
			}
			CancelAction();
		}
	}
}

private void HandleFlybyTargetSelection(CreatureStats target)
{
	flybyTarget = target;
	if (flybyTarget != null && GetParent<Node3D>().GlobalPosition.DistanceTo(flybyTarget.GlobalPosition) <= myMover.GetEffectiveMovementSpeed() + flybyStandardAction.Range.GetRange(myStats))
	{
		currentState = PlayerTurnState.SelectingFlybyAttackDestination;
		GD.Print($"Target {flybyTarget.Name} selected. Now select a final destination.");
		// Instantiate preview if needed
	}
}

private void HandleFlybyDestinationSelection(Vector3 point)
{
	List<Vector3> path = Pathfinding.Instance.FindPath(myStats, GetParent<Node3D>().GlobalPosition, point);
	if (path != null)
	{
		float pathCost = path.Count * 5f;
		if (pathCost <= myMover.GetEffectiveMovementSpeed() && path.Any(p => p.DistanceTo(flybyTarget.GlobalPosition) <= flybyStandardAction.Range.GetRange(myStats)))
		{
			_ = ExecutePlayerFlyby(path, flybyTarget, flybyStandardAction);
			CancelAction();
		}
	}
}

private async Task ExecutePlayerFlyby(List<Vector3> fullPath, CreatureStats target, Ability_SO actionToPerform)
{
	myActionManager.CommitToFlybyAttack(Pathfinding.CalculatePathCost(fullPath, myStats));
	
	int attackWaypointIndex = -1;
	float minDistance = float.MaxValue;
	for (int i = 0; i < fullPath.Count; i++)
	{
		float dist = fullPath[i].DistanceTo(target.GlobalPosition);
		if (dist < minDistance)
		{
			minDistance = dist;
			attackWaypointIndex = i;
		}
	}
	
	List<Vector3> pathToAttackPoint = fullPath.GetRange(0, attackWaypointIndex + 1);
	List<Vector3> pathFromAttackPoint = fullPath.GetRange(attackWaypointIndex + 1, fullPath.Count - (attackWaypointIndex + 1));

	if (pathToAttackPoint.Any()) await myMover.MoveAlongPathAsync(pathToAttackPoint, true);
	
	if (myStats != null && myStats.CurrentHP > 0)
	{
		GD.PrintRich($"<color=cyan>Player executes Flyby with {actionToPerform.AbilityName}!</color>");
		await CombatManager.ResolveAbility(myStats, target, target, target.GlobalPosition, actionToPerform, false);
		await ToSignal(GetTree().CreateTimer(1.2f), "timeout");
	}
	
	if (myStats != null && myStats.CurrentHP > 0 && pathFromAttackPoint.Any())
	{
		await myMover.MoveAlongPathAsync(pathFromAttackPoint, true);
	}
}

public void OnSelectHover()
{
	if (currentState != PlayerTurnState.AwaitingInput) CancelAction();
	if (myActionManager.CanPerformAction(ActionType.Move))
	{
		myActionManager.UseAction(ActionType.Move); 
		_ = myMover.HoverAsync();
	}
}

private void HandleAbilityTargetingClick(GridNode collider, Vector3 hitPoint)
{
	if (selectedAbility == null) { CancelAction(); return; }

	if (selectedAbility.TargetType == TargetType.Area_EnemiesOnly || selectedAbility.TargetType == TargetType.Area_FriendOrFoe || selectedAbility.TargetType == TargetType.Area_AlliesOnly)
	{
		ResolveAbility(null, null, hitPoint);
	}
	else
	{
		CreatureStats targetCreature = collider as CreatureStats ?? collider.GetNodeOrNull<CreatureStats>("CreatureStats");
		ObjectDurability targetObjectDurability = collider.GetNodeOrNull<ObjectDurability>("ObjectDurability") ?? collider as ObjectDurability;
		GridNode targetObject = collider;
		
		bool isValidTarget = (targetCreature != null && IsTargetValid(targetCreature, targetObject as Node3D)) || (targetObjectDurability != null);

		if (isValidTarget)
		{
			if (IsActionValidAgainstTarget(selectedAbility, targetCreature))
			{
				// Convert GameObject -> Node
				ResolveAbility(targetCreature, targetObject as Node3D, (targetObject as Node3D).GlobalPosition);
			}
			else
			{
				CancelAction();
			}
		}
	}
}

// --- PUBLIC METHODS (UI) ---
 public void OnSelectToggleSenses()
{
	if (myImageController != null && myActionManager.CanPerformAction(ActionType.Free))
	{
		myActionManager.ToggleImageSenses();
	}
}

public void OnSelectFlybyAttack()
{
	if (currentState != PlayerTurnState.AwaitingInput) CancelAction();

	if (myStats.HasFeat("Flyby Attack") && 
		myStats.Template.Speed_Fly > 0 &&
		myActionManager.CanPerformAction(ActionType.Move) && 
		myActionManager.CanPerformAction(ActionType.Standard))
	{
		currentState = PlayerTurnState.SelectingFlybyStandardAction;
		GD.Print("Flyby Attack: Select a standard action to perform during the move.");
	}
	else
	{
		GD.Print("Cannot perform Flyby Attack.");
	}
}

public void OnFlybyStandardActionSelected(Ability_SO chosenAction)
{
	if (currentState != PlayerTurnState.SelectingFlybyStandardAction) return;

	flybyStandardAction = chosenAction;
	currentState = PlayerTurnState.SelectingFlybyAttackTarget;
	GD.Print($"Action '{chosenAction.AbilityName}' selected. Now select a target.");
}

public void OnSelectAbility(Ability_SO ability)
{
	if (!myActionManager.CanPerformAction(ability.ActionCost) || !myStats.MyUsage.HasUsesRemaining(ability))
	{
		GD.Print($"Cannot use {ability.AbilityName}: Not enough actions or uses remaining.");
		return;
	}

	selectedAbility = ability;

	if (selectedAbility.TargetType == TargetType.Self)
	{
		ResolveAbility(myStats, GetParent<Node3D>(), GetParent<Node3D>().GlobalPosition);
		return;
	}

	if (ability.AvailableCommands != null && ability.AvailableCommands.Any())
	{
		currentState = PlayerTurnState.AwaitingCommandChoice;
		GD.Print($"Awaiting command choice for {ability.AbilityName}. (UI should open here)");
	}
	else 
	{
		chosenCommand = CommandWord.None;
		PrepareForTargeting();
		
		if (ability.AbilityName == "Discern Location")
		{
			var seenTargets = GetDiscernLocationSeenTargets();
			if (seenTargets.Count == 1)
			{
				OnSelectDiscernLocationSeenTarget(seenTargets[0]);
				return;
			}

			GD.PrintRich($"<color=cyan>Discern Location:</color> choose one previously seen creature. Available targets: {string.Join(", ", seenTargets.Select(t => t.Name))}");
		}
	}
	
	if (ability.EffectComponents.Any(e => e is CreateIllusionEffect))
	{
		selectedAbility = ability;
		currentState = PlayerTurnState.PlacingIllusion;

		var illusionEffect = ability.EffectComponents.First(e => e is CreateIllusionEffect) as CreateIllusionEffect;
		if (illusionEffect.IllusionPrefab != null)
		{
			illusionPreviewInstance = illusionEffect.IllusionPrefab.Instantiate<Node3D>();
			GetTree().CurrentScene.AddChild(illusionPreviewInstance);
			// Disable controller
			if(illusionPreviewInstance is IllusionController ic) ic.SetProcess(false); // Or equivalent logic

			if (IsGhostSoundSelected())
			{
				float startVolume = GetGhostSoundMaxHumanVolume();
				illusionPreviewInstance.Scale = new Vector3(startVolume, 1f, 1f);
			}
		}

		if (IsGhostSoundSelected())
		{
			GD.Print($"Placing {ability.AbilityName}. Left-click to place, mouse wheel sets volume in human equivalents, Right-click to cancel.");
		}
		else
		{
			GD.Print($"Placing {ability.AbilityName}. Left-click to place, 'R' to rotate, Right-click to cancel.");
		}
		return;
	}
}

private bool IsGhostSoundSelected()
{
	return selectedAbility != null && selectedAbility.AbilityName.ToLower().Contains("ghost sound");
}

private float GetGhostSoundMaxHumanVolume()
{
	int casterLevel = myStats?.Template?.CasterLevel ?? 0;
	return Mathf.Max(0.1f, SoundSystem.GetGhostSoundMaxHumanVolume(casterLevel));
}

private void AdjustGhostSoundPreviewVolume(float delta)
{
	if (illusionPreviewInstance == null) return;

	float maxVolume = GetGhostSoundMaxHumanVolume();
	float current = illusionPreviewInstance.Scale.X > 0f ? illusionPreviewInstance.Scale.X : maxVolume;
	float updated = Mathf.Clamp(current + delta, 0.1f, maxVolume);
	illusionPreviewInstance.Scale = new Vector3(updated, 1f, 1f);

	GD.Print($"Ghost Sound volume set to {updated:0.#}/{maxVolume:0.#} humans.");
}

public void OnCommandWordSelected(CommandWord command)
{
	if (currentState == PlayerTurnState.AwaitingCommandChoice)
	{
		chosenCommand = command;
		GD.Print($"Command '{command}' selected.");
		PrepareForTargeting();
	}
}

private void PrepareForTargeting()
{
	currentState = PlayerTurnState.SelectingAbilityTarget;

	if (AoeTemplatePrefab != null && (selectedAbility.TargetType == TargetType.Area_EnemiesOnly || selectedAbility.TargetType == TargetType.Area_FriendOrFoe || selectedAbility.TargetType == TargetType.Area_AlliesOnly))
		{
			currentAoeTemplate = AoeTemplatePrefab.Instantiate<Node3D>();
			GetTree().CurrentScene.AddChild(currentAoeTemplate);
			
			var shape = selectedAbility.AreaOfEffect.Shape;
			float r = selectedAbility.AreaOfEffect.Range;

			if (shape == AoEShape.Line)
			{
				float width = selectedAbility.AreaOfEffect.Width > 0 ? selectedAbility.AreaOfEffect.Width : 5f;
				currentAoeTemplate.Scale = new Vector3(width, 1, r);
			}
			else if (shape == AoEShape.Cone)
			{
				currentAoeTemplate.Scale = Vector3.One * r; 
			}
			else 
			{
				currentAoeTemplate.Scale = Vector3.One * r * 2;
			}
		}
	GD.Print($"Selected ability: {selectedAbility.AbilityName}. Waiting for target.");
}
private void ResolveAbility(CreatureStats creature, GridNode obj, Vector3 point)
{
// --- NEW: SUGGESTION MODE LOGIC ---
if (isControllingAlly)
{
var actionManagerForCheck = allyActionManager ?? myActionManager;
if (!actionManagerForCheck.CanPerformAction(selectedAbility.ActionCost))
{
GD.Print($"Cannot add {selectedAbility.AbilityName} to plan: Not enough actions remaining.");
CancelAction();
return;
}


var aiController = GetParent().GetNodeOrNull<AIController>("AIController");
		// Assuming we added an AIController to control allies? Or accessing ally's controller.
		// If `isControllingAlly` is true, `this` component is on the ally.
		
		var suggestedAIAction = new AIAction_CastGenericAbility(
			aiController, selectedAbility, creature, point, chosenCommand, false);
		
		currentSuggestedPlan.Actions.Add(suggestedAIAction);
		currentSuggestedPlan.UpdateName();
		GD.Print($"Added to plan: {suggestedAIAction.Name}. Current plan: {currentSuggestedPlan.Name}");
		
		actionManagerForCheck.UseAction(selectedAbility.ActionCost);
		
		CancelAction();
		return; 
	}

if (selectedAbility.AbilityName == "Vital Strike")
	{
		 myActionManager.UseAction(ActionType.Standard);
		 CombatManager.ResolveMeleeAttack(myStats, creature);
		 myStats.IsUsingVitalStrike = false; 
		 CancelAction();
		 return;
	}
	
	if (selectedAbility.AbilityName == "Total Defense")
	{
		myActionManager.UseAction(ActionType.Standard);
		myActionManager.IsUsingTotalDefense = true;
		GD.PrintRich($"<color=cyan>{myStats.Name} uses Total Defense!</color>");
		CancelAction();
		return; 
	}
	if (selectedAbility.AbilityName == "Fight Defensively")
	{
		myActionManager.IsFightingDefensively = true;
		GD.PrintRich($"<color=cyan>{myStats.Name} is Fighting Defensively this turn.</color>");
		CancelAction(); 
		return;
	}

	CreatureStats castingEntity = isControllingImage && myImageController != null 
		? myImageController.GetNode<CreatureStats>("CreatureStats") // Assuming hierarchy
		: myStats;
	
	myActionManager.UseAction(selectedAbility.ActionCost);
	myStats.MyUsage.ConsumeUse(selectedAbility);

	bool isMythic = false; 
	_ = CombatManager.ResolveAbility(castingEntity, creature, obj as Node3D, point, selectedAbility, isMythic, chosenCommand);
	
	if (myStats.IsUsingVitalStrike) myStats.IsUsingVitalStrike = false;
	CancelAction();
}

private void CancelAction()
 {
	currentState = PlayerTurnState.AwaitingInput;
	selectedAbility = null;
	chosenCommand = CommandWord.None;
	if (pathPreviewInstance != null) pathPreviewInstance.QueueFree();
	if (currentAoeTemplate != null) currentAoeTemplate.QueueFree();
	flybyTarget = null;
	flybyStandardAction = null;
	if (illusionPreviewInstance != null) illusionPreviewInstance.QueueFree();
}

public void OnSelectSwitchWeapon(ItemInstance weaponToEquip)
{
	if (currentState != PlayerTurnState.AwaitingInput) CancelAction();
	
	if (myActionManager.CanPerformAction(ActionType.Move))
	{
		myActionManager.UseAction(ActionType.Move);
		myStats.MyInventory.SwitchToWeapon(weaponToEquip);
	}
	else
	{
		GD.Print("Not enough actions remaining to switch weapon.");
	}
}

public void OnSelectNormalMove()
{
	PrepareMove(PlayerTurnState.SelectingNormalMoveTarget);
}

public void OnSelectAcrobaticMoveHalfSpeed()
{
	PrepareMove(PlayerTurnState.SelectingAcrobaticMoveHalfSpeedTarget);
}

public void OnSelectAcrobaticMoveFullSpeed()
{
	PrepareMove(PlayerTurnState.SelectingAcrobaticMoveFullSpeedTarget);
}

private void PrepareMove(PlayerTurnState targetState)
 {
	if (currentState != PlayerTurnState.AwaitingInput && currentState != PlayerTurnState.SuggestingAllyTurn)
	{
		CancelAction();
	}

	var actionManagerForCheck = isControllingAlly ? allyActionManager : myActionManager;

	if (actionManagerForCheck.CanPerformAction(ActionType.Move))
	{
		currentState = targetState;

		if (PathPreviewPrefab != null)
		{
			pathPreviewInstance = PathPreviewPrefab.Instantiate<Node3D>();
			GetTree().CurrentScene.AddChild(pathPreviewInstance);
			// Set color via material or script props if available on prefab
		}
		GD.Print($"Move mode activated ({targetState}). Select a destination.");
	}
	else
	{
		GD.Print("Not enough move actions remaining.");
	}
}

public void OnRequestReaction(CreatureStats spellCaster, Ability_SO incomingSpell, string skillName)
{
	GD.PrintRich($"<color=cyan>PLAYER REACTION:</color> UI should now show a button to use {skillName} to identify '{incomingSpell.AbilityName}'.");
}

private bool IsActionValidAgainstTarget(Ability_SO action, CreatureStats target)
{
	if (action == null || target == null) return true; 

	var harmlessEffect = myStats.MyEffects.ActiveEffects.FirstOrDefault(e => e.EffectData.EffectName == "Perceived as Harmless");
	if (harmlessEffect != null && harmlessEffect.SourceCreature == target)
	{
		bool isAggressive = action.Category == AbilityCategory.SpecialAttack ||
							(action.EffectComponents != null && action.EffectComponents.Any(c => c is DamageEffect));

		if (isAggressive)
		{
			GD.PrintRich($"<color=orange>Action Blocked!</color> You perceive {target.Name} as harmless and cannot take aggressive action against them this turn.");
			return false;
		}
	}

	return true; 
}

private bool IsTargetValid(CreatureStats creature, Node3D obj)
{
	if (selectedAbility == null) return false;
	
	 if (selectedAbility.AbilityName == "Discern Location")
	{
		if (creature == null) return false;

		var stateController = myStats.GetNodeOrNull<CombatStateController>("CombatStateController");
		return stateController != null && stateController.HasSeenCreature(creature);
	}


	Vector3 originPoint = isControllingImage && myImageController != null 
		? myImageController.GetParent<Node3D>().GlobalPosition 
		: GetParent<Node3D>().GlobalPosition;

	if (originPoint.DistanceTo(obj.GlobalPosition) > selectedAbility.Range.GetRange(myStats))
	{
		return false;
	}
	switch (selectedAbility.EntityType)
	{
		case TargetableEntityType.CreaturesOnly:
			if (creature == null) return false;
			if (selectedAbility.TargetType == TargetType.SingleEnemy && creature.IsInGroup("Player")) return false;
			if (selectedAbility.TargetType == TargetType.SingleAlly && creature.IsInGroup("Enemy")) return false;
			break;
		case TargetableEntityType.ObjectsOnly:
			// Check layer bitmask logic manually or use group
			if (creature != null) return false; 
			// Assumed object layer check logic
			break;
		case TargetableEntityType.CreaturesAndObjects:
			if (creature == null) 
			{
				// Check if object
			}
			break;
	}

	return true;
}
public List<CreatureStats> GetDiscernLocationSeenTargets()
{
	var stateController = myStats.GetNodeOrNull<CombatStateController>("CombatStateController");
	if (stateController == null) return new List<CreatureStats>();

	return stateController.SeenCreaturesThisCombat
		.Where(c => c != null && c.IsInGroup("Player") != myStats.IsInGroup("Player") && c.CurrentHP > 0)
		.ToList();
}

public void OnSelectDiscernLocationSeenTarget(CreatureStats target)
{
	if (selectedAbility == null || selectedAbility.AbilityName != "Discern Location") return;

	if (target == null || !IsTargetValid(target, target))
	{
		GD.PrintRich("<color=orange>Invalid Discern Location target. Choose a creature you've already seen this match.</color>");
		return;
	}

	ResolveAbility(target, target, target.GlobalPosition);
}

public void OnConfirmSuggestion()
{
	if (!isControllingAlly || currentState != PlayerTurnState.SuggestingAllyTurn) return;

	var aiController = GetParent().GetNodeOrNull<AIController>("AIController");
	if (aiController != null)
	{
		GD.PrintRich($"<color=yellow>Player suggestion confirmed for {GetParent().Name}. AI is now deliberating...</color>");
		_ = aiController.DecideAndExecuteBestTurnPlan(currentSuggestedPlan);
	}
	
	EndPlayerTurn();
}

public void GenerateAvailableActions()
{
	var equippedWeapon = myStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
	if (equippedWeapon != null && equippedWeapon.WeaponType == WeaponType.Melee && equippedWeapon.RangeIncrement <= 0)
	{
		if (myActionManager.CanPerformAction(ActionType.Standard))
		{
			GD.Print("UI should now show a button for 'Throw Improvised Weapon'");
		}
	}
	
	if (myStats.HasFeat("Vital Strike") && myActionManager.CanPerformAction(ActionType.Standard))
	{
		var weapon = myStats.MyInventory?.GetEquippedItem(EquipmentSlot.MainHand);
		if (weapon != null) 
		{
			// UI Hook
		}
	}
}
public void OnSelectVitalStrike()
{
if (currentState != PlayerTurnState.AwaitingInput) CancelAction();

GD.Print("Vital Strike selected. Select target.");
	
	myStats.IsUsingVitalStrike = true; 
	
	var vitalStrikeAbility = new Ability_SO();
	vitalStrikeAbility.AbilityName = "Vital Strike";
	vitalStrikeAbility.ActionCost = ActionType.Standard;
	vitalStrikeAbility.Range = new RangeInfo { Type = RangeType.Custom, CustomRangeInFeet = myStats.GetEffectiveReach((Item_SO)null).max };
	vitalStrikeAbility.TargetType = TargetType.SingleEnemy;
	
	OnSelectAbility(vitalStrikeAbility);
}

private void AttemptRetrieveWeapon(CreatureStats adherer, WorldItem weapon)
{
	if (!myActionManager.CanPerformAction(ActionType.Standard))
	{
		GD.Print("Not enough actions to retrieve weapon.");
		return;
	}

	GD.Print($"Attempting to pry {weapon.ItemData.ItemName} off {adherer.Name}...");
	
	_ = ExecuteRetrieveRoutine(adherer, weapon);
}

private async Task ExecuteRetrieveRoutine(CreatureStats adherer, WorldItem weapon)
{
	await AoOManager.Instance.CheckAndResolve(myStats, ProvokingActionType.UseItem);
	
	if (myStats.CurrentHP <= 0) return;

	myActionManager.UseAction(ActionType.Standard);

	int dc = 17; 
	int roll = Dice.Roll(1, 20) + myStats.StrModifier;

	if (roll >= dc)
	{
		GD.PrintRich("<color=green>Success!</color> Weapon retrieved.");
		ItemManager.Instance.PickupItem(myStats, weapon);
		
		if (myStats.MyInventory.GetEquippedItem(EquipmentSlot.MainHand) == null)
		{
			var newItem = myStats.MyInventory.GetBackpackWeapons().LastOrDefault();
			if (newItem != null) myStats.MyInventory.EquipItem(newItem);
		}
	}
	else
	{
		GD.PrintRich("<color=red>Failed</color> to retrieve weapon.");
	}
}

private void AttemptBindSoulEngine(CreatureStats victim)
{
	if (!myActionManager.CanPerformAction(ActionType.FullRound))
	{
		GD.Print("Requires a Full-Round action to bind a body to the Soul Engine.");
		return;
	}

	var bindAbility = new Ability_SO { AbilityName = "Bind to Soul Engine", ActionCost = ActionType.FullRound, TargetType = TargetType.SingleEnemy };
	bindAbility.EffectComponents.Add(new Effect_BindSoulEngine());
	
	selectedAbility = bindAbility;
	ResolveAbility(victim, victim, victim.GlobalPosition);
}

private void AttemptPrySoulEngine(CreatureStats construct)
{
	if (!myActionManager.CanPerformAction(ActionType.Standard))
	{
		GD.Print("Not enough actions to pry the body.");
		return;
	}

	var pryAbility = new Ability_SO { AbilityName = "Pry Soul Engine", ActionCost = ActionType.Standard };
	pryAbility.EffectComponents.Add(new Effect_PrySoulEngine());
	
	selectedAbility = pryAbility;
	ResolveAbility(construct, construct, construct.GlobalPosition);
}

private async Task ExecuteForcedFlee()
	{
		// 1. Find Flee Position (Using existing AI logic)
		Vector3? fleeDest = AISpatialAnalysis.FindBestFleePosition(myStats, myMover);

		// 2. Execute Move (or Cower if trapped)
		if (fleeDest.HasValue)
		{
			// If Panicked, we must drop items (Handled by StatusEffectController), 
			// but we also need to use all means to flee.
			// For sim simplicity, we use the standard "Run/Withdraw" logic provided by the mover.
			
			// Calculate distance to use correct action type (Move vs Double Move/Run)
			float dist = GetParent<Node3D>().GlobalPosition.DistanceTo(fleeDest.Value);
			float speed = myMover.GetEffectiveMovementSpeed();
			
			if (dist > speed)
			{
				if (myActionManager.CanPerformAction(ActionType.FullRound))
				{
					myActionManager.UseAction(ActionType.FullRound);
					// Use RunAsync if available in Mover, or standard MoveToAsync
					await myMover.RunAsync(fleeDest.Value);
				}
				else if (myActionManager.CanPerformAction(ActionType.Move))
				{
					myActionManager.UseAction(ActionType.Move);
					await myMover.MoveToAsync(fleeDest.Value);
				}
			}
			else if (myActionManager.CanPerformAction(ActionType.Move))
			{
				myActionManager.UseAction(ActionType.Move);
				await myMover.MoveToAsync(fleeDest.Value);
			}
		}
		else
		{
			GD.PrintRich($"<color=red>{GetParent().Name} is cornered and cowers!</color>");
			// Rule: If cornered, a Panicked creature cowers (Total Defense).
			if (myActionManager.CanPerformAction(ActionType.Standard))
			{
				myActionManager.UseAction(ActionType.Standard);
				myActionManager.IsUsingTotalDefense = true;
				
				// Optional: Add "Cowering" visual/status if not implicitly handled by Total Defense state
			}
		}

		// 3. End Turn
		await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		EmitSignal(SignalName.TurnEnded);
	}
}

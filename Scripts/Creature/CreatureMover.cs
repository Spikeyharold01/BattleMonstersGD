using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// =================================================================================================
// FILE: CreatureMover.cs (GODOT VERSION - PART 1)
// PURPOSE: Handles physical movement and hazard interaction.
// ATTACH TO: Creature Root Node.
// =================================================================================================

public partial class CreatureMover : Node
{
    // The visual speed of the creature's movement in units per second.
    [Export] public float MoveSpeed = 5f;

    // To track async task cancellation, we use a token source or flag in Godot.
    // Since C# Tasks are harder to "Stop" than Coroutines, we use a cancellation flag.
    private bool isMoving = false;
    private bool cancelMove = false;

    // A cached reference to this creature's stats component.
    private CreatureStats myStats;

    private List<EnvironmentalHazard> triggeredHazardsThisMove = new List<EnvironmentalHazard>();
    private bool isCheckedByWind = false; 

    public override void _Ready()
    {
        myStats = GetParent() as CreatureStats ?? GetNode<CreatureStats>("CreatureStats");
        triggeredHazardsThisMove = new List<EnvironmentalHazard>();
    }

    /// <summary>
    /// Called by TurnManager at the start of this creature's turn.
    /// </summary>
    public void OnTurnStart()
    {
        triggeredHazardsThisMove.Clear();
        isCheckedByWind = false;
    }

    /// <summary>
    /// A simple public method to initiate a move. "Fire and forget".
    /// </summary>
    public void MoveTo(Vector3 destination, bool isFlying = false)
    {
        _ = MoveToAsync(destination, isFlying);
    }

    /// <summary>
    /// The main async method for initiating a move.
    /// </summary>
    public async Task MoveToAsync(Vector3 destination, bool isFlying = false)
    {
        // --- MOUNTED COMBAT LOGIC ---
        if (myStats.IsMount)
        {
            GD.PrintRich($"[color=yellow]{myStats.Name} cannot move independently while being ridden.[/color]");
            return;
        }
        if (myStats.IsMounted)
        {
            // Delegate to mount
            await myStats.MyMount.GetNode<CreatureMover>("CreatureMover").MoveToAsync(destination, isFlying);
            return;
        }
        // --- END MOUNTED COMBAT LOGIC ---

        if (isCheckedByWind)
        {
            if (!PerformFlyManeuverCheck(20, "Move while Checked by wind"))
            {
                GD.PrintRich($"[color=red]{myStats.Name} fails to move against the wind and loses its move action.[/color]");
                return;
            }
        }

        if (myStats == null || myStats.Template == null) return;

        List<Vector3> path = Pathfinding.Instance.FindPath(myStats, myStats.GlobalPosition, destination);

        if (path != null)
        {
            float pathCost = path.Count * 5f;
            float effectiveMovementSpeed = GetEffectiveMovementSpeed();

            // Blinded check logic
            if (myStats.MyEffects.HasCondition(Condition.Blinded) && pathCost > (effectiveMovementSpeed / 2f))
            {
                int acrobaticsRoll = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Acrobatics);
                if (acrobaticsRoll < 10)
                {
                    GD.PrintRich($"[color=orange]{myStats.Name} is blinded and fails a DC 10 Acrobatics check! Falls prone.[/color]");
                    var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
                    if(proneEffect != null) myStats.MyEffects.AddEffect((StatusEffect_SO)proneEffect.Duplicate(), myStats);
                    return;
                }
                else
                {
                    GD.Print($"{myStats.Name} is blinded but succeeds on a DC 10 Acrobatics check.");
                }
            }

            if (pathCost <= effectiveMovementSpeed)
            {
                await MoveAlongPathAsync(path, isFlying);
            }
            else
            {
                GD.Print($"Path is too long ({pathCost}ft) for effective speed ({effectiveMovementSpeed}ft).");
            }
        }
    }

    // --- NEW METHODS FOR FLY SKILL ---

    public void HandleDamageWhileFlying(bool hasWings)
    {
        if (myStats == null || myStats.Template.Speed_Fly <= 0 || !hasWings) return;

        int flyCheck = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Fly);
        if (flyCheck < 10)
        {
            GD.PrintRich($"[color=orange]{myStats.Name} took damage while flying and failed check ({flyCheck}). Losing 10ft altitude![/color]");
            // Assuming Parent Node is the CharacterBody3D that moves
            var body = GetParent<Node3D>();
            body.GlobalPosition -= new Vector3(0, 10f, 0);
        }
    }

    /// <summary>
    /// Converts global wind into the effective wind the creature actually experiences.
    /// Output expected: a potentially reduced wind category when protective effects are active.
    /// </summary>
    public WindStrength GetEffectiveWindStrength(WindStrength incomingWind)
    {
        int reductionSteps = myStats?.MyEffects?.GetWindSeverityReductionSteps() ?? 0;
        int adjustedValue = Mathf.Max((int)WindStrength.None, (int)incomingWind - reductionSteps);
        return (WindStrength)adjustedValue;
    }

    public async Task OnTurnStart_WindCheck()
    {
        var weather = WeatherManager.Instance?.CurrentWeather;
        WindStrength effectiveWind = GetEffectiveWindStrength(weather?.WindStrength ?? WindStrength.None);
        if (weather == null || effectiveWind <= WindStrength.Moderate) return;

        bool isChecked = false;
        bool isBlownAway = false;

        // Size check logic matches Unity exactly
        if (effectiveWind == WindStrength.Strong && myStats.Template.Size <= CreatureSize.Tiny) isChecked = true;
        if (effectiveWind == WindStrength.Severe) { if (myStats.Template.Size <= CreatureSize.Small) isChecked = true; if (myStats.Template.Size <= CreatureSize.Tiny) isBlownAway = true; }
        if (effectiveWind == WindStrength.Windstorm) { if (myStats.Template.Size <= CreatureSize.Medium) isChecked = true; if (myStats.Template.Size <= CreatureSize.Small) isBlownAway = true; }
        if (effectiveWind == WindStrength.Hurricane) { if (myStats.Template.Size <= CreatureSize.Large) isChecked = true; if (myStats.Template.Size <= CreatureSize.Medium) isBlownAway = true; }
        if (effectiveWind == WindStrength.Tornado) { if (myStats.Template.Size <= CreatureSize.Huge) isChecked = true; if (myStats.Template.Size <= CreatureSize.Large) isBlownAway = true; }

        if (isBlownAway)
        {
            int flyCheck = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Fly);
            if (flyCheck < 25)
            {
                int distance = Dice.Roll(2, 6) * 10;
                int damage = Dice.Roll(2, 6);
                Vector3 windDirection = WeatherManager.Instance.CurrentWindDirection; // Needs Godot Vector3
                var body = GetParent<Node3D>();
                Vector3 startPos = body.GlobalPosition;
                Vector3 destination = startPos + (windDirection * distance);

                GD.PrintRich($"[color=red]{myStats.Name} is blown away! (Fly {flyCheck} vs 25). Pushed {distance}ft.[/color]");

                // Raycast collision check
                var spaceState = body.GetWorld3D().DirectSpaceState;
                var query = PhysicsRayQueryParameters3D.Create(startPos, destination, 1); // Mask 1 for Walls
                var result = spaceState.IntersectRay(query);

                if (result.Count > 0)
                {
                    Vector3 hitPoint = (Vector3)result["position"];
                    body.GlobalPosition = hitPoint - (windDirection * (myStats.Template.Space / 2f));
                    GD.PrintRich($"[color=red]...and collides, taking {damage} nonlethal damage![/color]");
                    myStats.TakeNonlethalDamage(damage);
                }
                else
                {
                    body.GlobalPosition = destination;
                }
                await ToSignal(GetTree().CreateTimer(0.5f), "timeout");
            }
        }

        if (isChecked)
        {
            GD.Print($"{myStats.Name} is 'Checked' by wind.");
            isCheckedByWind = true;
        }
    }

    public void OnTurnEnd_FlyCheck()
    {
        var body = GetParent<Node3D>();
        GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);
        
        if (currentNode.terrainType == TerrainType.Ground || currentNode.terrainType == TerrainType.Water || currentNode.terrainType == TerrainType.Solid) return;

        float distanceMoved = myStats.GetNode<ActionManager>("ActionManager").MoveActionDistanceUsed;

        if (distanceMoved == 0)
        {
            if (!PerformFlyManeuverCheck(15, "Hover"))
            {
                HandleFall(currentNode, GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition), true);
            }
        }
        else if (distanceMoved < myStats.Template.Speed_Fly / 2f)
        {
            if (!PerformFlyManeuverCheck(10, "Move less than half speed"))
            {
                HandleFall(currentNode, GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition), true);
            }
        }
    }

    public async Task MoveAlongPathAsync(List<Vector3> path, bool isFlying = false)
    {
        if (myStats.IsMount)
        {
            GD.PrintRich($"[color=yellow]{Name} cannot move independently while ridden.[/color]");
            return;
        }
        if (myStats.IsMounted)
        {
            await myStats.MyMount.GetNode<CreatureMover>("CreatureMover").MoveAlongPathAsync(path, isFlying);
            return;
        }

        if (path == null || !path.Any()) return;

        if (isMoving) cancelMove = true; // Stop existing
        // Wait a frame for cleanup if needed, or just proceed logic flow
        triggeredHazardsThisMove.Clear();
        
        await MoveAndHandleAoO(path, isFlying);
    }

    public async Task FiveFootStepAsync(Vector3 destination)
    {
        var body = GetParent<Node3D>();
        if (body.GlobalPosition.DistanceTo(destination) > GridManager.Instance.nodeDiameter * 1.5f)
        {
            GD.PrintErr($"{Name} tried to 5-foot step to non-adjacent square.");
            return;
        }

        if (isMoving) cancelMove = true;
        
        var path = new List<Vector3> { destination };
        await FollowPath(path);
    }

    public async Task WithdrawToAsync(Vector3 destination)
    {
        if (myStats.IsMount) return;
        if (myStats.IsMounted)
        {
            await myStats.MyMount.GetNode<CreatureMover>("CreatureMover").WithdrawToAsync(destination);
            return;
        }

        if (myStats == null || myStats.Template == null) return;

        var body = GetParent<Node3D>();
        List<Vector3> path = Pathfinding.Instance.FindPath(myStats, body.GlobalPosition, destination);

        if (path != null)
        {
            float pathCost = path.Count * 5f;
            float effectiveMovementSpeed = GetEffectiveMovementSpeed() * 2f;

            if (pathCost <= effectiveMovementSpeed)
            {
                if (isMoving) cancelMove = true;
                triggeredHazardsThisMove.Clear();
                await WithdrawAndFollowPath(path);
            }
            else
            {
                GD.Print($"Withdraw path too long.");
            }
        }
    }

    private async Task WithdrawAndFollowPath(List<Vector3> path)
    {
        await AoOManager.Instance.CheckAndResolveForWithdraw(myStats);

        if (!GodotObject.IsInstanceValid(this) || myStats.CurrentHP <= 0) return;

        await FollowPath(path);
    }
	public async Task AcrobaticMoveAsync(Vector3 destination, bool moveAtFullSpeed)
    {
        var inv = myStats.MyInventory;
        var armor = inv?.GetEquippedItem(EquipmentSlot.Armor);
        if (armor != null && (armor.ArmorCategory == ArmorCategory.Medium || armor.ArmorCategory == ArmorCategory.Heavy))
        {
            GD.Print($"{myStats.Name} cannot use Acrobatics in medium/heavy armor.");
            return;
        }

        var body = GetParent<Node3D>();
        List<Vector3> path = Pathfinding.Instance.FindPath(myStats, body.GlobalPosition, destination);
        if (path == null || !path.Any()) return;

        float speedBudget = moveAtFullSpeed ? GetEffectiveMovementSpeed() : GetEffectiveMovementSpeed() / 2f;
        float pathCost = path.Count * 5f;
        if (pathCost > speedBudget)
        {
            GD.Print($"Acrobatics path too long.");
            return;
        }

        if (isMoving) cancelMove = true;
        triggeredHazardsThisMove.Clear();

        await AcrobaticMoveAndFollowPath(path, moveAtFullSpeed);
    }

    private async Task AcrobaticMoveAndFollowPath(List<Vector3> path, bool moveAtFullSpeed)
    {
        var threatenersAlongPath = new HashSet<CreatureStats>();
        threatenersAlongPath.UnionWith(AoOManager.Instance.GetThreateningCreatures(myStats));

        if (threatenersAlongPath.Any())
        {
            int highestCMD = threatenersAlongPath.Max(t => t.GetCMD());
            int dc = highestCMD + (threatenersAlongPath.Count - 1) * 2;
            
            var body = GetParent<Node3D>();
            if (GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition).terrainType == TerrainType.Ice)
            {
                dc += 5;
            }

            if (moveAtFullSpeed) dc += 10;

            int acrobaticsRoll = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Acrobatics);

            GD.Print($"{myStats.Name} Acrobatics Roll: {acrobaticsRoll} vs DC {dc}.");

            if (acrobaticsRoll < dc)
            {
                GD.PrintRich("<color=red>Acrobatics Failed!</color>");
                await AoOManager.Instance.CheckAndResolve(myStats, ProvokingActionType.Movement);
            }
            else
            {
                GD.PrintRich("<color=green>Acrobatics Successful!</color>");
            }
        }

        if (!GodotObject.IsInstanceValid(this) || myStats.CurrentHP <= 0) return;

        await FollowPath(path);
    }

    private async Task MoveAndHandleAoO(List<Vector3> path, bool isFlying)
    {
        await AoOManager.Instance.CheckAndResolve(myStats, ProvokingActionType.Movement);

        if (!GodotObject.IsInstanceValid(this) || myStats.CurrentHP <= 0) return;

        await FollowPath(path, isFlying);
    }

    public async Task HoverAsync()
    {
        if (myStats.Template.Speed_Fly > 0)
        {
            GD.Print($"{myStats.Name} spends its move action to hover.");
        }
        await Task.CompletedTask;
    }

    public float GetEffectiveMovementSpeed()
    {
        if (myStats.MyEffects.HasCondition(Condition.Gaseous))
        {
            var gasCtrl = myStats.GetNodeOrNull<GaseousFormController>("GaseousFormController");
            if (gasCtrl != null && gasCtrl.IsWindWalk && gasCtrl.InFastWindMode)
            {
                return 600f;
            }
            return myStats.Template.Speed_Fly;
        }

        CreatureStats mover = myStats.IsMounted ? myStats.MyMount : myStats;
        if (mover == null) return 0;

        float baseSpeed = mover.Template.Speed_Fly > 0 ? mover.Template.Speed_Fly : mover.Template.Speed_Land;

        if (mover.Template.MovementType == MovementType.Ground)
        {
            baseSpeed = mover.Template.Speed_Land;
            var inv = mover.GetNodeOrNull<InventoryController>("InventoryController");
            if (inv != null)
            {
                var equippedArmor = inv.GetEquippedItem(EquipmentSlot.Armor);
                if (equippedArmor != null && (equippedArmor.ArmorCategory == ArmorCategory.Medium || equippedArmor.ArmorCategory == ArmorCategory.Heavy))
                {
                    if (mover.Template.SubTypes != null && mover.Template.SubTypes.Contains("Dwarf"))
                    {
                        GD.Print($"{mover.Name} is a Dwarf; speed not reduced.");
                    }
                    else
                    {
                        baseSpeed = mover.Template.Speed_Land_Armored;
                    }
                }
            }
        }

        if (mover.Template.MovementType == MovementType.Flying)
        {
            baseSpeed = mover.Template.Speed_Fly;
        }
 if (mover.MyEffects != null)
        {
            baseSpeed += mover.MyEffects.GetTotalModifier(StatToModify.Speed);
            baseSpeed = Mathf.Max(0f, baseSpeed);
        }
        if (WeatherManager.Instance != null && WeatherManager.Instance.CurrentWeather != null)
        {
            var weather = WeatherManager.Instance.CurrentWeather;
            WindStrength effectiveWind = mover.GetNodeOrNull<CreatureMover>("CreatureMover")?.GetEffectiveWindStrength(weather.WindStrength) ?? weather.WindStrength;
            if (mover.Template.MovementType == MovementType.Flying)
            {
                if (effectiveWind == WindStrength.Strong) baseSpeed *= 0.5f;
                else if (effectiveWind == WindStrength.Severe) baseSpeed = 0f;
            }
        }

        var effects = mover.GetNodeOrNull<StatusEffectController>("StatusEffectController");
        if (effects != null)
        {
            if (effects.HasConditionStr("FarReachingStance")) return 0f;
			
            if (effects.HasCondition(Condition.Grappled))
            {
                bool isGrappleController = mover.CurrentGrappleState != null && mover.CurrentGrappleState.Controller == mover;

                if (!isGrappleController) return 0f;

                bool hasSwimmingGrapple = mover.Template.SpecialQualities.Contains("Swimming Grapple") || 
                                          mover.Template.KnownAbilities.Any(a => a.AbilityName == "Swimming Grapple");
                
                GridNode myNode = GridManager.Instance.NodeFromWorldPoint(mover.GlobalPosition);
                bool inWater = myNode.terrainType == TerrainType.Water;

                if (hasSwimmingGrapple && inWater)
                {
                    // Allow movement
                }
                else
                {
                    return 0f;
                }
            }
            if (effects.HasCondition(Condition.Impeded)) return 0f;
            if (effects.HasCondition(Condition.Blinded) || effects.HasCondition(Condition.Entangled)) baseSpeed *= 0.5f;
            if (effects.HasCondition(Condition.Exhausted)) baseSpeed *= 0.5f;
        }

        return baseSpeed;
    }


    /// <summary>
    /// Decides whether crossing a travel combat boundary should count as a real escape.
    ///
    /// Expected output:
    /// - true only when flee intent, morale collapse, or explicit disengage declaration is active.
    /// - false during ordinary repositioning so accidental edge drift is redirected inward.
    /// </summary>
    private bool IsEscapeAuthorized()
    {
        bool fleeIntent = myStats?.GetNodeOrNull<ActionManager>("ActionManager")?.IsRunning == true;
        bool moraleBroken = myStats != null && (myStats.MyEffects.HasCondition(Condition.Panicked) || myStats.MyEffects.HasCondition(Condition.Frightened));
        bool disengageDeclared = myStats != null && myStats.HasMeta("DisengageActionDeclared") && (bool)myStats.GetMeta("DisengageActionDeclared");
        return fleeIntent || moraleBroken || disengageDeclared;
    }

    private async Task FollowPath(List<Vector3> path, bool isFlying = false)
    {
        isMoving = true;
        var stealthController = myStats.GetNodeOrNull<StealthController>("StealthController");
        bool isSneaking = stealthController != null && stealthController.IsActivelyHiding;
        if (isSneaking)
        {
            stealthController.OnMovementWhileHidden();
        }

        var body = GetParent<Node3D>();
         Vector3 moveDirection = path.Count > 0 ? (path[^1] - body.GlobalPosition).Normalized() : body.GlobalBasis.Z;
        SoundSystem.EmitCreatureActionSound(myStats, isSneaking ? SoundActionType.Sneak : SoundActionType.Move, isSneaking, durationSeconds: Mathf.Max(1.0f, path.Count * 0.25f), direction: moveDirection);
        cancelMove = false;
		
        var startingEffect = EffectManager.Instance.GetEffectAtPosition(body.GlobalPosition);
        startingEffect?.ApplyLingeringEffects(myStats);

        GridNode previousNode = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);

        foreach (Vector3 waypoint in path)
        {
            if (cancelMove) break;
			// Check Wind Resistance
            GridNode currentNodeToCheck = GridManager.Instance.NodeFromWorldPoint(body.GlobalPosition);
            Vector3 windDir = GridManager.Instance.GetWindAtNode(currentNodeToCheck);
            
            // Check if moving INTO wind (Dot Product < -0.5 means facing opposite)
            Vector3 moveDir = (waypoint - body.GlobalPosition).Normalized();
            if (windDir != Vector3.Zero && moveDir.Dot(windDir) < -0.5f)
            {
                // Rule: "Medium or smaller... unable to move forward... unless DC 15 Strength check"
                if (myStats.Template.Size <= CreatureSize.Medium)
                {
                    int strCheck = Dice.Roll(1, 20) + myStats.StrModifier;
                    if (strCheck < 15)
                    {
                        GD.PrintRich($"[color=red]{myStats.Name} fails Strength check vs Wind ({strCheck} vs 15) and cannot move forward![/color]");
                        break; // Stop movement
                    }
                }
            }

            bool escapeAuthorized = IsEscapeAuthorized();
            if (CombatBoundaryService.ShouldBlockMovementStep(myStats, body.GlobalPosition, waypoint, escapeAuthorized))
            {
                break;
            }

            if (!CombatBoundaryService.IsInsideCombatBounds(waypoint) && CombatBoundaryService.CurrentMode == CombatBoundaryMode.TravelEscapable && escapeAuthorized)
            {
                // Travel escape is processed by the boundary service and this mover exits path simulation cleanly.
                break;
            }

            GridNode currentNode = GridManager.Instance.NodeFromWorldPoint(waypoint);
            int dy = currentNode.gridY - previousNode.gridY;

            if (currentNode.terrainType == TerrainType.Ice && !isFlying && !myStats.MyEffects.IgnoresSnowAndIceMovementPenalty())
            {
                int acro = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Acrobatics);
                if (acro < 10)
                {
                    GD.PrintRich($"[color=orange]{myStats.Name} slips on ice![/color]");
                    if (acro < 5)
                    {
                        GD.Print("...and falls prone!");
                        var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
                        if(proneEffect != null) myStats.MyEffects.AddEffect((StatusEffect_SO)proneEffect.Duplicate(), myStats);
                    }
                    break;
                }
            }

            if (myStats.MyEffects.HasEffect("On Fire"))
            {
                FireManager.Instance?.StartNewFire(waypoint);
            }

            if (isFlying && CheckForCollision(previousNode, currentNode)) break;

            if (isFlying)
            {
                Vector3 moveVector = waypoint - body.GlobalPosition;
                Vector3 previousMoveVector = body.GlobalPosition - previousNode.worldPosition;

                if (previousMoveVector != Vector3.Zero)
                {
                    float angle = Mathf.RadToDeg(previousMoveVector.AngleTo(moveVector));
                    if (angle > 45 && angle <= 90)
                    {
                        if (!PerformFlyManeuverCheck(15, "Turn > 45°")) break;
                    }
                    else if (angle > 90)
                    {
                        if (!PerformFlyManeuverCheck(20, "Turn 180°")) break;
                    }
                }

                if (moveVector.Y > 0)
                {
                    float ascentAngle = Mathf.RadToDeg(new Vector3(moveVector.X, 0, moveVector.Z).AngleTo(moveVector));
                    if (ascentAngle > 45)
                    {
                        if (!PerformFlyManeuverCheck(20, "Fly up > 45°")) break;
                    }
                }
            }

            if (dy < 0 && Mathf.Abs(dy * GridManager.Instance.nodeDiameter) > myStats.Template.Reach && !isFlying)
            {
                if (!myStats.MyEffects.HasCondition(Condition.Incorporeal))
                {
                    HandleFall(previousNode, currentNode);
                    break;
                }
            }


            if (dy > 1 || (currentNode.terrainType == TerrainType.Ground && GridManager.Instance.GetEffectiveLightLevel(currentNode) > 0)) // Checking jump validity implicitly
            {
                GD.Print($"{myStats.Name} jumps.");
            }

            CheckForHazardsAt(waypoint);

            // Movement Lerp Loop
            float t = 0;
            Vector3 startP = body.GlobalPosition;
            while (t < 1.0f)
            {
                if (cancelMove) break;
                // Godot Process Delta isn't available in standard Task loop easily
                // Use a small delay to simulate frame steps or integrate with _Process
                // Here we simulate loop:
                t += (MoveSpeed * 0.016f) / startP.DistanceTo(waypoint); 
                body.GlobalPosition = startP.Lerp(waypoint, t);
                await Task.Delay(16); // ~60fps wait
            }
            body.GlobalPosition = waypoint;

            if (myStats.CurrentGrappleState != null && myStats.CurrentGrappleState.Controller == myStats)
            {
                myStats.CurrentGrappleState.Target.GlobalPosition = waypoint;
            }
            previousNode = currentNode;
            
            foreach (var combatant in TurnManager.Instance.GetAllCombatants())
            {
                combatant.GetNodeOrNull<CombatStateController>("CombatStateController")?.OnCreatureMoved(myStats);
            }
        }
        isMoving = false;
    }

    private bool CheckForCollision(GridNode fromNode, GridNode toNode)
    {
        if (fromNode.terrainType == TerrainType.Air && (toNode.terrainType == TerrainType.Solid || toNode.terrainType == TerrainType.Canopy))
        {
            GD.PrintRich($"[color=red]{myStats.Name} has collided![/color]");
            if (myStats.Template.HasWings && !PerformFlyManeuverCheck(25, "Avoid falling"))
            {
                HandleFall(fromNode, toNode, true);
            }
            return true; 
        }
        return false;
    }

    private bool PerformFlyManeuverCheck(int dc, string maneuverName)
    {
		 // Helpless/Paralyzed creatures cannot make skill checks and cannot flap wings.
        if (myStats.MyEffects.HasCondition(Condition.Helpless) || myStats.MyEffects.HasCondition(Condition.Paralyzed))
        {
            GD.PrintRich($"[color=red]{myStats.Name} is helpless and cannot fly! Auto-fail {maneuverName}.[/color]");
            return false;
        }
        int flyCheck = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Fly);
        if (flyCheck >= dc)
        {
            GD.Print($"{myStats.Name} succeeds at {maneuverName}.");
            return true;
        }
        else
        {
            GD.PrintRich($"[color=red]{myStats.Name} fails {maneuverName}.[/color]");
            return false;
        }
    }

    private void HandleFall(GridNode fromNode, GridNode toNode, bool fromFailedFlyCheck = false)
    {
        int fallDistanceY = fromNode.gridY - toNode.gridY;
        float fallDistanceFeet = fallDistanceY * GridManager.Instance.nodeDiameter;

        GD.Print($"{myStats.Name} falls {fallDistanceFeet} feet!");

        if (myStats.Template.Speed_Fly > 0 && !fromFailedFlyCheck)
        {
            if (PerformFlyManeuverCheck(10, "Negate Falling Damage"))
            {
                GD.Print($"...negates damage!");
                return;
            }
        }

        if (toNode.terrainType == TerrainType.Water)
        {
            float waterDepth = (toNode.waterDepth + 1) * GridManager.Instance.nodeDiameter;
            int dc = 15 + 5 * Mathf.FloorToInt(fallDistanceFeet / 50f);
            
            float requiredDepth = 10f * Mathf.Ceil(fallDistanceFeet / 30f);
            if (waterDepth < requiredDepth && waterDepth < 30f)
            {
                dc += 5;
                fallDistanceFeet += 30f;
                GD.Print($"...shallow water! DC {dc}.");
            }

            int acrobaticsRoll = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Acrobatics);
            if (acrobaticsRoll >= dc) return;
        }
        else 
        {
            int acrobaticsRoll = Dice.Roll(1, 20) + myStats.GetSkillBonus(SkillType.Acrobatics);
            if (acrobaticsRoll >= 15)
            {
                fallDistanceFeet = Mathf.Max(0, fallDistanceFeet - 10f);
                GD.Print("...softens fall by 10 feet.");
            }
        }

        if (fallDistanceFeet > 0)
        {
            int diceToRoll = Mathf.Min(20, Mathf.FloorToInt(fallDistanceFeet / 10f));
            int fallDamage = Dice.Roll(diceToRoll, 6);

            if (toNode.terrainType == TerrainType.Water)
            {
                int nonlethalDice = Mathf.Min(2, diceToRoll);
                int nonlethalDamage = Dice.Roll(nonlethalDice, 3);
                myStats.TakeNonlethalDamage(nonlethalDamage);
                fallDamage -= nonlethalDamage;
            }

            if (fallDamage > 0) myStats.TakeDamage(fallDamage, "Falling");

            var proneEffect = GD.Load<StatusEffect_SO>("res://Data/StatusEffects/Prone_Effect.tres");
            if(proneEffect != null) myStats.MyEffects.AddEffect((StatusEffect_SO)proneEffect.Duplicate(), myStats);
        }
    }

    private void CheckForHazardsAt(Vector3 position)
    {
        var spaceState = GetParent<Node3D>().GetWorld3D().DirectSpaceState;
        var shape = new SphereShape3D { Radius = 0.5f };
        var query = new PhysicsShapeQueryParameters3D { Shape = shape, Transform = new Transform3D(Basis.Identity, position), CollisionMask = 4 }; // Hazard Mask
        var results = spaceState.IntersectShape(query);

        foreach (var dict in results)
        {
            var col = (Node3D)dict["collider"];
            var hazard = col.GetNodeOrNull<EnvironmentalHazard>("EnvironmentalHazard") ?? col as EnvironmentalHazard;

            if (hazard != null && !triggeredHazardsThisMove.Contains(hazard))
            {
                hazard.ApplyEffect(myStats);
                triggeredHazardsThisMove.Add(hazard);
            }
        }
    }
    
    public async Task RunAsync(Vector3 destination)
    {
        if (myStats == null) return;

        bool hasRunFeat = myStats.HasFeat("Run");
        float multiplier = hasRunFeat ? 5f : 4f;
        
        if (myStats.Template.Speed_Land_Armored < myStats.Template.Speed_Land)
        {
            multiplier = hasRunFeat ? 4f : 3f;
        }
if (myStats.Template.SpecialQualities != null && myStats.Template.SpecialQualities.Contains("Gallop"))
        {
            multiplier = 6f;
        }
        float runBudget = GetEffectiveMovementSpeed() * multiplier;
        var body = GetParent<Node3D>();
        
        List<Vector3> path = Pathfinding.Instance.FindPath(myStats, body.GlobalPosition, destination);
        if (path != null && (path.Count * 5f) <= runBudget)
        {
            myStats.GetNode<ActionManager>("ActionManager").IsRunning = true;
			Vector3 runDirection = path.Count > 0 ? (path[^1] - body.GlobalPosition).Normalized() : body.GlobalBasis.Z;
			SoundSystem.EmitCreatureActionSound(myStats, SoundActionType.Run, isSneaking: false, durationSeconds: Mathf.Max(1.0f, path.Count * 0.2f), direction: runDirection);
            await MoveAlongPathAsync(path);
        }
    }
}
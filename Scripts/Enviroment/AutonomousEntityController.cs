using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class AutonomousEntityController : Node3D
{
    public string EntityGroupId { get; private set; } 
    public CreatureStats Caster { get; private set; }
    public CreatureStats CurrentTarget { get; private set; }
    
    private Ability_SO sourceSpell;
    private Ability_SO payloadAbility;

    private int casterLevel;
    private int attackBonus;
    private float moveSpeed;
    private bool canFly;
    private bool usesAttackRoll;
    private bool allowIterativeAttacks;

    private float durationRounds;
    private int roundsActive = 0;
    private bool targetSwitchedThisRound = false;
    private HashSet<CreatureStats> targetsCheckedForSR = new HashSet<CreatureStats>();
    private float realTimeActionTimer = 0f;

    public void Initialize(string groupId, CreatureStats caster, CreatureStats initialTarget, Ability_SO source, Ability_SO payload, int cl, int attackBonus, float speed, bool fly, bool hasAttackRoll, bool iteratives)
    {
        this.EntityGroupId = groupId;
        this.Caster = caster;
        this.CurrentTarget = initialTarget;
        this.sourceSpell = source;
        this.payloadAbility = payload;
        this.casterLevel = cl;
        this.attackBonus = attackBonus;
        this.moveSpeed = speed;
        this.canFly = fly;
        this.usesAttackRoll = hasAttackRoll;
        this.allowIterativeAttacks = iteratives;

        this.durationRounds = cl;

        AddToGroup("AutonomousEntities");
        GD.PrintRich($"[color=purple]An autonomous entity ({EntityGroupId}) appears and targets {initialTarget?.Name ?? "no one"}.[/color]");
    }

    public void Redirect(CreatureStats newTarget)
    {
        if (CurrentTarget != newTarget)
        {
            CurrentTarget = newTarget;
            targetSwitchedThisRound = true;
            GD.PrintRich($"[color=purple]{EntityGroupId} redirected to {newTarget.Name}.[/color]");
        }
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(Caster) || Caster.CurrentHP <= 0)
        {
            QueueFree();
            return;
        }

        bool inTravelPhase = TurnManager.Instance == null || TurnManager.Instance.GetAllCombatants().Count == 0;

        if (inTravelPhase)
        {
            durationRounds -= (float)delta / 6f;
            if (durationRounds <= 0)
            {
                QueueFree();
                return;
            }

            if (CurrentTarget != null && GodotObject.IsInstanceValid(CurrentTarget))
            {
                Vector3 toTarget = CurrentTarget.GlobalPosition - GlobalPosition;
                if (!canFly) toTarget.Y = 0;

                if (toTarget.Length() > 2f)
                {
                    GlobalPosition += toTarget.Normalized() * moveSpeed * (float)delta;
                }

                realTimeActionTimer += (float)delta;
                if (realTimeActionTimer >= 6f)
                {
                    realTimeActionTimer = 0f;
                    _ = ExecutePayload();
                }
            }
        }
    }

    public async Task OnCasterTurnStart()
    {
        durationRounds--;
        if (durationRounds <= 0)
        {
            GD.Print($"{EntityGroupId} duration expired.");
            QueueFree();
            return;
        }

        float maxRange = sourceSpell.Range.GetRange(casterLevel);
        bool isValidTarget = CurrentTarget != null && GodotObject.IsInstanceValid(CurrentTarget) && CurrentTarget.CurrentHP > -CurrentTarget.Template.Constitution;
        bool inRange = isValidTarget && Caster.GlobalPosition.DistanceTo(CurrentTarget.GlobalPosition) <= maxRange;
        bool inSight = isValidTarget && LineOfSightManager.GetVisibility(Caster, CurrentTarget).HasLineOfSight;

        if (!isValidTarget || !inRange || !inSight)
        {
            CurrentTarget = null;
            GlobalPosition = Caster.GlobalPosition + new Vector3(2f, 2f, 0f);
            targetSwitchedThisRound = false;
            roundsActive++;
            return;
        }

        GlobalPosition = CurrentTarget.GlobalPosition + new Vector3(0f, canFly ? 2f : 0f, 1f);
        await ExecutePayload();
        
        roundsActive++;
        targetSwitchedThisRound = false;
    }

    private async Task ExecutePayload()
    {
        if (CurrentTarget == null || CurrentTarget.CurrentHP <= -CurrentTarget.Template.Constitution) return;

        if (sourceSpell.AllowsSpellResistance && CurrentTarget.Template.SpellResistance > 0 && !targetsCheckedForSR.Contains(CurrentTarget))
        {
            targetsCheckedForSR.Add(CurrentTarget);
            int srRoll = Dice.Roll(1, 20) + casterLevel;
            if (srRoll < CurrentTarget.Template.SpellResistance)
            {
                GD.PrintRich($"[color=red]{EntityGroupId} fails SR check against {CurrentTarget.Name} and is dispelled![/color]");
                QueueFree();
                return;
            }
        }

        int totalAttacks = 1;
        if (allowIterativeAttacks && roundsActive > 0 && !targetSwitchedThisRound)
        {
            totalAttacks = 1 + ((Mathf.Max(1, attackBonus) - 1) / 5);
        }

        int currentAttackBonus = attackBonus;
        var context = new EffectContext { Caster = Caster, PrimaryTarget = CurrentTarget, AllTargetsInAoE = new Godot.Collections.Array<CreatureStats> { CurrentTarget } };

        for (int i = 0; i < totalAttacks; i++)
        {
            if (CurrentTarget == null || CurrentTarget.CurrentHP <= -CurrentTarget.Template.Constitution) break;

            bool hitSuccess = true;

            if (usesAttackRoll)
            {
                int roll = RollManager.Instance.MakeD20Roll(Caster);
                int totalAttack = roll + currentAttackBonus;
                var visibility = LineOfSightManager.GetVisibility(Caster, CurrentTarget);
                int targetAC = CombatCalculations.CalculateFinalAC(CurrentTarget, false, visibility.CoverBonusToAC, Caster);

                hitSuccess = (totalAttack >= targetAC && roll != 1) || roll == 20;

                if (hitSuccess && visibility.ConcealmentMissChance > 0)
                {
                    if (Dice.Roll(1, 100) <= visibility.ConcealmentMissChance) hitSuccess = false;
                }
                
                GD.Print($"{EntityGroupId} attacks! Roll {totalAttack} vs AC {targetAC}. {(hitSuccess ? "Hit!" : "Miss.")}");
            }

            if (hitSuccess)
            {
                var saveResults = new Dictionary<CreatureStats, bool>();

                if (payloadAbility.SavingThrow != null && payloadAbility.SavingThrow.SaveType != SaveType.None)
                {
                    int saveDC = payloadAbility.SavingThrow.BaseDC;
                    if (payloadAbility.SavingThrow.IsDynamicDC)
                    {
                        saveDC = 10 + payloadAbility.SpellLevel + Caster.GetAbilityScore(payloadAbility.SavingThrow.DynamicDCStat);
                    }
                    
                    int saveRoll = RollManager.Instance.MakeD20Roll(CurrentTarget);
                    int saveBonus = payloadAbility.SavingThrow.SaveType switch
                    {
                        SaveType.Fortitude => CurrentTarget.GetFortitudeSave(Caster, payloadAbility),
                        SaveType.Reflex => CurrentTarget.GetReflexSave(Caster, payloadAbility),
                        SaveType.Will => CurrentTarget.GetWillSave(Caster, payloadAbility),
                        _ => 0
                    };

                    saveResults[CurrentTarget] = (saveRoll + saveBonus) >= saveDC;
                }

                foreach (var effect in payloadAbility.EffectComponents)
                {
                    effect.ExecuteEffect(context, payloadAbility, saveResults);
                }
            }

            currentAttackBonus -= 5;
            await ToSignal(GetTree().CreateTimer(0.4f), "timeout"); 
        }
    }
}
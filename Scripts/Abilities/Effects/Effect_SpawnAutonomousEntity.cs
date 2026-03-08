using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Effect_SpawnAutonomousEntity : AbilityEffectComponent
{
    [Export] public string EntityIdentifier = "Spiritual Weapon"; 
    [Export] public PackedScene EntityPrefab;
    [Export] public Ability_SO PayloadAbility; 
    
    [ExportGroup("Movement & Properties")]
    [Export] public float MoveSpeedFeet = 20f;
    [Export] public bool CanFly = true;

    [ExportGroup("Attack Rules")]
    [Export] public bool UsesAttackRoll = true;
    [Export] public bool AllowIterativeAttacks = true;
    [Export] public bool UseCasterBAB = true;
    [Export] public AbilityScore AttackStat = AbilityScore.Wisdom; 
    
    [ExportGroup("Overrides (For Soul Slave)")]
    [Export] public int CasterLevelOverride = 0; 
    [Export] public int BABOverride = 0; 

    public override void ExecuteEffect(EffectContext context, Ability_SO ability, Dictionary<CreatureStats, bool> targetSaveResults)
    {
        CreatureStats caster = context.Caster;
        CreatureStats target = context.PrimaryTarget;

        if (caster == null || EntityPrefab == null || PayloadAbility == null) return;

        int cl = CasterLevelOverride > 0 ? CasterLevelOverride : caster.Template.CasterLevel;
        
        int attackBonus = 0;
        if (UsesAttackRoll)
        {
            int bab = UseCasterBAB ? (BABOverride > 0 ? BABOverride : caster.Template.BaseAttackBonus) : 0;
            int statMod = AttackStat switch
            {
                AbilityScore.Wisdom => caster.WisModifier,
                AbilityScore.Charisma => caster.ChaModifier,
                AbilityScore.Intelligence => caster.IntModifier,
                AbilityScore.Strength => caster.StrModifier,
                AbilityScore.Dexterity => caster.DexModifier,
                _ => 0
            };
            attackBonus = bab + statMod;
        }

        var instance = EntityPrefab.Instantiate<Node3D>();
        caster.GetTree().CurrentScene.AddChild(instance);
        instance.GlobalPosition = caster.GlobalPosition + new Vector3(2f, CanFly ? 2f : 0f, 0f);

        var controller = new AutonomousEntityController();
        controller.Name = "AutonomousController";
        instance.AddChild(controller);

        controller.Initialize(EntityIdentifier, caster, target, ability, PayloadAbility, cl, attackBonus, MoveSpeedFeet, CanFly, UsesAttackRoll, AllowIterativeAttacks);

        if (TurnManager.Instance != null && TurnManager.Instance.GetAllCombatants().Count > 0)
        {
            _ = controller.OnCasterTurnStart();
        }
    }

    public override float GetAIEstimatedValue(EffectContext context)
    {
        if (context.PrimaryTarget == null) return 0f;
        return PayloadAbility.EffectComponents[0].GetAIEstimatedValue(context) * 3f;
    }
}
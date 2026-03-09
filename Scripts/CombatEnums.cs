// =================================================================================================
// FILE: CombatEnums.cs
// PURPOSE: Central repository for all combat-related Enums. 
// GLOBAL SCOPE: Accessible by all scripts without 'using' statements.
// =================================================================================================
using Godot;

public enum Condition 
{ 
    None, Shaken, Frightened, Panicked, Stunned, Dazzled, Entangled, Prone, Blinded, Deafened, Sickened, Grappled, Pinned, Helpless, Charging, Concentrating,
    Fatigued, Impeded, LightBlindness, VulnerableToCold, VulnerableToFire, Underwater, IsGrantedFlight,
    ScentMasked, Cursed_Inaction, Asleep,
    Feinted, SoulBound, Sanctuary,
    CompelledApproach, CompelledFlee, CompelledHalt, FumbledItems,
    Bleed, Broken, Confused, Cowering, Dazed, Dead, Disabled, Dying, EnergyDrained, Exhausted, Fascinated, FlatFooted, Incorporeal, Invisible, Nauseated, Paralyzed, Petrified, Stable, Staggered, Unconscious,
    TrueSeeing, ArcaneSight, SensingDeathwatch, SeeInvisibility, FreedomOfMovement,
	SensingChaos, SensingEvil, SensingGood, SensingLaw, Mistsight, Snowsight, Distracted, Gaseous, Silenced,
	WaterWalking, Swallowed, Polymorphed, WhirlwindForm, TrappedInWhirlwind
}

public enum StatToModify 
{
    None, AttackRoll, ArmorClass, MeleeDamage, Perception, SenseMotive, FortitudeSave, ReflexSave, WillSave, 
    Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma, RangedDamage,
    Acrobatics_Skill, Climb_Skill, EscapeArtist_Skill, Stealth_Skill,
    ConcentrationCheck, Initiative, Speed
}

public enum BonusType { Untyped, Morale, Circumstance, Dodge, Deflection, Competence, Sacred, Resistance, Racial, Enhancement, Natural }
public enum SaveCondition { None, AgainstFear, AgainstPoison, AgainstCompulsion, AgainstEnchantments, AgainstIllusions, AgainstArcane, AgainstDeathEffects, AgainstCharm, AgainstDisease, AgainstMindAffecting, AgainstTraps, AgainstNonmagicalDisease,
 AgainstEvil, AgainstGood, AgainstLaw, AgainstChaos, AgainstPositiveEnergy, AgainstNegativeEnergy, AgainstChanneling }
 
public enum SpecialDefense { None, BlockContact_EvilSummoned, GlobeOfInvulnerability_Lesser, GlobeOfInvulnerability_Normal, GlobeOfInvulnerability_Greater, BlockContact_GoodSummoned }

public enum TargetType { Self, SingleAlly, SingleEnemy, Area_FriendOrFoe, Area_EnemiesOnly, Area_AlliesOnly }
public enum AoEShape { Cone, Line, Burst, Emanation, Cylinder }
public enum SaveType { None, Fortitude, Reflex, Will }
public enum SaveEffect { None, Negates, HalfDamage, Partial }
public enum UsageType { AtWill, PerDay, CooldownDice, CooldownDuration }
public enum DCRule { BaseDC, Opposed_CMD }
public enum MagicSchool { Universal, Abjuration, Conjuration, Divination, Enchantment, Evocation, Illusion, Necromancy, Transmutation }
public enum ActionType { Standard, Move, Swift, FullRound, Immediate, Free, NotAnAction }

public enum AbilityCategory { Spell, Feat, SpecialAttack, SkillAction }
public enum SpecialAbilityType { None, Ex, Sp, Su }
public enum AttackRollType { None, Melee, Ranged, Melee_Touch, Ranged_Touch }
public enum CommandWord { None, Approach, Drop, Fall, Flee, Halt, Push, Pull }
public enum TargetableEntityType { CreaturesOnly, ObjectsOnly, CreaturesAndObjects }

public enum AbilityScore { None, Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma }
public enum MovementType { Ground, Flying, Burrowing, Swimming }
public enum CreatureSize { Fine, Diminituve, Tiny, Small, Medium, Large, Huge, Gargantuan, Colossal }
public enum CreatureType { Aberration, Animal, Construct, Dragon, Fey, Humanoid, MagicalBeast, MonstrousHumanoid, Ooze, Outsider, Plant, Undead, Vermin }
public enum SkillType { Acrobatics, Appraise, Balance, Bluff, Climb, Concentration, Craft, DecipherScript, Diplomacy, DisableDevice, Disguise, EscapeArtist, Fly, GatherInformation, HandleAnimal, Heal, Hide, Intimidate, Intimidation, Jump, Knowledge, KnowledgeArcana, KnowledgeDungeoneering, KnowledgeEngineering, KnowledgeGeography, KnowledgeHistory, KnowledgeLocal, KnowledgeNature, KnowledgeNobility, KnowledgePlanes, KnowledgeReligion, Linguistics, Listen, MoveSilently, OpenLock, Perception, Perform, Profession, Ride, Search, SenseMotive, SleightOfHand, Spellcraft, Spot, Stealth, Survival, Swim, Tumble, UseMagicDevice, UseRope, None }
public enum FlyManeuverability { Clumsy, Poor, Average, Good, Perfect }
public enum ManeuverType { BullRush, Grapple, Sunder, Trip, Disarm, DirtyTrick, Drag, Reposition, Steal, Overrun, Any }
public enum ManeuverCondition { None, WhileAttached, WhileCorporeal, OnACharge, ToMaintainGrapple, WhenTripping }

public enum Language
{
    Common, Aboleth, Abyssal, Aklo, Aquan, Auran, Boggard, Celestial, Cyclops, DarkFolk, Draconic,
    DrowSignLanguage, Druidic, Dwarven, Dziriak, Elven, Giant, Gnoll, Gnome, Goblin, Grippli,
    Halfling, Ignan, Infernal, Necril, Orc, Protean, Rougarou, Sphinx, Sylvan, Tengu,
    Terran, Treant, Undercommon, Vegepygmy
}

public enum EnvironmentProperty { Any, Arctic, Cold, Temperate, Warm, Arid, Hot, SevereHeat, Humid, Terrestrial, Aquatic, Coastal, Subterranean, Urban, Forested, Mountainous, OpenTerrain, Planar }

public enum ItemType { Weapon, Armor, Shield, Potion, Gear, SpellComponent }
public enum EquipmentSlot { None, MainHand, OffHand, Shield, Armor, Head, Neck, Shoulders, Chest, Wrists, Hands, Ring1, Ring2, Feet, Belt }
public enum WeaponType { Melee, Thrown, Projectile }
public enum ArmorCategory { None, Light, Medium, Heavy }
public enum WeaponHandedness { Light, OneHanded, TwoHanded }
public enum WeaponMaterial { Normal, Adamantine, ColdIron, Silver, Mithral, Darkwood, Wood }

public enum FeatType { Passive_StatBonus, Activated_Action, CombatOption_Toggle }
public enum ImmunityType { None, MindAffecting, Paralysis, Poison, Sleep, Stun, Disease, DeathEffects, NecromancyEffects, FortitudeSaves_NoDamage, NonlethalDamage, AbilityDamage, AbilityDrain, Fatigue, Exhaustion, EnergyDrain, Polymorph, PhysicalAbilityDamage  }

public enum HealthState { Any, FullHealth, Wounded, Dying }
public enum Relationship { Any, Ally, Enemy }

public enum RangeType { Self, Touch, Close, Medium, Long, Custom }

// From Weather_SO
public enum WindStrength 
{
    None,       // Calm, no effect.
    Light,      // A gentle breeze, likely no gameplay penalty.
    Moderate,   // A steady wind, could apply minor penalties.
    Strong,     // A gale that significantly impedes movement and attacks.
    Severe      // A storm-force wind, major penalties and potential damage.
}

// From TacticalTag_SO
public enum TacticalRole { Buff_Offensive, Buff_Defensive, Debuff_Offensive, Debuff_Defensive, BattlefieldControl, Healing }
public enum CounterStrategy { None, Counters_Fear, Counters_Darkness, Exploits_LightSensitivity, Counters_Poison, Counters_Evil, Counters_MindControl, Counters_SummonedCreatures, Counters_NormalVision }
public enum TerrainType { Solid, Ground, Air, Water, Canopy, Ice }
public enum TimeOfDay { Dawn, Day, Dusk, Night }
public enum MoonPhase { NewMoon, Crescent, Quarter, Gibbous, FullMoon }
public enum AuraStrength { Dim, Faint, Moderate, Strong, Overwhelming }
public enum FeedbackType
{
KilledTarget,
DamagedHighestThreat,
WeaponBypassedDR, // Positive feedback for using a weapon that overcame DR.
WeaponHitVulnerability, // Strong positive feedback for exploiting a damage type weakness.
WeaponWasResisted, // Negative feedback for using a resisted damage type.
WeaponWasImmune, // Strong negative feedback for using an ineffective damage type.
MissedHighAC,
AchievedFlankAndHit,
AvoidedDamageByStayingAtRange,
DebuffedThreatAndTheyMissed,
HealedAllyWhoThenGotAKill,
UsedBuffAndAllyHit,
SurvivedTurnUsingDefense,
HidingSpotSucceeded, // (New) Feedback for when hiding prevents an attack.
HidingSpotFailed // (New) Feedback for when the AI tries to hide but is found and attacked anyway.
}
public enum GrappleSubAction { Pin, Damage, Move }
public enum HeatSeverity { Hot, Severe, Extreme }
public enum PlayerTurnState
{
AwaitingInput,
SuggestingAllyTurn,
AwaitingCommandChoice,
SelectingMoveTarget,
SelectingNormalMoveTarget,
SelectingFlybyStandardAction,
SelectingFlybyAttackTarget,
SelectingFlybyAttackDestination,
SelectingAcrobaticMoveHalfSpeedTarget,
SelectingAcrobaticMoveFullSpeedTarget,
SelectingAbilityTarget,
PlacingIllusion
}
/// <summary>
/// Used by CombatMagic to classify the reaction opportunity.
/// </summary>
public enum ReactionType { Spellcraft, KnowledgeArcana, Counterspell }
public enum EffectTag { None, Curse, Disease, Poison, Fear, MindAffecting, Enchantment, Transmutation }
public enum BehaviorTag { Unknown, Aggressive, Predatory, Defensive, Control, Support, Escape, Balanced }
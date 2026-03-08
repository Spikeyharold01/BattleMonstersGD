using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// =================================================================================================
// FILE: CombatMagic.cs
// PURPOSE: Handles spell resolution, targeting, counterspells, and illusion interactions.
// GODOT PORT: Full fidelity using async/await for timing.
// =================================================================================================
public static class CombatMagic
{
    /// <summary>
    /// A universal, data-driven method to resolve any ability or spell.
    /// It reads the "recipe" from the Ability_SO's effect components and executes them.
    /// </summary>
    public static async Task ResolveAbility(CreatureStats caster, CreatureStats primaryTarget, Node3D targetObject, Vector3 aimPoint, Ability_SO ability, bool isMythicCast, CommandWord command = CommandWord.None)
    {
        if (caster == null || ability == null) return;

        CreatureStats trueCaster = caster;
        Vector3 originPoint = caster.GlobalPosition;

        // Rule: Spells can originate from the projected image.
        if (caster.IsProjectedImage)
        {
            trueCaster = caster.Caster; // The real caster is stored on the image.
            originPoint = caster.GlobalPosition; // The origin is the image's location.
            GD.PrintRich($"[color=purple]Spell '{ability.AbilityName}' is originating from {caster.Name}, but cast by {trueCaster.Name}.[/color]");
            
            // Rule: Caster must have line of effect to the image.
            if (!LineOfSightManager.HasLineOfEffect(trueCaster, trueCaster.GlobalPosition, originPoint))
            {
                GD.PrintRich($"[color=yellow]Spell fizzles because {trueCaster.Name} no longer has Line of Effect to their image.[/color]");
                return;
            }
        }

        GD.PrintRich($"[color=yellow]{trueCaster.Name} uses ability: {ability.AbilityName}[/color]");

 // Hearing integration: verbal components and explicit audible abilities create sound signatures.
        bool hasVerbalComponent = ability.Components != null && ability.Components.HasVerbal;
        bool emitsAudibleAction = ability.EmitsAudibleAction;
        bool requiresSoundOutput = hasVerbalComponent || emitsAudibleAction;
        if (requiresSoundOutput)
        {
            bool casterSilenced = HasSilenceEffect(trueCaster, includeMythic: true);
            if (casterSilenced && !CanBypassSilenceFromMythicDispelConfig(ability, isMythicCast))
            {
                GD.PrintRich($"[color=orange]{trueCaster.Name} cannot use {ability.AbilityName} while silenced.[/color]");
                return;
            }

            if (ability.BreakStealthOnAudibleAction || hasVerbalComponent)
            {
                string noiseReason = hasVerbalComponent ? "verbal spellcasting" : "audible ability use";
                trueCaster.GetNodeOrNull<StealthController>("StealthController")?.BreakStealthFromNoise(noiseReason);
            }

            SoundActionType actionSoundType = emitsAudibleAction ? ability.AudibleActionType : SoundActionType.Cast;
            float actionSoundDuration = ability.AudibleDurationSeconds > 0f ? ability.AudibleDurationSeconds : 1.5f;
            SoundSystem.EmitCreatureActionSound(trueCaster, actionSoundType, isSneaking: false, durationSeconds: actionSoundDuration);
        }

        // --- NEW: SANCTUARY / INVISIBILITY BREAK LOGIC ---
        // Rule: "The subject cannot attack without breaking the spell."
        // We define "Attack" as any spell that targets an enemy or deals damage.
        bool isOffensive = (ability.TargetType == TargetType.SingleEnemy || 
                            ability.TargetType == TargetType.Area_EnemiesOnly || 
                            ability.EffectComponents.Any(e => e is DamageEffect));

        if (isOffensive)
        {
            // Iterate backwards to safely remove effects while looping
            for (int i = trueCaster.MyEffects.ActiveEffects.Count - 1; i >= 0; i--)
            {
                var effect = trueCaster.MyEffects.ActiveEffects[i];
                
                // Break Sanctuary
                if (effect.EffectData.ConditionApplied == Condition.Sanctuary)
                {
                    GD.Print($"{trueCaster.Name} casts an offensive spell, breaking Sanctuary.");
                    trueCaster.MyEffects.RemoveEffect(effect.EffectData.EffectName);
                }
                // Break Invisibility
                else if (effect.EffectData.ConditionApplied == Condition.Invisible && effect.EffectData.BreaksOnAttack)
                {
                    GD.Print($"{trueCaster.Name} casts an offensive spell, breaking Invisibility.");
                    trueCaster.MyEffects.RemoveEffect(effect.EffectData.EffectName);
                }
            }
        }
        // -------------------------------------------------

        // --- WHIRLWIND / DEBRIS CONCENTRATION CHECK ---
        int envConcentrationDC = 0;
        if (trueCaster.MyEffects.HasCondition(Condition.TrappedInWhirlwind)) envConcentrationDC = 15 + ability.SpellLevel;
        
        var casterNode = GridManager.Instance?.NodeFromWorldPoint(trueCaster.GlobalPosition);
        if (casterNode != null && casterNode.environmentalTags.Contains("DebrisCloud")) envConcentrationDC = 15 + ability.SpellLevel;

        if (envConcentrationDC > 0)
        {
            GD.PrintRich($"[color=orange]{trueCaster.Name} is in violent winds/debris and must concentrate to cast![/color]");
            if (!CombatManager.CheckConcentration(trueCaster, envConcentrationDC)) return; // Spell lost
        }

        // --- REACTION PHASE ---
        // Before resolving the spell, check for reactions from ALL combatants who can perceive the casting.
        bool isCountered = false;
        if (ability.SpellLevel > 0)
        {
            foreach (var potentialReactor in TurnManager.Instance.GetAllCombatants())
            {
                if (potentialReactor == trueCaster) continue;

                var visibility = CombatCalculations.GetVisibilityFromPoint(potentialReactor.GlobalPosition, originPoint);
                if (!visibility.HasLineOfSight) continue;

                // Create a temporary list to hold potential reaction Tasks for this reactor
                var reactionTasks = new List<Task>();

                // OPPORTUNITY 1: SPELLCRAFT
                int spellcraftRanks = potentialReactor.Template.SkillRanks?.Find(s => s.Skill == SkillType.Spellcraft)?.Ranks ?? 0;
                if (spellcraftRanks > 0)
                {
                    reactionTasks.Add(HandleReaction(potentialReactor, trueCaster, ability, ReactionType.Spellcraft));
                }

                // OPPORTUNITY 2: KNOWLEDGE (ARCANA) - Only if targeted
                // We need to pre-calculate targets to check this
                var tempContext = new EffectContext { Caster = trueCaster, PrimaryTarget = primaryTarget, AimPoint = aimPoint, AllTargetsInAoE = new Godot.Collections.Array<CreatureStats>() };
                PopulateAbilityTargets(tempContext, ability, originPoint); 
                if (tempContext.AllTargetsInAoE.Contains(potentialReactor))
                {
                    int knowledgeRanks = potentialReactor.Template.SkillRanks?.Find(s => s.Skill == SkillType.KnowledgeArcana)?.Ranks ?? 0;
                    if (knowledgeRanks > 0)
                    {
                        reactionTasks.Add(HandleReaction(potentialReactor, trueCaster, ability, ReactionType.KnowledgeArcana));
                    }
                }

                // OPPORTUNITY 3: COUNTERSPELL
                var dispelMagic = potentialReactor.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName.Contains("Dispel Magic"));
                if (dispelMagic != null && potentialReactor.MyUsage.HasUsesRemaining(dispelMagic))
                {
                    reactionTasks.Add(HandleReaction(potentialReactor, trueCaster, ability, ReactionType.Counterspell));
                }

                // Execute all potential reactions for this one reactor
                foreach (var reactionTask in reactionTasks)
                {
                    await reactionTask;
                    // Check if a counterspell succeeded
                    if (TurnManager.Instance.WasSpellCountered(trueCaster, ability))
                    {
                        isCountered = true;
                        break; // Stop further reactions if the spell is countered
                    }
                }
                if (isCountered) break; // Stop checking other reactors
            }
        }

        if (isCountered)
        {
            GD.Print($"The spell '{ability.AbilityName}' was countered and fizzles!");
            return; // End the entire ability resolution.
        }

        // --- PREPARE EFFECT CONTEXT ---
        var context = new EffectContext
        {
            Caster = trueCaster, // The context always uses the TRUE caster for calcs like DC.
            PrimaryTarget = primaryTarget,
            TargetObject = targetObject,
            AimPoint = aimPoint,
            SelectedCommand = command,
            IsMythicCast = isMythicCast,
            AllTargetsInAoE = new Godot.Collections.Array<CreatureStats>()
        };

        // --- TARGET GATHERING ---
        if (ability.TargetType == TargetType.Self) {
            context.AllTargetsInAoE.Add(trueCaster);
        } else if (ability.TargetType == TargetType.SingleAlly || ability.TargetType == TargetType.SingleEnemy) {
            if(primaryTarget != null && originPoint.DistanceTo(primaryTarget.GlobalPosition) <= ability.Range.GetRange(trueCaster))
            {
                context.AllTargetsInAoE.Add(primaryTarget);
            }
        } else 
        { // This covers all Area effects
            // First, check if the center of the AoE is within the spell's range from the origin.
            if (originPoint.DistanceTo(aimPoint) <= ability.Range.GetRange(trueCaster))
            {
                var potentialTargets = TurnManager.Instance.GetAllCombatants();
                foreach (var p_target in potentialTargets)
                {
                    if (IsTargetInArea(ability, originPoint, aimPoint, p_target.GlobalPosition))
                    {
                        context.AllTargetsInAoE.Add(p_target);
                    }
                }
            }
        }
        
        // --- FILTERING LOOP (Reverse Order) ---
        for (int i = context.AllTargetsInAoE.Count - 1; i >= 0; i--)
        {
            var target = context.AllTargetsInAoE[i];
			
			var banishmentController = target.GetNodeOrNull<TemporaryBanishmentController>("TemporaryBanishmentController");
            if (banishmentController != null && banishmentController.IsBanished)
            {
                context.AllTargetsInAoE.RemoveAt(i);
                continue;
            }


 // 1. Language-Dependent Check
            if (ability.IsLanguageDependent)
            {
                bool targetIsSilenced = target.MyEffects != null && target.MyEffects.HasCondition(Condition.Silenced);
                if (targetIsSilenced || !trueCaster.SharesLanguageWith(target))
                {
                    GD.PrintRich($"[color=orange]{ability.AbilityName} fails against {target.Name} (Language-dependent/Silenced).[/color]");
                    context.AllTargetsInAoE.RemoveAt(i);
                    continue;
                }
            }

            // 2. Spell Resistance Check
            if (ability.AllowsSpellResistance && target.Template.SpellResistance > 0)
            {
                int casterLevelCheck = Dice.Roll(1, 20) + trueCaster.Template.CasterLevel;
                if (casterLevelCheck < target.Template.SpellResistance)
                {
                    GD.PrintRich($"[color=orange]{target.Name}'s Spell Resistance ({target.Template.SpellResistance}) resisted {ability.AbilityName}! (Check: {casterLevelCheck})[/color]");
                    CombatMemory.RecordSRSuccess(target);
                    context.AllTargetsInAoE.RemoveAt(i);
                    continue;
                }
                else
                {
                    GD.Print($"{target.Name}'s Spell Resistance ({target.Template.SpellResistance}) was overcome. (Check: {casterLevelCheck})");
                }
            }
			 // --- ADD THIS BLOCK ---
            // 2b. Globe of Invulnerability Check
            var targetStatusCtrl = target.GetNodeOrNull<StatusEffectController>("StatusEffectController");
            if (targetStatusCtrl != null)
            {
                int maxLevelBlocked = -1;
                if (targetStatusCtrl.HasSpecialDefense(SpecialDefense.GlobeOfInvulnerability_Lesser)) maxLevelBlocked = 3;
                else if (targetStatusCtrl.HasSpecialDefense(SpecialDefense.GlobeOfInvulnerability_Normal)) maxLevelBlocked = 4;
                else if (targetStatusCtrl.HasSpecialDefense(SpecialDefense.GlobeOfInvulnerability_Greater)) maxLevelBlocked = 9; // Ad-hoc definition if needed

                if (maxLevelBlocked >= 0 && ability.SpellLevel >= 0 && ability.SpellLevel <= maxLevelBlocked)
                {
                    GD.PrintRich($"[color=cyan]{target.Name}'s Globe of Invulnerability suppresses {ability.AbilityName} (Level {ability.SpellLevel}).[/color]");
                    context.AllTargetsInAoE.RemoveAt(i);
                    continue;
                }
            }
            // ----------------------

            // 3. NEW: Sanctuary Check
            // Only targeted spells trigger Sanctuary ("directly attack"). Area spells do not.
            bool isDirectAttack = ability.TargetType == TargetType.SingleEnemy;
            if (isDirectAttack && !CombatManager.CheckSanctuary(trueCaster, target))
            {
                // Note: CheckSanctuary handles logging and saving throw resolution.
                context.AllTargetsInAoE.RemoveAt(i);
                continue;
            }
			 // 4. NEW: Incorporeal Spell Avoidance
            bool causesDamage = ability.EffectComponents.Any(e => e is DamageEffect);
            if (target.MyEffects != null && target.MyEffects.HasCondition(Condition.Incorporeal) && !causesDamage)
            {
                if (Dice.Roll(1, 100) > 50)
                {
                    GD.PrintRich($"[color=gray]{target.Name}'s incorporeal nature causes {ability.AbilityName} to pass harmlessly through them.[/color]");
                    context.AllTargetsInAoE.RemoveAt(i);
                    continue;
                }
            }
        }

        if (context.AllTargetsInAoE.Count == 0 && targetObject == null)
        {
            GD.Print("...but the ability affected no targets.");
            return;
        }

        // --- RESOLVE SAVES & EFFECTS ---
        var targetSaveResults = new Dictionary<CreatureStats, bool>();
        foreach (var target in context.AllTargetsInAoE)
        {
            // Notify the target (triggers Tortured Mind etc.)
            target.NotifyTargetedBySpell(trueCaster, ability);
            bool saved = false;

            if (ability.MinimumTargetIntelligenceToAffect > 0 && target.Template != null && target.Template.Intelligence < ability.MinimumTargetIntelligenceToAffect)
            {
                GD.Print($"{target.Name} is unaffected by {ability.AbilityName} due to insufficient Intelligence.");
                targetSaveResults[target] = true;
                continue;
            }

            if (ability.SavingThrow.SaveType != SaveType.None)
            {
                // Trait Immunity Checks
                if (ability.SavingThrow.SaveType == SaveType.Fortitude)
                {
                    bool doesDamage = ability.EffectComponents.Any(e => e is DamageEffect);
                    if (doesDamage && target.HasImmunity(ImmunityType.FortitudeSaves_Damage))
                    {
                        GD.Print($"{target.Name} is immune to Fortitude saves for damaging effects.");
                        continue; 
                    }
                    if (!doesDamage && target.HasImmunity(ImmunityType.FortitudeSaves_NoDamage))
                    {
                        GD.Print($"{target.Name} is immune to Fortitude saves for non-damaging effects.");
                        continue; 
                    }
                }

                // Calculate DC
                int dc = ability.SavingThrow.BaseDC;
                if (ability.SavingThrow.IsDynamicDC)
                {
                    int statMod = 0;
                    switch (ability.SavingThrow.DynamicDCStat)
                    {
                        case AbilityScore.Charisma: statMod = trueCaster.ChaModifier; break;
                        case AbilityScore.Wisdom: statMod = trueCaster.WisModifier; break;
                        case AbilityScore.Constitution: statMod = trueCaster.ConModifier; break;
                        case AbilityScore.Intelligence: statMod = trueCaster.IntModifier; break;
                    }
                    // Rule: Spell DC = 10 + spell level + stat mod
                    // Note: Stick to existing logic to avoid breaking balance:
                    dc = 10 + Mathf.FloorToInt(trueCaster.Template.CasterLevel / 2f) + statMod + ability.SavingThrow.DynamicDCBonus;
                }
                else if (ability.SavingThrow.IsSpecialAbilityDC)
                {
                    int statMod = 0; 
                    // Rule: Special Ability DC = 10 + 1/2 HD + stat mod
					switch (ability.SavingThrow.DynamicDCStat)
                    {
                        case AbilityScore.Charisma: statMod = trueCaster.ChaModifier; break;
                        case AbilityScore.Wisdom: statMod = trueCaster.WisModifier; break;
                        case AbilityScore.Constitution: statMod = trueCaster.ConModifier; break;
                        case AbilityScore.Intelligence: statMod = trueCaster.IntModifier; break;
                        case AbilityScore.Strength: statMod = trueCaster.StrModifier; break;
                        case AbilityScore.Dexterity: statMod = trueCaster.DexModifier; break;
                    }
                    int hitDiceCount = 0;
                    if (!string.IsNullOrEmpty(trueCaster.Template.HitDice))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trueCaster.Template.HitDice, @"^\d+");
                        if (match.Success) int.TryParse(match.Value, out hitDiceCount);
                    }
                     dc = 10 + Mathf.FloorToInt(hitDiceCount / 2f) + statMod + ability.SavingThrow.DynamicDCBonus;
                }

                int saveRoll = RollManager.Instance.MakeD20Roll(target);
                int saveBonus = 0;
                switch(ability.SavingThrow.SaveType)
                {
                   case SaveType.Fortitude: saveBonus = target.GetFortitudeSave(trueCaster, ability); break;
                   case SaveType.Reflex:    saveBonus = target.GetReflexSave(trueCaster, ability); break;
                   case SaveType.Will:      saveBonus = target.GetWillSave(trueCaster, ability); break;
                }

                if (ability.SaveBonusWhenTargetTypeDiffersFromCaster != 0 && trueCaster?.Template != null && target.Template != null && trueCaster.Template.Type != target.Template.Type)
                {
                    saveBonus += ability.SaveBonusWhenTargetTypeDiffersFromCaster;
                }
                
                if (saveRoll + saveBonus >= dc)
                {
                    saved = true;
                }
                GD.Print($"{target.Name} saving throw vs {ability.AbilityName} (DC {dc}): Rolled {saveRoll+saveBonus}. Success: {saved}");
            }
            targetSaveResults[target] = saved;
		    }
		context.LastSaveResults = targetSaveResults;
		

        // --- EXECUTE COMPONENTS ---
        foreach (var effectComponent in ability.EffectComponents)
        {
            if (effectComponent is DamageEffect)
            {
                CombatMemory.RecordOffensiveAction(trueCaster);
            }
            effectComponent.ExecuteEffect(context, ability, targetSaveResults);
        }

        // --- EXECUTE MYTHIC COMPONENTS ---
        if (isMythicCast && ability.MythicComponents != null)
        {
            GD.PrintRich($"[color=purple]Resolving Mythic effects for {ability.AbilityName}![/color]");
            foreach (var mythicComponent in ability.MythicComponents)
            {
                mythicComponent.ExecuteMythicEffect(context, ability);
            }
            CombatMemory.RecordMythicStatus(trueCaster);
        }
    }
	
	/// <summary>
    /// Resolves an attack roll for a spell or special ability.
    /// </summary>
    /// <returns>True if the attack hits, false otherwise.</returns>
    public static bool ResolveAbilityAttack(CreatureStats attacker, CreatureStats defender, Ability_SO ability)
    {
        bool isTouch = ability.AttackRollType == AttackRollType.Melee_Touch || ability.AttackRollType == AttackRollType.Ranged_Touch;
        bool isRanged = ability.AttackRollType == AttackRollType.Ranged || ability.AttackRollType == AttackRollType.Ranged_Touch;

        int attackBonus = attacker.Template.BaseAttackBonus + (isRanged ? attacker.DexModifier : attacker.StrModifier) + attacker.GetSizeModifier();
        
        if (isRanged)
        {
            attackBonus += CombatAttacks.CalculateFiringIntoMeleePenalty(attacker, defender);
            // TODO: Add range increment penalties for abilities.
        }

        var visibility = LineOfSightManager.GetVisibility(attacker, defender);
        int finalAC = CombatCalculations.CalculateFinalAC(defender, isTouch, visibility.CoverBonusToAC, attacker);

        int roll = RollManager.Instance.MakeD20Roll(attacker);
        int totalAttackRoll = roll + attackBonus;

        GD.Print($"{attacker.Name}'s {ability.AbilityName} attack roll vs {defender.Name}: {totalAttackRoll} vs AC {finalAC} (Touch: {isTouch})");

        return (totalAttackRoll >= finalAC && roll != 1) || roll == 20;
    }
	
	public static bool CheckConcentration(CreatureStats caster, int dc)
    {
        if (caster == null) return false;
        int bonus = caster.GetConcentrationBonus();
        int roll = Dice.Roll(1, 20);
        int total = roll + bonus;
        if (total >= dc)
        {
            GD.PrintRich($"[color=green]{caster.Name} Concentration check SUCCEEDS! (Rolled {total} vs DC {dc})[/color]");
            return true;
        }
        else
        {
            GD.PrintRich($"[color=red]{caster.Name} Concentration check FAILS! (Rolled {total} vs DC {dc}). The spell is lost![/color]");
            return false;
        }
    }
	
    /// <summary>
    /// Public helper to resolve a Dispel Magic counterspell attempt.
    /// </summary>
    public static void ResolveCounterspell(CreatureStats counterspeller, CreatureStats originalCaster, Ability_SO spellToCounter, Ability_SO dispelSpell)
    {
        GD.PrintRich($"[color=red]INTERRUPT![/color] {counterspeller.Name} attempts to counter '{spellToCounter.AbilityName}' with '{dispelSpell.AbilityName}'.");
        counterspeller.MyUsage.ConsumeUse(dispelSpell); // Consume the use of Dispel Magic

        var dispelEffect = dispelSpell.EffectComponents.OfType<Effect_Dispel>().FirstOrDefault();
        int bonus = dispelEffect?.DispelCheckBonus ?? 0; // Get bonus from Greater Dispel, if applicable

        int dispelRoll = Dice.Roll(1, 20) + counterspeller.Template.CasterLevel + bonus;
        int dc = 11 + originalCaster.Template.CasterLevel;

        GD.Print($"Counterspell check: Rolls {dispelRoll} vs DC {dc}.");
        
        if (dispelRoll >= dc)
        {
            GD.PrintRich($"[color=green]Success! The spell '{spellToCounter.AbilityName}' is countered and fails![/color]");
            TurnManager.Instance.SetSpellAsCountered(originalCaster, spellToCounter);
        }
        else
        {
            GD.PrintRich("[color=red]Counterspell attempt failed.[/color] The spell continues.");
        }
    }
	
    /// <summary>
    /// Resolves a Will save to disbelieve an illusion upon interaction.
    /// </summary>
    public static void ResolveIllusionDisbelief(IllusionController illusion, CreatureStats interactor)
    {
        // Rule: True Seeing automatically penetrates illusions.
        if (interactor.MyEffects.HasCondition(Condition.TrueSeeing))
        {
            GD.PrintRich($"[color=cyan]{interactor.Name}'s True Seeing automatically penetrates the {illusion.SourceAbility.AbilityName}![/color]");
            illusion.AddDisbeliever(interactor);
            CombatMemory.RecordDisbelief(interactor, illusion.GetNode<CreatureStats>("CreatureStats"));
            return;
        }
        // Pathfinder Rule: Disbelieving a figment uses a standard Will save DC.
        // DC = 10 + spell level + caster's ability modifier.
        int spellLevel = illusion.SourceAbility.SpellLevel;
        int statMod = 0; 
        switch (illusion.Caster.Template.PrimaryCastingStat)
        {
            case AbilityScore.Intelligence: statMod = illusion.Caster.IntModifier; break;
            case AbilityScore.Charisma: statMod = illusion.Caster.ChaModifier; break;
            default: statMod = illusion.Caster.IntModifier; break; 
        }
        int dc = 10 + spellLevel + statMod;

        int saveRoll = Dice.Roll(1, 20) + interactor.GetWillSave(illusion.Caster, illusion.SourceAbility);

        if (saveRoll >= dc)
        {
            GD.PrintRich($"[color=cyan]{interactor.Name} succeeds the Will save (Roll: {saveRoll} vs DC: {dc}) and disbelieves the {illusion.SourceAbility.AbilityName}![/color]");
            illusion.AddDisbeliever(interactor);
        }
        else
        {
            GD.Print($"{interactor.Name} fails the Will save (Roll: {saveRoll} vs DC: {dc}) and perceives the illusion as real.");
        }
    }

    /// <summary>
    /// Resolves a Will save to disbelieve a Glamer illusion (like Veil).
    /// </summary>
    public static void ResolveGlamerDisbelief(CreatureStats observer, CreatureStats veiledCreature)
    {
        var veilController = veiledCreature.MyVeil;
        if (veilController == null) return;
        
        // Use the same DC logic as other illusions.
        int dc = 10 + veilController.SourceAbility.SpellLevel + veilController.Caster.WisModifier; // Assuming Wiz logic priority for now
        int saveRoll = Dice.Roll(1, 20) + observer.GetWillSave(veilController.Caster, veilController.SourceAbility);
        
        if (saveRoll >= dc)
        {
            GD.PrintRich($"[color=cyan]{observer.Name} disbelieves the Veil on {veiledCreature.Name}![/color]");
            CombatMemory.RecordDisbelief(observer, veiledCreature);
        }
        else
        {
            GD.Print($"{observer.Name} fails to disbelieve the Veil and perceives it as real.");
        }
    }
	
    /// <summary>
    /// A unified helper coroutine to manage the reaction process for a specific creature and skill.
    /// </summary>
    private static async Task HandleReaction(CreatureStats reactor, CreatureStats caster, Ability_SO incomingSpell, ReactionType reactionType)
    {
        // Ask the correct controller (Player or AI) to make a decision.
        var playerController = reactor.GetNodeOrNull<PlayerActionController>("PlayerActionController");
        if (playerController != null)
        {
            playerController.OnRequestReaction(caster, incomingSpell, reactionType.ToString());
        }
        else
        {
            var aiController = reactor.GetNodeOrNull<AIController>("AIController");
            aiController?.OnRequestReaction(caster, incomingSpell, reactionType.ToString());
        }

        // Pause execution and wait for the reaction decision from this specific creature.
        // Requires TurnManager to support Wait Logic.
        await TurnManager.Instance.WaitForReaction(reactor, 5f, (choseToReact) => {
            if (choseToReact)
            {
                // If they react, execute the appropriate identification check.
                if (reactionType == ReactionType.Spellcraft)
                {
                    ResolveSpellcraftCheck(reactor, caster, incomingSpell);
                }
                else if (reactionType == ReactionType.KnowledgeArcana)
                {
                    ResolveKnowledgeArcanaCheck(reactor, caster, incomingSpell);
                }
                else if (reactionType == ReactionType.Counterspell)
                {
                    var dispelMagic = reactor.Template.KnownAbilities.FirstOrDefault(a => a.AbilityName.Contains("Dispel Magic"));
                    if (dispelMagic != null)
                    {
                        ResolveCounterspell(reactor, caster, incomingSpell, dispelMagic);
                    }
                }
            }
        });
    }
	
    /// <summary>
    /// Private helper to resolve the Identify Spell Spellcraft check.
    /// </summary>
    private static void ResolveSpellcraftCheck(CreatureStats identifier, CreatureStats caster, Ability_SO incomingSpell)
    {
        var identifyAbilitySO = new Ability_SO();
        var effect = new IdentifySpellEffect(); // Resource instantiation in C#
        identifyAbilitySO.EffectComponents = new Godot.Collections.Array<AbilityEffectComponent> { effect };
        
        var context = new IdentifySpellContext // Needs to be castable to EffectContext or derived
        { 
            Caster = identifier, 
            PrimaryTarget = caster,
            IncomingSpell = incomingSpell
        };
        
        // This likely requires IdentifySpellEffect to handle the specialized context internally
        // or IdentifySpellContext to inherit EffectContext.
        // Assuming IdentifySpellEffect is robust:
        effect.ExecuteEffect(context, identifyAbilitySO, new Dictionary<CreatureStats, bool>());
    }
	
    /// <summary>
    /// Private helper to resolve the Identify Incoming Spell (Knowledge Arcana) check.
    /// </summary>
    private static void ResolveKnowledgeArcanaCheck(CreatureStats identifier, CreatureStats caster, Ability_SO incomingSpell)
    {
        var identifyEffect = new IdentifyIncomingSpellEffect();
        var context = new EffectContext { Caster = identifier, PrimaryTarget = caster };
        var dummyAbility = new Ability_SO();
        dummyAbility.AbilityName = incomingSpell.AbilityName; // Pass name for logging
        dummyAbility.SpellLevel = incomingSpell.SpellLevel;
        
        identifyEffect.ExecuteEffect(context, dummyAbility, new Dictionary<CreatureStats, bool>());
    }


private static bool HasSilenceEffect(CreatureStats creature, bool includeMythic)
    {
        if (creature?.MyEffects == null) return false;
        if (creature.MyEffects.HasCondition(Condition.Silenced)) return true;

        if (creature.MyEffects.ActiveEffects == null) return false;

        foreach (var effect in creature.MyEffects.ActiveEffects)
        {
            string effectName = effect?.EffectData?.EffectName ?? string.Empty;
            if (!effectName.Contains("silence", System.StringComparison.OrdinalIgnoreCase)) continue;

            bool isMythic = effectName.Contains("mythic", System.StringComparison.OrdinalIgnoreCase);
            if (!includeMythic && isMythic) continue;
            return true;
        }

        return false;
    }

    private static bool CanBypassSilenceFromMythicDispelConfig(Ability_SO ability, bool isMythicCast)
    {
        if (!isMythicCast || ability?.EffectComponents == null) return false;

        foreach (var component in ability.EffectComponents)
        {
            if (component is not Effect_Dispel dispel) continue;
            if (!dispel.RequireMythicCast) continue;
            if (!dispel.AllowsMythicSilenceBypassForVerbalCasting) continue;

            return true;
        }

        return false;
    }
    private static bool IsTargetInArea(Ability_SO ability, Vector3 originPoint, Vector3 aimPoint, Vector3 targetPosition)
    {
        if (ability?.AreaOfEffect == null) return false;

        float effectiveRange = ability.AreaOfEffect.Range;
        float effectiveAngle = ability.AreaOfEffect.Angle;
        float effectiveWidth = ability.AreaOfEffect.Width;
        float effectiveHeight = ability.AreaOfEffect.Height;
        AoEShape effectiveShape = ability.AreaOfEffect.Shape;

        if (ability.AreaOfEffect.UseAlternateWhenCenteredOnCaster)
        {
            float activationDistance = Mathf.Max(0f, ability.AreaOfEffect.AlternateActivationDistanceFeet);
            if (originPoint.DistanceTo(aimPoint) <= activationDistance)
            {
                effectiveShape = ability.AreaOfEffect.AlternateShape;
                effectiveRange = ability.AreaOfEffect.AlternateRange;
                effectiveAngle = ability.AreaOfEffect.AlternateAngle;
                effectiveWidth = ability.AreaOfEffect.AlternateWidth;
                effectiveHeight = ability.AreaOfEffect.AlternateHeight;
                aimPoint = originPoint;
            }
        }

        if (effectiveShape == AoEShape.Cylinder)
        {
            float hDist = new Vector2(aimPoint.X, aimPoint.Z).DistanceTo(new Vector2(targetPosition.X, targetPosition.Z));
            float vDist = targetPosition.Y - aimPoint.Y;
            return hDist <= effectiveRange && vDist >= 0 && vDist <= effectiveHeight;
        }

        if (effectiveShape == AoEShape.Cone)
        {
            Vector3 coneDirection = (aimPoint - originPoint).Normalized();
            if (coneDirection == Vector3.Zero) coneDirection = -Vector3.Forward;

            Vector3 toTarget = targetPosition - originPoint;
            float distance = toTarget.Length();
            if (distance > effectiveRange || distance <= 0.001f) return false;

            float halfAngle = Mathf.Max(1f, effectiveAngle * 0.5f);
            float angle = Mathf.RadToDeg(coneDirection.AngleTo(toTarget.Normalized()));
            return angle <= halfAngle;
        }

        if (effectiveShape == AoEShape.Line)
        {
            float lineWidth = Mathf.Max(1f, effectiveWidth);
            return DistancePointToSegment(targetPosition, originPoint, aimPoint) <= (lineWidth * 0.5f)
                   && originPoint.DistanceTo(targetPosition) <= effectiveRange;
        }

        return aimPoint.DistanceTo(targetPosition) <= effectiveRange;
    }


    private static float DistancePointToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
    {
        Vector3 segment = segmentEnd - segmentStart;
        float segmentLenSq = segment.LengthSquared();
        if (segmentLenSq <= 0.0001f) return point.DistanceTo(segmentStart);

        float t = Mathf.Clamp((point - segmentStart).Dot(segment) / segmentLenSq, 0f, 1f);
        Vector3 projection = segmentStart + (segment * t);
        return point.DistanceTo(projection);
    }

    /// <summary>
    /// Helper method to pre-calculate the targets of an ability for reaction checks.
    /// </summary>
    private static void PopulateAbilityTargets(EffectContext context, Ability_SO ability, Vector3 originPoint)
    {
        if (ability.TargetType == TargetType.Self) {
            context.AllTargetsInAoE.Add(context.Caster);
        } else if (ability.TargetType == TargetType.SingleAlly || ability.TargetType == TargetType.SingleEnemy) {
            if(context.PrimaryTarget != null && originPoint.DistanceTo(context.PrimaryTarget.GlobalPosition) <= ability.Range.GetRange(context.Caster))
            {
                context.AllTargetsInAoE.Add(context.PrimaryTarget);
            }
        } else { // This covers all Area effects
            if (originPoint.DistanceTo(context.AimPoint) <= ability.Range.GetRange(context.Caster))
            {
                var potentialTargets = TurnManager.Instance.GetAllCombatants();
                foreach (var p_target in potentialTargets)
                {
                    if (IsTargetInArea(ability, originPoint, context.AimPoint, p_target.GlobalPosition))
                    {
                        context.AllTargetsInAoE.Add(p_target);
                    }
                }
            }
        }
    }
}
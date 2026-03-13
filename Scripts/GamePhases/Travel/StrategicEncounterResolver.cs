using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed class StrategicResolutionResult
{
    public bool EncounterTriggered;
    public bool HuntedEscalationTriggered;
    public int AiInteractionCount;
	public List<StrategicEntity> EngagingEntities = new List<StrategicEntity>();
}

/// <summary>
/// Strategic-hour orchestration layer.
///
/// This resolver keeps one fully persistent 24x24 strategic map alive for the entire travel session,
/// advances weather and ecology each strategic hour, and only escalates to tactical flow when pressure
/// and detection say that a close encounter has truly formed.
/// </summary>
public sealed class StrategicEncounterResolver
{
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private TravelBiomeQuerySnapshot _snapshot;
    private TravelBiomeMapRuntime _runtime;
    private GamePhaseContext _context;

    // Existing encounter bridge so pre-existing TravelEncounter systems can still register entities.
    private readonly Dictionary<string, StrategicEntity> _encounterEntityLookup = new Dictionary<string, StrategicEntity>();

    public void Initialize(TravelBiomeQuerySnapshot snapshot, TravelBiomeMapRuntime runtime, GamePhaseContext context)
    {
        _snapshot = snapshot;
        _runtime = runtime;
        _context = context;

        if (_snapshot?.Procedural != null && _snapshot.Procedural.Seed != 0)
        {
            _rng.Seed = (ulong)_snapshot.Procedural.Seed + 313u;
        }
        else
        {
            _rng.Randomize();
        }

        EnsureStrategicMapRuntime();
        UpsertPartyEntity();
    }

    public void Shutdown()
    {
        _encounterEntityLookup.Clear();
        _snapshot = null;
        _runtime = null;
        _context = null;
    }

    public void RegisterEncounter(TravelActiveEncounter encounter)
    {
        if (encounter == null || _runtime?.StrategicMap == null)
        {
            return;
        }

        if (_encounterEntityLookup.ContainsKey(encounter.EncounterId))
        {
            return;
        }

        CreatureStats lead = encounter.Members?.FirstOrDefault(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead);
        if (lead?.Template == null)
        {
            return;
        }

        Vector2I tile = _runtime.StrategicMap.PlayerTileCoord;
        StrategicMapTileData tileData = _runtime.StrategicMap.Tiles[tile.X, tile.Y];

        var entity = new StrategicEntity
        {
            EntityId = encounter.EncounterId,
            CreatureDefinition = lead.Template,
            CreatureRuntimeReference = lead,
            TileCoord = tile,
            Hunger = _rng.RandfRange(0.15f, 0.5f),
            InjuryState = 0f,
            HasHomeTile = false,
            HomeTile = tile,
            IsPlayerParty = false
        };

        tileData.StrategicEntities.Add(entity);
        _runtime.StrategicMap.AllEntities.Add(entity);
        _encounterEntityLookup[encounter.EncounterId] = entity;
    }

    public void UnregisterEncounter(string encounterId)
    {
        if (_runtime?.StrategicMap == null || string.IsNullOrWhiteSpace(encounterId))
        {
            return;
        }

        if (!_encounterEntityLookup.TryGetValue(encounterId, out StrategicEntity entity) || entity == null)
        {
            return;
        }

        if (_runtime.StrategicMap.TryGetTile(entity.TileCoord, out StrategicMapTileData tile))
        {
            tile.StrategicEntities.Remove(entity);
        }

        _runtime.StrategicMap.AllEntities.Remove(entity);
        _encounterEntityLookup.Remove(encounterId);
    }

    /// <summary>
    /// Executes one strategic hour tick.
    ///
    /// Expected output:
    /// - Weather manager advances and every strategic rule reads current weather effects.
    /// - Hunger, injuries, movement, and creature intentions update for the entire map.
    /// - AI-vs-AI interactions are resolved in a lightweight way.
    /// - Escalation flag is raised only when the player tile experiences meaningful contact pressure.
    /// </summary>
    public StrategicResolutionResult ResolveStrategicHourTurn()
    {
        EnsureStrategicMapRuntime();
        StrategicEntity party = UpsertPartyEntity();

        var result = new StrategicResolutionResult();
        if (party == null)
        {
            return result;
        }

        WeatherManager.Instance?.OnNewRound();

        foreach (StrategicEntity entity in _runtime.StrategicMap.AllEntities.ToList())
        {
            if (entity == null)
            {
                continue;
            }

            UpdateHungerAndInjury(entity);
            MoveEntity(entity);
        }

        result.AiInteractionCount = ResolveAiVsAiInteractions();

        StrategicDisposition contactDisposition;
        List<StrategicEntity> attackers;
        bool detected = EvaluatePlayerTileInteraction(party, out contactDisposition, out attackers);
        
        result.EncounterTriggered = detected && (contactDisposition == StrategicDisposition.Hostile || contactDisposition == StrategicDisposition.Animal);
        result.HuntedEscalationTriggered = detected && contactDisposition == StrategicDisposition.Hostile;
        if (result.EncounterTriggered) result.EngagingEntities = attackers;

        return result;
    }

    private void EnsureStrategicMapRuntime()
    {
        if (_runtime?.StrategicMap == null)
        {
            return;
        }

        if (_runtime.StrategicMap.Tiles == null)
        {
            _runtime.StrategicMap.Tiles = new StrategicMapTileData[TravelScaleDefinitions.StrategicMapTilesPerSide, TravelScaleDefinitions.StrategicMapTilesPerSide];
        }
    }

    private StrategicEntity UpsertPartyEntity()
    {
        if (_runtime?.StrategicMap == null || _context?.CreaturePersistence?.PersistentCreatures == null)
        {
            return null;
        }

        List<CreatureStats> partyMembers = _context.CreaturePersistence.PersistentCreatures
            .Where(c => c != null && GodotObject.IsInstanceValid(c) && !c.IsDead && (c.IsInGroup("Player") || c.IsInGroup("Ally")))
            .ToList();

        if (partyMembers.Count == 0)
        {
            return null;
        }

        StrategicEntity party = _runtime.StrategicMap.AllEntities.FirstOrDefault(e => e != null && e.IsPlayerParty);
        if (party == null)
        {
            party = new StrategicEntity
            {
                EntityId = "party",
                CreatureDefinition = partyMembers[0].Template,
                CreatureRuntimeReference = partyMembers[0],
                TileCoord = _runtime.StrategicMap.PlayerTileCoord,
                Hunger = 0.1f,
                InjuryState = 0f,
                IsPlayerParty = true,
                HasHomeTile = false,
                HomeTile = _runtime.StrategicMap.PlayerTileCoord
            };

            _runtime.StrategicMap.AllEntities.Add(party);
        }

        party.TileCoord = _runtime.StrategicMap.PlayerTileCoord;
        return party;
    }

    private void UpdateHungerAndInjury(StrategicEntity entity)
    {
        if (entity.IsPlayerParty)
        {
            return;
        }

        float weatherExhaustion = Mathf.Max(0f, WeatherManager.Instance?.CurrentWeather?.FlyPenalty ?? 0) * 0.01f;
        
        // Larger tribes gather/ration more efficiently. Individuals starve faster.
        float groupSizeDivisor = Mathf.Max(1f, Mathf.Sqrt(entity.GroupSize)); 
        float hungerTick = (0.04f + weatherExhaustion) / groupSizeDivisor;

        entity.Hunger = Mathf.Clamp(entity.Hunger + hungerTick, 0f, 1f);

        // Injury naturally drifts upward for starving creatures and slowly eases for stable creatures.
        if (entity.Hunger > 0.85f)
        {
            entity.InjuryState = Mathf.Clamp(entity.InjuryState + 0.04f, 0f, 1f);
        }
        else
        {
            entity.InjuryState = Mathf.Clamp(entity.InjuryState - 0.01f, 0f, 1f);
        }
    }

    private void MoveEntity(StrategicEntity entity)
    {
        if (entity == null || entity.IsPlayerParty || entity.CreatureDefinition == null)
        {
            return;
        }

        float feetPerHour = Mathf.Max(0f, entity.CreatureDefinition.Speed_Land) * 600f;
        float weatherMovePenalty = Mathf.Abs(Mathf.Min(0f, WeatherManager.Instance?.CurrentWeather?.FlyPenalty ?? 0f));
        float movementFactor = Mathf.Clamp(1f - (weatherMovePenalty * 0.05f), 0.25f, 1f);
        int tileSteps = Mathf.Clamp(Mathf.RoundToInt((feetPerHour / TravelScaleDefinitions.StrategicTileFeet) * movementFactor), 0, 3);

        if (tileSteps <= 0)
        {
            return;
        }

        // Intent is shaped by hunger, disposition pressure near player, weather friction, and strategic abilities.
        Vector2I target = ChooseIntentTile(entity);
        RelocateEntity(entity, target, tileSteps);
    }

     private Vector2I ChooseIntentTile(StrategicEntity entity)
    {
        Vector2I playerTile = _runtime.StrategicMap.PlayerTileCoord;
        bool hungry = entity.Hunger >= 0.55f;

        Vector2I direction = Vector2I.Zero;
        if (hungry)
        {
            // Predators hunt by moving toward nearby Disturbance (activity/tracks/noise)
            Vector2I bestTile = entity.TileCoord;
            float highestDisturbance = -1f;
            for (int x = -2; x <= 2; x++)
            {
                for (int y = -2; y <= 2; y++)
                {
                    int checkX = Mathf.Clamp(entity.TileCoord.X + x, 0, _runtime.StrategicMap.Width - 1);
                    int checkY = Mathf.Clamp(entity.TileCoord.Y + y, 0, _runtime.StrategicMap.Height - 1);
                    float dist = _runtime.StrategicMap.Tiles[checkX, checkY].Disturbance;
                    if (dist > highestDisturbance)
                    {
                        highestDisturbance = dist;
                        bestTile = new Vector2I(checkX, checkY);
                    }
                }
            }
            if (bestTile != entity.TileCoord && highestDisturbance > 0.1f)
            {
                direction = new Vector2I(Math.Sign(bestTile.X - entity.TileCoord.X), Math.Sign(bestTile.Y - entity.TileCoord.Y));
            }
            else direction = new Vector2I(_rng.RandiRange(-1, 1), _rng.RandiRange(-1, 1));
        }
        else if (entity.HasHomeTile)
        {
            // If they have a home and aren't hungry, they move towards it. 
            // If they are ALREADY on it, they stay perfectly still (Direction = Zero).
            if (entity.TileCoord == entity.HomeTile) direction = Vector2I.Zero;
            else direction = new Vector2I(Math.Sign(entity.HomeTile.X - entity.TileCoord.X), Math.Sign(entity.HomeTile.Y - entity.TileCoord.Y));
        }
        else
        {
            direction = new Vector2I(_rng.RandiRange(-1, 1), _rng.RandiRange(-1, 1));
        }

        return entity.TileCoord + direction;
    }

    private void RelocateEntity(StrategicEntity entity, Vector2I desiredTile, int tileSteps)
    {
        Vector2I clampedTarget = new Vector2I(
            Mathf.Clamp(desiredTile.X, 0, _runtime.StrategicMap.Width - 1),
            Mathf.Clamp(desiredTile.Y, 0, _runtime.StrategicMap.Height - 1));

        Vector2I current = entity.TileCoord;
        Vector2I stepped = current;
        for (int i = 0; i < tileSteps; i++)
        {
            stepped = new Vector2I(
                stepped.X + Math.Sign(clampedTarget.X - stepped.X),
                stepped.Y + Math.Sign(clampedTarget.Y - stepped.Y));
        }

        stepped = new Vector2I(
            Mathf.Clamp(stepped.X, 0, _runtime.StrategicMap.Width - 1),
            Mathf.Clamp(stepped.Y, 0, _runtime.StrategicMap.Height - 1));

        if (stepped == current)
        {
            return;
        }

        StrategicMapTileData oldTile = _runtime.StrategicMap.Tiles[current.X, current.Y];
        StrategicMapTileData newTile = _runtime.StrategicMap.Tiles[stepped.X, stepped.Y];
        oldTile.StrategicEntities.Remove(entity);
        newTile.StrategicEntities.Add(entity);
        oldTile.Disturbance = Mathf.Clamp(oldTile.Disturbance + 0.01f, 0f, 3f);
        entity.TileCoord = stepped;
    }

    private int ResolveAiVsAiInteractions()
    {
        int interactions = 0;

        for (int x = 0; x < _runtime.StrategicMap.Width; x++)
        {
            for (int y = 0; y < _runtime.StrategicMap.Height; y++)
            {
                StrategicMapTileData tile = _runtime.StrategicMap.Tiles[x, y];
                if (tile?.StrategicEntities == null || tile.StrategicEntities.Count < 2)
                {
                    continue;
                }

                List<StrategicEntity> locals = tile.StrategicEntities.Where(e => e != null && !e.IsPlayerParty).ToList();
                for (int i = 0; i < locals.Count; i++)
                {
                    for (int j = i + 1; j < locals.Count; j++)
                    {
                        StrategicEntity a = locals[i];
                        StrategicEntity b = locals[j];
                        if (a.CreatureDefinition == null || b.CreatureDefinition == null || a.InjuryState >= 1f || b.InjuryState >= 1f)
                        {
                            continue;
                        }

                        bool isSameSpecies = a.CreatureDefinition == b.CreatureDefinition;
                        bool isSolitary = a.CreatureDefinition.AverageGroupSize <= 1; // Strict solitary rule
                        bool aIsStarving = a.Hunger >= 0.85f;
                        bool bIsStarving = b.Hunger >= 0.85f;

                        bool isIntelligent = a.CreatureDefinition.Intelligence > 6;
                        bool isEvilOrChaotic = a.CreatureDefinition.Alignment.Contains("Evil") || a.CreatureDefinition.Alignment.Contains("Chaos");

                        // --- ECOLOGICAL MATRIX ---
                        if (isSameSpecies && !isSolitary)
                        {
                            bool willMerge = false;
                            bool willCannibalize = false;

                            if (isIntelligent)
                            {
                                if (isEvilOrChaotic)
                                {
                                    if (aIsStarving || bIsStarving)
                                    {
                                        // Evil/Chaotic starving tribes might cannibalize or merge out of desperation
                                        if (_rng.Randf() > 0.5f) willCannibalize = true;
                                        else willMerge = true;
                                    }
                                    else willMerge = true;
                                }
                                else { willMerge = true; } // Good/Neutral intelligent creatures always merge
                            }
                            else // Low IQ (< 6)
                            {
                                // Animals ignore each other unless starving and predatory
                                if ((aIsStarving || bIsStarving) && isEvilOrChaotic) willCannibalize = true;
                            }

                            if (willMerge)
                            {
                                // Weighted average lowers hunger based on group sizes pooling resources
                                a.Hunger = ((a.Hunger * a.GroupSize) + (b.Hunger * b.GroupSize)) / (a.GroupSize + b.GroupSize);
                                a.GroupSize += b.GroupSize;
                                b.InjuryState = 1.0f; // Mark B for cleanup
                                GD.Print($"[color=gray][Ecology] Tribes of {a.CreatureDefinition.CreatureName} merged. Size: {a.GroupSize}.[/color]");
                                continue;
                            }
                            
                            if (!willCannibalize) continue; // They ignore each other
                        }
                        else if (isSameSpecies && isSolitary)
                        {
                            // Solitary creatures NEVER merge. If starving and predatory, they fight.
                            if (!((aIsStarving || bIsStarving) && isEvilOrChaotic)) continue; 
                        }

                        StrategicDisposition aVsB = ResolveDisposition(a.CreatureDefinition, b.CreatureDefinition);
                        
                        // CANNIBALISM: Starving creatures ignore "Friendly" disposition if it's food
                        if (aVsB == StrategicDisposition.Friendly && !aIsStarving && !bIsStarving)
                        {
                            continue;
                        }

                        // GROUP MATH: Multiply base power by the number of individuals in the tribe
                        float powerA = (1f + ComputeStrategicAbilityModifier(a, b)) * a.GroupSize;
                        float powerB = (1f + ComputeStrategicAbilityModifier(b, a)) * b.GroupSize;
                        
                        float powerDiff = powerA - powerB;
                        float combatWeight = 0.2f + (Mathf.Abs(powerDiff) * 0.02f) + tile.Disturbance * 0.1f;
                        
                        if (_rng.Randf() > Mathf.Clamp(combatWeight, 0.05f, 0.75f)) continue; // They avoid each other

                        interactions++;
                        StrategicEntity winner = powerDiff >= 0f ? a : b;
                        StrategicEntity loser = powerDiff >= 0f ? b : a;
                        
                        // Loser tribe takes casualties (reduced group size) or injuries
                        int casualties = Mathf.CeilToInt(loser.GroupSize * _rng.RandfRange(0.2f, 0.6f));
                        loser.GroupSize -= casualties;
                        if (loser.GroupSize <= 0) loser.InjuryState = 1.0f; // Tribe wiped out
                        
                        // Winner feeds and resets hunger
                        winner.Hunger = 0f;
                        tile.Disturbance = Mathf.Clamp(tile.Disturbance + 0.3f, 0f, 3f);
                        
                        string act = (isSameSpecies && (aIsStarving || bIsStarving)) ? "cannibalized" : "clashed with";
                        GD.Print($"[color=gray][Ecology] A group of {winner.GroupSize} {winner.CreatureDefinition.CreatureName}s {act} a group of {loser.CreatureDefinition.CreatureName}s![/color]");
                    }
                }
                
                // Cleanup wiped out tribes
                tile.StrategicEntities.RemoveAll(e => e.InjuryState >= 1.0f);

                tile.Disturbance = Mathf.Max(0f, tile.Disturbance - 0.02f);
            }
        }

        return interactions;
    }

    private bool EvaluatePlayerTileInteraction(StrategicEntity party, out StrategicDisposition dominantDisposition, out List<StrategicEntity> engagingEntities)
    {
        dominantDisposition = StrategicDisposition.Friendly;
        engagingEntities = new List<StrategicEntity>();

        if (!_runtime.StrategicMap.TryGetTile(party.TileCoord, out StrategicMapTileData tile))
        {
            return false;
        }

        if (tile.IsExitTile)
        {
            return false;
        }

        CreatureTemplate_SO playerTemplate = party.CreatureDefinition;
        bool anyDetected = false;

        foreach (StrategicEntity entity in tile.StrategicEntities.Where(e => e != null && !e.IsPlayerParty).ToList())
        {
            float weatherDetectionPenalty = Mathf.Abs(Mathf.Min(0, WeatherManager.Instance?.CurrentWeather?.PerceptionPenalty ?? 0)) * 0.02f;
            float baseDetection = Mathf.Clamp(0.35f + (entity.Hunger * 0.25f) + tile.Disturbance * 0.15f - weatherDetectionPenalty, 0.05f, 0.95f);

            float stealthCounter = 0.5f;
            if (party.CreatureRuntimeReference != null)
            {
                stealthCounter = Mathf.Clamp((party.CreatureRuntimeReference.GetSkillBonus(SkillType.Stealth) + 10f) / 30f, 0.05f, 0.95f);
            }

            float finalChance = Mathf.Clamp(baseDetection - (stealthCounter * 0.3f) + ComputeStrategicAbilityModifier(entity, party) * 0.02f, 0.02f, 0.98f);
            if (_rng.Randf() > finalChance)
            {
                continue;
            }

            anyDetected = true;
            StrategicDisposition disposition = ResolveDisposition(entity.CreatureDefinition, playerTemplate);
            if (disposition == StrategicDisposition.Hostile || disposition == StrategicDisposition.Animal)
            {
                dominantDisposition = disposition;
                engagingEntities.Add(entity); // Capture the attacker
                return true;
            }

            dominantDisposition = disposition;
        }

        return anyDetected;
    }

    private static StrategicDisposition ResolveDisposition(CreatureTemplate_SO actor, CreatureTemplate_SO target)
    {
        if (actor == null)
        {
            return StrategicDisposition.Suspicious;
        }

        if (actor.Intelligence < 5)
        {
            return StrategicDisposition.Animal;
        }

        if (target == null)
        {
            return StrategicDisposition.Suspicious;
        }

        AlignmentAxes actorAxes = PartyRosterManager.ParseAlignment(actor.Alignment);
        AlignmentAxes targetAxes = PartyRosterManager.ParseAlignment(target.Alignment);

        bool moralMismatch = (actorAxes.Moral == AlignmentAxisValue.Good && targetAxes.Moral == AlignmentAxisValue.Evil) ||
                             (actorAxes.Moral == AlignmentAxisValue.Evil && targetAxes.Moral == AlignmentAxisValue.Good);

        bool orderMismatch = (actorAxes.Order == AlignmentAxisValue.Lawful && targetAxes.Order == AlignmentAxisValue.Chaotic) ||
                             (actorAxes.Order == AlignmentAxisValue.Chaotic && targetAxes.Order == AlignmentAxisValue.Lawful);

        bool exactMatch = actorAxes.Moral == targetAxes.Moral && actorAxes.Order == targetAxes.Order;

        if (moralMismatch)
        {
            return StrategicDisposition.Hostile;
        }

        if (exactMatch)
        {
            return StrategicDisposition.Friendly;
        }

        if (orderMismatch)
        {
            return StrategicDisposition.Suspicious;
        }

        return StrategicDisposition.Suspicious;
    }

    /// <summary>
    /// Strategic ability adapter.
    ///
    /// Expected output:
    /// - Existing abilities are queried through existing ability components.
    /// - Components provide AI-estimated value and that value becomes strategic pressure.
    /// - Signature keywords add tiny strategic flavor for lure, ambush, illusion, burrow, stealth, and spell-like effects.
    /// </summary>
    private float ComputeStrategicAbilityModifier(StrategicEntity actor, StrategicEntity target)
    {
        CreatureTemplate_SO actorTemplate = actor?.CreatureDefinition;
        if (actorTemplate?.KnownAbilities == null || actorTemplate.KnownAbilities.Count == 0)
        {
            return 0f;
        }

        var context = new EffectContext
        {
            Caster = actor?.CreatureRuntimeReference,
            PrimaryTarget = target?.CreatureRuntimeReference,
            Ability = null,
            AimPoint = Vector3.Zero,
            LastSaveResults = new Dictionary<CreatureStats, bool>()
        };

        float total = 0f;
        foreach (Ability_SO ability in actorTemplate.KnownAbilities)
        {
            if (ability == null || ability.EffectComponents == null)
            {
                continue;
            }

            context.Ability = ability;
            foreach (AbilityEffectComponent component in ability.EffectComponents)
            {
                if (component == null)
                {
                    continue;
                }

                total += Mathf.Clamp(component.GetAIEstimatedValue(context), -2f, 4f) * 0.05f;
            }

            string lowered = (ability.AbilityName ?? string.Empty).ToLowerInvariant();
            if (lowered.Contains("voice") || lowered.Contains("mimic")) total += 0.12f;
            if (lowered.Contains("web")) total += 0.16f;
            if (lowered.Contains("illusion")) total += 0.12f;
            if (lowered.Contains("burrow") || lowered.Contains("stealth")) total += 0.1f;
            if (ability.Category == AbilityCategory.Spell || ability.SpecialAbilityType != SpecialAbilityType.None) total += 0.04f;
        }

        float weatherAbilityPenalty = Mathf.Abs(Mathf.Min(0f, WeatherManager.Instance?.CurrentWeather?.RangedAttackPenalty ?? 0f)) * 0.01f;
        float baseModifier = Mathf.Max(-1f, total - weatherAbilityPenalty);

        // --- DESPERATION CURVE ---
        if (actor.Hunger >= 0.5f && actor.Hunger < 0.85f)
        {
            baseModifier += 0.3f; // Desperate: Highly aggressive and dangerous
        }
        else if (actor.Hunger >= 0.85f)
        {
            baseModifier -= 0.4f; // Starving: Weak and lethargic
        }

        return baseModifier;
    }
}

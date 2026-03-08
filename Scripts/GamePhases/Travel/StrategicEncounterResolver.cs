using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed class StrategicResolutionResult
{
    public bool EncounterTriggered;
    public bool HuntedEscalationTriggered;
    public int AiInteractionCount;
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
        bool detected = EvaluatePlayerTileInteraction(party, out contactDisposition);
        result.EncounterTriggered = detected && (contactDisposition == StrategicDisposition.Hostile || contactDisposition == StrategicDisposition.Animal);
        result.HuntedEscalationTriggered = detected && contactDisposition == StrategicDisposition.Hostile;

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
        entity.Hunger = Mathf.Clamp(entity.Hunger + 0.04f + weatherExhaustion, 0f, 1f);

        // Injury naturally drifts upward for starving creatures and slowly eases for stable creatures.
        if (entity.Hunger > 0.8f)
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

        StrategicDisposition disposition = ResolveDisposition(entity.CreatureDefinition, _context?.CreaturePersistence?.PersistentCreatures?.FirstOrDefault(c => c != null && c.IsInGroup("Player"))?.Template);
        bool aggressive = disposition == StrategicDisposition.Hostile || disposition == StrategicDisposition.Animal;

        Vector2I direction = Vector2I.Zero;
        if (hungry && aggressive)
        {
            direction = new Vector2I(Math.Sign(playerTile.X - entity.TileCoord.X), Math.Sign(playerTile.Y - entity.TileCoord.Y));
        }
        else if (entity.HasHomeTile)
        {
            direction = new Vector2I(Math.Sign(entity.HomeTile.X - entity.TileCoord.X), Math.Sign(entity.HomeTile.Y - entity.TileCoord.Y));
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
                        if (a.CreatureDefinition == null || b.CreatureDefinition == null)
                        {
                            continue;
                        }

                        StrategicDisposition aVsB = ResolveDisposition(a.CreatureDefinition, b.CreatureDefinition);
                        if (aVsB == StrategicDisposition.Friendly)
                        {
                            continue;
                        }

                        float abilityPressure = ComputeStrategicAbilityModifier(a, b) - ComputeStrategicAbilityModifier(b, a);
                        float combatWeight = 0.2f + (Mathf.Abs(abilityPressure) * 0.05f) + tile.Disturbance * 0.1f;
                        if (_rng.Randf() > Mathf.Clamp(combatWeight, 0.05f, 0.75f))
                        {
                            continue;
                        }

                        interactions++;
                        StrategicEntity loser = abilityPressure >= 0f ? b : a;
                        loser.InjuryState = Mathf.Clamp(loser.InjuryState + _rng.RandfRange(0.1f, 0.35f), 0f, 1f);
                        tile.Disturbance = Mathf.Clamp(tile.Disturbance + 0.15f, 0f, 3f);
                    }
                }

                tile.Disturbance = Mathf.Max(0f, tile.Disturbance - 0.02f);
            }
        }

        return interactions;
    }

    private bool EvaluatePlayerTileInteraction(StrategicEntity party, out StrategicDisposition dominantDisposition)
    {
        dominantDisposition = StrategicDisposition.Friendly;

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
        return Mathf.Max(-1f, total - weatherAbilityPenalty);
    }
}

# Ice Storm — Godot Inspector Data Setup

Because this spell creates instant damage *and* a lingering zone, we stack our components to execute the spell perfectly in sequence.

## 1) Create the Lingering Status Effect
This is the penalty that will be dynamically applied and removed from creatures as they walk in and out of the storm.
Create a `StatusEffect_SO` named `SE_IceStorm_PerceptionPenalty`:
*   **EffectName:** `Ice Storm Sleet`
*   **DurationInRounds:** `0` (It is managed by the `PersistentEffectController` dynamically).
*   **Modifications:** Add one entry.
    *   **StatToModify:** `Perception`
    *   **ModifierValue:** `-4`
    *   **BonusType:** `Untyped` (or `Circumstance`)

## 2) Create the Environmental Zone Prefab
1. Create a new Node3D scene and name it `IceStormZone_Prefab`.
2. Add a `PersistentEffectController` node to the root.
3. Add an `EnvironmentalZone` node to the root.
4. In the `EnvironmentalZone` inspector:
   *   **IsCylinderShape:** `True`
   *   **Radius:** `20`
   *   **CylinderHeight:** `40`
   *   **IsDifficultTerrain:** `True`
   *   **MovementCostPenalty:** `1` (This effectively doubles movement cost).
   *   **Tags:** Add `"Precipitation"` and `"Cold"` if you want it to interact with other weather rules.
5. Add whatever Particle effects (snow/sleet) you want as children. Save the prefab.

## 3) Create the Ability_SO
Create a new `Ability_SO` named `Spell_IceStorm`:
*   **AbilityName:** `Ice Storm`
*   **Category:** `Spell`
*   **ActionCost:** `Standard`
*   **TargetType:** `Area_FriendOrFoe`
*   **Range.Type:** `Long`
*   **AreaOfEffect.Shape:** `Cylinder`
*   **AreaOfEffect.Range:** `20`
*   **AreaOfEffect.Height:** `40`
*   **SavingThrow.SaveType:** `None`
*   **AllowsSpellResistance:** `true`
*   **Effect Components:** Add **THREE** components to this array in the following order:

    1.  **DamageEffect** (Bludgeoning)
        *   `Damage.DamageType`: `Bludgeoning`
        *   `Damage.DiceCount`: `3`
        *   `Damage.DieSides`: `6`
        *   `ScalesWithCasterLevel`: `false`
    
    2.  **DamageEffect** (Cold)
        *   `Damage.DamageType`: `Cold`
        *   `Damage.DiceCount`: `2`
        *   `Damage.DieSides`: `6`
        *   `ScalesWithCasterLevel`: `false`

    3.  **CreatePersistentEffect** (The Zone)
        *   `EffectPrefab`: Assign your `IceStormZone_Prefab`
        *   `LingeringEffects`: Add `1` element to this sub-array -> **ApplyStatusEffect**.
            *   `EffectToApply`: Assign `SE_IceStorm_PerceptionPenalty`.

### Why this works for Player / AI / Arena / Travel:
1. When cast, the engine evaluates the area and rolls Spell Resistance for all targets.
2. It hits valid targets with 3d6 Bludgeoning.
3. It hits valid targets with 2d6 Cold.
4. It spawns the Prefab. The `PersistentEffectController` calculates the duration (1 round/level) and begins tracking anyone entering the cylinder, applying the -4 Perception status effect.
5. The `EnvironmentalZone` registers with the `GridManager`, making all nodes inside the 20x40 cylinder cost double movement to traverse!
6. The AI inherently understands how to value this because `DamageEffect` and `CreatePersistentEffect` both return high values via `GetAIEstimatedValue`!
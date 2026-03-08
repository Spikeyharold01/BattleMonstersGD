# Swallow Whole (Ex) — Godot Inspector Data Setup

**Swallow Whole** is an active standard action that a creature can take if it begins its turn grappling an opponent with its mouth (via Grab). 

Because the AI assesses all abilities in the `SpecialAttacks` array, assigning this ability tells the AI to prioritize swallowing an opponent instead of maintaining a normal pin.

## 1) Create the Ability_SO
Create a new `Ability_SO` named `Swallow_Whole`:
*   **AbilityName:** `Swallow Whole`
*   **DescriptionForTooltip:** `If the creature begins its turn with an opponent grappled in its mouth, it can attempt a new combat maneuver check. If it succeeds, it swallows its prey.`
*   **Category:** `SpecialAttack`
*   **SpecialAbilityType:** `Ex`
*   **ActionCost:** `Standard` (It replaces the standard action used to maintain a grapple)
*   **TargetType:** `SingleEnemy`
*   **Range.Type:** `Touch`
*   **Effect Components:** Add `Effect_SwallowWhole`
    *   `SizeDifferenceRequired`: `1` (Usually 1 size smaller, adjust if a specific monster states otherwise).
    *   `StomachDamage`: Configure the dice and damage type here (e.g., `2d6 Bludgeoning` or `1d8 Acid`). *This varies by monster, so you may need a unique Swallow Whole ability asset for different monsters if their stomach damage differs.*

## 2) Apply to the Creature
1.  Open the `CreatureTemplate_SO` for the monster (e.g., Purple Worm, T-Rex).
2.  Add the `Swallow_Whole` ability to the **Special Attacks** array.
3.  Ensure the creature also has an attack with the **Grab** quality to initiate the grapple in the first place.

### Why this works for Player / AI / Arena / Travel:
*   **Arena AI:** The `AIAction_CastGenericAbility` loop scans all `SpecialAttacks`. If the AI is grappling an enemy, `Effect_SwallowWhole.GetAIEstimatedValue()` returns a massive score, guaranteeing the AI will try to swallow the target instead of just doing normal damage.
*   **Internal Rules:** The `SwallowedController` forces the victim to share the swallower's position. The combat engine automatically recognizes `Condition.Swallowed` and restricts weapons to Light Slashing/Piercing, overrides the AC, and tracks cut damage to expel the victim organically.
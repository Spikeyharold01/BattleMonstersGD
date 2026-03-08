# Silence — Godot Inspector Data Setup

Because `Silence` can be attached to a mobile creature (allowing a Will Save) OR a stationary point in space (no save), it is best to provide the player with two variations of the `Ability_SO` to handle UI targeting correctly.

## 1) StatusEffect_SO (`SE_Silenced`)
Create this first, as the Effect Components need to reference it.
*   **EffectName:** `Silenced`
*   **ConditionApplied:** `Silenced`
*   **ResistDamageTypes:** Add one array entry: `Sonic`
*   **DamageResistanceAmount:** `9999` (This effectively guarantees sonic damage immunity).
*   *Note: Language-based attack immunity is automatically handled engine-side because the creature now bears the `Silenced` condition flag.*

## 2) Ability_SO (`Silence - Target Creature`)
Used to target an unwilling enemy or willing ally. The area will follow them as they move.
*   **TargetType:** `SingleEnemy` (Or create a `SingleAlly` duplicate if your UI requires explicit separation for friendly casting).
*   **Range.Type:** `Long`
*   **SavingThrow.SaveType:** `Will`
*   **SavingThrow.EffectOnSuccess:** `Negates`
*   **AllowsSpellResistance:** `true`
*   **Components.HasVerbal:** `true`
*   **Components.HasSomatic:** `true`
*   **Effect Components:** Add `Effect_Silence`
    *   `SilencedStatusTemplate`: Assign `SE_Silenced`
    *   `Radius`: `20`
    *   `RoundsPerLevel`: `1`

## 3) Ability_SO (`Silence - Target Point`)
Used to place a stationary 20-ft radius zone of silence on the map.
*   **TargetType:** `Area_FriendOrFoe` (This tells the UI to show an area-template selection over the map).
*   **AreaOfEffect.Shape:** `Burst`
*   **AreaOfEffect.Range:** `20`
*   **Range.Type:** `Long`
*   **SavingThrow.SaveType:** `None` (Points in space do not receive saves).
*   **AllowsSpellResistance:** `false`
*   **Effect Components:** Add `Effect_Silence` (configured identically to the Creature version).
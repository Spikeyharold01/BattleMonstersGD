# Control Weather (Spell & Creature Variants) — Godot Inspector Data Setup

Because this system is entirely data-driven, you can use the exact same `Effect_ControlWeather` component to create the core spell for a Druid, and the restricted sub-ability for an Akhlut.

## 1) Prepare the `Weather_SO` Assets
Ensure you have `Weather_SO` assets created in your Godot FileSystem for the various weather types (e.g., `Weather_ClearSkies.tres`, `Weather_Blizzard.tres`, `Weather_Hurricane.tres`, `Weather_Fog.tres`).

## 2) Setup the Generic Spell (Control Weather)
Create a new `Ability_SO` named `Spell_ControlWeather`:
*   **AbilityName:** `Control Weather`
*   **Category:** `Spell`
*   **ActionCost:** `FullRound` (Translates the 10-minute cast time for gameplay purposes, though the manifestation delay is handled by the effect).
*   **TargetType:** `Self`
*   **Range.Type:** `Self`
*   **IsMythicCapable:** `true`
*   **Effect Components:** Add `Effect_ControlWeather`
    *   **Allowed Weathers:** Add *every* seasonal `Weather_SO` asset you want the caster to be able to pick from.

## 3) Setup the Akhlut's Ecological Variant
The Akhlut has a special (Su) version of this spell restricted to "Windy or Cold weather only".
Create a new `Ability_SO` named `Special_Akhlut_ControlWeather`:
*   **AbilityName:** `Control Weather (Cold/Windy)`
*   **Category:** `SpecialAttack` (Or Spell-like Ability `Sp`)
*   **ActionCost:** `Standard`
*   **TargetType:** `Self`
*   **Effect Components:** Add `Effect_ControlWeather`
    *   **Allowed Weathers:** Add *ONLY* the `Weather_SO` assets that fit the restriction (e.g., `Weather_Blizzard.tres`, `Weather_Sleet.tres`, `Weather_Hurricane.tres`).

### How the AI Handles It:
During the AI's turn planning phase, `AIAction_Magic` will automatically unroll the `AllowedWeathers` array into separate prospective actions. 

The AI will score each weather type based on its current tactical situation:
*   It will pick **Blizzard** if it has Snowsight and the player doesn't.
*   It will pick **Hurricane** if it is a Huge creature fighting Medium creatures (blowing them away).
*   It will pick **Clear Skies** if it is currently losing an active weather battle.

**Combat vs Travel Phase:**
The `GetAIEstimatedValue` explicitly checks if it is in an active Arena combat. If the creature is casting the non-mythic version (which takes 100 rounds to manifest), the AI will assign it a score of `-1` in Arena combat, preventing it from wasting its turn. However, during the **Travel Phase**, the AI will happily use it to alter the biome pressure!
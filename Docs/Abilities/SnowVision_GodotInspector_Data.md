# Snow Vision (Ex) — Godot Inspector Data Setup

**Snow Vision** is a passive extraordinary ability that allows a creature to ignore visual and perception penalties associated with snow and blizzard conditions. 

Because the combat and travel engines natively read the creature's trait flags, no custom logic scripts or controllers are needed. 

To implement this on a creature, follow these two steps:

## 1) Toggle the Creature Template Flag
Open the specific `CreatureTemplate_SO` (e.g., an Ice Elemental or Akhlut).
1. Go to the **Senses** group.
2. Check the box for **Has Snowsight**.

*(Note: If you imported the creature using the `CreatureImporter` script and the original spreadsheet listed "Snow Vision" under Senses/Other, this box will already be checked automatically!)*

## 2) Create the Tooltip/UI Ability_SO (Optional)
To ensure the player can read what "Snow Vision" does when inspecting the creature in the UI, create a purely informational ability asset.

Create a new `Ability_SO` named `Snow_Vision`:
*   **AbilityName:** `Snow Vision`
*   **DescriptionForTooltip:** `This creature can see perfectly well in snowy conditions, and does not take any penalties on Perception checks or ranged attacks while in snowy weather.`
*   **IsImplemented:** `true`
*   **Category:** `SpecialAttack` (Or `SpecialQuality` depending on your UI layout)
*   **SpecialAbilityType:** `Ex`
*   **ActionCost:** `NotAnAction`
*   **Effect Components:** *(Leave empty)*

Add this `Ability_SO` to the **Special Qualities** array of the `CreatureTemplate_SO`. 

### Why this works for Player / AI / Arena / Travel:
*   **Arena Combat:** The `LineOfSightManager` will check the `HasSnowsight` flag and ignore the 20% concealment miss chance normally applied by snow-tagged grid nodes. `CombatAttacks` will ignore the typical ranged penalty.
*   **Travel Phase:** The `SoundSystem` and `ScentSystem` use `CreatureStats.GetPerceptionBonus()` to determine if a travel stimulus (like a snapped twig or distant roar) is noticed. By ignoring the weather penalty in that calculation, the AI and Player will detect ecological events at full range during blizzards.
*   **Visuals:** `WeatherManager.UpdatePlayerWeatherVisibility()` already checks this flag to clear the screen of blinding snow particle overlays when controlling this unit.
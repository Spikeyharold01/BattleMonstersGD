# Beast Shape I-IV — Godot Inspector Data Setup

Because this system is data-driven, one script (`Effect_BeastShape`) powers all four tiers of the spell, as well as their Mythic and Augmented variants.

## 1) Setting up the Ability_SO
Create an `Ability_SO` for whichever tier you are granting the creature (e.g., `Spell_BeastShape_II`).
*   **AbilityName:** `Beast Shape II`
*   **Category:** `Spell`
*   **TargetType:** `Self`
*   **ActionCost:** `Standard`
*   **IsMythicCapable:** `true`
*   **Components.HasSomatic:** `true`
*   **Components.HasVerbal:** `true`
*   **Effect Components:** Add `Effect_BeastShape`

## 2) Configuring the Effect_BeastShape Component
In the Inspector for the `Effect_BeastShape` component:
*   **Spell Tier:** Set this to match the spell level (1, 2, 3, or 4). The script will automatically enforce the hard limits on allowed speeds, sizes, and abilities based on this number.
*   **Minutes Per Level:** `1` (Standard duration).
*   **Allowed Forms:** Drag and drop specific `CreatureTemplate_SO` assets into this array. 
    *   *Note: You must supply a list of templates the caster is allowed to transform into (e.g., Wolf, Eagle, Dire Bear). The AI will dynamically analyze this list during combat and pick the one that gives it the best tactical advantage (like picking an Eagle if enemies are flying).*

### How it works with Shore Storming / Terrain Controllers
If you want to use this logic for a creature that physically polymorphs into another shape during combat via magic, use this spell. 

If you want a creature to seamlessly transition forms when entering Water (like the Akhlut), use the `PassiveTerrainTransformController` as previously documented. The engine supports both seamlessly—one is tied to the spell action economy and duration, and the other is tied purely to grid positioning!
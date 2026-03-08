# Savage Bite (Ex) — Godot Inspector Data Setup

**Savage Bite** is a passive extraordinary ability that alters the critical threat range of a specific natural attack. Because the `CombatAttacks.cs` script automatically reads the `CriticalThreatRange` from a creature's `NaturalAttack` data, no custom scripting is needed. 

To implement this on a creature, follow these two steps:

## 1) Adjust the Creature's Melee Attack Data
Open the specific `CreatureTemplate_SO` (e.g., the creature that possesses Savage Bite).
1. Go to the **Offense & Movement** group.
2. Expand the **Melee Attacks** array.
3. Find the entry for the **Bite** attack.
4. Change **CriticalThreatRange** from `20` to `18`. 
*(This automatically makes the bite threaten a critical hit on an 18, 19, or 20).*

## 2) Create the Tooltip/UI Ability_SO (Optional but Recommended)
To ensure the player can read what "Savage Bite" does when inspecting the creature, create a purely informational ability asset and add it to the creature's Special Attacks list.

Create a new `Ability_SO` named `Savage_Bite`:
*   **AbilityName:** `Savage Bite`
*   **DescriptionForTooltip:** `This creature's bite threatens a critical hit on a roll of 18–20.`
*   **IsImplemented:** `true`
*   **Category:** `SpecialAttack`
*   **SpecialAbilityType:** `Ex`
*   **ActionCost:** `NotAnAction` (Since it is a passive physical trait)
*   **Effect Components:** *(Leave empty)*

Finally, add this `Ability_SO` to the **Special Attacks** array of the `CreatureTemplate_SO`. 

### Why this works for Player / AI / Arena / Travel:
Since this modifies the base `NaturalAttack` data container, the AI's `AIAction_SingleNaturalAttack` and `AIAction_FullAttack` will natively calculate the higher threat range into their expected damage scores (`chanceToHit` and `expectedDamage`), making the AI automatically value this bite attack higher without needing dedicated AI logic.
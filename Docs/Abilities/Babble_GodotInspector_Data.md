# Babble (Su) - Godot Inspector Setup

This document outlines how to create the Allip's **Babble** ability using the engine's built-in Aura and Acoustic systems. It requires configuring the mechanical CC (Crowd Control) aura, and applying a passive effect to the Allip to make it constantly broadcast its muttering to the AI's sound systems.

## 1. Create the Status Effects
We need three `StatusEffect_SO` assets.

**A. The Fascinated Debuff**
1. Create `SE_Babble_Fascinated.tres`.
2. **Effect Name:** `Fascinated by Babble`
3. **Condition Applied:** `Fascinated`
4. **Duration In Rounds:** `0` *(We leave it 0 here; the Aura Controller sets the duration dynamically).*

**B. The 24-Hour Immunity Buff**
1. Create `SE_Babble_Immunity.tres`.
2. **Effect Name:** `Immune to Babble`
3. **Duration In Rounds:** `14400` *(This equals 24 hours of combat rounds)*.

**C. The Acoustic Emitter (Muttering)**
1. Create `SE_Babble_Acoustics.tres`.
2. **Effect Name:** `Constant Muttering`
3. **Duration In Rounds:** `0` *(Permanent)*
4. Under **Ongoing Sound Emission**:
   * **Emits Sound Each Turn:** `On` (True)
   * **Ongoing Sound Intensity:** `3.0` *(Loud enough to be heard within 60ft)*
   * **Ongoing Sound Duration Seconds:** `6.0` *(One full round)*
   * **Ongoing Sound Type:** `Chanting`

## 2. Ability Resource Setup
Create a new `Ability_SO` named `Ability_Babble.tres`.

### Core Info
* **Ability Name:** `Babble`
* **Category:** `SpecialAttack`
* **Special Ability Type:** `Su` (Supernatural)

### Saving Throw
* **Save Type:** `Will`
* **Base DC:** `15` *(Fallback)*
* **Is Special Ability DC:** `On` (True) *(Will use 10 + 1/2 HD + Stat Mod)*
* **Dynamic DC Stat:** `Charisma`

## 3. Creature Integration (The Allip)
Open the Allip's `CreatureTemplate_SO` and its Character Prefab.

**A. Assign the Acoustics**
1. On the `CreatureTemplate_SO`, scroll to **Passive Effects**.
2. Add `SE_Babble_Acoustics.tres` to the array. (This ensures the Allip is constantly emitting sound to the AI and player).

**B. Configure the Aura Node**
1. Open the Allip's 3D Character Prefab.
2. Add a new child Node and attach the `PersistentAuraController.cs` script.
3. Configure the script:
   * **Aura Name:** `Babble`
   * **Radius:** `60`
   * **Source Ability:** Drag and drop `Ability_Babble.tres`.
   * **Effect On Fail:** Drag and drop `SE_Babble_Fascinated.tres`.
   * **Immunity On Save:** Drag and drop `SE_Babble_Immunity.tres`.
   * **Check Line of Sight:** `Off` (False). *(Babble is auditory, so it travels through walls as long as the sound can reach them).*

## How it works at Runtime
1. At the start of an enemy's turn, `TurnManager` triggers the Allip's `PersistentAuraController`.
2. The controller checks if the enemy is within 60ft and lacks the `Immune to Babble` status.
3. If valid, the enemy rolls a Will save against a dynamically generated DC (based on the Allip's Charisma).
4. **On Success:** The controller applies the 24-hour immunity effect.
5. **On Failure:** The controller applies the `Fascinated by Babble` status. (By standard PF1e rules, `CombatCalculations` and `TakeDamage` will automatically break `Fascinated` if the target takes damage).
6. Separately, because of the `SE_Babble_Acoustics.tres` passive, the `StatusEffectController` calls `SoundSystem.EmitSound` every single round. This allows the AI `SearchEffect` and `AIAction_IdentifyScentDirection` (sound tracking logic) to perfectly locate the Allip even while it is invisible or hiding in the walls.
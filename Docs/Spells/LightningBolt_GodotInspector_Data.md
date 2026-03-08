# Lightning Bolt — Godot Inspector Data Setup

Because `CombatMagic` natively handles the complex Line geometry math and `Effect_PenetratingBeam` handles the barrier interactions, this spell requires zero custom scripting and is set up entirely in the inspector.

## 1) Create the Ability_SO
Create a new `Ability_SO` named `Spell_LightningBolt`:
*   **AbilityName:** `Lightning Bolt`
*   **Category:** `Spell`
*   **School:** `Evocation`
*   **ActionCost:** `Standard`
*   **TargetType:** `Area_EnemiesOnly` (Or `Area_FriendOrFoe` if you want the UI to let the player casually blast their allies).
*   **Range.Type:** `Long` (Or `Custom` set to 120ft).
*   **AreaOfEffect.Shape:** `Line`
*   **AreaOfEffect.Range:** `120`
*   **AreaOfEffect.Width:** `5` (Pathfinder lines are 5ft wide).
*   **SavingThrow.SaveType:** `Reflex`
*   **SavingThrow.EffectOnSuccess:** `HalfDamage`
*   **AllowsSpellResistance:** `true`
*   **IsMythicCapable:** `true`
*   **Components.HasVerbal:** `true`
*   **Components.HasSomatic:** `true`
*   **Components.HasMaterial:** `true`

## 2) Add the Effect Component
Under **Effect Components**, add `Effect_PenetratingBeam`:
*   **Damage.DamageType:** `Electricity`
*   **Damage.DiceCount:** `1` (This is technically ignored by the beam script in favor of Caster Level, but good for reference).
*   **Damage.DieSides:** `6`
*   **MaxDiceCap:** `10`
*   **SetsCombustiblesOnFire:** `true`

*(Note: The script automatically handles the Mythic version by doubling the `MaxDiceCap` to 20 and bypassing Electricity Resistance if the target fails their save).*

### How the AI Handles It:
During the Arena phase, `AISpatialAnalysis.FindBestPlacementForAreaEffect` will automatically calculate multiple intersecting lines between enemies and score them. It will position itself to line up the maximum number of targets, passing the perfect `AimPoint` to the `EffectContext`. 

`Effect_PenetratingBeam.GetAIEstimatedValue()` then scores that line, heavily penalizing it if the beam would hit an allied monster!
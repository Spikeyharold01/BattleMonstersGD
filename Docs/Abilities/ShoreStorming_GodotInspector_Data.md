# Shore Storming (Ex) — Godot Inspector Data Setup

**Shore Storming** allows the Akhlut to transform between an Orca and a Wolf-Orca hybrid based on the terrain, granting a massive initiative bonus and a free charge attack if combat begins right after it transitions.

We achieve this purely through data using the highly reusable `PassiveTerrainTransformController`.

## 1) Create the "Shore Storming Momentum" Status Effect
This buff represents the burst of speed they get when breaching the water or diving in.
Create a new `StatusEffect_SO` named `SE_ShoreStormingMomentum`:
*   **EffectName:** `Shore Storming Momentum`
*   **DurationInRounds:** `1` (This equals 6 seconds of real-time in Travel Mode).
*   **ConditionApplied:** `Charging` (This automatically gives the AI/Player the +2 Attack / -2 AC rules for a charge on their first attack!).
*   **Modifications:** Add one entry.
    *   **StatToModify:** `Initiative`
    *   **ModifierValue:** `8`
    *   **BonusType:** `Untyped`

## 2) Create the Terrain Transform Rule (Water)
Create a new `TerrainTransformRule_SO` named `TransformRule_Akhlut_Water`:
*   **TargetTerrain:** `Water`
*   **VisualPrefabOverride:** Assign the Orca 3D Model / Cylinder Prefab here.
*   **TemplateModifier:** (Optional) If you want to remove its Land Speed while in Orca form, create a `CreatureTemplateModifier_SO` that zeroes out Land Speed, and assign it here. (The rules state its statistics remain the same, so you can leave this blank).
*   **BuffOnEnter:** Assign `SE_ShoreStormingMomentum`
*   **BuffOnExit:** Assign `SE_ShoreStormingMomentum`

*(Because the rules state it gains the bonus when moving from water to land OR land to water, we put the buff on both the Enter and Exit triggers of the Water rule).*

## 3) Attach to the Akhlut Prefab
1. Open the Akhlut's root creature scene.
2. Add a new `PassiveTerrainTransformController` node as a child of the root.
3. In the Inspector for the controller, add `1` element to the **Rules** array.
4. Assign `TransformRule_Akhlut_Water` to that slot.

### Why this works for Player / AI / Arena / Travel:
*   **Travel Phase:** As the Akhlut roams in Travel Mode, crossing from a ground tile to a water tile will instantly swap its visual model to an Orca. It gains a 6-second invisible status effect granting +8 Initiative.
*   **Arena Phase (Combat Start):** If the player triggers combat within those 6 seconds, `TurnManager` pulls the +8 Initiative directly into the initiative roll. 
*   **AI Combat:** On round 1, because the creature possesses `Condition.Charging` from the buff, the `CombatAttacks` engine will apply charge modifiers automatically to its first strike.
# Vulnerabilities (Ex or Su) - Godot Inspector Setup

This document explains how **Vulnerabilities** to both energy types (e.g., Fire, Cold) and non-energy types (e.g., Light, Water) are handled by the data-driven framework.

*Note: If you use the Excel Creature Importer, vulnerabilities listed in the spreadsheet's `weaknesses` columns are automatically parsed and added to the template.*

## Resource Details
* **Type:** `CreatureTemplate_SO`
* **Array:** `Weaknesses`

## Inspector Configuration

To give a creature a vulnerability, locate its `CreatureTemplate_SO` and expand the **Defense** -> **Weaknesses** array. 

1. Click **Add Element** to create a new `DamageResistance` resource.
2. In the **Damage Types** array of that new resource, add a string for the vulnerability keyword.
   * *Example 1 (Energy):* `"Fire"`, `"Cold"`, `"Acid"`, `"Electricity"`, `"Sonic"`
   * *Example 2 (Non-Energy):* `"Light"`, `"Water"`, `"Sound"`
3. **Amount:** Leave this at `0`. The framework rules process the math automatically based on the keyword's presence.

## How it works at Runtime

### Energy Vulnerabilities (+50% Damage)
When a creature takes damage (via `CreatureStats.TakeDamage`), the system checks the incoming `DamageType` string. If it matches an entry in the `Weaknesses` list, the incoming damage is automatically multiplied by 1.5x (a +50% increase) before being applied to the creature's HP.

### Non-Energy Vulnerabilities (-4 to Saves)
When a creature makes a saving throw against an ability (`GetFortitudeSave`, `GetReflexSave`, `GetWillSave`), the system reads the `Ability_SO` causing the effect. 

If the creature has a non-energy weakness (like `"Light"`), and that keyword is found anywhere in the incoming ability's `AbilityName` or `DescriptionForTooltip` (e.g., *Searing Light*, *Daylight*), the creature automatically suffers a **-4 penalty** to their saving throw. 

*Because this penalty is calculated directly inside the core save accessors, the AI will automatically factor the -4 penalty into its success predictions when deciding whether to cast the spell.*
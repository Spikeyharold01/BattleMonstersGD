# Godot Beginner Setup Worksheet: Arena → Travel → Arena (with Travel Encounters)

This worksheet is designed for a **brand new Godot user**.
It gives you:

1. A literal node-by-node scene tree blueprint (exact names)
2. A paste-ready debug phase switch script
3. A minimum valid player + enemy prefab checklist

---

## 1) Literal scene tree blueprint (exact names)

Create this exact tree in a new scene named `MainTest.tscn`:

```text
MainTest (Node3D)
├── ArenaRoot (Node3D)
│   ├── PlayerCreature (CharacterBody3D or Node3D with CreatureStats)
│   └── AllyCreature_01 (CharacterBody3D or Node3D with CreatureStats)
├── GamePhaseManager (Node) [attach: res://Scripts/GamePhases/GamePhaseManager.cs]
├── DebugPhaseInput (Node) [attach: res://Scripts/Debug/DebugPhaseInput.cs]
├── DirectionalLight3D (DirectionalLight3D)
└── Camera3D (Camera3D)
```

> Notes:
> - `PhaseRoot` and `CreatureStorage` are auto-created by `GamePhaseManager` if missing.
> - Do **not** rename `ArenaRoot` or `GamePhaseManager` in this tutorial.

### Inspector wiring

Select `GamePhaseManager` and set:

- **ArenaRoot** = `../ArenaRoot`
- **TravelBiomeDefinition** = your `BiomeTravelDefinition` resource (create one in step 4 below)
- **ArenaEnterCallback** = leave empty for now
- **ArenaExitCallback** = leave empty for now

### Project main scene

Set this as startup scene:

- Project → Project Settings → Application → Run → Main Scene = `res://MainTest.tscn`

---

## 2) Paste-ready debug phase switch script

Create folder `res://Scripts/Debug/` and file `DebugPhaseInput.cs`.
Attach it to the `DebugPhaseInput` node from the tree above.

```csharp
using Godot;

/// <summary>
/// Beginner debug controls for phase switching.
/// T = Travel, R = Arena
/// </summary>
public partial class DebugPhaseInput : Node
{
    [Export] public NodePath GamePhaseManagerPath;

    private GamePhaseManager _manager;

    public override void _Ready()
    {
        if (GamePhaseManagerPath != null && !GamePhaseManagerPath.IsEmpty)
        {
            _manager = GetNodeOrNull<GamePhaseManager>(GamePhaseManagerPath);
        }

        // Fallback: find by common name in current scene.
        if (_manager == null)
        {
            _manager = GetTree().CurrentScene?.GetNodeOrNull<GamePhaseManager>("GamePhaseManager");
        }

        if (_manager == null)
        {
            GD.PrintErr("DebugPhaseInput: Could not find GamePhaseManager. Set GamePhaseManagerPath in Inspector.");
            return;
        }

        _manager.PhaseChanged += OnPhaseChanged;

        GD.Print("Debug controls ready: [T] Travel, [R] Arena");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_manager == null || @event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.Keycode == Key.T)
        {
            _manager.SwitchPhase(GamePhaseType.Travel);
        }
        else if (keyEvent.Keycode == Key.R)
        {
            _manager.SwitchPhase(GamePhaseType.Arena);
        }
    }

    public override void _ExitTree()
    {
        if (_manager != null)
        {
            _manager.PhaseChanged -= OnPhaseChanged;
        }
    }

    private static void OnPhaseChanged(GamePhaseType previous, GamePhaseType current)
    {
        GD.Print($"[DebugPhaseInput] Phase changed: {previous} -> {current}");
    }
}
```

### Inspector for DebugPhaseInput

- Set **GamePhaseManagerPath** = `../GamePhaseManager`

---

## 3) Minimum valid player prefab checklist (worksheet)

Use this checklist for your **PlayerCreature** scene/prefab:

### Required structure

- [ ] Root node exists and is placeable in 3D world (`CharacterBody3D` recommended)
- [ ] A `CreatureStats` component exists (either on root or as a child named `CreatureStats`)
- [ ] Has a visible mesh (so you can see it)
- [ ] Has collision shape (so Area3D/body interactions work)

### Required groups (critical)

Open Node dock → Groups:

- [ ] Add group `Creature`
- [ ] Add group `Player`
- [ ] (Optional) Add group `Ally` only if your design needs it

### Why these matter

- `Creature` is required for persistence registration.
- `Player` is required for travel spawn selection and exit detection.

---

## 4) Minimum valid ally prefab checklist

For each ally in arena:

- [ ] Has `CreatureStats`
- [ ] Group `Creature`
- [ ] Group `Ally`
- [ ] **NOT** in group `Player`

---

## 5) Minimum valid enemy prefab + template checklist

Travel encounters are spawned from `CreatureTemplate_SO` resources.
Each template must point to a valid enemy scene.

### Enemy scene (prefab)

- [ ] Enemy scene exists (e.g., `res://Prefabs/Enemies/WolfEnemy.tscn`)
- [ ] Scene has `CreatureStats` (root or child named `CreatureStats`)
- [ ] Mesh/collision are present so it can exist in world

### Enemy template (`CreatureTemplate_SO`)

- [ ] Resource exists (e.g., `res://Data/Creatures/WolfEnemyTemplate.tres`)
- [ ] `CharacterPrefab` set to your enemy scene
- [ ] `NaturalEnvironmentProperties` contains at least one biome-compatible property (or leave biome filter empty)

---

## 6) Minimum valid travel biome resource checklist

Create a resource: `res://Data/Biomes/TestBiome.tres` of type `BiomeTravelDefinition`.

Set:

- [ ] `BiomeName` = `Test Biome`
- [ ] `ProceduralGenerationSettings` assigned
  - [ ] Width = 16
  - [ ] Height = 16
  - [ ] TileSize = 2
  - [ ] Seed = 0 (random each run)
- [ ] `EncounterCreaturePool` contains at least 1 enemy template
- [ ] `EncounterDensity` = 1.5 (good for testing)
- [ ] `GroupSpawnSettings` assigned (defaults okay)
- [ ] `EnvironmentalModifiers` assigned (defaults okay)
- [ ] `RequiredEnvironmentProperties` either empty OR matching enemy template ecology

Then assign this resource to `GamePhaseManager.TravelBiomeDefinition`.

---

## 7) Quick test procedure (exact)

1. Press Play (starts in Arena)
2. Press **T** (switch to Travel)
3. Confirm:
   - travel floor tiles appear
   - player is moved to travel spawn
   - ally is moved to ally spawn
4. Move around for encounter spawns
5. Walk to far corner exit zone
6. Confirm auto-return to Arena
7. Press **R** at any time to force arena return

---

## 8) Common failure checklist

If something seems broken, check these in order:

1. [ ] `GamePhaseManager` script attached and `ArenaRoot` assigned
2. [ ] `DebugPhaseInput` attached and `GamePhaseManagerPath` set
3. [ ] Player is in `Creature` and `Player` groups
4. [ ] Ally is in `Creature` and `Ally` groups
5. [ ] Travel biome assigned on manager
6. [ ] Encounter pool is not empty
7. [ ] Every encounter template has `CharacterPrefab` assigned
8. [ ] Player has collision/body that can enter Area3D exit

If #3 or #7 are wrong, Travel often appears to “do nothing”.

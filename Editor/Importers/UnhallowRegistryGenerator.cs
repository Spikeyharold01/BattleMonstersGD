#if TOOLS
using Godot;
using System.Collections.Generic;

[Tool]
public partial class UnhallowRegistryGenerator : EditorPlugin
{
    private readonly string[] targetSpells = new string[]
    {
        "aid", "bane", "bless", "cause fear", "darkness", "daylight", "death ward", 
        "deeper darkness", "detect magic", "detect good", "dimensional anchor", 
        "discern lies", "dispel magic", "endure elements", "freedom of movement", 
        "invisibility purge", "protection from energy", "remove fear", "resist energy", 
        "silence", "tongues", "zone of truth"
    };

    public override void _EnterTree()
    {
        AddToolMenuItem("Generate Unhallow Registry", new Callable(this, nameof(GenerateRegistry)));
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem("Generate Unhallow Registry");
    }

    public void GenerateRegistry()
    {
        string registryPath = "res://Data/Abilities/UnhallowAllowedSpells.tres";
        string searchDir = "res://Data/Abilities/";

        var registry = new UnhallowTiedSpellList_SO();
        registry.AllowedSpells = new Godot.Collections.Array<Ability_SO>();

        List<string> files = GetAllTresFiles(searchDir);

        foreach (string file in files)
        {
            var ability = GD.Load<Ability_SO>(file);
            if (ability != null)
            {
                string abilityNameLower = ability.AbilityName.ToLower();
                foreach (var target in targetSpells)
                {
                    if (abilityNameLower == target)
                    {
                        registry.AllowedSpells.Add(ability);
                        GD.Print($"[Unhallow Generator] Found and linked: {ability.AbilityName}");
                        break;
                    }
                }
            }
        }

        ResourceSaver.Save(registry, registryPath);
        GD.PrintRich($"[color=green]Successfully generated Unhallow Registry at {registryPath} with {registry.AllowedSpells.Count}/22 spells.[/color]");
    }

    private List<string> GetAllTresFiles(string path)
    {
        List<string> files = new List<string>();
        using var dir = DirAccess.Open(path);
        if (dir != null)
        {
            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (fileName != "")
            {
                if (dir.CurrentIsDir())
                {
                    if (fileName != "." && fileName != "..")
                        files.AddRange(GetAllTresFiles(path + fileName + "/"));
                }
                else if (fileName.EndsWith(".tres"))
                {
                    files.Add(path + fileName);
                }
                fileName = dir.GetNext();
            }
        }
        return files;
    }
}
#endif
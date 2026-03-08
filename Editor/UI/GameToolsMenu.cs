#if TOOLS
using Godot;
using System;

[Tool]
public partial class GameToolsMenu : EditorPlugin
{
    private PopupMenu _popup;
    private EditorFileDialog _fileDialog;
    private int _currentAction = -1; // 0 = Creature, 1 = Weapon, 2 = Range

    public override void _EnterTree()
    {
        _popup = new PopupMenu();
        _popup.AddItem("Import Creatures (XLSX)", 0);
        _popup.AddItem("Import Weapons (Folder)", 1);
        _popup.AddItem("Update Weapon Ranges (CSV)", 2);
        _popup.IdPressed += OnMenuIdPressed;

        AddToolSubmenuItem("Game Tools", _popup);

        _fileDialog = new EditorFileDialog();
        _fileDialog.FileSelected += OnFileSelected;
        _fileDialog.DirSelected += OnDirSelected;
        EditorInterface.Singleton.GetBaseControl().AddChild(_fileDialog);
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem("Game Tools");
        if (_popup != null) _popup.QueueFree();
        if (_fileDialog != null) _fileDialog.QueueFree();
    }

    private void OnMenuIdPressed(long id)
    {
        _currentAction = (int)id;
        _fileDialog.Filters = new string[] {}; // Reset filters

        switch (id)
        {
            case 0: // Creatures
                _fileDialog.Access = EditorFileDialog.AccessEnum.Filesystem;
                _fileDialog.FileMode = EditorFileDialog.FileModeEnum.OpenFile;
                _fileDialog.Filters = new string[] { "*.xlsx ; Excel Files" };
                _fileDialog.PopupCenteredRatio(0.6f);
                break;
            case 1: // Weapons (Folder)
                _fileDialog.Access = EditorFileDialog.AccessEnum.Filesystem;
                _fileDialog.FileMode = EditorFileDialog.FileModeEnum.OpenDir;
                _fileDialog.PopupCenteredRatio(0.6f);
                break;
            case 2: // Ranges
                _fileDialog.Access = EditorFileDialog.AccessEnum.Filesystem;
                _fileDialog.FileMode = EditorFileDialog.FileModeEnum.OpenFile;
                _fileDialog.Filters = new string[] { "*.csv ; CSV Files" };
                _fileDialog.PopupCenteredRatio(0.6f);
                break;
        }
    }

    private void OnFileSelected(string path)
    {
        // Convert res:// path to global system path for System.IO operations if necessary
        string globalPath = ProjectSettings.GlobalizePath(path);

        switch (_currentAction)
        {
            case 0:
                CreatureImporter.ImportCreatures(globalPath);
                break;
            case 2:
                WeaponRangeUpdater.UpdateWeaponRanges(globalPath);
                break;
        }
    }

    private void OnDirSelected(string path)
    {
        string globalPath = ProjectSettings.GlobalizePath(path);
        if (_currentAction == 1)
        {
            WeaponImporter.ImportWeapons(globalPath);
        }
    }
}
#endif
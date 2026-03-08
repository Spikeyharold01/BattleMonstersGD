using Godot;

[GlobalClass]
public partial class WeaponNameFilter_SO : Resource
{
    [Export] public string RequiredWeaponName;

    public bool IsWeaponValid(string weaponName)
    {
        if (string.IsNullOrEmpty(RequiredWeaponName) || string.IsNullOrEmpty(weaponName))
        {
            return false;
        }
        return weaponName.Equals(RequiredWeaponName, System.StringComparison.OrdinalIgnoreCase);
    }
}
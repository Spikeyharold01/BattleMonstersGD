using Godot;

// =================================================================================================
// FILE: TemporaryBanishmentController.cs
// PURPOSE: Generic runtime controller for temporary banishment/exile effects.
// ATTACH TO: Creature root as child node, or create dynamically.
// =================================================================================================
public partial class TemporaryBanishmentController : GridNode
{
    private CreatureStats owner;
    private Vector3 returnPosition;
    private int roundsRemaining;
    private int hpAtBanish;
    private bool sentToWrongPlane;

    private bool wasPlayer;
    private bool wasEnemy;
    private uint cachedCollisionLayer;
    private uint cachedCollisionMask;

    public bool IsBanished => roundsRemaining > 0;
    public int RoundsRemaining => roundsRemaining;

    public override void _Ready()
    {
        owner = GetParent<CreatureStats>();
    }

    public void ApplyTemporaryBanishment(int durationRounds, bool wrongPlane)
    {
        if (owner == null) return;

        int rolledDuration = Mathf.Max(1, durationRounds);

        if (IsBanished)
        {
            roundsRemaining = Mathf.Max(roundsRemaining, rolledDuration);
            sentToWrongPlane = sentToWrongPlane || wrongPlane;
            return;
        }

        returnPosition = owner.GlobalPosition;
        hpAtBanish = owner.CurrentHP;
        roundsRemaining = rolledDuration;
        sentToWrongPlane = wrongPlane;

        wasPlayer = owner.IsInGroup("Player");
        wasEnemy = owner.IsInGroup("Enemy");

        if (owner is CollisionObject3D collisionObject)
        {
            cachedCollisionLayer = collisionObject.CollisionLayer;
            cachedCollisionMask = collisionObject.CollisionMask;
            collisionObject.CollisionLayer = 0;
            collisionObject.CollisionMask = 0;
        }

        owner.Visible = false;
        if (wasPlayer) owner.RemoveFromGroup("Player");
        if (wasEnemy) owner.RemoveFromGroup("Enemy");

        GD.PrintRich($"[color=purple]{owner.Name} is banished for {roundsRemaining} round(s).[/color]");
    }

    public void AdvanceBanishmentRound()
    {
        if (!IsBanished) return;

        roundsRemaining--;
        if (roundsRemaining > 0)
        {
            GD.Print($"{owner.Name} remains banished for {roundsRemaining} more round(s).");
            return;
        }

        ReturnFromBanishment();
    }

    private void ReturnFromBanishment()
    {
        owner.GlobalPosition = returnPosition;
        owner.Visible = true;

        if (owner is CollisionObject3D collisionObject)
        {
            collisionObject.CollisionLayer = cachedCollisionLayer;
            collisionObject.CollisionMask = cachedCollisionMask;
        }

        if (wasPlayer) owner.AddToGroup("Player");
        if (wasEnemy) owner.AddToGroup("Enemy");

        if (sentToWrongPlane)
        {
            int desiredHp = Mathf.Max(1, Mathf.FloorToInt(hpAtBanish * 0.9f));
            if (owner.CurrentHP > desiredHp)
            {
                int hpLoss = owner.CurrentHP - desiredHp;
                owner.TakeDamage(hpLoss, "Planar", null);
                GD.PrintRich($"[color=orange]{owner.Name} returns from the wrong plane and loses {hpLoss} HP.[/color]");
            }
        }

        sentToWrongPlane = false;
        GD.PrintRich($"[color=purple]{owner.Name} returns from banishment.[/color]");
    }
}
using Godot;

namespace FortressRush;

/// <summary>A single destructible wall block. Blocks sit on lanes and stop runners until destroyed.</summary>
public partial class Block : MeshInstance3D
{
    public float Hp;
    public Fortress Fortress;   // owning fortress (renamed: Node already owns "Owner")
    public bool IsCore;
    public int LaneIdx; // global lane 0..5; -1 for off-lane decoration

    public void TakeDamage(float dmg)
    {
        Hp -= dmg;
        if (Hp <= 0f) DestroySelf();
    }

    private void DestroySelf()
    {
        Fortress?.OnBlockRemoved(this);
        var fx = FX.BlockBurst(GlobalPosition, Game.ColorOf(Fortress.Team), IsCore ? 24 : 12);
        Game.Instance?.AddChild(fx);
        QueueFree();
    }
}

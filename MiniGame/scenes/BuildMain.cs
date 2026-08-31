using Godot;

namespace FortressRush;

/// <summary>
/// Build-time scene generation (per the engine guide): run once headless to emit
/// scenes/Main.tscn. `godot --headless --script scenes/BuildMain.cs`
/// </summary>
public partial class BuildMain : SceneTree
{
    public override void _Initialize()
    {
        // Direct C# instantiation avoids SetScript() and its wrapper-disposal trap.
        var main = new Game { Name = "Main" };

        var packed = new PackedScene();
        if (packed.Pack(main) != Error.Ok)
        {
            GD.PushError("Pack(Main) failed");
            Quit(1);
            return;
        }

        // validate the pack: instantiate and compare node counts before saving
        int expected = CountNodes(main);
        var test = packed.Instantiate();
        int got = CountNodes(test);
        test.Free();
        if (got < expected)
        {
            GD.PushError($"Serialization dropped nodes: expected {expected}, got {got}");
            Quit(1);
            return;
        }

        var err = ResourceSaver.Save(packed, "res://scenes/Main.tscn");
        if (err != Error.Ok)
        {
            GD.PushError($"Save failed: {err}");
            Quit(1);
            return;
        }
        GD.Print($"Main.tscn saved (nodes={got})");
        main.Free();
        Quit(0);
    }

    private static int CountNodes(Node n)
    {
        int c = 1;
        foreach (var child in n.GetChildren())
            c += CountNodes(child);
        return c;
    }
}

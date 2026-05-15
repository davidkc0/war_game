namespace WarGame;

using Godot;
using Chickensoft.GameTools.Displays;

#if RUN_TESTS
using System.Reflection;
using Chickensoft.GoDotTest;
using Chickensoft.GodotNodeInterfaces;
#endif

// This entry-point file is responsible for determining if we should run tests.
//
// If you want to edit your game's main entry-point, please see Game.tscn and
// Game.cs instead.

public partial class Main : Node2D
{
  // Template default was Display.UHD4k (3840x2160) which makes everything
  // we draw render at ~1/3 of the actual window size on a 1280x720 display.
  // Using window-native 1600x900 so 1 logical px == 1 actual px and the
  // RTS map has room to breathe. The window is resizable; Game.cs
  // recalculates the layout when the viewport size changes.
  public Vector2I DesignResolution => new(1600, 900);
#if RUN_TESTS
  public TestEnvironment Environment = default!;
#endif

  public override void _Ready()
  {
    // Chickensoft's LookGood(UIFixed) was forcing the viewport to a fixed
    // design resolution and letterboxing inside the window — the opposite
    // of what an RTS wants. Skipping it; viewport size now follows window
    // size via the project's "disabled" stretch mode (Game.cs handles the
    // SizeChanged signal to recenter the map).

#if RUN_TESTS
    // If this is a debug build, use GoDotTest to examine the
    // command line arguments and determine if we should run tests.
    Environment = TestEnvironment.From(OS.GetCmdlineArgs());
    if (Environment.ShouldRunTests)
    {
      RuntimeContext.IsTesting = true;
      CallDeferred("RunTests");
      return;
    }
#endif

    // If we don't need to run tests, we can just switch to the game scene.
    CallDeferred("RunScene");
  }

#if RUN_TESTS
  private void RunTests()
    => _ = GoTest.RunTests(Assembly.GetExecutingAssembly(), this, Environment);
#endif

  private void RunScene()
    => GetTree().ChangeSceneToFile("res://src/MatchSetup.tscn");
}

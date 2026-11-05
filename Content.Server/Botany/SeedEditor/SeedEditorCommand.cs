using Content.Server.Administration;
using Robust.Shared.Console;

namespace Content.Server.Botany.SeedEditor;

/// <summary>
/// Opens the seed editor.
/// </summary>
[AdminCommand(SeedEditorSystem.RequiredFlags)]
public sealed partial class SeedEditorCommand : LocalizedEntityCommands
{
    [Dependency] private SeedEditorSystem _seedEditor = default!;

    public override string Command => "seededitor";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _seedEditor.OpenEditor(player);
    }
}

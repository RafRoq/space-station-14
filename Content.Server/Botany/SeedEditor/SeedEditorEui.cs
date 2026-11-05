using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Botany.SeedEditor;
using Content.Shared.Eui;

namespace Content.Server.Botany.SeedEditor;

/// <summary>
/// Admin window to edit a seed's stats, traits, chemicals and gases and get a packet of it.
/// </summary>
public sealed partial class SeedEditorEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly SeedEditorData? _initialData;

    public SeedEditorEui(SeedEditorData? initialData)
    {
        IoCManager.InjectDependencies(this);
        _initialData = initialData;
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new SeedEditorEuiState(_initialData);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not (SeedEditorRequestSpeciesMessage or SeedEditorSpawnSeedMessage))
            return;

        if (!_admin.HasAdminFlag(Player, SeedEditorSystem.RequiredFlags))
        {
            Close();
            return;
        }

        var seedEditor = _entityManager.System<SeedEditorSystem>();
        if (msg is SeedEditorRequestSpeciesMessage request)
        {
            SendMessage(new SeedEditorDataMessage(seedEditor.CreateData(request.Species)));
            return;
        }

        var spawn = (SeedEditorSpawnSeedMessage) msg;
        if (Player.AttachedEntity is not { } user)
        {
            SendMessage(new SeedEditorResultMessage(Loc.GetString("seed-editor-error-no-entity")));
            return;
        }

        seedEditor.TrySpawnSeed(user, spawn.Data, out _, out var error);
        SendMessage(new SeedEditorResultMessage(error));
    }
}

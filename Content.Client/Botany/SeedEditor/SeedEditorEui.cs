using Content.Client.Eui;
using Content.Shared.Botany.SeedEditor;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client.Botany.SeedEditor;

[UsedImplicitly]
public sealed partial class SeedEditorEui : BaseEui
{
    private readonly SeedEditorWindow _window;

    public SeedEditorEui()
    {
        _window = new SeedEditorWindow();
        _window.OnClose += OnWindowClosed;
        _window.OnSpawnSeed += data => SendMessage(new SeedEditorSpawnSeedMessage(data));
        _window.OnRequestSpecies += species => SendMessage(new SeedEditorRequestSpeciesMessage(species));
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.OnClose -= OnWindowClosed;
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is SeedEditorEuiState { Data: { } data })
            _window.LoadData(data);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case SeedEditorDataMessage data:
                _window.LoadData(data.Data);
                break;
            case SeedEditorResultMessage result:
                _window.ShowResult(result.Error);
                break;
        }
    }

    private void OnWindowClosed()
    {
        SendMessage(new CloseEuiMessage());
    }
}

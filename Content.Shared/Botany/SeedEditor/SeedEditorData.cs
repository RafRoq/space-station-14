using Content.Shared.Atmos;
using Content.Shared.Botany.Traits;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Eui;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Botany.SeedEditor;

/// <summary>
/// Everything the seed editor can change about a seed.
/// </summary>
[Serializable, NetSerializable]
public sealed class SeedEditorData
{
    /// <summary>
    /// The plant species of the seed.
    /// </summary>
    public EntProtoId Species;

    /// <summary>
    /// The simple values (numbers, toggles, enums) of the plant components the seed carries,
    /// written the way they would be in YAML: component name, then data field name, then value.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Fields = new();

    /// <summary>
    /// The allowed values of the fields that only take some values, like enums: component name, then data field name.
    /// </summary>
    public Dictionary<string, Dictionary<string, List<string>>> FieldOptions = new();

    /// <summary>
    /// The heritable traits of the plant.
    /// </summary>
    public List<ProtoId<PlantTraitPrototype>> Traits = new();

    /// <summary>
    /// The names of the trait components of the plant, like the kudzu or seedless traits.
    /// </summary>
    public HashSet<string> TraitComponents = new();

    /// <summary>
    /// The chemicals in the plant's produce.
    /// </summary>
    public Dictionary<ProtoId<ReagentPrototype>, SeedEditorChemical> Chemicals = new();

    /// <summary>
    /// Gases the plant consumes, per growth tick.
    /// </summary>
    public Dictionary<Gas, float> ConsumeGasses = new();

    /// <summary>
    /// Gases the plant exudes, per growth tick.
    /// </summary>
    public Dictionary<Gas, float> ExudeGasses = new();
}

/// <summary>
/// The amount of a chemical in a plant's produce, see <see cref="Components.PlantChemQuantity"/>.
/// </summary>
[Serializable, NetSerializable]
public record struct SeedEditorChemical(FixedPoint2 Min, FixedPoint2 Max, float PotencyDivisor);

/// <summary>
/// Sent to the seed editor when it opens on an existing seed or plant, to edit a copy of it.
/// </summary>
[Serializable, NetSerializable]
public sealed class SeedEditorEuiState(SeedEditorData? data) : EuiStateBase
{
    public readonly SeedEditorData? Data = data;
}

/// <summary>
/// Asks the server for the default editor data of a plant species.
/// </summary>
[Serializable, NetSerializable]
public sealed class SeedEditorRequestSpeciesMessage(EntProtoId species) : EuiMessageBase
{
    public readonly EntProtoId Species = species;
}

/// <summary>
/// Editor data for the seed editor to show.
/// </summary>
[Serializable, NetSerializable]
public sealed class SeedEditorDataMessage(SeedEditorData data) : EuiMessageBase
{
    public readonly SeedEditorData Data = data;
}

/// <summary>
/// Asks the server for a seed packet made from the edited data.
/// </summary>
[Serializable, NetSerializable]
public sealed class SeedEditorSpawnSeedMessage(SeedEditorData data) : EuiMessageBase
{
    public readonly SeedEditorData Data = data;
}

/// <summary>
/// Tells the seed editor whether its last seed packet was made, and why not.
/// </summary>
[Serializable, NetSerializable]
public sealed class SeedEditorResultMessage(string? error) : EuiMessageBase
{
    public readonly string? Error = error;
}

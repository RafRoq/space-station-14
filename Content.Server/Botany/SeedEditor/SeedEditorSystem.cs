using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.SeedEditor;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Server.Botany.SeedEditor;

/// <summary>
/// Makes seed packets from <see cref="SeedEditorData"/> and lets admins open the seed editor on seeds and plants.
/// </summary>
public sealed partial class SeedEditorSystem : SharedSeedEditorSystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private EuiManager _eui = default!;

    /// <summary>
    /// The admin permission needed to use the seed editor.
    /// </summary>
    public const AdminFlags RequiredFlags = AdminFlags.Spawn;

    [SubscribeLocalEvent]
    private void OnSeedVerbs(Entity<SeedComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        AddEditVerb(ref args, ent.Comp.PlantProtoId, () => ent.Comp.PlantData);
    }

    [SubscribeLocalEvent]
    private void OnProduceVerbs(Entity<ProduceComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (ent.Comp.PlantProtoId is { } species)
            AddEditVerb(ref args, species, () => ent.Comp.PlantData);
    }

    [SubscribeLocalEvent]
    private void OnPlantVerbs(Entity<PlantComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (MetaData(ent).EntityPrototype?.ID is { } species)
            AddEditVerb(ref args, species, () => ent.Owner);
    }

    /// <summary>
    /// Adds the verb that opens the seed editor on a copy of a seed or plant.
    /// </summary>
    /// <param name="species">The plant species.</param>
    /// <param name="getPlant">Gets the plant or plant snapshot to copy, if any, when the verb is used.</param>
    private void AddEditVerb(ref GetVerbsEvent<Verb> args, EntProtoId species, Func<EntityUid?> getPlant)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) || !_admin.HasAdminFlag(args.User, RequiredFlags))
            return;

        var session = actor.PlayerSession;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("seed-editor-verb"),
            Category = VerbCategory.Debug,
            Icon = new SpriteSpecifier.Rsi(new ResPath("/Textures/Objects/Specific/Hydroponics/seeds.rsi"), "seed"),
            Act = () =>
            {
                // Read the plant when the verb is used, so the editor gets its current state.
                var data = getPlant() is { } plant && Exists(plant)
                    ? CreateData(species, plant)
                    : CreateData(species);
                OpenEditor(session, data);
            },
            Impact = LogImpact.Low,
        });
    }

    /// <summary>
    /// Opens the seed editor for an admin, optionally editing a copy of an existing seed.
    /// </summary>
    public void OpenEditor(ICommonSession session, SeedEditorData? data = null)
    {
        _eui.OpenEui(new SeedEditorEui(data), session);
    }

    /// <summary>
    /// Makes a seed packet from the editor data and puts it in the user's hands.
    /// </summary>
    /// <param name="user">Who gets the seed packet.</param>
    /// <param name="data">The seed to make.</param>
    /// <param name="seed">The seed packet, if it was made.</param>
    /// <param name="error">Why the seed packet could not be made.</param>
    public bool TrySpawnSeed(
        EntityUid user,
        SeedEditorData data,
        [NotNullWhen(true)] out EntityUid? seed,
        [NotNullWhen(false)] out string? error)
    {
        seed = null;
        if (!ProtoMan.TryIndex(data.Species, out var species)
            || !species.TryGetComponent<PlantComponent>(out _, Factory)
            || !species.TryGetComponent<PlantDataComponent>(out var plantData, Factory))
        {
            error = Loc.GetString("seed-editor-error-species", ("species", data.Species));
            return false;
        }

        // Build the plant snapshot the seed carries the same way plant snapshots are cloned:
        // an uninitialized entity that only holds the plant components.
        var snapshot = EntityManager.CreateEntityUninitialized(null);
        try
        {
            foreach (var registration in GetSeedComponents())
            {
                if (IsTraitComponent(registration))
                {
                    if (data.TraitComponents.Contains(registration.Name))
                        EntityManager.AddComponent(snapshot, Factory.GetComponent(registration));

                    continue;
                }

                if (!species.Components.TryGetValue(registration.Name, out var entry))
                    continue;

                if (!TryCreateComponent(registration, entry.Component, data, out var component, out error))
                    return false;

                EntityManager.AddComponent(snapshot, component);
            }

            EntityManager.InitializeAndStartEntity(snapshot, doMapInit: false);
            seed = Botany.SpawnSeedPacket(plantData, data.Species, snapshot, Transform(user).Coordinates, user);
        }
        finally
        {
            // The seed packet stores its own copy of the snapshot.
            Del(snapshot);
        }

        _adminLog.Add(LogType.EntitySpawn,
            LogImpact.Medium,
            $"{ToPrettyString(user):user} made an edited {data.Species} seed {ToPrettyString(seed.Value):seed} with the seed editor");

        error = null;
        return true;
    }

    /// <summary>
    /// Creates a plant component from the species' version of it, with the edited values.
    /// Goes through serialization, like loading the component from YAML would.
    /// </summary>
    private bool TryCreateComponent(
        ComponentRegistration registration,
        IComponent speciesComponent,
        SeedEditorData data,
        [NotNullWhen(true)] out IComponent? component,
        [NotNullWhen(false)] out string? error)
    {
        component = null;
        if (Serialization.WriteValue(registration.Type, speciesComponent, alwaysWrite: true, notNullableOverride: true)
            is not MappingDataNode mapping)
        {
            error = Loc.GetString("seed-editor-error-component", ("component", registration.Name));
            return false;
        }

        if (data.Fields.TryGetValue(registration.Name, out var fields))
        {
            foreach (var (field, value) in fields)
            {
                mapping[field] = new ValueDataNode(value);
            }
        }

        AddCollectionFields(registration.Type, mapping, data);

        var errors = Serialization.ValidateNode(registration.Type, mapping).GetErrors().ToList();
        if (errors.Count > 0)
        {
            var reasons = string.Join("; ", errors.Select(e => $"{e.Node}: {e.ErrorReason}"));
            error = Loc.GetString("seed-editor-error-invalid", ("component", registration.Name), ("reasons", reasons));
            return false;
        }

        component = (IComponent) Serialization.Read(registration.Type, mapping, notNullableOverride: true)!;
        error = null;
        return true;
    }

    /// <summary>
    /// Writes the edited traits, chemicals and gases into the serialized component.
    /// </summary>
    private void AddCollectionFields(Type type, MappingDataNode mapping, SeedEditorData data)
    {
        if (type == typeof(PlantComponent))
        {
            mapping["traits"] = new SequenceDataNode(data.Traits.Select(trait => new ValueDataNode(trait.Id)).ToList<DataNode>());
        }
        else if (type == typeof(PlantChemicalsComponent))
        {
            // Chemicals the species does not have by default are not inherent, like ones gained from mutations,
            // so they are kept when the plant changes species.
            var speciesChemicals = CreateData(data.Species).Chemicals;

            var chemicals = new MappingDataNode();
            foreach (var (reagent, quantity) in data.Chemicals)
            {
                chemicals[reagent.Id] = new MappingDataNode
                {
                    ["min"] = new ValueDataNode(quantity.Min.Float().ToString(CultureInfo.InvariantCulture)),
                    ["max"] = new ValueDataNode(quantity.Max.Float().ToString(CultureInfo.InvariantCulture)),
                    ["potencyDivisor"] = new ValueDataNode(quantity.PotencyDivisor.ToString(CultureInfo.InvariantCulture)),
                    ["inherent"] = new ValueDataNode(speciesChemicals.ContainsKey(reagent).ToString()),
                };
            }

            mapping["chemicals"] = chemicals;
        }
        else if (type == typeof(PlantConsumeExudeGasComponent))
        {
            mapping["consumeGasses"] = GasMapping(data.ConsumeGasses);
            mapping["exudeGasses"] = GasMapping(data.ExudeGasses);
        }
    }

    /// <inheritdoc/>
    protected override List<string>? GetFieldOptions(Type componentType, string field)
    {
        // Data fields are the member names in camel case by default.
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var memberType = componentType.GetField(field, flags)?.FieldType
            ?? componentType.GetProperty(field, flags)?.PropertyType;

        return memberType is { IsEnum: true } ? Enum.GetNames(memberType).ToList() : null;
    }

    private static MappingDataNode GasMapping(Dictionary<Content.Shared.Atmos.Gas, float> gases)
    {
        var mapping = new MappingDataNode();
        foreach (var (gas, amount) in gases)
        {
            mapping[gas.ToString()] = new ValueDataNode(amount.ToString(CultureInfo.InvariantCulture));
        }

        return mapping;
    }
}

using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Botany.Traits.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Shared.Botany.SeedEditor;

/// <summary>
/// Turns seeds and plants into <see cref="SeedEditorData"/> for the admin seed editor.
/// </summary>
/// <remarks>
/// The editor works on the components that seeds carry, as listed by the plant snapshot cloning settings,
/// so new plant stats show up in it without changes here.
/// </remarks>
public abstract partial class SharedSeedEditorSystem : EntitySystem
{
    [Dependency] protected BotanySystem Botany = default!;
    [Dependency] protected ISerializationManager Serialization = default!;

    /// <summary>
    /// The components that a seed carries, in a stable order.
    /// </summary>
    [PublicAPI]
    public List<ComponentRegistration> GetSeedComponents()
    {
        var components = new List<ComponentRegistration>();
        if (!ProtoMan.TryIndex(Botany.SettingsId, out var settings))
            return components;

        foreach (var name in settings.Components)
        {
            if (Factory.TryGetRegistration(name, out var registration))
                components.Add(registration);
        }

        components.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return components;
    }

    /// <summary>
    /// Whether a component is one of the plant trait components, which the editor shows as toggles.
    /// </summary>
    [PublicAPI]
    public static bool IsTraitComponent(ComponentRegistration registration)
    {
        return registration.Type.IsAssignableTo(typeof(PlantTraitsComponent));
    }

    /// <summary>
    /// Gets the editor data of a plant species, as it grows by default.
    /// </summary>
    [PublicAPI]
    public SeedEditorData CreateData(EntProtoId species)
    {
        var data = new SeedEditorData { Species = species };
        if (!ProtoMan.TryIndex(species, out var proto))
            return data;

        foreach (var registration in GetSeedComponents())
        {
            if (proto.Components.TryGetValue(registration.Name, out var entry))
                AddComponentData(data, registration, entry.Component);
        }

        return data;
    }

    /// <summary>
    /// Gets the editor data of an existing plant, or of a plant snapshot such as the one stored in a seed.
    /// </summary>
    [PublicAPI]
    public SeedEditorData CreateData(EntProtoId species, EntityUid plant)
    {
        var data = new SeedEditorData { Species = species };
        foreach (var registration in GetSeedComponents())
        {
            if (EntityManager.TryGetComponent(plant, registration.Type, out var component))
                AddComponentData(data, registration, component);
        }

        return data;
    }

    private void AddComponentData(SeedEditorData data, ComponentRegistration registration, IComponent component)
    {
        if (IsTraitComponent(registration))
        {
            data.TraitComponents.Add(registration.Name);
            return;
        }

        switch (component)
        {
            case PlantComponent plant:
                data.Traits.AddRange(plant.Traits);
                break;
            case PlantChemicalsComponent chemicals:
                foreach (var (reagent, quantity) in chemicals.Chemicals)
                {
                    data.Chemicals[reagent] = new SeedEditorChemical(quantity.Min, quantity.Max, quantity.PotencyDivisor);
                }
                break;
            case PlantConsumeExudeGasComponent gases:
                data.ConsumeGasses = new(gases.ConsumeGasses);
                data.ExudeGasses = new(gases.ExudeGasses);
                break;
        }

        // Every simple value becomes an editable field; lists and dictionaries are edited above.
        if (Serialization.WriteValue(registration.Type, component, alwaysWrite: true, notNullableOverride: true)
            is not MappingDataNode mapping)
            return;

        var fields = new Dictionary<string, string>();
        var options = new Dictionary<string, List<string>>();
        foreach (var (field, node) in mapping)
        {
            if (node is not ValueDataNode value)
                continue;

            fields[field] = value.Value;
            if (GetFieldOptions(registration.Type, field) is { } fieldOptions)
                options[field] = fieldOptions;
        }

        if (fields.Count > 0)
            data.Fields[registration.Name] = fields;

        if (options.Count > 0)
            data.FieldOptions[registration.Name] = options;
    }

    /// <summary>
    /// Gets the allowed values of a field that only takes some values, like an enum.
    /// Finding a field's type takes reflection, which client content code is not allowed to use,
    /// so editor data with these options comes from the server.
    /// </summary>
    protected virtual List<string>? GetFieldOptions(Type componentType, string field)
    {
        return null;
    }
}

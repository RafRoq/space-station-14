#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Botany.SeedEditor;
using Content.Shared.Atmos;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.SeedEditor;
using Content.Shared.Botany.Traits;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Botany;

[TestOf(typeof(SeedEditorSystem))]
public sealed class SeedEditorTest : GameTest
{
    private static readonly EntProtoId TomatoPlants = "TomatoPlants";
    private static readonly ProtoId<PlantTraitPrototype> Squash = "Squash";
    private static readonly ProtoId<PlantTraitPrototype> Slippery = "Slippery";

    [SidedDependency(Side.Server)] private readonly SeedEditorSystem _seedEditor = null!;

    [Test]
    public async Task SpeciesDataMatchesPrototype()
    {
        await Server.WaitAssertion(() =>
        {
            var proto = SProtoMan.Index(TomatoPlants);
            Assert.That(proto.TryGetComponent<PlantComponent>(out var plant, SEntMan.ComponentFactory), Is.True);
            Assert.That(proto.TryGetComponent<PlantHarvestComponent>(out var harvest, SEntMan.ComponentFactory), Is.True);

            var data = _seedEditor.CreateData(TomatoPlants);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(float.Parse(data.Fields["Plant"]["potency"], System.Globalization.CultureInfo.InvariantCulture),
                    Is.EqualTo(plant!.Potency));
                Assert.That(data.Fields["PlantHarvest"]["harvestRepeat"], Is.EqualTo(Enum.GetName(harvest!.HarvestRepeat)));
                Assert.That(data.Traits, Does.Contain(Squash));
                Assert.That(data.Chemicals, Is.Not.Empty);

                // Enum fields come with their allowed values, so the window can show a dropdown.
                Assert.That(data.FieldOptions["PlantHarvest"]["harvestRepeat"], Is.EquivalentTo(Enum.GetNames<HarvestType>()));
                Assert.That(data.FieldOptions.TryGetValue("Plant", out var plantOptions) && plantOptions.ContainsKey("potency"),
                    Is.False);
            }
        });
    }

    [Test]
    public async Task SpawnedSeedHasEditedValues()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SSpawnAtPosition("MobHuman", map.GridCoords);

            var data = _seedEditor.CreateData(TomatoPlants);
            data.Fields["Plant"]["potency"] = "42";
            data.Fields["PlantHarvest"]["harvestRepeat"] = nameof(HarvestType.SelfHarvest);
            data.Traits.Remove(Squash);
            data.Traits.Add(Slippery);
            data.TraitComponents.Add("PlantTraitScream");
            data.Chemicals["Omnizine"] = new SeedEditorChemical(FixedPoint2.New(2), FixedPoint2.New(20), 5);
            data.ConsumeGasses[Gas.Plasma] = 0.5f;

            Assert.That(_seedEditor.TrySpawnSeed(user, data, out var seed, out var error), Is.True, error);

            var seedComp = SComp<SeedComponent>(seed!.Value);
            Assert.That(seedComp.PlantProtoId, Is.EqualTo(TomatoPlants));
            Assert.That(seedComp.PlantData, Is.Not.Null);

            // Read the seed back the way the editor would show it.
            var result = _seedEditor.CreateData(TomatoPlants, seedComp.PlantData!.Value);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Fields["Plant"]["potency"], Is.EqualTo("42"));
                Assert.That(result.Fields["PlantHarvest"]["harvestRepeat"], Is.EqualTo(nameof(HarvestType.SelfHarvest)));
                Assert.That(result.Traits, Does.Contain(Slippery));
                Assert.That(result.Traits, Does.Not.Contain(Squash));
                Assert.That(result.TraitComponents, Does.Contain("PlantTraitScream"));
                Assert.That(result.Chemicals["Omnizine"], Is.EqualTo(data.Chemicals["Omnizine"]));
                Assert.That(result.ConsumeGasses[Gas.Plasma], Is.EqualTo(0.5f));
            }

            // A chemical the species does not grow by default counts as a mutated one.
            var chemicals = SComp<PlantChemicalsComponent>(seedComp.PlantData.Value).Chemicals;
            Assert.That(chemicals["Omnizine"].Inherent, Is.False);
        });
    }

    [Test]
    public async Task InvalidValuesAreRejected()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SSpawnAtPosition("MobHuman", map.GridCoords);
            var seedsBefore = SEntMan.EntityQuery<SeedComponent>().Count();

            var data = _seedEditor.CreateData(TomatoPlants);
            data.Fields["Plant"]["potency"] = "very";

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_seedEditor.TrySpawnSeed(user, data, out var seed, out var error), Is.False);
                Assert.That(seed, Is.Null);
                Assert.That(error, Is.Not.Null);
                Assert.That(SEntMan.EntityQuery<SeedComponent>().Count(), Is.EqualTo(seedsBefore));
            }
        });
    }

    /// <summary>
    /// The editor works on serialized component data, so make sure every species survives the round trip.
    /// </summary>
    [Test]
    public async Task EverySpeciesCanBeSpawnedUnchanged()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SSpawnAtPosition("MobHuman", map.GridCoords);
            var species = SProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract
                    && p.TryGetComponent<PlantComponent>(out _, SEntMan.ComponentFactory)
                    && p.TryGetComponent<PlantDataComponent>(out _, SEntMan.ComponentFactory))
                .Select(p => p.ID)
                .ToList();
            Assert.That(species, Is.Not.Empty);

            using (Assert.EnterMultipleScope())
            {
                foreach (var id in species)
                {
                    var data = _seedEditor.CreateData(id);
                    Assert.That(_seedEditor.TrySpawnSeed(user, data, out var seed, out var error), Is.True, $"{id}: {error}");
                    if (seed != null)
                        SDeleteNow(seed.Value);
                }
            }
        });
    }
}

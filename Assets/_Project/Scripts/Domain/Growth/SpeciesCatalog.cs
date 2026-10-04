using System;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>Lookup of the three species configs by genome species id.</summary>
    public sealed class SpeciesCatalog
    {
        readonly SpeciesConfig[] _bySpecies = new SpeciesConfig[3];

        public SpeciesCatalog(SpeciesConfig kuromatsu, SpeciesConfig momiji, SpeciesConfig yamazakura)
        {
            _bySpecies[(int)SpeciesId.Kuromatsu] = kuromatsu ?? throw new ArgumentNullException(nameof(kuromatsu));
            _bySpecies[(int)SpeciesId.Momiji] = momiji ?? throw new ArgumentNullException(nameof(momiji));
            _bySpecies[(int)SpeciesId.Yamazakura] = yamazakura ?? throw new ArgumentNullException(nameof(yamazakura));
        }

        public static SpeciesCatalog CreateDefault() =>
            new SpeciesCatalog(SpeciesConfig.Kuromatsu(), SpeciesConfig.Momiji(), SpeciesConfig.Yamazakura());

        public SpeciesConfig Get(SpeciesId id)
        {
            int index = (int)id;
            return (uint)index < (uint)_bySpecies.Length ? _bySpecies[index] : _bySpecies[0];
        }
    }
}

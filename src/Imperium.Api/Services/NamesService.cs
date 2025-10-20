using System.Text;

namespace Imperium.Api.Services;

public class NamesService
{
    private readonly string[] _malePraenomina = new[]
    {
        "Marcus","Gaius","Lucius","Publius","Quintus","Titus","Gnaeus","Sextus","Aulus","Spurius",
        "Decimus","Tiberius","Servius","Appius","Numerius","Manius","Faustus","Flavius","Julius","Claudius",
        "Cassius","Brutus","Pompeius","Antonius","Varro","Caius","Longinus","Valerius","Cornelius","Aemilius"
    };

    private readonly string[] _femalePraenomina = new[]
    {
        "Aurelia","Julia","Claudia","Cornelia","Valeria","Livia","Flavia","Marcia","Tullia","Antonia",
        "Octavia","Agrippina","Calpurnia","Aemilia","Domitia","Fabia","Lucretia","Sabina","Crispina","Helena",
        "Cassia","Drusilla","Lollia","Pompeia","Severina","Vibia","Flaminia","Licinia","Sulpicia","Terentia"
    };

    private readonly string[] _nomina = new[]
    {
        "Aurelius","Julius","Claudius","Cornelius","Valerius","Flavius","Tullius","Antonius","Octavius","Calpurnius",
        "Aemilius","Domitius","Fabius","Lucretius","Sabinus","Crispinus","Helenaeus","Cassius","Drusus","Lollius",
        "Pompeius","Severus","Vibius","Flaminius","Licinius","Sulpicius","Terentius","Varro","Longinus","Vergilius",
        "Marcellus","Catullus","Tacitus","Ovidius","Livianus","Galenus","Seneca","Hadrianus","Traianus","Nervanus"
    };

    private readonly string[] _cognomina = new[]
    {
        "Maximus","Minor","Felix","Celer","Rufus","Severus","Niger","Varus","Lupus","Aquila",
        "Cato","Cicero","Scaevola","Gracchus","Paullus","Scaurus","Crassus","Regulus","Lenticus","Aratus",
        "Callidus","Fortis","Sagitta","Silvanus","Agricola","Marinus","Navarchus","Scriba","Mercator","Fabri",
        "Argentus","Vitruvius","Candidus","Serenus","Justus","Pius","Urbanus","Victor","Sabinus","Germanicus"
    };

    public IEnumerable<(string Name, bool IsFemale)> GenerateNames(int count, int? seed = null)
    {
        var rnd = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxTries = count * 10;
        int tries = 0;
        while (seen.Count < count && tries < maxTries)
        {
            tries++;
            bool female = rnd.NextDouble() < 0.45; // немного меньше половины
            var praenomen = female ? _femalePraenomina[rnd.Next(_femalePraenomina.Length)] : _malePraenomina[rnd.Next(_malePraenomina.Length)];
            var nomen = _nomina[rnd.Next(_nomina.Length)];
            var cognomen = _cognomina[rnd.Next(_cognomina.Length)];
            var full = new StringBuilder()
                .Append(praenomen).Append(' ')
                .Append(nomen).Append(' ')
                .Append(cognomen)
                .ToString();
            if (seen.Add(full))
            {
                yield return (full, female);
            }
        }
    }

    public IEnumerable<(string Name, bool IsFemale)> GenerateNamesUnique(int count, ISet<string> alreadyUsed, int? seed = null)
    {
        var rnd = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        var seen = new HashSet<string>(alreadyUsed ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var produced = 0;
        var maxTries = count * 20; // allow more attempts when skipping used names
        int tries = 0;
        while (produced < count && tries < maxTries)
        {
            tries++;
            bool female = rnd.NextDouble() < 0.45;
            var praenomen = female ? _femalePraenomina[rnd.Next(_femalePraenomina.Length)] : _malePraenomina[rnd.Next(_malePraenomina.Length)];
            var nomen = _nomina[rnd.Next(_nomina.Length)];
            var cognomen = _cognomina[rnd.Next(_cognomina.Length)];
            var full = new StringBuilder().Append(praenomen).Append(' ').Append(nomen).Append(' ').Append(cognomen).ToString();
            if (seen.Add(full))
            {
                produced++;
                yield return (full, female);
            }
        }
    }
}

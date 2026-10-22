namespace Corico.Core.Backup;

/// <summary>
/// Conservazione dei backup locali (sez. 6.2): tutti quelli delle ultime 48 ore,
/// il più recente di ogni giorno per 30 giorni, il più recente di ogni mese per 12 mesi.
/// </summary>
public static class PoliticaConservazione
{
    public static IReadOnlyList<T> DaEliminare<T>(IEnumerable<T> backup, Func<T, DateTime> dataOra, DateTime adesso)
    {
        var ordinati = backup.OrderByDescending(dataOra).ToList();
        var daTenere = new HashSet<T>();

        foreach (var b in ordinati.Where(b => adesso - dataOra(b) <= TimeSpan.FromHours(48)))
            daTenere.Add(b);

        foreach (var gruppo in ordinati.Where(b => adesso - dataOra(b) <= TimeSpan.FromDays(30)).GroupBy(b => dataOra(b).Date))
            daTenere.Add(gruppo.First());

        foreach (var gruppo in ordinati.Where(b => dataOra(b) > adesso.AddMonths(-12)).GroupBy(b => (dataOra(b).Year, dataOra(b).Month)))
            daTenere.Add(gruppo.First());

        // Il backup più recente non viene mai eliminato.
        if (ordinati.Count > 0) daTenere.Add(ordinati[0]);

        return ordinati.Where(b => !daTenere.Contains(b)).ToList();
    }
}

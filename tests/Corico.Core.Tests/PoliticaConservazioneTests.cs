using Corico.Core.Backup;

namespace Corico.Core.Tests;

public class PoliticaConservazioneTests
{
    private static readonly DateTime Adesso = new(2026, 10, 21, 12, 0, 0);

    [Fact]
    public void Conserva_tutto_nelle_ultime_48_ore()
    {
        var backup = Enumerable.Range(0, 24).Select(i => Adesso.AddHours(-2 * i)).ToList();
        Assert.Empty(PoliticaConservazione.DaEliminare(backup, d => d, Adesso));
    }

    [Fact]
    public void Oltre_48_ore_uno_al_giorno_poi_uno_al_mese()
    {
        var backup = new List<DateTime>
        {
            Adesso.AddDays(-5).Date.AddHours(9),
            Adesso.AddDays(-5).Date.AddHours(18), // più recente del giorno: resta
            Adesso.AddDays(-60).Date.AddHours(10),
            Adesso.AddDays(-58).Date.AddHours(10), // più recente del mese di agosto: resta
            Adesso.AddMonths(-13),                  // oltre 12 mesi: eliminato
        };

        var eliminati = PoliticaConservazione.DaEliminare(backup, d => d, Adesso);

        Assert.Contains(backup[0], eliminati);
        Assert.DoesNotContain(backup[1], eliminati);
        Assert.Contains(backup[2], eliminati);
        Assert.DoesNotContain(backup[3], eliminati);
        Assert.Contains(backup[4], eliminati);
    }

    [Fact]
    public void Il_backup_piu_recente_non_viene_mai_eliminato()
    {
        var unico = Adesso.AddYears(-3);
        Assert.Empty(PoliticaConservazione.DaEliminare([unico], d => d, Adesso));
    }
}

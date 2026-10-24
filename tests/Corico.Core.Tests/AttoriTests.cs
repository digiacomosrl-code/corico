using Corico.Core.Dati;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Tests;

public class AttoriTests
{
    private static readonly DateOnly Oggi = new(2026, 10, 21);

    private static async Task<(AttoriService Attori, Guid Commessa, DatiAttore Rossi, DatiAttore Bianchi)> PreparaAsync(AmbienteDiProva amb)
    {
        var attori = amb.Get<AttoriService>();
        var commessa = await amb.Get<CommesseService>().CreaAsync("Scuola di Caltagirone");
        var rossi = await attori.SalvaAsync(new DatiAttore { Titolo = "Ing.", Nominativo = "Mario Rossi", Ente = "Comune di Caltagirone", Email = "m.rossi@comune.it", Pec = "rossi@pec.it" });
        var bianchi = await attori.SalvaAsync(new DatiAttore { Titolo = "Arch.", Nominativo = "Anna Bianchi", Email = "a.bianchi@studio.it" });
        return (attori, commessa.Id, rossi, bianchi);
    }

    [Fact]
    public async Task Ricerca_per_parole_su_nome_ente_e_recapiti()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var (attori, _, _, _) = await PreparaAsync(amb);

        Assert.Equal("Mario Rossi", Assert.Single(await attori.ElencoAsync("caltagirone rossi")).Nominativo);
        Assert.Equal("Anna Bianchi", Assert.Single(await attori.ElencoAsync("studio")).Nominativo);
        Assert.Equal("Ing. Mario Rossi", (await attori.ElencoAsync("rossi"))[0].NomeCompleto);
    }

    [Fact]
    public async Task Email_non_valida_rifiutata_e_duplicati_segnalati()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var (attori, _, _, _) = await PreparaAsync(amb);

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => attori.SalvaAsync(new DatiAttore { Nominativo = "Luca Verdi", Email = "luca.verdi" }));
        var duplicati = await attori.PossibiliDuplicatiAsync(new DatiAttore { Nominativo = "Altro", Email = "M.ROSSI@comune.it" });
        Assert.Equal("Stesso indirizzo email o PEC", Assert.Single(duplicati).Motivo);
    }

    [Fact]
    public async Task Piu_soggetti_per_figura_e_figure_in_carica()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var (attori, commessa, rossi, bianchi) = await PreparaAsync(amb);

        await attori.AggiungiIncaricoAsync(commessa, RuoloCommessa.DirettoreLavori, rossi.Id, new DateOnly(2026, 3, 1), "Determina n. 12");
        await attori.AggiungiIncaricoAsync(commessa, RuoloCommessa.DirettoreLavori, bianchi.Id, null);
        await attori.AggiungiIncaricoAsync(commessa, RuoloCommessa.Cse, rossi.Id, null);

        var dl = await attori.FigureInCaricaAsync(commessa, ruolo: RuoloCommessa.DirettoreLavori);
        Assert.Equal(2, dl.Count);
        var figura = dl.Single(f => f.AttoreId == rossi.Id);
        Assert.Equal(("Ing. Mario Rossi", "rossi@pec.it", "Comune di Caltagirone"), (figura.NomeCompleto, figura.Pec, figura.Ente));
        Assert.Equal(3, (await attori.FigureInCaricaAsync(commessa)).Count);

        // Lo stesso soggetto non può essere inserito due volte nella stessa figura nello stesso periodo.
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => attori.AggiungiIncaricoAsync(commessa, RuoloCommessa.Cse, rossi.Id, Oggi));
    }

    [Fact]
    public async Task Sostituzione_in_corso_d_opera_mantiene_lo_storico()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var (attori, commessa, rossi, bianchi) = await PreparaAsync(amb);
        var primo = await attori.AggiungiIncaricoAsync(commessa, RuoloCommessa.Rup, rossi.Id, new DateOnly(2026, 1, 10));

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => attori.SostituisciAsync(primo.Id, primo.Versione, bianchi.Id, new DateOnly(2026, 1, 5)));
        await attori.SostituisciAsync(primo.Id, primo.Versione, bianchi.Id, new DateOnly(2026, 6, 1), "Nuova nomina prot. 345");

        var incarichi = await attori.IncarichiAsync(commessa);
        Assert.Equal(2, incarichi.Count);
        var chiuso = incarichi.Single(i => i.AttoreId == rossi.Id);
        Assert.Equal(new DateOnly(2026, 5, 31), chiuso.Al);
        Assert.False(chiuso.InCarica);
        Assert.Equal("Anna Bianchi", Assert.Single(await attori.FigureInCaricaAsync(commessa, ruolo: RuoloCommessa.Rup)).Nominativo);
        // Alla data di una comunicazione passata risulta il RUP di allora.
        Assert.Equal("Mario Rossi", Assert.Single(await attori.FigureInCaricaAsync(commessa, new DateOnly(2026, 4, 1), RuoloCommessa.Rup)).Nominativo);

        // Il cambio è nello storico della commessa con una nota leggibile.
        await using var db = await amb.Get<IDbContextFactory<CoreDbContext>>().CreateDbContextAsync();
        var voci = await db.Storico.Where(v => v.CommessaId == commessa && v.Entita == nameof(IncaricoCommessa)).ToListAsync();
        Assert.Contains(voci, v => v.Nota == "Responsabile unico del procedimento: dal 01/06/2026 Anna Bianchi subentra a Mario Rossi (Nuova nomina prot. 345)");

        // Un attore usato in una commessa non si elimina: si archivia.
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => attori.EliminaAsync(rossi.Id));
        Assert.Single(await attori.IncarichiDiAttoreAsync(rossi.Id));
    }

    [Fact]
    public async Task Termina_e_correggi_controllano_le_date()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var (attori, commessa, rossi, _) = await PreparaAsync(amb);
        var i = await attori.AggiungiIncaricoAsync(commessa, RuoloCommessa.Capocantiere, rossi.Id, new DateOnly(2026, 9, 1));

        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => attori.TerminaIncaricoAsync(i.Id, i.Versione, new DateOnly(2026, 8, 1)));
        await attori.TerminaIncaricoAsync(i.Id, i.Versione, new DateOnly(2026, 10, 1), "Trasferito");
        Assert.Empty(await attori.FigureInCaricaAsync(commessa));

        var chiuso = Assert.Single(await attori.IncarichiAsync(commessa));
        var corretto = await attori.CorreggiIncaricoAsync(chiuso.Id, chiuso.Versione, new DateOnly(2026, 9, 2), null, "Correzione");
        Assert.True(corretto.InCarica);

        await attori.EliminaIncaricoAsync(corretto.Id, corretto.Versione);
        Assert.Empty(await attori.IncarichiAsync(commessa));
    }
}

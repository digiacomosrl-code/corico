using Corico.Core.Dominio;
using Corico.Core.Dati;
using Corico.Core.Servizi;
using Corico.Core.Utenti;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Tests;

public class UtentiTests
{
    [Fact]
    public void Password_impronta_e_verifica()
    {
        var impronta = Password.Impronta("Cantiere2026");
        Assert.StartsWith("PBKDF2-SHA512$", impronta);
        Assert.NotEqual(impronta, Password.Impronta("Cantiere2026")); // sale diverso a ogni impronta
        Assert.True(Password.Verifica("Cantiere2026", impronta));
        Assert.False(Password.Verifica("cantiere2026", impronta));
        Assert.False(Password.Verifica("Cantiere2026", "non-valida"));
    }

    [Theory]
    [InlineData("corta1")]
    [InlineData("solamentelettere")]
    [InlineData("1234567890")]
    public void Password_debole_rifiutata(string password) =>
        Assert.Throws<OperazioneNonValidaException>(() => Password.ControllaRequisiti(password));

    [Fact]
    public async Task Primo_amministratore_una_sola_volta_e_accesso()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var utenti = amb.Get<UtentiService>();
        Assert.False(await utenti.EsistonoUtentiAsync());

        await utenti.CreaPrimoAmministratoreAsync("Salvatore", "Salvatore Di Giacomo", "Cantiere2026");
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => utenti.CreaPrimoAmministratoreAsync("altro", "Altro", "Cantiere2026"));

        var esito = await utenti.AccediAsync(" SALVATORE ", "Cantiere2026");
        Assert.True(esito.Riuscito);
        Assert.Equal(RuoloUtente.Amministratore, esito.Identita!.Ruolo);
        Assert.False((await utenti.AccediAsync("salvatore", "sbagliata1")).Riuscito);
    }

    [Fact]
    public async Task Blocco_dopo_tentativi_errati()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var utenti = amb.Get<UtentiService>();
        await utenti.CreaPrimoAmministratoreAsync("admin", "Amministratore", "Cantiere2026");

        for (var i = 0; i < UtentiService.TentativiMassimi; i++)
            await utenti.AccediAsync("admin", "sbagliata1");
        var bloccato = await utenti.AccediAsync("admin", "Cantiere2026");
        Assert.False(bloccato.Riuscito);
        Assert.Contains("bloccato", bloccato.Errore);

        amb.Orologio.Advance(UtentiService.DurataBlocco + TimeSpan.FromSeconds(1));
        Assert.True((await utenti.AccediAsync("admin", "Cantiere2026")).Riuscito);
    }

    [Fact]
    public async Task Nuovo_utente_con_password_provvisoria_da_cambiare()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var utenti = amb.Get<UtentiService>();
        var admin = await utenti.CreaPrimoAmministratoreAsync("admin", "Amministratore", "Cantiere2026");

        string provvisoria;
        using (UtenteCorrente.Imposta(admin))
            provvisoria = await utenti.CreaAsync("mario.rossi", "Mario Rossi", null, RuoloUtente.Operatore);

        var esito = await utenti.AccediAsync("mario.rossi", provvisoria);
        Assert.True(esito.Identita!.CambioPasswordObbligatorio);
        using (UtenteCorrente.Imposta(esito.Identita))
            await utenti.CambiaPasswordAsync(esito.Identita.Id, provvisoria, "Personale99");
        Assert.False((await utenti.IdentitaAsync(esito.Identita.Id))!.CambioPasswordObbligatorio);
        Assert.True((await utenti.AccediAsync("mario.rossi", "Personale99")).Riuscito);
    }

    [Fact]
    public async Task Resta_sempre_un_amministratore_attivo()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var utenti = amb.Get<UtentiService>();
        var admin = await utenti.CreaPrimoAmministratoreAsync("admin", "Amministratore", "Cantiere2026");
        var riga = (await utenti.ElencoAsync()).Single();

        using (UtenteCorrente.Imposta(admin))
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() =>
                utenti.ModificaAsync(riga.Id, riga.Versione, riga.NomeCompleto, null, RuoloUtente.Operatore, true));
    }

    [Fact]
    public async Task Permessi_per_ruolo_sui_salvataggi()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var utenti = amb.Get<UtentiService>();
        var clienti = amb.Get<ClientiService>();
        var admin = await utenti.CreaPrimoAmministratoreAsync("admin", "Amministratore", "Cantiere2026");
        string pOperatore, pLettore;
        using (UtenteCorrente.Imposta(admin))
        {
            pOperatore = await utenti.CreaAsync("operatore", "Oreste Operatore", null, RuoloUtente.Operatore);
            pLettore = await utenti.CreaAsync("lettore", "Lucia Lettrice", null, RuoloUtente.Consultazione);
        }
        var operatore = (await utenti.AccediAsync("operatore", pOperatore)).Identita!;
        var lettore = (await utenti.AccediAsync("lettore", pLettore)).Identita!;

        using (UtenteCorrente.Imposta(operatore))
        {
            await clienti.SalvaAsync(new DatiCliente { Denominazione = "Comune di Caltagirone" });
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => utenti.CreaAsync("nuovo", "Nuovo", null, RuoloUtente.Amministratore));
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => utenti.ReimpostaPasswordAsync(lettore.Id));
        }

        using (UtenteCorrente.Imposta(lettore))
        {
            var ex = await Assert.ThrowsAsync<OperazioneNonValidaException>(() => clienti.SalvaAsync(new DatiCliente { Denominazione = "Altro ente" }));
            Assert.Contains("sola consultazione", ex.Message);
            // La propria password si può sempre cambiare.
            await utenti.CambiaPasswordAsync(lettore.Id, pLettore, "Lettura2026");
        }

        using (UtenteCorrente.Imposta(lettore with { Attivo = false }))
            await Assert.ThrowsAsync<OperazioneNonValidaException>(() => utenti.CambiaPasswordAsync(lettore.Id, "Lettura2026", "Lettura2027"));

        Assert.Single(await clienti.ElencoAsync());
        await using var db = await amb.Get<IDbContextFactory<CoreDbContext>>().CreateDbContextAsync();
        var voce = await db.Storico.SingleAsync(v => v.Entita == "Cliente");
        Assert.Equal("Oreste Operatore", voce.Utente);
    }
}

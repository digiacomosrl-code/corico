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

    [Fact]
    public async Task Conflitto_mostra_differenze_e_si_puo_riapplicare()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var clienti = amb.Get<ClientiService>();
        var mario = new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore);
        var lucia = new IdentitaUtente(Guid.NewGuid(), "lucia", "Lucia Bianchi", RuoloUtente.Operatore);
        var salvato = await clienti.SalvaAsync(new DatiCliente { Denominazione = "Comune di Caltagirone", Comune = "Caltagirone" });

        // Entrambi aprono la scheda; Mario salva per primo.
        var diMario = (await clienti.ElencoAsync()).Single();
        var diLucia = (await clienti.ElencoAsync()).Single();
        diMario.Telefono = "0933 111111";
        using (UtenteCorrente.Imposta(mario)) await clienti.SalvaAsync(diMario);
        Assert.StartsWith("Ultima modifica di Mario Rossi il ", (await clienti.ElencoAsync()).Single().UltimaModifica);

        diLucia.Telefono = "0933 222222";
        ConflittoModificaException conflitto;
        using (UtenteCorrente.Imposta(lucia))
            conflitto = await Assert.ThrowsAsync<ConflittoModificaException>(() => clienti.SalvaAsync(diLucia));
        Assert.Equal("Mario Rossi", conflitto.SalvatoDa);
        var d = Assert.Single(conflitto.Differenze);
        Assert.Equal(("Telefono", "0933 222222", "0933 111111"), (d.Campo, d.TuoValore, d.ValoreSalvato));
        Assert.True(conflitto.Riapplicabile);

        // Lucia sceglie «Riapplica»: il suo valore sostituisce quello di Mario.
        using (UtenteCorrente.Imposta(lucia))
        using (ConflittoModificaException.Accetta(conflitto))
            await clienti.SalvaAsync(diLucia);
        Assert.Equal("0933 222222", (await clienti.ElencoAsync()).Single().Telefono);
    }

    [Fact]
    public async Task Riapplica_dal_servizio_conflitti_chiude_il_pannello()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var clienti = amb.Get<ClientiService>();
        await clienti.SalvaAsync(new DatiCliente { Denominazione = "Ente" });
        var a = (await clienti.ElencoAsync()).Single();
        var b = (await clienti.ElencoAsync()).Single();
        a.Note = "prima"; await clienti.SalvaAsync(a);
        b.Note = "seconda";
        var conflitti = new ConflittiService();
        var ricaricato = false;
        try { await clienti.SalvaAsync(b); }
        catch (ConflittoModificaException c) { conflitti.Mostra(c, () => clienti.SalvaAsync(b), () => { ricaricato = true; return Task.CompletedTask; }); }
        Assert.True(conflitti.PuoRiapplicare);
        await conflitti.RiapplicaAsync();
        Assert.Null(conflitti.Corrente);
        Assert.True(ricaricato);
        Assert.Equal("seconda", (await clienti.ElencoAsync()).Single().Note);
    }

    [Fact]
    public void Presenza_per_commessa_e_pacchetto()
    {
        var presenza = new PresenzaService(TimeProvider.System);
        var mario = new IdentitaUtente(Guid.NewGuid(), "mario", "Mario Rossi", RuoloUtente.Operatore);
        var lucia = new IdentitaUtente(Guid.NewGuid(), "lucia", "Lucia Bianchi", RuoloUtente.Operatore);
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), commessa = Guid.NewGuid(), pacchetto = Guid.NewGuid();
        presenza.Collega(s1, mario); presenza.Collega(s2, lucia);
        presenza.ApriCommessa(s1, commessa); presenza.ApriCommessa(s2, commessa);
        presenza.ApriPacchetto(s1, pacchetto);
        Assert.Equal(["Lucia Bianchi"], presenza.AltriSullaCommessa(commessa, mario.Id));
        Assert.Empty(presenza.AltriSulPacchetto(pacchetto, mario.Id));
        Assert.Equal(["Mario Rossi"], presenza.AltriSulPacchetto(pacchetto, lucia.Id));
        presenza.ChiudiPacchetto(s1, Guid.NewGuid()); // un altro pacchetto: non cambia nulla
        Assert.Single(presenza.AltriSulPacchetto(pacchetto, lucia.Id));
        presenza.Scollega(s1);
        Assert.Empty(presenza.AltriSulPacchetto(pacchetto, lucia.Id));
    }
}

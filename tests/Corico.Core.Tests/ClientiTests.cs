using Corico.Core.Servizi;

namespace Corico.Core.Tests;

public class ClientiTests
{
    [Theory]
    [InlineData("Edilzeta S.r.l.", "EDILZETA SRL")]
    [InlineData("Impresa Rossi", "Rossi s.a.s.")]
    [InlineData("Società Città", "Societa Citta")]
    public void Nomi_simili_riconosciuti(string a, string b) =>
        Assert.True(ControlloDuplicati.NomiSimili(ControlloDuplicati.NormalizzaNome(a), ControlloDuplicati.NormalizzaNome(b)));

    [Fact]
    public void Partita_iva_normalizzata() =>
        Assert.Equal(ControlloDuplicati.NormalizzaCodice("01234567890"), ControlloDuplicati.NormalizzaCodice("IT 012 345 678 90"));

    [Fact]
    public async Task Possibili_duplicati_segnalati_ma_non_bloccanti()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var clienti = amb.Get<ClientiService>();
        await clienti.SalvaAsync(new DatiCliente { Denominazione = "Comune di Caltagirone", PartitaIva = "00123450876" });

        var nuovo = new DatiCliente { Denominazione = "Altro ente", PartitaIva = "IT00123450876" };
        var duplicati = await clienti.PossibiliDuplicatiAsync(nuovo);
        Assert.Single(duplicati);
        Assert.Equal("Stessa partita IVA", duplicati[0].Motivo);

        await clienti.SalvaAsync(nuovo); // l'utente può comunque salvare
        Assert.Equal(2, (await clienti.ElencoAsync()).Count);
    }
}

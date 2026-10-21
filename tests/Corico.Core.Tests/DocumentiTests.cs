using System.Text;
using Corico.Core.Servizi;

namespace Corico.Core.Tests;

public class DocumentiTests
{
    private static MemoryStream Contenuto(string testo) => new(Encoding.UTF8.GetBytes(testo));

    [Fact]
    public async Task Caricamento_con_nome_interno_e_duplicati_esatti()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var commessa = await amb.Get<CommesseService>().CreaAsync("Commessa");
        var documenti = amb.Get<DocumentiService>();

        var primo = await documenti.CaricaAsync(Contenuto("contratto"), "Contratto firmato.pdf", "application/pdf", commessa.Id, "Contratto");
        var secondo = await documenti.CaricaAsync(Contenuto("contratto"), "copia.pdf", "application/pdf", null);

        Assert.False(primo.GiaPresente);
        Assert.True(secondo.GiaPresente);
        Assert.Equal(primo.Documento.Id, secondo.Documento.Id);
        Assert.Equal("2026-001", secondo.Documento.CodiceCommessa);
        Assert.Single(await documenti.ElencoAsync());

        var file = Directory.GetFiles(amb.Percorsi.Documenti, "*.pdf", SearchOption.AllDirectories).Single();
        Assert.Equal(primo.Documento.Id.ToString("N") + ".pdf", Path.GetFileName(file));
    }

    [Fact]
    public async Task Verifica_di_coerenza_rileva_file_alterati_mancanti_e_non_registrati()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var documenti = amb.Get<DocumentiService>();
        await documenti.CaricaAsync(Contenuto("uno"), "uno.txt", "text/plain", null);
        await documenti.CaricaAsync(Contenuto("due"), "due.txt", "text/plain", null);
        Assert.Empty(await documenti.VerificaCoerenzaAsync());

        var files = Directory.GetFiles(amb.Percorsi.Documenti, "*.txt", SearchOption.AllDirectories);
        await File.WriteAllTextAsync(files[0], "alterato");
        File.Delete(files[1]);
        await File.WriteAllTextAsync(Path.Combine(amb.Percorsi.Documenti, "estraneo.txt"), "x");

        var problemi = await documenti.VerificaCoerenzaAsync();
        Assert.Equal(3, problemi.Count);
    }

    [Fact]
    public async Task Archiviazione_reversibile_senza_cancellare_il_file()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var documenti = amb.Get<DocumentiService>();
        var d = (await documenti.CaricaAsync(Contenuto("x"), "x.txt", "text/plain", null)).Documento;

        await documenti.ImpostaArchiviazioneAsync(d.Id, true);
        Assert.Empty(await documenti.ElencoAsync());
        Assert.Single(await documenti.ElencoAsync(includiArchiviati: true));
        Assert.NotNull(await documenti.ApriAsync(d.Id));
    }
}

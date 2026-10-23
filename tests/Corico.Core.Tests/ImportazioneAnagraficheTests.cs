using System.Text;
using ClosedXML.Excel;
using Corico.Core.Dominio;
using Corico.Core.Servizi;

namespace Corico.Core.Tests;

public class ImportazioneAnagraficheTests
{
    // Intestazioni come nell'esportazione del gestionale; dati di fantasia.
    private const string CsvGestionale =
        "Ditta;Rag. soc. ditta ( attivita');Codice;Ragione sociale ( attivita');Indirizzo (att.);Cap;Citta' (att.);Provincia (att.);Partita iva;Codice fiscale;Alias;Partita IVA estera;Telefono;Fax;E-mail\r\n" +
        "500;IMPRESA ALFA S.P.A.;1;FORNITORE PRO FORME;;;;;;;;;;;\r\n" +
        "500;IMPRESA ALFA S.P.A.;2;CALCESTRUZZI DEL SUD S.R.L.;VIA DELL'INDUSTRIA, 4;97015;MODICA;rg;123450789;123450789;IT 00 X 0000;;0932 000000;;INFO@CALCESTRUZZI.EXAMPLE\r\n" +
        "500;IMPRESA ALFA S.P.A.;3;BIANCHI GEOM. PAOLO;VIA ROMA 1;97100;RAGUSA;RG;1987654321;BNCPLA70A01H163X;;;;;\r\n" +
        "500;IMPRESA ALFA S.P.A.;4;;;;;;;;;;;;\r\n" +
        "725;IMPRESA BETA S.R.L.;9;CALCESTRUZZI DEL SUD SRL;VIA DELL'INDUSTRIA 4;97015;MODICA;RG;00123450789;;;;;;\r\n" +
        "725;IMPRESA BETA S.R.L.;10;FERRAMENTA ÀNGELO;VIA SICILIA 2;97015;MODICA;RG;1234567;;;;;;\r\n";

    private static MemoryStream Csv(string testo, Encoding codifica) => new(codifica.GetBytes(testo));

    private static Encoding Windows1252 => CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    [Fact]
    public async Task Csv_del_gestionale_letto_con_soli_campi_di_corico()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var imp = amb.Get<ImportazioneAnagraficheService>();
        var a = await imp.AnteprimaAsync(TipoAnagrafica.Fornitori, Csv(CsvGestionale, Windows1252), "fornitori.csv");

        Assert.Equal(6, a.Righe.Count);
        Assert.Contains("Cap", a.ColonneIgnorate);
        Assert.Contains("Alias", a.ColonneIgnorate);
        Assert.Equal(["500 – IMPRESA ALFA S.P.A.", "725 – IMPRESA BETA S.R.L."], a.Ditte);

        var cls = a.Righe.Single(r => r.Riga == 3);
        Assert.Equal("CALCESTRUZZI DEL SUD S.R.L.", cls.Denominazione);
        Assert.Equal("00123450789", cls.PartitaIva);           // zeri iniziali persi dal gestionale
        Assert.Equal("00123450789", cls.CodiceFiscale);
        Assert.Equal("RG", cls.Provincia);
        Assert.Equal("info@calcestruzzi.example", cls.Email);
        Assert.Equal(EsitoRigaAnagrafica.Nuovo, cls.Esito);

        Assert.Equal("BNCPLA70A01H163X", a.Righe.Single(r => r.Riga == 4).CodiceFiscale);
        Assert.Equal(EsitoRigaAnagrafica.Scartato, a.Righe.Single(r => r.Riga == 5).Esito);
        var ripetuto = a.Righe.Single(r => r.Riga == 6);
        Assert.Equal(EsitoRigaAnagrafica.RipetutoNelFile, ripetuto.Esito);
        Assert.False(ripetuto.Importa);
        Assert.Equal("FERRAMENTA ÀNGELO", a.Righe.Single(r => r.Riga == 7).Denominazione);
        Assert.Equal("00001234567", a.Righe.Single(r => r.Riga == 7).PartitaIva);
    }

    [Fact]
    public async Task Importazione_salta_i_gia_presenti_e_si_puo_ripetere()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var imp = amb.Get<ImportazioneAnagraficheService>();
        var fornitori = amb.Get<FornitoriService>();
        await fornitori.SalvaAsync(new DatiFornitore { Denominazione = "Bianchi Paolo", PartitaIva = "01987654321" });

        var a = await imp.AnteprimaAsync(TipoAnagrafica.Fornitori, Csv(CsvGestionale, Encoding.UTF8), "fornitori.csv");
        Assert.Equal(EsitoRigaAnagrafica.GiaPresente, a.Righe.Single(r => r.Riga == 4).Esito);
        Assert.Equal(3, a.Conta(EsitoRigaAnagrafica.Nuovo));     // pro forme, calcestruzzi, ferramenta

        Assert.Equal(3, await imp.ConfermaAsync(a));
        Assert.Equal(4, (await fornitori.ElencoAsync()).Count);

        // Una seconda importazione dello stesso file non crea doppioni.
        var b = await imp.AnteprimaAsync(TipoAnagrafica.Fornitori, Csv(CsvGestionale, Encoding.UTF8), "fornitori.csv");
        Assert.Equal(0, b.Conta(EsitoRigaAnagrafica.Nuovo));
        await Assert.ThrowsAsync<OperazioneNonValidaException>(() => imp.ConfermaAsync(b));
    }

    [Fact]
    public async Task Excel_xlsx_con_intestazioni_semplici()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Clienti");
            ws.Cell(1, 1).Value = "Denominazione"; ws.Cell(1, 2).Value = "P.IVA"; ws.Cell(1, 3).Value = "Comune"; ws.Cell(1, 4).Value = "CAP";
            ws.Cell(2, 1).Value = "Comune di Esempio"; ws.Cell(2, 2).Value = 12345678; ws.Cell(2, 3).Value = "Paeseinventato"; ws.Cell(2, 4).Value = 96010;
            wb.SaveAs(ms);
        }
        ms.Position = 0;
        var imp = amb.Get<ImportazioneAnagraficheService>();
        var a = await imp.AnteprimaAsync(TipoAnagrafica.Clienti, ms, "clienti.xlsx");
        var r = Assert.Single(a.Righe);
        Assert.Equal(("Comune di Esempio", "00012345678", "Paeseinventato"), (r.Denominazione, r.PartitaIva, r.Comune));
        Assert.Equal(1, await imp.ConfermaAsync(a));
        Assert.Equal("00012345678", Assert.Single(await amb.Get<ClientiService>().ElencoAsync()).PartitaIva);
    }

    [Theory]
    [InlineData("932000111", "0932000111")]
    [InlineData("3331234567", "3331234567")]
    [InlineData("0932 762044", "0932 762044")]
    public void Telefono_fisso_con_zero_iniziale(string letto, string atteso) =>
        Assert.Equal(atteso, ImportazioneAnagraficheService.Telefono(letto));

    [Fact]
    public async Task File_non_adatti_spiegati()
    {
        await using var amb = await AmbienteDiProva.CreaAsync();
        var imp = amb.Get<ImportazioneAnagraficheService>();
        var xls = await Assert.ThrowsAsync<OperazioneNonValidaException>(() => imp.AnteprimaAsync(TipoAnagrafica.Clienti, new MemoryStream([1]), "vecchio.xls"));
        Assert.Contains(".xlsx", xls.Message);
        var senza = await Assert.ThrowsAsync<OperazioneNonValidaException>(() =>
            imp.AnteprimaAsync(TipoAnagrafica.Clienti, Csv("Codice;Cap\r\n1;97015\r\n", Encoding.UTF8), "x.csv"));
        Assert.Contains("ragione sociale", senza.Message);
    }
}

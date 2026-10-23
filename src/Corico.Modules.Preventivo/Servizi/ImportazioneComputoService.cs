using Corico.Core.Backup;
using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record AnteprimaComputo(ComputoLetto Letto, decimal TotaleCalcolato, decimal TotaleSicurezza, int VociSenzaElenco,
    OrigineComputo Origine = OrigineComputo.Xpwe, string Formato = "XPWE (PriMus)", decimal? TotaleDocumento = null)
{
    /// <summary>Differenza tra il totale scritto nel documento e quello ricalcolato; null se il documento non lo riporta.</summary>
    public decimal? Differenza => TotaleDocumento is null ? null : TotaleCalcolato - TotaleDocumento;
}

public sealed record EsitoImportazione(Guid ComputoId, int Voci, decimal TotaleCalcolato, Guid DocumentoId, string? Backup)
{
    /// <summary>Voci con la WBS scritta nel file e voci a cui è stata riportata quella del computo precedente.</summary>
    public int VociConWbsDalFile { get; init; }
    public int VociConWbsRiportata { get; init; }
}

/// <summary>
/// Importazione del computo (sez. 12): anteprima con segnalazioni, poi conferma esplicita.
/// La conferma esegue un backup, archivia il file originale e crea un nuovo computo attivo;
/// il computo precedente resta consultabile.
/// </summary>
public sealed class ImportazioneComputoService(
    IDbContextFactory<PreventivoDbContext> fabbrica,
    DocumentiService documenti,
    BackupService backup)
{
    /// <summary>Formati accettati, per il selettore dei file.</summary>
    public const string EstensioniAccettate = ".xpwe,.XPWE,.xlsx,.XLSX,.pdf,.PDF,.p7m,.P7M";

    public static bool FileExcel(string nomeFile) => Path.GetExtension(nomeFile).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>PDF del computo, anche firmato digitalmente («computo.pdf.p7m»).</summary>
    public static bool FilePdf(string nomeFile) => Path.GetExtension(nomeFile).ToLowerInvariant() is ".pdf" or ".p7m";

    /// <summary>Anteprima di un computo XPWE, Excel (tabellare o stampa PriMus) o PDF, secondo l'estensione del file.</summary>
    public static AnteprimaComputo Anteprima(Stream contenuto, string nomeFile)
    {
        if (FilePdf(nomeFile))
        {
            var pdf = LettoreComputoPdf.Leggi(contenuto);
            return Calcola(pdf.Letto) with { Origine = OrigineComputo.Pdf, Formato = pdf.Formato, TotaleDocumento = pdf.TotaleDocumento };
        }
        if (!FileExcel(nomeFile)) return Anteprima(contenuto);
        var excel = LettoreComputoExcel.Leggi(contenuto);
        return Calcola(excel.Letto) with { Origine = OrigineComputo.Excel, Formato = excel.Formato, TotaleDocumento = excel.TotaleDocumento };
    }

    public static AnteprimaComputo Anteprima(Stream xpwe) => Calcola(LettoreXpwe.Leggi(xpwe));

    private static AnteprimaComputo Calcola(ComputoLetto letto)
    {
        decimal totale = 0, sicurezza = 0;
        var senzaElenco = 0;
        var conWbs = letto.Voci.Count(v => CodiciWbs.Normalizza(v.CodiceWbs) is not null);
        if (conWbs > 0 && !letto.Segnalazioni.Any(x => x.Testo.StartsWith("WBS:")))
            letto.Segnalazioni.Add(new(GravitaSegnalazione.Informazione,
                $"WBS: {conWbs} voci su {letto.Voci.Count} hanno un codice WBS nel file; i codici mancanti vengono aggiunti alla WBS della commessa."));
        foreach (var v in letto.Voci)
        {
            var ep = letto.Elenco(v.IdElenco);
            if (ep is null) { senzaElenco++; continue; }
            var r = CalcoloRicavi.Voce(v.Quantita, ep.Prezzo, 0, NonSoggetta(ep));
            totale += r.Lordo;
            sicurezza += r.LordoNonSoggetto;
        }
        return new AnteprimaComputo(letto, totale, sicurezza, senzaElenco);
    }

    public async Task<EsitoImportazione> ConfermaAsync(Guid commessaId, byte[] contenuto, string nomeFile,
        decimal? totaleDichiarato, CancellationToken ct = default)
    {
        AnteprimaComputo anteprima;
        using (var ms = new MemoryStream(contenuto))
            anteprima = Anteprima(ms, nomeFile);

        var esitoBackup = await backup.EseguiBackupLocaleAsync(MotivoBackup.PrimaDiImportazione, ct);
        if (!esitoBackup.Riuscito)
            throw new OperazioneNonValidaException("Importazione annullata: il backup preventivo non è riuscito. " + esitoBackup.Messaggio);

        EsitoCaricamento doc;
        using (var ms = new MemoryStream(contenuto))
            doc = await documenti.CaricaAsync(ms, nomeFile,
                FileExcel(nomeFile) ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                    : Path.GetExtension(nomeFile).Equals(".p7m", StringComparison.OrdinalIgnoreCase) ? "application/pkcs7-mime"
                    : FilePdf(nomeFile) ? "application/pdf" : "application/xml",
                commessaId, "Computo metrico", ct);

        var letto = anteprima.Letto;
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        db.InserimentiSoloInRiepilogo = true;
        await using var transazione = await db.Database.BeginTransactionAsync(ct);
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);

        var precedenti = await db.Computi.Where(c => c.CommessaId == commessaId && c.Attivo).ToListAsync(ct);
        foreach (var precedente in precedenti)
            precedente.Attivo = false;
        var wbsPrecedente = await WbsDelComputoPrecedenteAsync(db, precedenti.Select(c => c.Id).ToList(), ct);

        var computo = new Computo
        {
            PreventivoId = preventivo.Id, CommessaId = commessaId, Origine = anteprima.Origine, Attivo = true,
            Descrizione = $"Computo importato da {Path.GetFileName(nomeFile)}", NomeFile = Path.GetFileName(nomeFile),
            DocumentoId = doc.Documento.Id, TotaleImportato = anteprima.TotaleCalcolato, TotaleDichiarato = totaleDichiarato ?? anteprima.TotaleDocumento,
            Oggetto = letto.Oggetto, Comune = letto.Comune, Committente = letto.Committente, Impresa = letto.Impresa,
        };
        db.Computi.Add(computo);

        var categorie = new Dictionary<(string, string), Guid>();
        foreach (var c in letto.Categorie)
        {
            var cat = new Categoria
            {
                ComputoId = computo.Id, CommessaId = commessaId, Livello = Enum.Parse<LivelloCategoria>(c.Livello),
                IdOriginale = c.Id, Codice = c.Codice, Descrizione = c.Descrizione,
            };
            db.Categorie.Add(cat);
            categorie[(c.Livello, c.Id)] = cat.Id;
        }

        var nuoveVoci = new List<VoceComputo>();
        var elenco = new Dictionary<string, VoceElencoPrezzi>();
        foreach (var e in letto.ElencoPrezzi)
        {
            var ep = new VoceElencoPrezzi
            {
                ComputoId = computo.Id, CommessaId = commessaId, IdOriginale = e.Id, Tariffa = e.Tariffa, Articolo = e.Articolo,
                DescrizioneRidotta = e.DesRidotta, DescrizioneBreve = e.DesBreve, DescrizioneEstesa = e.DesEstesa,
                UnitaOriginale = e.UnitaOriginale, Unita = e.Unita, Prezzo = e.Prezzo,
                IncidenzaSicurezza = e.IncSicurezza, IncidenzaManodopera = e.IncManodopera,
                IncidenzaMateriali = e.IncMateriali, IncidenzaAttrezzature = e.IncAttrezzature,
                SuperCapitoloId = e.IdSuperCapitolo is null ? null : categorie.GetValueOrDefault(("SuperCapitolo", e.IdSuperCapitolo)),
                HaAnalisi = e.Analisi.Count > 0, SpeseGeneraliAnalisi = e.SpeseGenerali, UtileAnalisi = e.Utile,
            };
            elenco[e.Id] = ep;
            db.ElencoPrezzi.Add(ep);
        }
        foreach (var e in letto.ElencoPrezzi)
        {
            var ordine = 0;
            foreach (var r in e.Analisi)
            {
                db.RigheAnalisi.Add(new RigaAnalisi
                {
                    VoceElencoPrezziId = elenco[e.Id].Id, CommessaId = commessaId, Ordine = ++ordine, Tipo = r.Tipo,
                    ElencoPrezziRiferitoId = r.IdElencoRiferito is null ? null : elenco.GetValueOrDefault(r.IdElencoRiferito)?.Id,
                    Descrizione = r.Descrizione, Unita = r.Unita, Quantita = r.Quantita, Prezzo = r.Prezzo,
                });
            }
        }

        foreach (var v in letto.Voci)
        {
            var ep = v.IdElenco is null ? null : elenco.GetValueOrDefault(v.IdElenco);
            var letta = letto.Elenco(v.IdElenco);
            var voce = new VoceComputo
            {
                ComputoId = computo.Id, CommessaId = commessaId, Ordine = v.Ordine, IdOriginale = v.Id,
                VoceElencoPrezziId = ep?.Id, Tariffa = ep?.Tariffa,
                Descrizione = ep?.DescrizioneBreve ?? ep?.DescrizioneRidotta ?? DescrizioneDaMisure(v),
                Unita = ep?.Unita, Quantita = v.Quantita, Prezzo = ep?.Prezzo ?? 0,
                PercentualeNonSoggetta = letta is null ? 0 : NonSoggetta(letta),
                SuperCategoriaId = v.IdSuperCategoria is null ? null : categorie.GetValueOrDefault(("SuperCategoria", v.IdSuperCategoria)),
                CategoriaId = v.IdCategoria is null ? null : categorie.GetValueOrDefault(("Categoria", v.IdCategoria)),
                SubCategoriaId = v.IdSubCategoria is null ? null : categorie.GetValueOrDefault(("SubCategoria", v.IdSubCategoria)),
                CodiceWbs = CodiciWbs.Normalizza(v.CodiceWbs),
                Note = ep is null ? "Voce senza elenco prezzi nel file originale: completare prezzo e descrizione." : null,
            };
            var ordine = 0;
            foreach (var m in v.Misure)
            {
                voce.RigheMisura.Add(new RigaMisura
                {
                    CommessaId = commessaId, Ordine = ++ordine, Descrizione = m.Descrizione, PartiUguali = m.PartiUguali,
                    Lunghezza = m.Lunghezza, Larghezza = m.Larghezza, AltezzaPeso = m.AltezzaPeso, Quantita = m.Quantita,
                    VoceRiferita = m.VoceRiferita,
                });
            }
            db.VociComputo.Add(voce);
            nuoveVoci.Add(voce);
        }

        // WBS: quella scritta nel file; per le voci senza, quella che la stessa voce aveva nel computo precedente.
        var dalFile = nuoveVoci.Count(v => v.CodiceWbs is not null);
        var riportate = 0;
        foreach (var v in nuoveVoci.Where(v => v.CodiceWbs is null))
        {
            if (wbsPrecedente.TryGetValue(ChiaveWbs(v.Tariffa, v.Descrizione), out var codice)) { v.CodiceWbs = codice; riportate++; }
        }
        var titoli = letto.Wbs.ToDictionary(w => CodiciWbs.Normalizza(w.Codice) ?? "", w => w.Descrizione);
        await WbsService.AggiungiMancantiAsync(db, commessaId,
            letto.Wbs.Select(w => new ElementoWbsDati(w.Codice, w.Descrizione))
                .Concat(nuoveVoci.Where(v => v.CodiceWbs is not null).Select(v => v.CodiceWbs!).SelectMany(ConLivelliSuperiori).Distinct()
                    .Select(c => new ElementoWbsDati(c, titoli.GetValueOrDefault(c) ?? $"WBS {c} (dal computo, da descrivere)"))), ct);

        db.NotaStorico = $"Importazione computo {Path.GetFileName(nomeFile)}";
        await db.SaveChangesAsync(ct);
        db.RegistraOperazione("Importazione", nameof(Computo), computo.Id, commessaId,
            $"Importato {Path.GetFileName(nomeFile)}: {letto.Voci.Count} voci, {letto.ElencoPrezzi.Count} prezzi di elenco, " +
            $"totale calcolato {Formato.Euro(anteprima.TotaleCalcolato)}.");
        await db.SaveChangesAsync(ct);
        await transazione.CommitAsync(ct);

        return new EsitoImportazione(computo.Id, letto.Voci.Count, anteprima.TotaleCalcolato, doc.Documento.Id, esitoBackup.Percorso)
        {
            VociConWbsDalFile = dalFile, VociConWbsRiportata = riportate,
        };
    }

    /// <summary>Il codice e i suoi livelli superiori («1.4.3» → 1, 1.4, 1.4.3), perché la struttura resti completa e filtrabile.</summary>
    private static IEnumerable<string> ConLivelliSuperiori(string codice)
    {
        var livelli = new List<string>();
        for (string? c = codice; c is not null; c = CodiciWbs.Padre(c)) livelli.Insert(0, c);
        return livelli;
    }

    private static string ChiaveWbs(string? tariffa, string descrizione) =>
        (tariffa ?? "").Trim().ToLowerInvariant() + "|" + descrizione.Trim().ToLowerInvariant();

    /// <summary>
    /// WBS delle voci del computo che viene sostituito, per tariffa e descrizione: si riporta solo se tutte le voci
    /// uguali avevano la stessa WBS (una tariffa usata in lavorazioni diverse resta da assegnare).
    /// </summary>
    private static async Task<Dictionary<string, string>> WbsDelComputoPrecedenteAsync(PreventivoDbContext db, List<Guid> computi, CancellationToken ct)
    {
        if (computi.Count == 0) return [];
        var voci = await db.VociComputo.AsNoTracking().Where(v => computi.Contains(v.ComputoId) && !v.Archiviata)
            .Select(v => new { v.Tariffa, v.Descrizione, v.CodiceWbs }).ToListAsync(ct);
        return voci.GroupBy(v => ChiaveWbs(v.Tariffa, v.Descrizione))
            .Where(g => g.All(v => v.CodiceWbs is not null) && g.Select(v => v.CodiceWbs).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().CodiceWbs!);
    }

    /// <summary>Quota non soggetta a ribasso proposta dall'incidenza di sicurezza del prezzo (0–100%).</summary>
    internal static decimal NonSoggetta(VoceElencoLetta ep) => Math.Clamp(ep.IncSicurezza ?? 0, 0, 100);

    private static string DescrizioneDaMisure(VoceComputoLetta v) =>
        v.Misure.Select(m => m.Descrizione).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d) && !decimal.TryParse(d, out _))
        ?? $"Voce n. {v.Ordine} senza elenco prezzi";
}

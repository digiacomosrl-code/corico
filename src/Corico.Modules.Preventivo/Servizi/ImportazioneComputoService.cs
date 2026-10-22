using Corico.Core.Backup;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

public sealed record AnteprimaComputo(ComputoLetto Letto, decimal TotaleCalcolato, decimal TotaleSicurezza, int VociSenzaElenco);

public sealed record EsitoImportazione(Guid ComputoId, int Voci, decimal TotaleCalcolato, Guid DocumentoId, string? Backup);

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
    public static AnteprimaComputo Anteprima(Stream xpwe)
    {
        var letto = LettoreXpwe.Leggi(xpwe);
        decimal totale = 0, sicurezza = 0;
        var senzaElenco = 0;
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

    public async Task<EsitoImportazione> ConfermaXpweAsync(Guid commessaId, byte[] contenuto, string nomeFile,
        decimal? totaleDichiarato, CancellationToken ct = default)
    {
        AnteprimaComputo anteprima;
        using (var ms = new MemoryStream(contenuto))
            anteprima = Anteprima(ms);

        var esitoBackup = await backup.EseguiBackupLocaleAsync(MotivoBackup.PrimaDiImportazione, ct);
        if (!esitoBackup.Riuscito)
            throw new OperazioneNonValidaException("Importazione annullata: il backup preventivo non è riuscito. " + esitoBackup.Messaggio);

        EsitoCaricamento doc;
        using (var ms = new MemoryStream(contenuto))
            doc = await documenti.CaricaAsync(ms, nomeFile, "application/xml", commessaId, "Computo metrico", ct);

        var letto = anteprima.Letto;
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        await using var transazione = await db.Database.BeginTransactionAsync(ct);
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);

        foreach (var precedente in await db.Computi.Where(c => c.CommessaId == commessaId && c.Attivo).ToListAsync(ct))
            precedente.Attivo = false;

        var computo = new Computo
        {
            PreventivoId = preventivo.Id, CommessaId = commessaId, Origine = OrigineComputo.Xpwe, Attivo = true,
            Descrizione = $"Computo importato da {Path.GetFileName(nomeFile)}", NomeFile = Path.GetFileName(nomeFile),
            DocumentoId = doc.Documento.Id, TotaleImportato = anteprima.TotaleCalcolato, TotaleDichiarato = totaleDichiarato,
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
                CodiceWbs = v.CodiceWbs,
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
        }

        db.NotaStorico = $"Importazione computo {Path.GetFileName(nomeFile)}";
        await db.SaveChangesAsync(ct);
        db.RegistraOperazione("Importazione", nameof(Computo), computo.Id, commessaId,
            $"Importato {Path.GetFileName(nomeFile)}: {letto.Voci.Count} voci, totale calcolato {anteprima.TotaleCalcolato:N2} €.");
        await db.SaveChangesAsync(ct);
        await transazione.CommitAsync(ct);

        return new EsitoImportazione(computo.Id, letto.Voci.Count, anteprima.TotaleCalcolato, doc.Documento.Id, esitoBackup.Percorso);
    }

    /// <summary>Quota non soggetta a ribasso proposta dall'incidenza di sicurezza del prezzo (0–100%).</summary>
    internal static decimal NonSoggetta(VoceElencoLetta ep) => Math.Clamp(ep.IncSicurezza ?? 0, 0, 100);

    private static string DescrizioneDaMisure(VoceComputoLetta v) =>
        v.Misure.Select(m => m.Descrizione).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d) && !decimal.TryParse(d, out _))
        ?? $"Voce n. {v.Ordine} senza elenco prezzi";
}

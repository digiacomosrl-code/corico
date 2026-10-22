using Corico.Core.Backup;
using Corico.Core.Calcolo;
using Corico.Core.Dominio;
using Corico.Core.Servizi;
using Corico.Modules.Preventivo.Dati;
using Corico.Modules.Preventivo.Dominio;
using Corico.Modules.Preventivo.Importazione;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Foglio CR proposto come offerta di un pacchetto, con la verifica dei totali rispetto al foglio.</summary>
public sealed class FoglioProposto(FoglioCr foglio, decimal ricavoCalcolato, decimal costoCalcolato)
{
    public FoglioCr Foglio { get; } = foglio;
    public bool Includi { get; set; } = true;
    public decimal RicavoCalcolato { get; } = ricavoCalcolato;
    public decimal CostoCalcolato { get; } = costoCalcolato;
    public bool RicavoConcorde => Math.Abs(RicavoCalcolato - (Foglio.RicavoNettoFoglio ?? 0)) < 0.005m;
    public bool CostoConcorde => Math.Abs(CostoCalcolato - (Foglio.CostoFoglio ?? 0)) < 0.005m;
}

public sealed class PacchettoProposto
{
    public required string Descrizione { get; set; }
    public RigaHome? Home { get; init; }
    public List<string> NoteHome { get; } = [];
    public List<FoglioProposto> Fogli { get; } = [];

    /// <summary>Foglio collegato dal tabellone HOME: la sua offerta diventa quella di riferimento.</summary>
    public string? FoglioScelto { get; set; }
}

public sealed class AnteprimaPianificazione
{
    public required PianificazioneLetta Letta { get; init; }
    public decimal RibassoProposto { get; init; }
    public decimal RibassoAttuale { get; init; }
    public List<PacchettoProposto> Pacchetti { get; } = [];
    public List<Segnalazione> Segnalazioni { get; } = [];
    public int PacchettiEsistenti { get; init; }
    public bool ComputoPresente { get; init; }

    public decimal RibassoDaApplicare => RibassoAttuale != 0 ? RibassoAttuale : RibassoProposto;
}

public sealed record VerificaFoglio(string Pacchetto, string Foglio, bool Scelto,
    decimal RicavoFoglio, decimal RicavoCorico, decimal CostoFoglio, decimal CostoCorico)
{
    public bool Concorde => Math.Abs(RicavoFoglio - RicavoCorico) < 0.005m && Math.Abs(CostoFoglio - CostoCorico) < 0.005m;
}

public sealed record EsitoImportazionePianificazione(int Pacchetti, int Offerte, int VociCreate, int VociCollegate,
    IReadOnlyList<VerificaFoglio> Verifiche, IReadOnlyList<string> Avvisi, string? Backup);

/// <summary>
/// Importazione dei file Excel aziendali di pianificazione (fogli CR e tabellone HOME, decisione 5 del 22/10/2026):
/// ogni foglio CR diventa un'offerta, i fogli con la stessa descrizione diventano offerte alternative dello stesso pacchetto,
/// le righe del tabellone HOME diventano pacchetti con le loro note.
/// </summary>
public sealed class ImportazionePianificazioneService(
    IDbContextFactory<PreventivoDbContext> fabbrica,
    DocumentiService documenti,
    BackupService backup)
{
    public async Task<AnteprimaPianificazione> AnteprimaAsync(Guid commessaId, Stream xlsx, CancellationToken ct = default)
    {
        var letta = LettoreFogliCr.Leggi(xlsx);
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);

        var ribassi = letta.Fogli.Where(f => f.Ribasso is > 0).GroupBy(f => f.Ribasso!.Value)
            .OrderByDescending(g => g.Count()).Select(g => g.Key * 100m).ToList();
        var anteprima = new AnteprimaPianificazione
        {
            Letta = letta,
            RibassoProposto = ribassi.FirstOrDefault(),
            RibassoAttuale = preventivo.RibassoPercentuale,
            PacchettiEsistenti = await db.Pacchetti.CountAsync(p => p.CommessaId == commessaId && !p.Archiviato, ct),
            ComputoPresente = await db.Computi.AnyAsync(c => c.CommessaId == commessaId && c.Attivo, ct),
        };
        var ribasso = anteprima.RibassoDaApplicare;

        // Raggruppamento: fogli con la stessa descrizione = offerte alternative dello stesso pacchetto.
        var gruppi = letta.Fogli.GroupBy(f => LettoreFogliCr.Normalizza(f.Descrizione ?? NomeFoglio(f.Nome)))
            .ToDictionary(g => g.Key, g => g.ToList());
        var usati = new HashSet<string>();

        foreach (var h in letta.Home)
        {
            var chiave = LettoreFogliCr.Normalizza(h.Attivita);
            var esistente = anteprima.Pacchetti.FirstOrDefault(p => p.Home is not null && LettoreFogliCr.Normalizza(p.Home.Attivita) == chiave);
            if (esistente is not null)
            {
                // Righe ripetute nel tabellone: si conserva la prima, le altre diventano note.
                esistente.NoteHome.Add($"Riga {h.Riga} del tabellone: {string.Join(" – ", new[] { h.Gruppo, h.Fornitore, h.Note }.Where(x => x is not null))}");
                continue;
            }
            var p = new PacchettoProposto { Descrizione = h.Attivita, Home = h, FoglioScelto = h.FoglioCollegato };
            var gruppo = h.FoglioCollegato is not null
                ? gruppi.FirstOrDefault(g => g.Value.Any(f => f.Nome == h.FoglioCollegato)).Key
                : gruppi.Keys.FirstOrDefault(k => k == chiave && !usati.Contains(k))
                  ?? gruppi.FirstOrDefault(g => !usati.Contains(g.Key) && g.Value.Any(f => LettoreFogliCr.Normalizza(NomeFoglio(f.Nome)) == chiave)).Key;
            if (gruppo is not null && usati.Add(gruppo))
                p.Fogli.AddRange(gruppi[gruppo].Select(f => Proponi(f, ribasso)));
            anteprima.Pacchetti.Add(p);
        }
        foreach (var (chiave, fogli) in gruppi.Where(g => !usati.Contains(g.Key)))
        {
            var p = new PacchettoProposto { Descrizione = fogli[0].Descrizione ?? NomeFoglio(fogli[0].Nome) };
            p.Fogli.AddRange(fogli.Select(f => Proponi(f, ribasso)));
            anteprima.Pacchetti.Add(p);
        }
        foreach (var p in anteprima.Pacchetti.Where(p => p.FoglioScelto is null))
        {
            var definitivi = p.Fogli.Where(f => f.Foglio.Stato == StatoCosto.Definitivo).ToList();
            p.FoglioScelto = p.Fogli.Count == 1 ? p.Fogli[0].Foglio.Nome : definitivi.Count == 1 ? definitivi[0].Foglio.Nome : null;
        }

        Segnala(anteprima);
        return anteprima;
    }

    private static void Segnala(AnteprimaPianificazione a)
    {
        var s = a.Segnalazioni;
        if (a.RibassoAttuale == 0 && a.RibassoProposto > 0)
            s.Add(new(GravitaSegnalazione.Informazione, $"Il ribasso d'asta della commessa sarà impostato al {Formato.Percento(a.RibassoProposto)}, come nei fogli."));
        foreach (var f in a.Letta.Fogli.Where(f => f.Ribasso is { } r && r != 0 && r * 100m != a.RibassoDaApplicare))
            s.Add(new(GravitaSegnalazione.Attenzione,
                $"Il foglio «{f.Nome}» usa un ribasso del {Formato.Percento(f.Ribasso * 100m)}, diverso da quello unico di commessa ({Formato.Percento(a.RibassoDaApplicare)}): i ricavi verranno ricalcolati."));
        var zero = a.Letta.Fogli.Where(f => f.Ribasso == 0).Select(f => f.Nome).ToList();
        if (zero.Count > 0 && a.RibassoDaApplicare > 0)
            s.Add(new(GravitaSegnalazione.Informazione,
                $"Fogli con ribasso zero ({string.Join(", ", zero)}): le loro voci saranno marcate come non soggette a ribasso (oneri di sicurezza)."));
        var integrazioni = a.Letta.Fogli.Where(f => f.IntegrazioneCm is { } i && i != 0).ToList();
        if (integrazioni.Count > 0)
            s.Add(new(GravitaSegnalazione.Informazione,
                $"{integrazioni.Count} fogli contengono un'«Integrazione C.M.»: non viene importata (decisione del 22/10/2026, modulo futuro) ed è riportata nelle note dell'offerta."));
        var diverse = a.Pacchetti.SelectMany(p => p.Fogli).Where(f => !f.RicavoConcorde || !f.CostoConcorde).ToList();
        if (diverse.Count > 0)
            s.Add(new(GravitaSegnalazione.Attenzione,
                $"{diverse.Count} fogli hanno totali che Corico ricalcola in modo diverso: sono evidenziati nella tabella."));
        if (a.Letta.FogliIgnorati.Count > 0)
            s.Add(new(GravitaSegnalazione.Informazione, $"Fogli non importati perché vuoti o con struttura diversa: {string.Join(", ", a.Letta.FogliIgnorati)}."));
        if (a.PacchettiEsistenti > 0)
            s.Add(new(GravitaSegnalazione.Attenzione,
                $"La commessa ha già {a.PacchettiEsistenti} pacchetti: l'importazione ne aggiunge di nuovi senza modificare quelli esistenti."));
        if (!a.ComputoPresente)
            s.Add(new(GravitaSegnalazione.Informazione,
                "La commessa non ha un computo: le voci dei fogli formeranno un computo ricostruito, da sostituire quando sarà disponibile quello ufficiale."));
        var senzaScelta = a.Pacchetti.Where(p => p.Fogli.Count > 1 && p.FoglioScelto is null).Select(p => p.Descrizione).ToList();
        if (senzaScelta.Count > 0)
            s.Add(new(GravitaSegnalazione.Attenzione,
                $"Pacchetti con più offerte alternative e nessuna collegata al tabellone: {string.Join(", ", senzaScelta)}. Scegli l'offerta di riferimento qui sotto o dopo l'importazione."));
    }

    private static FoglioProposto Proponi(FoglioCr f, decimal ribassoCommessa)
    {
        var nonSoggetta = NonSoggetta(f, ribassoCommessa);
        var ricavo = f.Righe.Where(r => r.Tipo == TipoRigaCr.Voce)
            .Sum(r => CalcoloRicavi.Voce(r.Quantita, r.Prezzo, ribassoCommessa, nonSoggetta).Netto);
        var costo = f.Righe.Where(r => r.Tipo != TipoRigaCr.Nota).Sum(r => r.Costo);
        return new FoglioProposto(f, ricavo, costo);
    }

    /// <summary>I fogli a ribasso zero in una commessa con ribasso sono oneri di sicurezza: voci non soggette a ribasso.</summary>
    private static decimal NonSoggetta(FoglioCr f, decimal ribassoCommessa) => f.Ribasso == 0 && ribassoCommessa > 0 ? 100m : 0m;

    public async Task<EsitoImportazionePianificazione> ConfermaAsync(Guid commessaId, byte[] contenuto, string nomeFile,
        AnteprimaPianificazione anteprima, CancellationToken ct = default)
    {
        var esitoBackup = await backup.EseguiBackupLocaleAsync(MotivoBackup.PrimaDiImportazione, ct);
        if (!esitoBackup.Riuscito)
            throw new OperazioneNonValidaException("Importazione annullata: il backup preventivo non è riuscito. " + esitoBackup.Messaggio);
        EsitoCaricamento doc;
        using (var ms = new MemoryStream(contenuto))
            doc = await documenti.CaricaAsync(ms, nomeFile,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", commessaId, "Pianificazione costi (Excel)", ct);

        var file = Path.GetFileName(nomeFile);
        var avvisi = new List<string>();
        var verifiche = new List<VerificaFoglio>();
        int nPacchetti = 0, nOfferte = 0, vociCreate = 0, vociCollegate = 0;

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        db.InserimentiSoloInRiepilogo = true;
        await using var transazione = await db.Database.BeginTransactionAsync(ct);

        var preventivo = await PreventivoService.OttieniOCreaAsync(db, commessaId, ct);
        if (preventivo.RibassoPercentuale == 0 && anteprima.RibassoProposto > 0)
            preventivo.RibassoPercentuale = anteprima.RibassoProposto;
        var ribasso = preventivo.RibassoPercentuale;

        var computo = await db.Computi.FirstOrDefaultAsync(c => c.CommessaId == commessaId && c.Attivo, ct);
        if (computo is null)
        {
            computo = new Computo
            {
                PreventivoId = preventivo.Id, CommessaId = commessaId, Origine = OrigineComputo.Excel, Attivo = true,
                Descrizione = $"Computo ricostruito dai fogli CR di {file}", NomeFile = file, DocumentoId = doc.Documento.Id,
            };
            db.Computi.Add(computo);
        }
        var assegnate = (await db.PacchettiVoci.Where(v => v.CommessaId == commessaId).Select(v => v.VoceComputoId).ToListAsync(ct)).ToHashSet();
        var disponibili = (await db.VociComputo.Where(v => v.ComputoId == computo.Id && !v.Archiviata).OrderBy(v => v.Ordine).ToListAsync(ct))
            .Where(v => !assegnate.Contains(v.Id)).ToList();
        var ordineVoce = disponibili.Count == 0 ? 0 : await db.VociComputo.Where(v => v.ComputoId == computo.Id).MaxAsync(v => v.Ordine, ct);
        var ordinePacchetto = await db.Pacchetti.Where(p => p.CommessaId == commessaId).MaxAsync(p => (int?)p.Ordine, ct) ?? 0;
        var oggi = DateOnly.FromDateTime(DateTime.Today);

        foreach (var pp in anteprima.Pacchetti)
        {
            var fogli = pp.Fogli.Where(f => f.Includi).ToList();
            if (pp.Home is null && fogli.Count == 0) continue;
            var scelto = fogli.FirstOrDefault(f => f.Foglio.Nome == pp.FoglioScelto);
            var base_ = scelto ?? fogli.FirstOrDefault();

            var pacchetto = new Pacchetto
            {
                CommessaId = commessaId, Ordine = ++ordinePacchetto, Codice = pp.Home?.Gruppo, Descrizione = pp.Descrizione,
                RiferimentoOda = pp.Home?.Oda, Tipo = TipoDa(pp.Home?.Oda, pp.Descrizione, base_?.Foglio),
                StatoCosto = base_?.Foglio.Stato ?? StatoCosto.Previsione,
                Note = pp.Home?.Fornitore is { } fh && scelto is null ? $"Fornitore indicato nel tabellone: {fh}" : null,
            };
            db.Pacchetti.Add(pacchetto);
            nPacchetti++;

            if (pp.Home is { } h && (h.Note is not null || h.Firma is not null))
                db.NotePacchetti.Add(new NotaPacchetto
                {
                    PacchettoId = pacchetto.Id, CommessaId = commessaId, Data = h.DataNota ?? oggi, Iniziali = h.Firma,
                    Testo = h.Note ?? "Riga firmata nel tabellone HOME, senza nota.",
                });
            foreach (var n in pp.NoteHome)
                db.NotePacchetti.Add(new NotaPacchetto { PacchettoId = pacchetto.Id, CommessaId = commessaId, Data = oggi, Testo = n });
            if (base_ is not null && base_.Foglio.Stato == StatoCosto.Definitivo && base_.Foglio.StatoTesto is { } st
                && !st.Contains("definitiv", StringComparison.OrdinalIgnoreCase))
                pacchetto.Note = string.Join("\n", new[] { pacchetto.Note,
                    $"Stato nel foglio Excel: «{st}», registrato come Definitivo fino al Modulo 4 (consuntivo)." }.Where(x => x is not null));

            // Voci del pacchetto: prima il foglio di riferimento, poi le alternative.
            var vociGruppo = new Dictionary<(string Chiave, int Occorrenza), VoceComputo>();
            var offerte = new List<(FoglioProposto Foglio, Offerta Offerta, Dictionary<VoceComputo, RigaCr> Righe)>();
            foreach (var fp in fogli.OrderBy(f => f == base_ ? 0 : 1))
            {
                var f = fp.Foglio;
                var nonSoggetta = NonSoggetta(f, ribasso);
                var offerta = new Offerta
                {
                    PacchettoId = pacchetto.Id, CommessaId = commessaId, NomeFornitore = f.Fornitore ?? (fp == scelto ? pp.Home?.Fornitore : null),
                    Tipo = f.TipoOfferta, Origine = $"Foglio «{f.Nome}» di {file}", DocumentoId = doc.Documento.Id,
                };
                var note = new List<string>();
                if (f.StatoTesto is not null) note.Add($"Stato nel foglio: {f.StatoTesto}.");
                if (f.IntegrazioneCm is { } icm && icm != 0)
                    note.Add($"Integrazione C.M. nel foglio: {Formato.Euro(icm)} (non importata: modulo futuro).");

                var righeVoce = new Dictionary<VoceComputo, RigaCr>();
                var occorrenze = new Dictionary<string, int>();
                var ordineCorpo = 0;
                foreach (var r in f.Righe)
                {
                    switch (r.Tipo)
                    {
                        case TipoRigaCr.Nota:
                            note.Add(string.Join(" ", new[] { r.Articolo, r.Descrizione }.Where(x => x is not null)));
                            continue;
                        case TipoRigaCr.CostoACorpo:
                            offerta.CostiACorpo.Add(new CostoACorpo
                            {
                                CommessaId = commessaId, Ordine = ++ordineCorpo, Descrizione = r.Descrizione ?? r.Articolo ?? "Costo a corpo",
                                Importo = r.Costo,
                            });
                            continue;
                    }

                    var chiave = LettoreFogliCr.ChiaveTariffa(r.Articolo) ?? "D:" + LettoreFogliCr.Normalizza(r.Descrizione ?? "");
                    var k = occorrenze[chiave] = occorrenze.GetValueOrDefault(chiave) + 1;
                    if (!vociGruppo.TryGetValue((chiave, k), out var voce))
                    {
                        voce = r.Articolo is null ? null : disponibili.FirstOrDefault(v => LettoreFogliCr.ChiaveTariffa(v.Tariffa) == chiave);
                        if (voce is not null)
                        {
                            disponibili.Remove(voce);
                            vociCollegate++;
                            if (voce.Prezzo != r.Prezzo)
                                avvisi.Add($"«{f.Nome}», riga {r.Riga}: prezzo {Formato.Numero(r.Prezzo)} diverso da quello del computo ({Formato.Numero(voce.Prezzo)}) per {voce.Tariffa}; vale quello del computo.");
                            if (nonSoggetta == 100 && voce.PercentualeNonSoggetta == 0) voce.PercentualeNonSoggetta = 100;
                        }
                        else
                        {
                            // Le voci presenti solo in un'offerta alternativa entrano nel computo con quantità zero,
                            // così il computo riflette la soluzione scelta; la quantità vive nell'offerta.
                            voce = new VoceComputo
                            {
                                ComputoId = computo.Id, CommessaId = commessaId, Ordine = ++ordineVoce,
                                Tariffa = r.Articolo, Descrizione = r.Descrizione ?? r.Articolo ?? pp.Descrizione,
                                Unita = UnitaMisura.Normalizza(r.Unita), Quantita = fp == base_ ? r.Quantita : 0, Prezzo = r.Prezzo,
                                PercentualeNonSoggetta = nonSoggetta,
                                Note = fp == base_ ? $"Dal foglio «{f.Nome}», riga {r.Riga}."
                                    : $"Voce presente solo nell'offerta alternativa «{f.Nome}» (riga {r.Riga}).",
                            };
                            db.VociComputo.Add(voce);
                            vociCreate++;
                        }
                        vociGruppo[(chiave, k)] = voce;
                        db.PacchettiVoci.Add(new PacchettoVoce { PacchettoId = pacchetto.Id, CommessaId = commessaId, VoceComputoId = voce.Id });
                    }
                    if (righeVoce.ContainsKey(voce))
                        avvisi.Add($"«{f.Nome}», riga {r.Riga}: voce ripetuta nello stesso foglio, sommata alla precedente.");
                    righeVoce[voce] = righeVoce.TryGetValue(voce, out var prec)
                        ? prec with
                        {
                            Quantita = prec.Quantita + r.Quantita,
                            Fornitura = Media(prec, r, x => x.Fornitura), FornituraEPosa = Media(prec, r, x => x.FornituraEPosa),
                            ManodoperaENoli = Media(prec, r, x => x.ManodoperaENoli),
                        }
                        : r;
                }
                offerta.Note = note.Count == 0 ? null : string.Join("\n", note);
                db.Offerte.Add(offerta);
                nOfferte++;
                offerte.Add((fp, offerta, righeVoce));
            }

            // Costi e quantità per voce di ogni offerta; le voci assenti da un foglio valgono zero in quell'offerta.
            foreach (var (fp, offerta, righe) in offerte)
            {
                foreach (var voce in vociGruppo.Values.Distinct())
                {
                    righe.TryGetValue(voce, out var r);
                    var quantita = r?.Quantita ?? 0;
                    offerta.Voci.Add(new OffertaVoce
                    {
                        CommessaId = commessaId, VoceComputoId = voce.Id,
                        QuantitaPianificazione = quantita == voce.Quantita ? null : quantita,
                        NotaQuantita = quantita == voce.Quantita ? null
                            : r is null ? $"Voce non prevista nel foglio «{fp.Foglio.Nome}»."
                            : $"Quantità del foglio «{fp.Foglio.Nome}».",
                        Fornitura = r?.Fornitura ?? 0, FornituraEPosa = r?.FornituraEPosa ?? 0, ManodoperaENoli = r?.ManodoperaENoli ?? 0,
                    });
                }
                if (fp == scelto) pacchetto.OffertaSceltaId = offerta.Id;

                var vociCalcolo = vociGruppo.Values.Distinct()
                    .Select(v => new VoceDiCalcolo(v.Id, v.Tariffa, v.Descrizione, v.Unita, v.Quantita, v.Prezzo, v.PercentualeNonSoggetta)).ToList();
                var risultato = CalcoloPacchetti.Calcola(offerta, vociCalcolo, ribasso);
                verifiche.Add(new VerificaFoglio(pp.Descrizione, fp.Foglio.Nome, fp == scelto,
                    fp.Foglio.RicavoNettoFoglio ?? 0, risultato.RicavoNetto, fp.Foglio.CostoFoglio ?? 0, risultato.Costo));
            }
        }

        db.NotaStorico = $"Importazione pianificazione {file}";
        await db.SaveChangesAsync(ct);
        db.RegistraOperazione("Importazione", nameof(Pacchetto), null, commessaId,
            $"Importato {file}: {nPacchetti} pacchetti, {nOfferte} offerte, {vociCreate} voci create e {vociCollegate} collegate al computo; " +
            $"{verifiche.Count(v => v.Concorde)} fogli su {verifiche.Count} con ricavo e costo identici all'Excel.");
        await db.SaveChangesAsync(ct);
        await transazione.CommitAsync(ct);

        return new EsitoImportazionePianificazione(nPacchetti, nOfferte, vociCreate, vociCollegate, verifiche, avvisi, esitoBackup.Percorso);
    }

    private static decimal Media(RigaCr a, RigaCr b, Func<RigaCr, decimal> costo)
    {
        var q = a.Quantita + b.Quantita;
        return q == 0 ? costo(b) : (a.Quantita * costo(a) + b.Quantita * costo(b)) / q;
    }

    private static TipoAffidamento TipoDa(string? oda, string descrizione, FoglioCr? foglio)
    {
        var t = LettoreFogliCr.Normalizza(oda ?? "");
        if (t.Contains("NOLO")) return TipoAffidamento.NoloACaldo;
        if (t.Contains("SUB")) return TipoAffidamento.Subappalto;
        if (LettoreFogliCr.Normalizza(descrizione).Contains("ECONOMIA")) return TipoAffidamento.LavoriInEconomia;
        return foglio?.TipoOfferta == TipoOfferta.SolaFornitura ? TipoAffidamento.Fornitura : TipoAffidamento.FornituraEPosa;
    }

    private static string NomeFoglio(string nome)
    {
        var n = nome.Trim();
        if (n.StartsWith("CR", StringComparison.OrdinalIgnoreCase)) n = n[2..];
        return n.Trim(' ', '_');
    }
}

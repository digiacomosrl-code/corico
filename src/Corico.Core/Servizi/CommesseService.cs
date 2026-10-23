using Corico.Core.Calcolo;
using System.Text.Json;
using Corico.Core.Configurazione;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed record CommessaRiepilogo(
    Guid Id, string Codice, string Descrizione, StatoCommessa Stato, bool Archiviata,
    string? Cliente, string? CodiceEsterno, string? Comune, decimal? ValoreContrattuale);

/// <summary>Dati modificabili della scheda commessa, con la versione letta per il controllo dei conflitti.</summary>
public sealed class DatiCommessa
{
    public Guid Id { get; set; }
    public int Versione { get; set; }
    public string Codice { get; set; } = "";
    public StatoCommessa Stato { get; set; }
    public bool Archiviata { get; set; }

    public string Descrizione { get; set; } = "";
    public Guid? ClienteId { get; set; }
    public string? CodiceEsterno { get; set; }
    public string? IndirizzoCantiere { get; set; }
    public string? ComuneCantiere { get; set; }
    public string? ProvinciaCantiere { get; set; }
    public string? Responsabile { get; set; }
    public DateOnly? DataInizioPrevista { get; set; }
    public DateOnly? DataFinePrevista { get; set; }
    public decimal? ValoreContrattuale { get; set; }
    public Guid? ContrattoDocumentoId { get; set; }
    public string? ContrattoNumero { get; set; }
    public DateOnly? ContrattoData { get; set; }
    public string? Note { get; set; }

    /// <summary>Solo lettura: chi ha modificato la scheda per ultimo e quando.</summary>
    public string UltimaModifica { get; set; } = "";

    public DatiCommessa Clona() => (DatiCommessa)MemberwiseClone();

    internal static DatiCommessa Da(Commessa c) => new()
    {
        Id = c.Id, Versione = c.Versione, Codice = c.Codice, Stato = c.Stato, Archiviata = c.Archiviata,
        Descrizione = c.Descrizione, ClienteId = c.ClienteId, CodiceEsterno = c.CodiceEsterno,
        IndirizzoCantiere = c.IndirizzoCantiere, ComuneCantiere = c.ComuneCantiere, ProvinciaCantiere = c.ProvinciaCantiere,
        Responsabile = c.Responsabile, DataInizioPrevista = c.DataInizioPrevista, DataFinePrevista = c.DataFinePrevista,
        ValoreContrattuale = c.ValoreContrattuale, ContrattoDocumentoId = c.ContrattoDocumentoId,
        ContrattoNumero = c.ContrattoNumero, ContrattoData = c.ContrattoData, Note = c.Note,
        UltimaModifica = Formato.UltimaModifica(c),
    };
}

public sealed class CommesseService(IDbContextFactory<CoreDbContext> fabbrica, PercorsiDati percorsi, TimeProvider orologio)
{
    private static readonly SemaphoreSlim BloccoCodici = new(1, 1);

    /// <summary>
    /// Crea una commessa con il solo codice automatico e la descrizione (sez. 11.3).
    /// Il progressivo dell'anno non viene mai riutilizzato, anche se la commessa sarà archiviata.
    /// </summary>
    public async Task<DatiCommessa> CreaAsync(string descrizione, Guid? clienteId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(descrizione))
            throw new OperazioneNonValidaException("La descrizione della commessa è obbligatoria.");

        await BloccoCodici.WaitAsync(ct);
        try
        {
            return await CreaInternoAsync(descrizione, clienteId, ct);
        }
        finally
        {
            BloccoCodici.Release();
        }
    }

    private async Task<DatiCommessa> CreaInternoAsync(string descrizione, Guid? clienteId, CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        await using var transazione = await db.Database.BeginTransactionAsync(ct);

        var anno = orologio.GetLocalNow().Year;
        var contatore = await db.ContatoriCodice.FindAsync([anno], ct);
        if (contatore is null)
        {
            contatore = new ContatoreCodice { Anno = anno };
            db.ContatoriCodice.Add(contatore);
        }
        // Il registro esterno al database impedisce di riassegnare un codice
        // già usato anche dopo il ripristino di un backup precedente.
        var registro = LeggiRegistroCodici();
        contatore.Ultimo = Math.Max(contatore.Ultimo, registro.GetValueOrDefault(anno)) + 1;

        var commessa = new Commessa
        {
            Codice = FormattaCodice(anno, contatore.Ultimo),
            Descrizione = descrizione.Trim(),
            ClienteId = clienteId,
        };
        db.Commesse.Add(commessa);
        await db.SaveChangesAsync(ct);
        registro[anno] = contatore.Ultimo;
        ScriviRegistroCodici(registro);
        await transazione.CommitAsync(ct);
        return DatiCommessa.Da(commessa);
    }

    private string FileRegistroCodici => Path.Combine(percorsi.Config, "codici-commessa-assegnati.json");

    private Dictionary<int, int> LeggiRegistroCodici() =>
        File.Exists(FileRegistroCodici)
            ? JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(FileRegistroCodici)) ?? []
            : [];

    private void ScriviRegistroCodici(Dictionary<int, int> registro)
    {
        var temporaneo = FileRegistroCodici + ".tmp";
        File.WriteAllText(temporaneo, JsonSerializer.Serialize(registro));
        File.Move(temporaneo, FileRegistroCodici, overwrite: true);
    }

    public static string FormattaCodice(int anno, int progressivo) => $"{anno}-{progressivo:000}";

    public async Task<IReadOnlyList<CommessaRiepilogo>> ElencoAsync(
        string? testo = null, StatoCommessa? stato = null, bool includiArchiviate = false, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var query = db.Commesse.AsNoTracking().AsQueryable();
        if (!includiArchiviate) query = query.Where(c => !c.Archiviata);
        if (stato is not null) query = query.Where(c => c.Stato == stato);
        if (!string.IsNullOrWhiteSpace(testo))
        {
            var t = testo.Trim().ToLower();
            query = query.Where(c => c.Codice.ToLower().Contains(t) || c.Descrizione.ToLower().Contains(t)
                || (c.CodiceEsterno != null && c.CodiceEsterno.ToLower().Contains(t))
                || (c.Cliente != null && c.Cliente.Denominazione.ToLower().Contains(t)));
        }

        return await query
            .OrderByDescending(c => c.Codice)
            .Select(c => new CommessaRiepilogo(c.Id, c.Codice, c.Descrizione, c.Stato, c.Archiviata,
                c.Cliente != null ? c.Cliente.Denominazione : null, c.CodiceEsterno, c.ComuneCantiere, c.ValoreContrattuale))
            .ToListAsync(ct);
    }

    public async Task<DatiCommessa?> OttieniAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.Commesse.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : DatiCommessa.Da(c);
    }

    public async Task<DatiCommessa> AggiornaAsync(DatiCommessa dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Descrizione))
            throw new OperazioneNonValidaException("La descrizione della commessa è obbligatoria.");
        if (dati.DataInizioPrevista is not null && dati.DataFinePrevista is not null && dati.DataFinePrevista < dati.DataInizioPrevista)
            throw new OperazioneNonValidaException("La data di fine prevista precede la data di inizio prevista.");
        if (dati.ValoreContrattuale < 0)
            throw new OperazioneNonValidaException("Il valore contrattuale non può essere negativo.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await CaricaPerModificaAsync(db, dati.Id, dati.Versione, ct);

        c.Descrizione = dati.Descrizione.Trim();
        c.ClienteId = dati.ClienteId;
        c.CodiceEsterno = Pulisci(dati.CodiceEsterno);
        c.IndirizzoCantiere = Pulisci(dati.IndirizzoCantiere);
        c.ComuneCantiere = Pulisci(dati.ComuneCantiere);
        c.ProvinciaCantiere = Pulisci(dati.ProvinciaCantiere)?.ToUpperInvariant();
        c.Responsabile = Pulisci(dati.Responsabile);
        c.DataInizioPrevista = dati.DataInizioPrevista;
        c.DataFinePrevista = dati.DataFinePrevista;
        c.ValoreContrattuale = dati.ValoreContrattuale;
        c.ContrattoDocumentoId = dati.ContrattoDocumentoId;
        c.ContrattoNumero = Pulisci(dati.ContrattoNumero);
        c.ContrattoData = dati.ContrattoData;
        c.Note = Pulisci(dati.Note);

        await db.SaveChangesAsync(ct);
        return DatiCommessa.Da(c);
    }

    public async Task<DatiCommessa> CambiaStatoAsync(Guid id, int versione, StatoCommessa nuovoStato, string? nota = null, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await CaricaPerModificaAsync(db, id, versione, ct);
        if (c.Stato == nuovoStato) return DatiCommessa.Da(c);

        c.Stato = nuovoStato;
        db.NotaStorico = nota ?? "Cambio di stato";
        await db.SaveChangesAsync(ct);
        return DatiCommessa.Da(c);
    }

    public async Task<DatiCommessa> ImpostaArchiviazioneAsync(Guid id, int versione, bool archiviata, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await CaricaPerModificaAsync(db, id, versione, ct);
        c.Archiviata = archiviata;
        db.NotaStorico = archiviata ? "Commessa archiviata" : "Commessa riattivata";
        await db.SaveChangesAsync(ct);
        return DatiCommessa.Da(c);
    }

    /// <summary>Dati non ancora compilati, da evidenziare nella scheda (sez. 11.3). Non blocca il salvataggio.</summary>
    public static IReadOnlyList<string> DatiMancanti(DatiCommessa c)
    {
        var mancanti = new List<string>();
        if (c.ClienteId is null) mancanti.Add("Cliente");
        if (string.IsNullOrWhiteSpace(c.ComuneCantiere)) mancanti.Add("Comune del cantiere");
        if (string.IsNullOrWhiteSpace(c.Responsabile)) mancanti.Add("Responsabile");
        if (c.DataInizioPrevista is null) mancanti.Add("Data di inizio prevista");
        if (c.DataFinePrevista is null) mancanti.Add("Data di fine prevista");
        if (c.ValoreContrattuale is null) mancanti.Add("Valore contrattuale");
        return mancanti;
    }

    private static async Task<Commessa> CaricaPerModificaAsync(CoreDbContext db, Guid id, int versioneLetta, CancellationToken ct)
    {
        var c = await db.Commesse.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("La commessa non esiste.");
        // La condizione di aggiornamento usa la versione letta dall'utente, non quella appena caricata.
        db.Entry(c).Property(x => x.Versione).OriginalValue = versioneLetta;
        return c;
    }

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

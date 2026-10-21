using System.Text;
using System.Text.RegularExpressions;
using Corico.Core.Dati;
using Corico.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Corico.Core.Servizi;

public sealed class DatiCliente
{
    public Guid Id { get; set; }
    public int Versione { get; set; }
    public string Denominazione { get; set; } = "";
    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? Indirizzo { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? Note { get; set; }
    public bool Archiviato { get; set; }

    internal static DatiCliente Da(Cliente c) => new()
    {
        Id = c.Id, Versione = c.Versione, Denominazione = c.Denominazione, PartitaIva = c.PartitaIva,
        CodiceFiscale = c.CodiceFiscale, Indirizzo = c.Indirizzo, Comune = c.Comune, Provincia = c.Provincia,
        Email = c.Email, Telefono = c.Telefono, Note = c.Note, Archiviato = c.Archiviato,
    };
}

public sealed record PossibileDuplicato(Guid Id, string Denominazione, string Motivo);

public sealed partial class ClientiService(IDbContextFactory<CoreDbContext> fabbrica)
{
    public async Task<IReadOnlyList<DatiCliente>> ElencoAsync(string? testo = null, bool includiArchiviati = false, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var query = db.Clienti.AsNoTracking().AsQueryable();
        if (!includiArchiviati) query = query.Where(c => !c.Archiviato);
        if (!string.IsNullOrWhiteSpace(testo))
        {
            var t = testo.Trim().ToLower();
            query = query.Where(c => c.Denominazione.ToLower().Contains(t)
                || (c.PartitaIva != null && c.PartitaIva.Contains(t))
                || (c.CodiceFiscale != null && c.CodiceFiscale.ToLower().Contains(t)));
        }
        var elenco = await query.OrderBy(c => c.Denominazione).ToListAsync(ct);
        return elenco.Select(DatiCliente.Da).ToList();
    }

    public async Task<DatiCliente?> OttieniAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.Clienti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : DatiCliente.Da(c);
    }

    /// <summary>
    /// Clienti già presenti con stessa partita IVA, stesso codice fiscale o nome simile (sez. 11.4).
    /// La segnalazione non impedisce il salvataggio: decide l'utente.
    /// </summary>
    public async Task<IReadOnlyList<PossibileDuplicato>> PossibiliDuplicatiAsync(DatiCliente dati, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var altri = await db.Clienti.AsNoTracking().Where(c => c.Id != dati.Id).ToListAsync(ct);

        var piva = NormalizzaCodice(dati.PartitaIva);
        var cf = NormalizzaCodice(dati.CodiceFiscale);
        var nome = NormalizzaNome(dati.Denominazione);
        var risultato = new List<PossibileDuplicato>();

        foreach (var c in altri)
        {
            if (piva is not null && NormalizzaCodice(c.PartitaIva) == piva)
                risultato.Add(new(c.Id, c.Denominazione, "Stessa partita IVA"));
            else if (cf is not null && NormalizzaCodice(c.CodiceFiscale) == cf)
                risultato.Add(new(c.Id, c.Denominazione, "Stesso codice fiscale"));
            else if (nome.Length >= 3 && NomiSimili(nome, NormalizzaNome(c.Denominazione)))
                risultato.Add(new(c.Id, c.Denominazione, "Denominazione simile"));
        }
        return risultato;
    }

    public async Task<DatiCliente> SalvaAsync(DatiCliente dati, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dati.Denominazione))
            throw new OperazioneNonValidaException("La denominazione del cliente è obbligatoria.");

        await using var db = await fabbrica.CreateDbContextAsync(ct);
        Cliente c;
        if (dati.Id == Guid.Empty)
        {
            c = new Cliente();
            db.Clienti.Add(c);
        }
        else
        {
            c = await db.Clienti.FirstOrDefaultAsync(x => x.Id == dati.Id, ct)
                ?? throw new OperazioneNonValidaException("Il cliente non esiste.");
            db.Entry(c).Property(x => x.Versione).OriginalValue = dati.Versione;
        }

        c.Denominazione = dati.Denominazione.Trim();
        c.PartitaIva = Pulisci(dati.PartitaIva);
        c.CodiceFiscale = Pulisci(dati.CodiceFiscale)?.ToUpperInvariant();
        c.Indirizzo = Pulisci(dati.Indirizzo);
        c.Comune = Pulisci(dati.Comune);
        c.Provincia = Pulisci(dati.Provincia)?.ToUpperInvariant();
        c.Email = Pulisci(dati.Email);
        c.Telefono = Pulisci(dati.Telefono);
        c.Note = Pulisci(dati.Note);

        await db.SaveChangesAsync(ct);
        return DatiCliente.Da(c);
    }

    public async Task<DatiCliente> ImpostaArchiviazioneAsync(Guid id, int versione, bool archiviato, CancellationToken ct = default)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var c = await db.Clienti.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new OperazioneNonValidaException("Il cliente non esiste.");
        db.Entry(c).Property(x => x.Versione).OriginalValue = versione;
        c.Archiviato = archiviato;
        await db.SaveChangesAsync(ct);
        return DatiCliente.Da(c);
    }

    internal static string? NormalizzaCodice(string? codice)
    {
        if (string.IsNullOrWhiteSpace(codice)) return null;
        var s = new string(codice.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (s.StartsWith("IT") && s.Length == 13) s = s[2..];
        return s.Length == 0 ? null : s;
    }

    internal static string NormalizzaNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "";
        var s = nome.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        s = new string(s.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());
        s = FormeGiuridiche().Replace(" " + s + " ", " ");
        return SpaziMultipli().Replace(s, " ").Trim();
    }

    internal static bool NomiSimili(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b) || b.Contains(a));

    private static string? Pulisci(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"\s(srl|srls|spa|sas|snc|sapa|scarl|scrl|soc|coop|societa|cooperativa|ditta|impresa)(?=\s)")]
    private static partial Regex FormeGiuridiche();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();
}

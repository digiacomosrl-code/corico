using System.Text;
using Corico.Modules.Documenti.Dati;
using Microsoft.EntityFrameworkCore;

namespace Corico.Modules.Documenti.Servizi;

public sealed record PaginaTrovata(Guid DocumentoId, int Pagina, string Estratto, double Rilevanza);

/// <summary>
/// Ricerca nel contenuto con l'indice testuale di SQLite (FTS5). È l'unico punto con SQL specifico di SQLite:
/// passando a SQL Server o PostgreSQL si sostituisce solo questa classe e la tabella creata dalla migrazione.
/// L'indice ignora maiuscole e accenti; a capo e impaginazione non contano perché si cercano parole.
/// </summary>
public sealed class IndiceRicerca(IDbContextFactory<DocumentiDbContext> fabbrica)
{
    public const string Tabella = "DOC_Ricerca";

    /// <summary>Segni che delimitano le parole trovate nell'estratto (mai presenti nel testo dei documenti).</summary>
    public const char InizioEvidenza = '\u0001', FineEvidenza = '\u0002';


    public async Task IndicizzaAsync(Guid documentoId, IEnumerable<(int Pagina, string Testo)> pagine, CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"DELETE FROM DOC_Ricerca WHERE documento = {documentoId.ToString()}", ct);
        foreach (var (pagina, testo) in pagine.Where(p => !string.IsNullOrWhiteSpace(p.Testo)))
            await db.Database.ExecuteSqlAsync($"INSERT INTO DOC_Ricerca (testo, documento, pagina) VALUES ({testo}, {documentoId.ToString()}, {pagina})", ct);
        await tx.CommitAsync(ct);
    }

    public async Task RimuoviAsync(Guid documentoId, CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlAsync($"DELETE FROM DOC_Ricerca WHERE documento = {documentoId.ToString()}", ct);
    }

    /// <summary>Pagine che contengono tutte le parole cercate (l'ultima anche come inizio di parola), le più pertinenti per prime.</summary>
    public async Task<IReadOnlyList<PaginaTrovata>> CercaAsync(string testo, int massimo, CancellationToken ct)
    {
        var espressione = Espressione(testo);
        if (espressione is null) return [];
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT documento, pagina, snippet({Tabella}, 0, char(1), char(2), '…', 16), bm25({Tabella}) FROM {Tabella} " +
            $"WHERE {Tabella} MATCH $q ORDER BY bm25({Tabella}) LIMIT $n";
        var q = cmd.CreateParameter(); q.ParameterName = "$q"; q.Value = espressione; cmd.Parameters.Add(q);
        var n = cmd.CreateParameter(); n.ParameterName = "$n"; n.Value = massimo; cmd.Parameters.Add(n);
        var risultati = new List<PaginaTrovata>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            risultati.Add(new(Guid.Parse(r.GetString(0)), r.GetInt32(1), r.GetString(2), -r.GetDouble(3)));
        return risultati;
    }

    public async Task<int> ContaPagineAsync(CancellationToken ct)
    {
        await using var db = await fabbrica.CreateDbContextAsync(ct);
        return await db.Database.SqlQueryRaw<int>($"SELECT count(*) AS Value FROM {Tabella}").SingleAsync(ct);
    }

    /// <summary>
    /// Parole dell'utente come termini tra virgolette (nessuna sintassi speciale possibile), in «e»;
    /// l'ultima parola vale anche come inizio di parola (es. «REI» trova «REI60»).
    /// </summary>
    internal static string? Espressione(string testo)
    {
        var parole = Somiglianza.Parole(testo);
        if (parole.Count == 0) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < parole.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('"').Append(parole[i]).Append('"');
            if (i == parole.Count - 1) sb.Append('*');
        }
        return sb.ToString();
    }
}

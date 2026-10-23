namespace Corico.Modules.Documenti.Servizi;

public sealed record TipoProposto(string Tipo, string Motivo);

/// <summary>
/// Tipo di documento proposto con regole semplici sul testo (specifica Modulo 3, sez. 4): vince la parola chiave
/// che compare per prima nelle prime pagine, perché titolo e intestazione dicono di che documento si tratta.
/// La proposta resta da confermare.
/// </summary>
public static class Classificatore
{
    public const string Offerta = "Offerta fornitore";
    public const string Listino = "Listino";
    public const string Computo = "Computo / perizia";
    public const string Contratto = "Contratto";
    public const string Ordine = "Ordine";
    public const string Sal = "SAL / stato finale";
    public const string Corrispondenza = "Corrispondenza";

    /// <summary>Tipi di partenza, nell'ordine in cui si propongono all'utente.</summary>
    public static readonly string[] Tipi = [Offerta, Listino, Computo, Contratto, Ordine, Sal, Corrispondenza, "Altro"];

    private static readonly (string Tipo, string[] Parole)[] Regole =
    [
        (Sal, ["stato finale", "stato di avanzamento", "stato d'avanzamento", "certificato di pagamento"]),
        (Computo, ["computo metrico", "perizia di variante", "perizia di assestamento", "elenco prezzi unitari", "stima dei lavori"]),
        (Listino, ["listino prezzi", "listino"]),
        (Ordine, ["ordine di acquisto", "ordine d'acquisto", "ordine di fornitura", "conferma d'ordine"]),
        (Contratto, ["contratto di subappalto", "contratto d'appalto", "contratto di appalto", "contratto"]),
        (Offerta, ["offerta economica", "preventivo cliente", "offerta", "preventivo"]),
    ];

    /// <summary>Caratteri esaminati dall'inizio del documento.</summary>
    private const int Ampiezza = 4000;

    /// <summary>Una mail è sempre corrispondenza: offerte e computi arrivano come allegati, che hanno una proposta propria.</summary>
    public static TipoProposto? Proponi(IReadOnlyList<string> pagine, bool mail)
    {
        if (mail) return new(Corrispondenza, "è una mail");
        var testo = string.Join("\n", pagine).ToLowerInvariant();
        if (testo.Length > Ampiezza) testo = testo[..Ampiezza];
        testo = string.Join(' ', testo.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

        (string Tipo, string Parola, int Posizione)? migliore = null;
        foreach (var (tipo, parole) in Regole)
            foreach (var parola in parole)
            {
                var i = testo.IndexOf(parola, StringComparison.Ordinal);
                if (i >= 0 && (migliore is null || i < migliore.Value.Posizione)) migliore = (tipo, parola, i);
            }

        return migliore is { } m ? new(m.Tipo, $"il testo contiene «{m.Parola}»") : null;
    }
}

using Corico.Core.Calcolo;

namespace Corico.Modules.Preventivo.Servizi;

public readonly record struct RicavoVoce(decimal Lordo, decimal PrezzoRibassato, decimal Netto, decimal LordoNonSoggetto);

/// <summary>
/// Regole di calcolo dei ricavi (specifica Modulo 1, regole R1–R3), identiche alle formule
/// dei fogli CR: nessun arrotondamento intermedio.
/// </summary>
public static class CalcoloRicavi
{
    /// <param name="ribassoPercentuale">Ribasso unico di commessa, es. 21,5.</param>
    /// <param name="percentualeNonSoggetta">Quota del prezzo esclusa dal ribasso (100 = oneri di sicurezza).</param>
    public static RicavoVoce Voce(decimal quantita, decimal prezzo, decimal ribassoPercentuale, decimal percentualeNonSoggetta)
    {
        var quotaSoggetta = 1m - percentualeNonSoggetta / 100m;
        var prezzoRibassato = prezzo * (1m - ribassoPercentuale / 100m * quotaSoggetta);
        var lordo = quantita * prezzo;
        return new RicavoVoce(lordo, prezzoRibassato, quantita * prezzoRibassato, lordo * percentualeNonSoggetta / 100m);
    }
}

/// <summary>Quadro economico dei ricavi della commessa (sez. 16.3 del prompt).</summary>
public sealed record QuadroEconomico(
    decimal RibassoPercentuale,
    decimal ImportoLavoriLordo,
    decimal ImportoNonSoggetto,
    decimal ImportoSoggetto,
    decimal ImportoRibasso,
    decimal ImportoNetto,
    decimal? ValoreContrattuale,
    int NumeroVoci,
    int VociSenzaPrezzo)
{
    /// <summary>Differenza tra valore contrattuale sintetico e ricavo netto analitico: solo segnalata, mai corretta.</summary>
    public decimal? DifferenzaDaContratto => ValoreContrattuale is null ? null : ValoreContrattuale - ImportoNetto;

    public static QuadroEconomico Calcola(decimal ribasso, IEnumerable<(decimal Quantita, decimal Prezzo, decimal NonSoggetta)> voci, decimal? valoreContrattuale)
    {
        decimal lordo = 0, nonSoggetto = 0, netto = 0;
        int numero = 0, senzaPrezzo = 0;
        foreach (var v in voci)
        {
            var r = CalcoloRicavi.Voce(v.Quantita, v.Prezzo, ribasso, v.NonSoggetta);
            lordo += r.Lordo;
            nonSoggetto += r.LordoNonSoggetto;
            netto += r.Netto;
            numero++;
            if (v.Prezzo == 0) senzaPrezzo++;
        }
        return new QuadroEconomico(ribasso, lordo, nonSoggetto, lordo - nonSoggetto, lordo - netto, netto,
            valoreContrattuale, numero, senzaPrezzo);
    }
}

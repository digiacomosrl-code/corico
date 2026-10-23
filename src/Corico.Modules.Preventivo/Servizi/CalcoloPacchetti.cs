using Corico.Core.Calcolo;
using Corico.Modules.Preventivo.Dominio;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>Dati della voce del computo necessari al calcolo di un pacchetto.</summary>
public sealed record VoceDiCalcolo(Guid Id, string? Tariffa, string Descrizione, string? Unita,
    decimal Quantita, decimal Prezzo, decimal PercentualeNonSoggetta)
{
    /// <summary>Codice WBS della voce, se assegnato.</summary>
    public string? Wbs { get; init; }
}

/// <summary>Ricavo e costo di una voce secondo un'offerta.</summary>
public sealed record VoceOffertaCalcolata(
    VoceDiCalcolo Voce,
    decimal Quantita,
    bool QuantitaModificata,
    string? NotaQuantita,
    RicavoVoce Ricavo,
    decimal CostoUnitarioSintetico,
    decimal CostoSintetico,
    decimal CostoRighe,
    int NumeroRighe,
    bool Analitico = false)
{
    public decimal Costo => CostoSintetico + CostoRighe;
    public decimal? CostoUnitario => Quantita == 0 ? null : Costo / Quantita;
    public decimal Mdc => Ricavo.Netto - Costo;
}

public sealed record RisultatoOfferta(
    IReadOnlyList<VoceOffertaCalcolata> Voci,
    decimal RicavoLordo,
    decimal RicavoNetto,
    decimal CostoVoci,
    decimal CostoRigheNonAssegnate,
    int RigheNonAssegnate,
    decimal CostoACorpo,
    decimal TotaleRigheOfferta)
{
    public decimal Costo => CostoVoci + CostoRigheNonAssegnate + CostoACorpo;
    public decimal Mdc => RicavoNetto - Costo;
    public decimal? MarginePercentuale => Importi.MargineSulRicavo(RicavoNetto, Costo);
    public decimal? RicaricoPercentuale => Importi.RicaricoSulCosto(RicavoNetto, Costo);

    /// <summary>Totale dell'offerta come la scrive il fornitore: righe più voci a corpo.</summary>
    public decimal TotaleOffertaCalcolato => TotaleRigheOfferta + CostoACorpo;
}

/// <summary>
/// Regole di calcolo dei pacchetti (sez. 13.1, 14.1, 16.4), allineate ai fogli CR:
/// costo voce = quantità × (Fornitura + Fornitura e posa + Manodopera e noli) più le righe d'offerta assegnate;
/// ricavo netto sulla quantità di pianificazione; MDC = ricavo netto − costo.
/// </summary>
public static class CalcoloPacchetti
{
    /// <summary>Importo netto di una riga d'offerta, arrotondato al centesimo come nelle offerte dei fornitori.</summary>
    public static decimal ImportoRiga(decimal quantita, decimal prezzoListino, decimal scontoPercentuale) =>
        Importi.Arrotonda(quantita * prezzoListino * (1m - scontoPercentuale / 100m));

    public static decimal PrezzoNetto(decimal prezzoListino, decimal scontoPercentuale) =>
        prezzoListino * (1m - scontoPercentuale / 100m);

    public static RisultatoOfferta Calcola(Offerta offerta, IReadOnlyList<VoceDiCalcolo> vociPacchetto, decimal ribassoPercentuale)
    {
        var perVoce = offerta.Voci.ToDictionary(v => v.VoceComputoId);
        var righePerVoce = offerta.Righe.Where(r => r.VoceComputoId is not null)
            .GroupBy(r => r.VoceComputoId!.Value)
            .ToDictionary(g => g.Key, g => (Importo: g.Sum(r => ImportoRiga(r.Quantita, r.PrezzoListino, r.ScontoPercentuale)), Numero: g.Count()));

        var voci = new List<VoceOffertaCalcolata>();
        foreach (var v in vociPacchetto)
        {
            perVoce.TryGetValue(v.Id, out var ov);
            var quantita = ov?.QuantitaPianificazione ?? v.Quantita;
            var unitario = CostoUnitario(ov);
            righePerVoce.TryGetValue(v.Id, out var righe);
            voci.Add(new VoceOffertaCalcolata(v, quantita,
                ov?.QuantitaPianificazione is { } qp && qp != v.Quantita, ov?.NotaQuantita,
                CalcoloRicavi.Voce(quantita, v.Prezzo, ribassoPercentuale, v.PercentualeNonSoggetta),
                unitario, quantita * unitario, righe.Importo, righe.Numero, ov is { Risorse.Count: > 0 }));
        }

        var idVoci = vociPacchetto.Select(v => v.Id).ToHashSet();
        var nonAssegnate = offerta.Righe.Where(r => r.VoceComputoId is null || !idVoci.Contains(r.VoceComputoId.Value)).ToList();
        var totaleRighe = offerta.Righe.Sum(r => ImportoRiga(r.Quantita, r.PrezzoListino, r.ScontoPercentuale));

        return new RisultatoOfferta(voci,
            voci.Sum(v => v.Ricavo.Lordo), voci.Sum(v => v.Ricavo.Netto), voci.Sum(v => v.Costo),
            nonAssegnate.Sum(r => ImportoRiga(r.Quantita, r.PrezzoListino, r.ScontoPercentuale)), nonAssegnate.Count,
            offerta.CostiACorpo.Where(c => !c.Incluso).Sum(c => c.Importo), totaleRighe);
    }

    /// <summary>
    /// Costo unitario della voce secondo l'offerta: somma delle risorse (livello analitico) se presenti,
    /// altrimenti Fornitura + Fornitura e posa + Manodopera e noli (livello sintetico). Mai entrambi.
    /// </summary>
    public static decimal CostoUnitario(OffertaVoce? ov) =>
        ov is null ? 0
        : ov.Risorse.Count > 0 ? ov.Risorse.Sum(Risorse.CostoUnitario)
        : ov.Fornitura + ov.FornituraEPosa + ov.ManodoperaENoli;

    /// <summary>Ricavo del pacchetto senza offerta: quantità del computo.</summary>
    public static (decimal Lordo, decimal Netto) RicavoDaComputo(IEnumerable<VoceDiCalcolo> voci, decimal ribassoPercentuale)
    {
        decimal lordo = 0, netto = 0;
        foreach (var v in voci)
        {
            var r = CalcoloRicavi.Voce(v.Quantita, v.Prezzo, ribassoPercentuale, v.PercentualeNonSoggetta);
            lordo += r.Lordo;
            netto += r.Netto;
        }
        return (lordo, netto);
    }
}

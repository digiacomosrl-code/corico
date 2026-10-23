using Corico.Core.Calcolo;
using Corico.Modules.Preventivo.Dominio;

namespace Corico.Modules.Preventivo.Servizi;

/// <summary>
/// Stato economico completo del preventivo in un istante: è sia il riepilogo della bozza
/// sia il contenuto, congelato, di una versione ufficiale.
/// </summary>
public sealed record Fotografia(
    DateTime DataUtc,
    decimal RibassoPercentuale,
    decimal? ObiettivoMarginePercentuale,
    TotaliFotografia Totali,
    IReadOnlyList<VoceFotografia> Voci,
    IReadOnlyList<PacchettoFotografia> Pacchetti,
    IReadOnlyList<SpesaFotografia> SpeseGenerali,
    IReadOnlyList<IntegrazioneFotografia> Integrazioni);

public sealed record TotaliFotografia(
    decimal ImportoLavoriLordo,
    decimal ImportoNonSoggetto,
    decimal ImportoRibasso,
    decimal RicavoNettoComputo,
    decimal? ValoreContrattuale,
    decimal RicavoPacchetti,
    decimal RicavoNonAssegnato,
    int VociNonAssegnate,
    decimal IntegrazioniCommessa,
    decimal Costo,
    decimal RicavoSenzaCosto,
    int PacchettiSenzaCosto,
    decimal SpeseGenerali)
{
    /// <summary>Ricavo di commessa (R10): pacchetti (con le quantità di pianificazione e le integrazioni), voci libere e integrazioni di commessa.</summary>
    public decimal Ricavo => RicavoPacchetti + RicavoNonAssegnato + IntegrazioniCommessa;

    /// <summary>MDC di commessa (R9 su R10 e R11).</summary>
    public decimal Mdc => Ricavo - Costo;

    public decimal? MarginePercentuale => Importi.MargineSulRicavo(Ricavo, Costo);
    public decimal? RicaricoPercentuale => Importi.RicaricoSulCosto(Ricavo, Costo);

    /// <summary>Risultato (R12): MDC meno spese generali.</summary>
    public decimal Risultato => Mdc - SpeseGenerali;

    public decimal? RisultatoPercentuale => Ricavo == 0 ? null : Risultato / Ricavo * 100m;
}

public sealed record VoceFotografia(
    Guid Id, int Ordine, string? Tariffa, string Descrizione, string? Unita,
    decimal Quantita, decimal Prezzo, decimal PercentualeNonSoggetta, decimal RicavoNetto,
    Guid? PacchettoId, decimal? QuantitaPianificazione, decimal? Costo)
{
    /// <summary>Codice WBS della voce (assente nelle versioni salvate prima della 1.2.1).</summary>
    public string? Wbs { get; init; }
}

public sealed record PacchettoFotografia(
    Guid Id, int Ordine, string Descrizione, TipoAffidamento Tipo, StatoCosto Stato,
    string? Fornitore, string? NumeroOfferta, DateOnly? DataOfferta, DateOnly? ScadenzaOfferta,
    int NumeroVoci, int NumeroOfferte, decimal RicavoNetto, decimal Integrazioni, decimal? Costo,
    decimal? CostoPianificato = null, decimal ImportoOrdinato = 0, decimal PercentualeOrdinata = 0)
{
    // Costo: costo attuale (ordini emessi + quanto resta da ordinare ai prezzi pianificati); CostoPianificato: dall'offerta scelta.
    // Le fotografie precedenti al Modulo 2 non hanno CostoPianificato: coincide con il costo.
    public decimal? CostoOfferta => CostoPianificato ?? Costo;

    public decimal Ricavo => RicavoNetto + Integrazioni;
    public decimal? Mdc => Costo is null ? null : Ricavo - Costo;
    public decimal? MarginePercentuale => Importi.MargineSulRicavo(Ricavo, Costo);
}

public sealed record SpesaFotografia(
    Guid Id, string Descrizione, ModoSpesaGenerale Modo, decimal Importo, decimal Percentuale,
    BaseSpesaGenerale Base, decimal ImportoCalcolato, string? Note);

public sealed record IntegrazioneFotografia(Guid Id, string Descrizione, Guid? PacchettoId, string? Pacchetto, decimal Importo, string? Note);

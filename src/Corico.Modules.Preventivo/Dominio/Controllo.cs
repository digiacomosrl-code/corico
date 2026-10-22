using Corico.Core.Dominio;

namespace Corico.Modules.Preventivo.Dominio;

public enum ModoSpesaGenerale
{
    Importo = 0,
    Percentuale = 1,
}

/// <summary>Base esplicita su cui si applica una spesa generale in percentuale.</summary>
public enum BaseSpesaGenerale
{
    RicavoCommessa = 0,
    CostoCommessa = 1,
    ImportoLavoriLordo = 2,
}

/// <summary>
/// Spesa generale di commessa (regola R12): a importo o in percentuale su una base esplicita.
/// Non viene mai ripartita sulle lavorazioni: riduce solo il risultato finale.
/// </summary>
public sealed class SpesaGenerale : EntitaBase
{
    public Guid CommessaId { get; set; }
    public int Ordine { get; set; }
    public string Descrizione { get; set; } = "";
    public ModoSpesaGenerale Modo { get; set; }
    public decimal Importo { get; set; }
    public decimal Percentuale { get; set; }
    public BaseSpesaGenerale Base { get; set; }
    public string? Note { get; set; }
}

/// <summary>Integrazione di ricavo a importo (es. oneri di discarica da recuperare), di pacchetto o di commessa.</summary>
public sealed class IntegrazioneRicavo : EntitaBase
{
    public Guid CommessaId { get; set; }

    /// <summary>Pacchetto a cui si somma il ricavo; null = integrazione di commessa.</summary>
    public Guid? PacchettoId { get; set; }

    public string Descrizione { get; set; } = "";
    public decimal Importo { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Versione ufficiale del preventivo: fotografia completa e immutabile della bozza al momento
/// della pubblicazione, con prezzi congelati e totali calcolati.
/// </summary>
public sealed class VersionePreventivo : EntitaBase
{
    public Guid CommessaId { get; set; }
    public int Numero { get; set; }
    public string Nota { get; set; } = "";

    /// <summary>Fotografia in formato JSON (vedi <c>Servizi.Fotografia</c>).</summary>
    public string Fotografia { get; set; } = "";

    public decimal RibassoPercentuale { get; set; }
    public decimal Ricavo { get; set; }
    public decimal Costo { get; set; }
    public decimal Mdc { get; set; }
    public decimal SpeseGenerali { get; set; }
    public decimal Risultato { get; set; }
}

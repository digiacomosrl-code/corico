namespace Corico.Core.Dominio;

/// <summary>
/// Base delle tabelle principali: identificativo indipendente dal database,
/// campo di versione per il controllo ottimistico e dati di creazione/modifica.
/// </summary>
public abstract class EntitaBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Incrementata a ogni salvataggio; usata per rilevare modifiche concorrenti.</summary>
    public int Versione { get; set; }

    public DateTime CreatoIl { get; set; }
    public string CreatoDa { get; set; } = "";
    public DateTime? ModificatoIl { get; set; }
    public string? ModificatoDa { get; set; }
}

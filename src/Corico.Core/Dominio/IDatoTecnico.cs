namespace Corico.Core.Dominio;

/// <summary>
/// Dato tecnico creato automaticamente durante la consultazione (es. record iniziale del preventivo, codici risorsa):
/// il suo inserimento è ammesso anche agli utenti di sola consultazione.
/// </summary>
public interface IDatoTecnico;

/// <summary>Elemento la cui creazione è riservata all'amministratore (es. pubblicazione di una versione ufficiale, sez. 4).</summary>
public interface IRiservatoAmministratore;

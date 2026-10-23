using Corico.Modules.Preventivo.Dominio;

namespace Corico.Modules.Preventivo.Interfaccia;

/// <summary>Etichette in italiano dei valori elencati.</summary>
public static class Testi
{
    public static string Affidamento(TipoAffidamento t) => t switch
    {
        TipoAffidamento.Fornitura => "Fornitura",
        TipoAffidamento.FornituraEPosa => "Fornitura e posa",
        TipoAffidamento.Subappalto => "Subappalto",
        TipoAffidamento.NoloACaldo => "Nolo a caldo",
        TipoAffidamento.LavoriInEconomia => "Lavori in economia",
        TipoAffidamento.EsecuzioneDiretta => "Esecuzione diretta",
        _ => t.ToString(),
    };

    public static string Offerta(TipoOfferta t) => t switch
    {
        TipoOfferta.StimaInterna => "Stima interna",
        TipoOfferta.SolaFornitura => "Sola fornitura",
        TipoOfferta.FornituraEPosa => "Fornitura e posa",
        TipoOfferta.SolaPosa => "Sola posa",
        TipoOfferta.Nolo => "Nolo",
        _ => t.ToString(),
    };

    public static string Stato(StatoCosto s) => s switch
    {
        StatoCosto.Previsione => "Previsione",
        StatoCosto.Provvisorio => "Provvisorio",
        StatoCosto.Definitivo => "Definitivo",
        _ => s.ToString(),
    };

    public static string StatoRisposta(Dominio.StatoRisposta s) => s switch
    {
        Dominio.StatoRisposta.DaInviare => "Da inviare",
        Dominio.StatoRisposta.Inviata => "Inviata",
        Dominio.StatoRisposta.Ricevuta => "Offerta ricevuta",
        Dominio.StatoRisposta.Declinata => "Declinata",
        _ => s.ToString(),
    };

    public static string FontePrezzo(Dominio.FontePrezzo f) => f switch
    {
        Dominio.FontePrezzo.OrdineEmesso => "Ordine emesso",
        Dominio.FontePrezzo.Analisi => "Analisi aggiornata",
        Dominio.FontePrezzo.Revisione => "Nuova revisione",
        Dominio.FontePrezzo.Variazione => "Variazione %",
        _ => f.ToString(),
    };

    public static string StatoOrdine(Dominio.StatoOrdine s) => s switch
    {
        Dominio.StatoOrdine.Bozza => "Bozza",
        Dominio.StatoOrdine.Emesso => "Emesso",
        Dominio.StatoOrdine.Annullato => "Annullato",
        _ => s.ToString(),
    };

    public static string StatoBudget(Dominio.StatoBudget s) => s switch
    {
        Dominio.StatoBudget.InApprovazione => "In approvazione",
        Dominio.StatoBudget.Approvato => "Approvato",
        Dominio.StatoBudget.Respinto => "Respinto",
        _ => s.ToString(),
    };

    public static string BaseSpesa(BaseSpesaGenerale b) => b switch
    {
        BaseSpesaGenerale.RicavoCommessa => "Ricavo di commessa",
        BaseSpesaGenerale.CostoCommessa => "Costo pianificato",
        BaseSpesaGenerale.ImportoLavoriLordo => "Importo lavori lordo",
        _ => b.ToString(),
    };

    public static string Esito(Servizi.EsitoConfronto e) => e switch
    {
        Servizi.EsitoConfronto.Aggiunto => "Aggiunto",
        Servizi.EsitoConfronto.Eliminato => "Eliminato",
        _ => "Modificato",
    };

    public static string Categoria(CategoriaRisorsa c) => c switch
    {
        CategoriaRisorsa.Materiali => "Materiali e forniture",
        CategoriaRisorsa.Manodopera => "Manodopera",
        CategoriaRisorsa.Attrezzature => "Attrezzature e mezzi",
        CategoriaRisorsa.Subappalti => "Subappalti e lavorazioni esterne",
        CategoriaRisorsa.TrasportiESmaltimenti => "Trasporti e smaltimenti",
        CategoriaRisorsa.AltriCosti => "Altri costi diretti",
        _ => c.ToString(),
    };

    public static string StatoAnalisi(StatoAnalisi s) => s switch
    {
        Dominio.StatoAnalisi.Bozza => "Bozza",
        Dominio.StatoAnalisi.Confermata => "Confermata",
        Dominio.StatoAnalisi.Archiviata => "Archiviata",
        _ => s.ToString(),
    };

    public static string OrigineSpesa(Servizi.OrigineRigaSpesa o) => o switch
    {
        Servizi.OrigineRigaSpesa.Risorsa => "Risorsa",
        Servizi.OrigineRigaSpesa.Sintetico => "Costo sintetico",
        Servizi.OrigineRigaSpesa.RigaOfferta => "Riga d'offerta",
        Servizi.OrigineRigaSpesa.ACorpo => "A corpo",
        _ => o.ToString(),
    };
}

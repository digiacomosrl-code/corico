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
}

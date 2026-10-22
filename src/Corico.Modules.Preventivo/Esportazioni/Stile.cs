namespace Corico.Modules.Preventivo.Esportazioni;

/// <summary>Colori comuni a tutte le stampe ed esportazioni: tabelle sobrie e sempre uguali.</summary>
public static class Stile
{
    public const string Primario = "#1F3A5F";
    public const string TestoSecondario = "#5B6573";
    public const string RigaAlterna = "#F2F4F7";
    public const string Totale = "#E3E8EF";
    public const string Bordo = "#C9D1DB";
    public const string Negativo = "#B42318";
}

/// <summary>Intestazione delle stampe: commessa e riferimento (bozza o versione ufficiale).</summary>
public sealed record IntestazioneStampa(string Commessa, string Sottotitolo);

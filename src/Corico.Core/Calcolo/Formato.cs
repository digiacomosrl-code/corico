using System.Globalization;

namespace Corico.Core.Calcolo;

/// <summary>Formattazione italiana di importi, percentuali e date per l'interfaccia e i report.</summary>
public static class Formato
{
    public static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");
    public const string NonDisponibile = "n.d.";

    public static string Euro(decimal? valore) =>
        valore is null ? NonDisponibile : Importi.Arrotonda(valore.Value).ToString("#,##0.00 €", Italiano);

    public static string Numero(decimal? valore, int decimali = 2) =>
        valore is null ? NonDisponibile : Importi.Arrotonda(valore.Value, decimali).ToString("#,##0." + new string('0', decimali), Italiano);

    public static string Percento(decimal? valore) =>
        valore is null ? NonDisponibile : Importi.Arrotonda(valore.Value).ToString("0.00", Italiano) + " %";

    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", Italiano) ?? "";

    public static string DataOra(DateTime? utc) =>
        utc is null ? "" : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm", Italiano);

    /// <summary>«Ultima modifica di Mario Rossi il 23/10/2026 alle 09:12» (sez. 4.1).</summary>
    public static string UltimaModifica(string? da, DateTime? ilUtc)
    {
        if (string.IsNullOrWhiteSpace(da) || ilUtc is null) return "";
        var locale = DateTime.SpecifyKind(ilUtc.Value, DateTimeKind.Utc).ToLocalTime();
        return $"Ultima modifica di {da} il {locale.ToString("dd/MM/yyyy", Italiano)} alle {locale:HH:mm}";
    }

    public static string UltimaModifica(Dominio.EntitaBase e) =>
        e.ModificatoIl is { } il ? UltimaModifica(e.ModificatoDa, il) : UltimaModifica(e.CreatoDa, e.CreatoIl == default ? null : e.CreatoIl);
}

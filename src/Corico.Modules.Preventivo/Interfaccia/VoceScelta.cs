using Corico.Core.Servizi;

namespace Corico.Modules.Preventivo.Interfaccia;

/// <summary>Una scelta di <see cref="SceltaConRicerca"/>.</summary>
public sealed record VoceScelta(string Valore, string Testo, string? Dettaglio = null)
{
    public static IReadOnlyList<VoceScelta> Da(IEnumerable<DatiCliente> clienti) =>
        clienti.Select(c => new VoceScelta(c.Id.ToString(), c.Denominazione, Unisci(c.PartitaIva, c.Comune))).ToList();

    public static IReadOnlyList<VoceScelta> Da(IEnumerable<DatiFornitore> fornitori) =>
        fornitori.Select(f => new VoceScelta(f.Id.ToString(), f.Denominazione, Unisci(f.PartitaIva, f.Comune))).ToList();

    public static IReadOnlyList<VoceScelta> Da(IEnumerable<DatiAttore> attori) =>
        attori.Select(a => new VoceScelta(a.Id.ToString(), a.NomeCompleto, Unisci(a.Ente, a.Email ?? a.Pec))).ToList();

    private static string? Unisci(string? primo, string? secondo) =>
        string.Join(" · ", new[] { primo, secondo }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } d ? d : null;
}

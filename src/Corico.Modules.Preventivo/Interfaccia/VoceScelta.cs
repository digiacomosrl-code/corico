using Corico.Core.Servizi;

namespace Corico.Modules.Preventivo.Interfaccia;

/// <summary>Una scelta di <see cref="SceltaConRicerca"/>.</summary>
public sealed record VoceScelta(string Valore, string Testo, string? Dettaglio = null)
{
    public static IReadOnlyList<VoceScelta> Da(IEnumerable<DatiCliente> clienti) =>
        clienti.Select(c => new VoceScelta(c.Id.ToString(), c.Denominazione, Unisci(c.PartitaIva, c.Comune))).ToList();

    public static IReadOnlyList<VoceScelta> Da(IEnumerable<DatiFornitore> fornitori) =>
        fornitori.Select(f => new VoceScelta(f.Id.ToString(), f.Denominazione, Unisci(f.PartitaIva, f.Comune))).ToList();

    private static string? Unisci(string? partitaIva, string? comune) =>
        string.Join(" · ", new[] { partitaIva, comune }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } d ? d : null;
}

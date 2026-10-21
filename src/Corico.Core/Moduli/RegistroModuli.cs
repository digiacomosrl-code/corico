namespace Corico.Core.Moduli;

/// <summary>Elenco dei moduli attivi, disponibile a interfaccia e servizi.</summary>
public sealed class RegistroModuli(IEnumerable<ICoricoModulo> moduli)
{
    public IReadOnlyList<ICoricoModulo> Attivi { get; } = moduli.ToList();

    public IEnumerable<VoceMenu> VociMenu => Attivi.SelectMany(m => m.VociMenu).OrderBy(v => v.Ordine);

    public IEnumerable<SchedaCommessa> SchedeCommessa => Attivi.SelectMany(m => m.SchedeCommessa).OrderBy(s => s.Ordine);
}

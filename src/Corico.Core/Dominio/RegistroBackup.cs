namespace Corico.Core.Dominio;

public enum TipoBackup
{
    Locale = 0,
    Esterno = 1,
    Ripristino = 2,
}

public sealed class RegistroBackup
{
    public long Id { get; set; }
    public DateTime DataOra { get; set; }
    public TipoBackup Tipo { get; set; }
    public string Motivo { get; set; } = "";
    public bool Riuscito { get; set; }
    public string? Percorso { get; set; }
    public long? Dimensione { get; set; }
    public string? Impronta { get; set; }
    public int? DocumentiCopiati { get; set; }
    public string? Messaggio { get; set; }
    public string Utente { get; set; } = "";
}

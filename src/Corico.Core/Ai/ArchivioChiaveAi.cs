using System.Security.Cryptography;
using System.Text;
using Corico.Core.Configurazione;
using Corico.Core.Dominio;

namespace Corico.Core.Ai;

/// <summary>Conservazione della chiave d'accesso al servizio AI, mai nel codice né nel database.</summary>
public interface IArchivioChiaveAi
{
    bool Presente { get; }
    string? Leggi();
    void Salva(string chiave);
    void Rimuovi();
}

/// <summary>
/// Chiave cifrata con la protezione dati di Windows (DPAPI, ambito macchina) nel file Config\ai-chiave.bin della cartella dati:
/// è leggibile solo sul PC sede, quindi un backup copiato altrove non la contiene in chiaro.
/// </summary>
public sealed class ArchivioChiaveDpapi(PercorsiDati percorsi) : IArchivioChiaveAi
{
    private static readonly byte[] Entropia = "Corico.Ai.Chiave"u8.ToArray();

    private string File => Path.Combine(percorsi.Config, "ai-chiave.bin");

    public bool Presente => System.IO.File.Exists(File);

    public string? Leggi()
    {
        if (!Presente || !OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), Entropia, DataProtectionScope.LocalMachine));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Salva(string chiave)
    {
        if (!OperatingSystem.IsWindows())
            throw new OperazioneNonValidaException("La chiave d'accesso si può salvare solo sul PC sede con Windows.");
        Directory.CreateDirectory(percorsi.Config);
        System.IO.File.WriteAllBytes(File, ProtectedData.Protect(Encoding.UTF8.GetBytes(chiave), Entropia, DataProtectionScope.LocalMachine));
    }

    public void Rimuovi()
    {
        if (Presente) System.IO.File.Delete(File);
    }
}

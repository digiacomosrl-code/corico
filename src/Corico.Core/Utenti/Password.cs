using System.Security.Cryptography;

namespace Corico.Core.Utenti;

/// <summary>Impronta delle password con PBKDF2-SHA512 (210.000 iterazioni, sale di 16 byte), confronto a tempo costante.</summary>
public static class Password
{
    private const int Iterazioni = 210_000;
    private const int LunghezzaSale = 16;
    private const int LunghezzaImpronta = 32;
    public const int LunghezzaMinima = 8;

    public static string Impronta(string password)
    {
        var sale = RandomNumberGenerator.GetBytes(LunghezzaSale);
        var impronta = Rfc2898DeriveBytes.Pbkdf2(password, sale, Iterazioni, HashAlgorithmName.SHA512, LunghezzaImpronta);
        return $"PBKDF2-SHA512${Iterazioni}${Convert.ToBase64String(sale)}${Convert.ToBase64String(impronta)}";
    }

    public static bool Verifica(string password, string memorizzata)
    {
        var parti = memorizzata.Split('$');
        if (parti.Length != 4 || parti[0] != "PBKDF2-SHA512" || !int.TryParse(parti[1], out var iterazioni)) return false;
        var sale = Convert.FromBase64String(parti[2]);
        var attesa = Convert.FromBase64String(parti[3]);
        var calcolata = Rfc2898DeriveBytes.Pbkdf2(password, sale, iterazioni, HashAlgorithmName.SHA512, attesa.Length);
        return CryptographicOperations.FixedTimeEquals(calcolata, attesa);
    }

    /// <summary>Password provvisoria leggibile (senza caratteri ambigui), da cambiare al primo accesso.</summary>
    public static string Provvisoria()
    {
        const string caratteri = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return string.Concat(Enumerable.Range(0, 10).Select(_ => caratteri[RandomNumberGenerator.GetInt32(caratteri.Length)]));
    }

    public static void ControllaRequisiti(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < LunghezzaMinima)
            throw new Dominio.OperazioneNonValidaException($"La password deve avere almeno {LunghezzaMinima} caratteri.");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new Dominio.OperazioneNonValidaException("La password deve contenere almeno una lettera e un numero.");
    }
}

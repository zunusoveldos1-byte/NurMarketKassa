using System.Security.Cryptography;
using System.Text;

namespace NurMarketKassa.Services;

/// <summary>
/// Windows DPAPI helper for encrypting short sensitive values (passwords, connection strings).
/// Output format is Base64 over ProtectedData payload.
/// </summary>
public static class WindowsDpapiHelper
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NurMarketKassa:UserSecrets:v1");

    public static string ProtectToBase64(string? plainText, DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, scope);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectFromBase64(string? protectedBase64, DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
            return string.Empty;

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, scope);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (FormatException)
        {
            return string.Empty;
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }
}

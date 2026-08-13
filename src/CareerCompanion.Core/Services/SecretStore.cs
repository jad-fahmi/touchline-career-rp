using System.Security.Cryptography;
using System.Text;

namespace CareerCompanion.Core.Services;

public static class SecretStore
{
    public static string Protect(string value){if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Secret storage requires Windows.");return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));}
    public static string Unprotect(string value){if(!OperatingSystem.IsWindows())return string.Empty;try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser));}catch{return string.Empty;}}
}

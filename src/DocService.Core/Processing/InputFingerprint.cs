using System.Security.Cryptography;
using System.Text;

namespace DocService.Core.Processing;

internal static class InputFingerprint
{
    private const int PrefixLength = 12;

    /// <summary>First 12 hex chars of the SHA-256 of the text; lets logs correlate inputs without storing them.</summary>
    public static string Of(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..PrefixLength];
}

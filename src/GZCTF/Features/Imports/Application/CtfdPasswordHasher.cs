using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Generators;

namespace GZCTF.Features.Imports.Application;

/// <summary>Verifies bounded Passlib bcrypt_sha256 imports; new hashes always use ASP.NET Identity.</summary>
public sealed class CtfdPasswordHasher(IOptions<PasswordHasherOptions>? options = null) : IPasswordHasher<UserInfo>
{
    public const string ImportPrefix = "ctfd:";
    private readonly PasswordHasher<UserInfo> native = new(options);
    // Passlib v1 supports 2a/2b; v2 requires 2b. Costs are bounded before invoking BCrypt.
    private static readonly Regex Format = new(
        @"\A\$bcrypt-sha256\$(?:(?<type>2[ab]),(?<cost>[0-9]{1,2})|v=2,t=(?<type>2b),r=(?<cost>[0-9]{1,2}))\$(?<salt>[./A-Za-z0-9]{22})\$(?<digest>[./A-Za-z0-9]{31})\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public static bool IsSupported(string? hash)
    {
        if (hash is null || hash.Length > 128)
            return false;
        var match = Format.Match(hash);
        return match.Success && int.TryParse(match.Groups["cost"].Value, out var cost) && cost is >= 4 and <= 16;
    }

    public string HashPassword(UserInfo user, string password) => native.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(UserInfo user, string hashedPassword, string providedPassword)
    {
        if (!hashedPassword.StartsWith(ImportPrefix, StringComparison.Ordinal))
        {
            try
            { return native.VerifyHashedPassword(user, hashedPassword, providedPassword); }
            catch (FormatException) { return PasswordVerificationResult.Failed; }
        }
        var imported = hashedPassword[ImportPrefix.Length..];
        if (!IsSupported(imported))
            return PasswordVerificationResult.Failed;
        var match = Format.Match(imported);
        var salt = match.Groups["salt"].Value;
        var passwordBytes = Encoding.UTF8.GetBytes(providedPassword);
        // Algorithm specified by Passlib 1.7.4 bcrypt_sha256 documentation:
        // v1 = Base64(SHA256(UTF8(password))); v2 = Base64(HMAC_SHA256(saltASCII, UTF8(password))).
        var digest = imported.StartsWith("$bcrypt-sha256$v=2,", StringComparison.Ordinal)
            ? HMACSHA256.HashData(Encoding.ASCII.GetBytes(salt), passwordBytes)
            : SHA256.HashData(passwordBytes);
        var prepared = Convert.ToBase64String(digest).ToCharArray();
        var bcrypt = "$" + match.Groups["type"].Value + "$" +
                     int.Parse(match.Groups["cost"].Value, CultureInfo.InvariantCulture).ToString("D2", CultureInfo.InvariantCulture) +
                     "$" + salt + match.Groups["digest"].Value;
        try
        {
            return OpenBsdBCrypt.CheckPassword(bcrypt, prepared)
                ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Failed;
        }
        catch (ArgumentException) { return PasswordVerificationResult.Failed; }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(digest);
            Array.Clear(prepared);
        }
    }
}

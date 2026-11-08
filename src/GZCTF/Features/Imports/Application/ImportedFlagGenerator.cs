using System.Security.Cryptography;
using System.Text;
namespace GZCTF.Features.Imports.Application;

internal static class ImportedFlagGenerator
{
    internal static string Generate(string mode, string? template, string source, string challenge, Guid user)
    {
        if (mode is not ("native" or "gzctf" or "gzctf-random" or "ctfd-whale"))
            throw new InvalidOperationException("Unsupported imported flag mode.");
        if (template?.Contains("{userId}", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("The {userId} Flag placeholder is no longer supported. Use [GUID] instead.");
        // Native, GZCTF and Whale share one template engine; empty templates use a random Flag.
        return new DynamicFlagGenerator(string.IsNullOrWhiteSpace(template) ? "flag{[GUID]}" : template)
            .GenerateWithTeamHash(() => Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{source}::{challenge}::{user:N}")))[12..24]);
    }
}

using System.Security.Cryptography;
using System.Text;
namespace GZCTF.Features.Imports.Application;

internal static class ImportedFlagGenerator
{
    internal static string Generate(string mode, string? template, string source, string challenge, Guid user)
    {
        if (mode == "gzctf-random")
            return $"flag{Guid.NewGuid():B}";
        if (mode != "gzctf")
            throw new InvalidOperationException("Unsupported imported flag mode.");
        return new DynamicFlagGenerator(template ?? "").GenerateWithTeamHash(() =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}::{challenge}::{user:N}")))[12..24]);
    }
}

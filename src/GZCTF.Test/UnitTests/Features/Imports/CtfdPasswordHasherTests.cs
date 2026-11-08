using GZCTF.Features.Imports.Application;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.Imports;

public sealed class CtfdPasswordHasherTests
{
    // Official Passlib 1.7.4 documentation vector.
    private const string V2 = "$bcrypt-sha256$v=2,t=2b,r=12$n79VH.0Q2TMWmt3Oqt9uku$Kq4Noyk3094Y2QlB8NdRT8SvGiI4ft2";

    [Fact]
    public void Legacy_success_requests_native_rehash_and_wrong_password_fails()
    {
        var hasher = new CtfdPasswordHasher();
        var user = new UserInfo();
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,
            hasher.VerifyHashedPassword(user, "ctfd:" + V2, "password"));
        Assert.Equal(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(user, "ctfd:" + V2, "wrong"));
    }

    [Theory]
    [InlineData("$bcrypt-sha256$2b,4$n79VH.0Q2TMWmt3Oqt9uku$218k5nCh3D4YqdpX.Zv79C4bjOSjWea", "password")]
    [InlineData("$bcrypt-sha256$v=2,t=2b,r=4$n79VH.0Q2TMWmt3Oqt9uku$sdU2s51xApBVBMVw4RZiJY.7FC1fevu", "password")]
    public void Passlib_174_generated_v1_and_v2_vectors_verify(string hash, string password)
    {
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,
            new CtfdPasswordHasher().VerifyHashedPassword(new UserInfo(), "ctfd:" + hash, password));
    }

    [Theory]
    [InlineData("$bcrypt-sha256$2b,4$n79VH.0Q2TMWmt3Oqt9uku$IbzifjiJR2O8cUFm8SdYb7fnoUWDv7C")]
    [InlineData("$bcrypt-sha256$v=2,t=2b,r=4$n79VH.0Q2TMWmt3Oqt9uku$b6oSvXVsoAFr3iAQHUxXmZOGuyQeVwm")]
    public void Long_unicode_password_is_not_truncated(string hash)
    {
        var password = string.Concat(System.Linq.Enumerable.Repeat("密碼🔐", 25));
        var hasher = new CtfdPasswordHasher();
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, hasher.VerifyHashedPassword(new UserInfo(), "ctfd:" + hash, password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(new UserInfo(), "ctfd:" + hash, password + "x"));
    }

    [Theory]
    [InlineData("$bcrypt-sha256$v=2,t=2b,r=31$n79VH.0Q2TMWmt3Oqt9uku$Kq4Noyk3094Y2QlB8NdRT8SvGiI4ft2")]
    [InlineData("$bcrypt-sha256$v=3,t=2b,r=12$n79VH.0Q2TMWmt3Oqt9uku$Kq4Noyk3094Y2QlB8NdRT8SvGiI4ft2")]
    [InlineData("$bcrypt-sha256$v=2,t=2a,r=12$n79VH.0Q2TMWmt3Oqt9uku$Kq4Noyk3094Y2QlB8NdRT8SvGiI4ft2")]
    [InlineData("$2b$12$n79VH.0Q2TMWmt3Oqt9ukuKq4Noyk3094Y2QlB8NdRT8SvGiI4ft2")]
    [InlineData("garbage")]
    public void Unsupported_or_unbounded_hashes_are_rejected_before_expensive_work(string hash)
    {
        Assert.False(CtfdPasswordHasher.IsSupported(hash));
        Assert.Equal(PasswordVerificationResult.Failed,
            new CtfdPasswordHasher().VerifyHashedPassword(new UserInfo(), "ctfd:" + hash, "password"));
    }

    [Fact]
    public void Native_passwords_and_replacements_use_native_identity_hashes()
    {
        var hasher = new CtfdPasswordHasher();
        var user = new UserInfo();
        var native = hasher.HashPassword(user, "replacement");
        Assert.DoesNotContain("ctfd:", native);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, native, "replacement"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, native, "password"));
    }
}

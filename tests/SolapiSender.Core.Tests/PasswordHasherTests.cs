using SolapiSender.Core.Security;

namespace SolapiSender.Core.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_and_verify()
    {
        var stored = PasswordHasher.Hash("Secret1234", iterations: 1000);
        Assert.StartsWith("pbkdf2-sha256$1000$", stored);
        Assert.True(PasswordHasher.Verify("Secret1234", stored));
        Assert.False(PasswordHasher.Verify("secret1234", stored));
        Assert.True(PasswordHasher.NeedsRehash(stored));
    }

    [Fact]
    public void Same_password_gets_different_salt()
    {
        Assert.NotEqual(PasswordHasher.Hash("Secret1234", 1000), PasswordHasher.Hash("Secret1234", 1000));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("pbkdf2-sha256$abc$x$y")]
    [InlineData("pbkdf2-sha256$1000$not-base64$!!")]
    public void Malformed_hash_never_verifies(string stored) => Assert.False(PasswordHasher.Verify("x", stored));

    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletters", false)]
    [InlineData("1234567890", false)]
    [InlineData("myadmin12345", false)]
    [InlineData("Shop2026secure", true)]
    public void Policy(string pw, bool ok) => Assert.Equal(ok, PasswordHasher.ValidatePolicy(pw, "root") is null);
}

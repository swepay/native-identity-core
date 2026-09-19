using Bogus;

namespace Native.IdentityCore.Tests.Fakers;

/// <summary>Shared fakers for identity-core domain values — no ad-hoc literals in tests (GS-07).</summary>
public static class IdentityCoreFakers
{
    private static readonly Faker Faker = new();

    public static string NewTenantId() => Faker.Random.Guid().ToString("N");

    public static string NewUserRef() => $"user-{Faker.Random.AlphaNumeric(12)}";

    public static string NewJti() => Faker.Random.Guid().ToString();

    public static byte[] NewImageBytes(int length = 32) => Faker.Random.Bytes(length);

    /// <summary>A fixed, valid absolute-URI issuer for tests — never a real Swepay domain (none is confirmed for this product yet).</summary>
    public static string TestIssuer => "https://biometrics.example.test";
}

namespace RideForgeApi.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that runs only when a real Postgres is configured.
/// <para>
/// xUnit 2 has no runtime skip, so the decision is made when the attribute is constructed: with
/// <c>RIDEFORGE_TEST_DB</c> unset the test is reported as <em>skipped</em> — never as passed, which
/// would hide the fact that the database rules went unchecked.
/// </para>
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    /// <summary>Npgsql key-value connection string to a disposable Postgres the tests may migrate.</summary>
    public const string EnvironmentVariable = "RIDEFORGE_TEST_DB";

    public PostgresFactAttribute()
    {
        if (ConnectionString is null)
        {
            Skip = $"Real-Postgres test: set {EnvironmentVariable} to a local Postgres connection string to run it.";
        }
    }

    /// <summary>The configured test database, or <c>null</c> when none is.</summary>
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}

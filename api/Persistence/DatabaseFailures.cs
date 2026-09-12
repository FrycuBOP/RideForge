using System.Net.Sockets;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace RideForgeApi.Persistence;

/// <summary>
/// What counts as a database fault, what may be said about it in a log, and what the caller is told.
/// One cohesive rule kept together because the three questions only make sense as a set: the
/// sanitizing in <see cref="LogDatabaseFailure"/> is the reason <see cref="DatabaseUnavailable"/> can
/// stay generic, and <c>SavedRoutesEndpointTests</c> asserts on that pairing.
/// </summary>
public static class DatabaseFailures
{
    /// <summary>
    /// Anything the database layer threw. Npgsql's non-retrying execution strategy wraps transient
    /// failures (a refused or dropped connection) in an <see cref="InvalidOperationException"/>, and
    /// EF wraps command failures in a <see cref="DbUpdateException"/>, so the whole chain is searched.
    /// </summary>
    public static bool IsDatabaseFailure(Exception ex) =>
        ExceptionChain(ex).Any(e => e is NpgsqlException or DbUpdateException);

    /// <summary>
    /// Logs a database failure without the exception object. Npgsql's messages carry the host and port
    /// ("Failed to connect to …"), and no connection detail may reach a log line. What is kept is enough
    /// to diagnose: the exception types, the socket error, and — for a server-side refusal — the
    /// SQLSTATE and the server's own message (a missing grant or RLS policy reads 42501 here).
    /// </summary>
    public static void LogDatabaseFailure(ILogger logger, Exception ex, string operation)
    {
        var chain = string.Join(" > ", ExceptionChain(ex).Select(e =>
            e is SocketException socket ? $"{nameof(SocketException)}({socket.SocketErrorCode})" : e.GetType().Name));
        var server = ExceptionChain(ex).OfType<PostgresException>().FirstOrDefault();
        var serverMessage = server is null ? "none"
            : MayLogServerMessage(server.SqlState) ? server.MessageText
            : "withheld";

        logger.LogWarning(
            "{Operation} failed in the database: {ExceptionChain}; SQLSTATE {SqlState}: {ServerMessage}",
            operation, chain, server?.SqlState ?? "none", serverMessage);
    }

    /// <summary>
    /// A generic 503: the body says nothing about where or what the database is. The <em>detail</em> is
    /// the caller's, because "could not be saved" is nonsense on a read — the status mapping is what all
    /// three endpoints share, not the copy.
    /// </summary>
    public static IResult DatabaseUnavailable(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IEnumerable<Exception> ExceptionChain(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            yield return e;
        }
    }

    /// <summary>
    /// Whether a server-side failure's own message may be logged. The allowed codes describe the
    /// <em>statement</em> — a violated constraint, a missing grant or policy, a table an unapplied
    /// migration has not created yet — and naming them is the whole point of logging at all. Every other
    /// code echoes the connection identity back instead: 28P01 reads
    /// <c>password authentication failed for user "rideforge_api.&lt;project-ref&gt;"</c> and 3D000 names
    /// the database, so a password rotated in Supabase but not on Railway would write the pooler
    /// username and the project ref into the deploy log on every save. For those, only the SQLSTATE is
    /// kept — enough to look the cause up, with nothing of the connection in it.
    /// </summary>
    private static bool MayLogServerMessage(string sqlState) => sqlState is
        PostgresErrorCodes.UniqueViolation or
        PostgresErrorCodes.CheckViolation or
        PostgresErrorCodes.NotNullViolation or
        PostgresErrorCodes.StringDataRightTruncation or
        PostgresErrorCodes.InsufficientPrivilege or
        PostgresErrorCodes.UndefinedTable or
        PostgresErrorCodes.UndefinedColumn;
}

using Microsoft.Extensions.Logging;

namespace CasePriority.Core.Services;

/// <summary>
/// Source-generated, structured log messages for <see cref="CaseService"/>.
/// Stable event IDs and named placeholders ({CaseNumber}, {Version}, ...) keep
/// each value an individually searchable field. Deliberately excludes the case
/// subject and any sensitive data.
/// </summary>
internal static partial class CaseServiceLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Created case {CaseNumber} with severity {Severity} at version {Version}.")]
    public static partial void CaseCreated(ILogger logger, string caseNumber, int severity, long version);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Closed case {CaseNumber} at version {Version}.")]
    public static partial void CaseClosed(ILogger logger, string caseNumber, long version);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Reopened case {CaseNumber} at version {Version}.")]
    public static partial void CaseReopened(ILogger logger, string caseNumber, long version);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Escalated case {CaseNumber} at version {Version}.")]
    public static partial void CaseEscalated(ILogger logger, string caseNumber, long version);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Debug,
        Message = "Case {CaseNumber} was already escalated; version remains {Version}.")]
    public static partial void EscalationNoOp(ILogger logger, string caseNumber, long version);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Changed case {CaseNumber} severity to {Severity} at version {Version}.")]
    public static partial void SeverityChanged(ILogger logger, string caseNumber, int severity, long version);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Debug,
        Message = "Case {CaseNumber} already had severity {Severity}; version remains {Version}.")]
    public static partial void SeverityNoOp(ILogger logger, string caseNumber, int severity, long version);
}

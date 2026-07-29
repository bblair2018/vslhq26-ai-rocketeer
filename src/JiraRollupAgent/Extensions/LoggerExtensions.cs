using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace JiraRollupAgent.Extensions
{
    /// <summary>
    /// Extension methods enable you to "add" methods to existing types without creating a new derived type,
    /// recompiling, or otherwise modifying the original type. Extension methods are static methods, but they're
    /// called as if they were instance methods on the extended type.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static class LoggerExtensions
    {
        /// <summary>
        /// Extension method to add context for method being called, file path and line number. This is strongly typed.
        /// </summary>
        /// <param name="logger">The logger to enrich.</param>
        /// <param name="memberName">Captured automatically via <see cref="CallerMemberNameAttribute"/> - do not pass explicitly.</param>
        /// <param name="sourceFilePath">Captured automatically via <see cref="CallerFilePathAttribute"/> - do not pass explicitly.</param>
        /// <param name="sourceLineNumber">Captured automatically via <see cref="CallerLineNumberAttribute"/> - do not pass explicitly.</param>
        /// <returns>A logger enriched with <c>MemberName</c>/<c>FilePath</c>/<c>LineNumber</c> properties for the call site.</returns>
        public static Serilog.ILogger Here(this Serilog.ILogger logger,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            return logger
                .ForContext("MemberName", memberName)
                .ForContext("FilePath", sourceFilePath)
                .ForContext("LineNumber", sourceLineNumber);
        }
    }
}

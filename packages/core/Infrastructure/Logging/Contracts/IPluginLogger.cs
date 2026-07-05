using System;

namespace MusicBeePlugin.Infrastructure.Logging.Contracts
{
    /// <summary>
    ///     Defines the contract for plugin logging functionality.
    /// </summary>
    public interface IPluginLogger
    {
        /// <summary>
        ///     Logs a debug message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Debug(string message);

        /// <summary>
        ///     Logs a debug message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        void Debug(string messageTemplate, params object[] args);

        /// <summary>
        ///     Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Info(string message);

        /// <summary>
        ///     Logs an informational message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        void Info(string messageTemplate, params object[] args);

        /// <summary>
        ///     Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Warn(string message);

        /// <summary>
        ///     Logs a warning message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        void Warn(string messageTemplate, params object[] args);

        /// <summary>
        ///     Logs an error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void LogError(string message);

        /// <summary>
        ///     Logs an error message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        void LogError(string messageTemplate, params object[] args);

        /// <summary>
        ///     Logs an error message with exception details.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        void LogError(Exception exception, string message = null);

        /// <summary>
        ///     Logs an error message with exception details and structured parameters.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        void LogError(Exception exception, string messageTemplate, params object[] args);

        /// <summary>
        ///     Logs a fatal error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Fatal(string message);

        /// <summary>
        ///     Logs a fatal error message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        void Fatal(string messageTemplate, params object[] args);

        /// <summary>
        ///     Logs a fatal error message with exception details.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        void Fatal(Exception exception, string message = null);

        /// <summary>
        ///     Logs a fatal error message with exception details and structured parameters.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        void Fatal(Exception exception, string messageTemplate, params object[] args);
    }
}

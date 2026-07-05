using System;
using System.Globalization;
using MusicBeePlugin.Infrastructure.Logging.Contracts;
using NLog;

namespace MusicBeePlugin.Infrastructure.Logging.Implementations
{
    /// <summary>
    ///     Plugin logger implementation that wraps NLog functionality.
    /// </summary>
    public class PluginLogger : IPluginLogger
    {
        private static readonly IFormatProvider FormatProvider = CultureInfo.InvariantCulture;
        private readonly Logger _logger;

        /// <summary>
        ///     Initializes a new instance of the PluginLogger class.
        /// </summary>
        /// <param name="name">The logger name (typically the class name).</param>
        public PluginLogger(string name)
        {
            _logger = LogManager.GetLogger(name);
        }

        /// <summary>
        ///     Initializes a new instance of the PluginLogger class.
        /// </summary>
        /// <param name="type">The type to use for the logger name.</param>
        public PluginLogger(Type type)
        {
            _logger = LogManager.GetLogger(type.FullName);
        }

        /// <summary>
        ///     Logs a debug message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Debug(string message)
        {
            _logger.Debug(message);
        }

        /// <summary>
        ///     Logs a debug message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        public void Debug(string messageTemplate, params object[] args)
        {
            _logger.Debug(FormatProvider, messageTemplate, args);
        }

        /// <summary>
        ///     Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Info(string message)
        {
            _logger.Info(message);
        }

        /// <summary>
        ///     Logs an informational message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        public void Info(string messageTemplate, params object[] args)
        {
            _logger.Info(FormatProvider, messageTemplate, args);
        }

        /// <summary>
        ///     Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Warn(string message)
        {
            _logger.Warn(message);
        }

        /// <summary>
        ///     Logs a warning message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        public void Warn(string messageTemplate, params object[] args)
        {
            _logger.Warn(FormatProvider, messageTemplate, args);
        }

        /// <summary>
        ///     Logs an error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogError(string message)
        {
            _logger.Error(message);
        }

        /// <summary>
        ///     Logs an error message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        public void LogError(string messageTemplate, params object[] args)
        {
            _logger.Error(FormatProvider, messageTemplate, args);
        }

        /// <summary>
        ///     Logs an error message with exception details.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        public void LogError(Exception exception, string message = null)
        {
            if (string.IsNullOrEmpty(message))
                _logger.Error(exception);
            else
                _logger.Error(exception, message);
        }

        /// <summary>
        ///     Logs an error message with exception details and structured parameters.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        public void LogError(Exception exception, string messageTemplate, params object[] args)
        {
            _logger.Error(exception, messageTemplate, args);
        }

        /// <summary>
        ///     Logs a fatal error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void Fatal(string message)
        {
            _logger.Fatal(message);
        }

        /// <summary>
        ///     Logs a fatal error message with structured parameters.
        /// </summary>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        public void Fatal(string messageTemplate, params object[] args)
        {
            _logger.Fatal(FormatProvider, messageTemplate, args);
        }

        /// <summary>
        ///     Logs a fatal error message with exception details.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        public void Fatal(Exception exception, string message = null)
        {
            if (string.IsNullOrEmpty(message))
                _logger.Fatal(exception);
            else
                _logger.Fatal(exception, message);
        }

        /// <summary>
        ///     Logs a fatal error message with exception details and structured parameters.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template with placeholders.</param>
        /// <param name="args">The values to substitute into the template.</param>
        public void Fatal(Exception exception, string messageTemplate, params object[] args)
        {
            _logger.Fatal(exception, messageTemplate, args);
        }
    }
}

using System;
using System.Collections.Generic;
using MusicBeePlugin.Infrastructure.Logging.Contracts;

namespace MusicBeeRemote.Core.Tests.Mocks
{
    public class MockLogger : IPluginLogger
    {
        public List<string> DebugMessages { get; } = new List<string>();
        public List<string> InfoMessages { get; } = new List<string>();
        public List<string> WarnMessages { get; } = new List<string>();
        public List<string> ErrorMessages { get; } = new List<string>();
        public List<string> FatalMessages { get; } = new List<string>();
        public List<Exception> LoggedExceptions { get; } = new List<Exception>();

        public void Debug(string message) => DebugMessages.Add(message);
        public void Debug(string messageTemplate, params object[] args)
            => DebugMessages.Add(FormatMessage(messageTemplate, args));

        public void Info(string message) => InfoMessages.Add(message);
        public void Info(string messageTemplate, params object[] args)
            => InfoMessages.Add(FormatMessage(messageTemplate, args));

        public void Warn(string message) => WarnMessages.Add(message);
        public void Warn(string messageTemplate, params object[] args)
            => WarnMessages.Add(FormatMessage(messageTemplate, args));

        public void LogError(string message) => ErrorMessages.Add(message);
        public void LogError(string messageTemplate, params object[] args)
            => ErrorMessages.Add(FormatMessage(messageTemplate, args));

        public void LogError(Exception exception, string message = null)
        {
            LoggedExceptions.Add(exception);
            ErrorMessages.Add(message ?? exception.Message);
        }

        public void LogError(Exception exception, string messageTemplate, params object[] args)
        {
            LoggedExceptions.Add(exception);
            ErrorMessages.Add(FormatMessage(messageTemplate, args));
        }

        public void Fatal(string message) => FatalMessages.Add(message);
        public void Fatal(string messageTemplate, params object[] args)
            => FatalMessages.Add(FormatMessage(messageTemplate, args));

        public void Fatal(Exception exception, string message = null)
        {
            LoggedExceptions.Add(exception);
            FatalMessages.Add(message ?? exception.Message);
        }

        public void Fatal(Exception exception, string messageTemplate, params object[] args)
        {
            LoggedExceptions.Add(exception);
            FatalMessages.Add(FormatMessage(messageTemplate, args));
        }

        private static string FormatMessage(string template, object[] args)
        {
            if (args == null || args.Length == 0)
                return template;

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // NLog uses {name} style, not {0} style - just return template with args appended
                return $"{template} [{string.Join(", ", args)}]";
            }
        }

        public void Clear()
        {
            DebugMessages.Clear();
            InfoMessages.Clear();
            WarnMessages.Clear();
            ErrorMessages.Clear();
            FatalMessages.Clear();
            LoggedExceptions.Clear();
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;

namespace MusicBeePlugin.AndroidRemote.Error
{
    internal static class ErrorHandler
    {
        private static string _logFilePath;

        /// <summary>
        ///     Given an Exception it logs the time and the exception message to the log file stored in the _logFilePath
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        public static void LogError(Exception ex)
        {
            try
            {
                if (string.IsNullOrEmpty(_logFilePath))
                    return;
                if (!Directory.Exists(_logFilePath + "mb_remote"))
                    Directory.CreateDirectory(_logFilePath + "mb_remote");
                Stream stream = new FileStream(_logFilePath + "mb_remote\\error.log", FileMode.Append);
                using (var fWriter = new StreamWriter(stream))
                {
                    fWriter.WriteLine(DateTime.Now + "\n");
                    fWriter.WriteLine();
                    fWriter.Write(ex.ToString());
                    fWriter.WriteLine(Environment.NewLine);
                }
                stream.Close();
            }
            catch (IOException iException)
            {
                Debug.WriteLine(iException);
            }
        }

        public static void LogValue(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(_logFilePath))
                    return;
                if (!Directory.Exists(_logFilePath + "mb_remote"))
                    Directory.CreateDirectory(_logFilePath + "mb_remote");
                Stream stream = new FileStream(_logFilePath + "mb_remote\\error.log", FileMode.Append);
                using (var fWriter = new StreamWriter(stream))
                {
                    fWriter.WriteLine(DateTime.Now + "\n");
                    fWriter.WriteLine();
                    fWriter.Write(value);
                    fWriter.WriteLine(Environment.NewLine);
                }
                stream.Close();
            }
            catch (IOException iException)
            {
                Debug.WriteLine(iException);
            }
        }

        public static void VerboseValue(string value)
        {
            // All the method code should only run when debugging
#if DEBUG
            Debug.WriteLine($"{DateTime.UtcNow}-DEBUG: {value}");
#endif
        }

        /// <summary>
        ///     Sets the path where the Errors will be logged
        /// </summary>
        /// <param name="path">Path to store the log file.</param>
        /// <returns></returns>
        public static void SetLogFilePath(string path)
        {
            _logFilePath = path;
        }
    }
}
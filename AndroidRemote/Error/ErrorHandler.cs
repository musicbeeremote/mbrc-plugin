using System;
using System.Diagnostics;
using System.IO;

namespace MusicBeePlugin.AndroidRemote.Error
{
    internal static class ErrorHandler
    {
        private static string logFilePath;

        /// <summary>
        /// Given an Exception it logs the time and the exception message to the log file stored in the _logFilePath
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        public static void LogError(Exception ex)
        {
            try
            {
                if (String.IsNullOrEmpty(logFilePath))
                    return;
                if (!Directory.Exists(logFilePath + "mb_remote"))
                    Directory.CreateDirectory(logFilePath + "mb_remote");
                Stream stream = new FileStream(logFilePath + "mb_remote\\error.log", FileMode.Append);
                using (StreamWriter fWriter = new StreamWriter(stream))
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

        public static void LogValue(String value)
        {
            try
            {
                if (String.IsNullOrEmpty(logFilePath))
                    return;
                if (!Directory.Exists(logFilePath + "mb_remote"))
                    Directory.CreateDirectory(logFilePath + "mb_remote");
                Stream stream = new FileStream(logFilePath + "mb_remote\\error.log", FileMode.Append);
                using (StreamWriter fWriter = new StreamWriter(stream))
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

        /// <summary>
        /// Sets the path where the Errors will be logged
        /// </summary>
        /// <param name="path">Path to store the log file.</param>
        /// <returns></returns>
        public static void SetLogFilePath(String path)
        {
            logFilePath = path;
        }
    }
}
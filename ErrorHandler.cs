using System;
using System.IO;

namespace MusicBeePlugin
{
    class ErrorHandler
    {
        private static string _logFilePath;
        public static void LogError(Exception ex)
        {
            try
            {
                if (String.IsNullOrEmpty(_logFilePath))
                    return;
                Stream stream = new FileStream(_logFilePath + "mb_remote.log",FileMode.Append);
                using (StreamWriter fWriter = new StreamWriter(stream))
                {
                    fWriter.WriteLine(DateTime.Now + "\n");
                    fWriter.WriteLine();
                    fWriter.Write(ex.ToString());
                    fWriter.WriteLine(Environment.NewLine);
                }
            }
            catch (Exception iEx)
            {
                //
            }
        }
        public static void SetLogFilePath(String path)
        {
            _logFilePath = path;
        }
    }
}

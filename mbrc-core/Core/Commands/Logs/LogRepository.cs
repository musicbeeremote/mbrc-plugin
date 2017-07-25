using System.Collections.Generic;
using System.Linq;
using LiteDB;
using MusicBeeRemote.Core.Settings;

namespace MusicBeeRemote.Core.Commands.Logs
{
    public class LogRepository
    {
        private const string TableName = "execution_logs";
        private readonly IStorageLocationProvider _storageLocationProvider;

        public LogRepository(IStorageLocationProvider storageLocationProvider)
        {
            _storageLocationProvider = storageLocationProvider;
        }

        public void InsertLog(ExecutionLog log)
        {
            using (var db = new LiteDatabase(_storageLocationProvider.DatabaseFile))
            {
                var collection = db.GetCollection<ExecutionLog>(TableName);
                collection.Insert(log);
            }
        }

        public List<ExecutionLog> GetLogs()
        {
            IEnumerable<ExecutionLog> logs;
            using (var db = new LiteDatabase(_storageLocationProvider.DatabaseFile))
            {
                logs = db.GetCollection<ExecutionLog>(TableName).FindAll();
            }
            return logs.ToList();
        }
    }
}
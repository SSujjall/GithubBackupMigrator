namespace GithubBackupMigrator.Server.Services.Helpers
{
    public class LogHelper
    {
        private readonly string logFile;
        public LogHelper()
        {
            string baseDir = AppContext.BaseDirectory;
            logFile = Path.Combine(baseDir, "Backups", "repo-backup-log.txt");
        }

        public void Log(string message)
        {
            try
            {
                // Console output
                Console.WriteLine(message);

                // File logging
                string logDir = Path.GetDirectoryName(logFile);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                File.AppendAllText(logFile, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC: {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }
}

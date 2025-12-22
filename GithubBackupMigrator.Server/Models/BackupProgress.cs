namespace GithubBackupMigrator.Server.Models
{
    public class BackupProgress
    {
        public string Repo { get; set; } = string.Empty;
        public int Current { get; set; }
        public int Total { get; set; }
        public string Status { get; set; } = string.Empty; // Cloning, Skipped, Failed, Done
    }
}

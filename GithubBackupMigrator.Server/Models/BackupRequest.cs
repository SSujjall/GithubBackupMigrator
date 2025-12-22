namespace GithubBackupMigrator.Server.Models
{
    public class BackupRequest
    {
        public string SourceGithubUser { get; set; } = string.Empty;
        public string SourceGithubToken { get; set; } = string.Empty;
        public string TargetGithubUser { get; set; } = string.Empty;
        public string TargetGithubToken { get; set; } = string.Empty;
    }
}

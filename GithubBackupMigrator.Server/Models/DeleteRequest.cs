namespace GithubBackupMigrator.Server.Models
{
    public class DeleteRequest
    {
        public string GithubUser { get; set; } = string.Empty;
        public string GithubToken { get; set; } = string.Empty;
    }
}

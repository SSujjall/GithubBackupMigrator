using GithubBackupMigrator.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GithubBackupMigrator.Server.Services.Helpers
{
    public class SignalRHelper
    {
        private readonly IHubContext<SignalRHub> _hub;

        public SignalRHelper(IHubContext<SignalRHub> hub)
        {
            _hub = hub;
        }

        public async Task SendProgress(string jobId, string repo, int current, int total, string status, string message = "")
        {
            await _hub.Clients.Group(jobId).SendAsync("BackupProgress", new
            {
                Repo = repo,
                Current = current,
                Total = total,
                Status = status,
                Message = message,
                Timestamp = DateTime.UtcNow
            });
        }

        public async Task SendStatusUpdate(string jobId, string status, string message)
        {
            await _hub.Clients.Group(jobId).SendAsync("BackupStatus", new
            {
                Status = status,
                Message = message,
                Timestamp = DateTime.UtcNow
            });
        }

        public async Task SendSummaryUpdate(string jobId, int current, int total, int success, int failed, int skipped)
        {
            await _hub.Clients.Group(jobId).SendAsync("BackupSummary", new
            {
                Current = current,
                Total = total,
                Success = success,
                Failed = failed,
                Skipped = skipped,
                Percentage = total > 0 ? (int)((double)current / total * 100) : 0,
                Timestamp = DateTime.UtcNow
            });
        }

        public async Task SendFinishUpdate(string jobId = "", int total = 0, int success = 0, int failed = 0, int skipped = 0, bool isError = false, Exception ex = null)
        {
            if (isError)
            {
                await _hub.Clients.Group(jobId)
                    .SendAsync("BackupError", new
                    {
                        message = "Backup process failed",
                        error = ex.Message
                    });
            }
            else
            {
                await _hub.Clients.Group(jobId)
                   .SendAsync("BackupFinished", new
                   {
                       total,
                       success,
                       failed,
                       skipped,
                       message = "Backup process completed successfully"
                   });
            }
        }
    }
}
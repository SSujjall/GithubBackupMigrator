using Microsoft.AspNetCore.SignalR;

namespace GithubBackupMigrator.Server.Hubs
{
    public class SignalRHub: Hub
    {
        public async Task JoinJob(string jobId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
            await Clients.Caller.SendAsync("JoinedJob", jobId);
        }
    }
}

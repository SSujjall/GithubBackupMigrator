using GithubBackupMigrator.Server.Models;
using GithubBackupMigrator.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GithubBackupMigrator.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MainController(
        IBackupService _backupService
    ) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> StartBackup([FromBody] BackupRequest reqModel)
        {
            //string jobId = Guid.NewGuid().ToString();
            string jobId = "12345";


            _ = Task.Run(() => _backupService.StartBackupService(jobId, reqModel));

            return Ok(ApiResponse<object>.SuccessResponse(new { jobId = jobId }, "Backup started. Connect to SignalR hub with this jobId to track progress."));
        }
    }
}

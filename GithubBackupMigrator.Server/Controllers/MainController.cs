using GithubBackupMigrator.Server.Models;
using GithubBackupMigrator.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GithubBackupMigrator.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MainController : ControllerBase
    {
        private readonly IBackupService _backupServiceV1;
        private readonly IBackupService _backupServiceV2;

        public MainController(
            [FromKeyedServices("v1")] IBackupService backupServiceV1,
            [FromKeyedServices("v2")] IBackupService backupServiceV2
        )
        {
            _backupServiceV1 = backupServiceV1;
            _backupServiceV2 = backupServiceV2;
        }

        [HttpPost("v1")]
        public async Task<IActionResult> StartBackupV1([FromBody] BackupRequest reqModel)
        {
            string jobId = Guid.NewGuid().ToString();
            //string jobId = "12345";


            _ = Task.Run(() => _backupServiceV1.StartBackupService(jobId, reqModel));

            return Ok(ApiResponse<object>.SuccessResponse(new { jobId = jobId }, "Backup started. Connect to SignalR hub with this jobId to track progress."));
        }

        [HttpPost("v2")]
        public async Task<IActionResult> StartBackupV2([FromBody] BackupRequest reqModel)
        {
            string jobId = Guid.NewGuid().ToString();
            //string jobId = "12345";


            _ = Task.Run(() => _backupServiceV2.StartBackupService(jobId, reqModel));

            return Ok(ApiResponse<object>.SuccessResponse(new { jobId = jobId }, "Backup started. Connect to SignalR hub with this jobId to track progress."));
        }
    }
}

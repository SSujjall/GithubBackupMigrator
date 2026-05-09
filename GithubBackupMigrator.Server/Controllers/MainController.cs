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
            string jobId = Guid.NewGuid().ToString();
            //string jobId = "12345";


            _ = Task.Run(() => _backupService.StartBackupService(jobId, reqModel));

            return Ok(ApiResponse<object>.SuccessResponse(new { jobId = jobId }, "Backup started. Connect to SignalR hub with this jobId to track progress."));
        }

        [HttpDelete("delete-all")]
        public async Task<IActionResult> DeleteAllRepos([FromBody] DeleteRequest reqModel)
        {
            if (string.IsNullOrEmpty(reqModel.GithubToken))
                return BadRequest(ApiResponse<object>.FailureResponse("A GitHub token is required to delete repositories."));

            var (deleted, failed, failedRepos) = await _backupService.DeleteAllRepos(reqModel);

            var data = new { deleted, failed, failedRepos };

            if (failed > 0 && deleted == 0)
                return StatusCode(500, ApiResponse<object>.FailureResponse($"Failed to delete all {failed} repositories."));

            string message = failed > 0
                ? $"Deleted {deleted} repositories. {failed} failed."
                : $"Successfully deleted all {deleted} repositories.";

            return Ok(ApiResponse<object>.SuccessResponse(data, message));
        }
    }
}

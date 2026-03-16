using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for managing background agent jobs
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IUnitOfWork unitOfWork,
        ILogger<JobsController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Get jobs for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AgentJob>>> GetJobs(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] JobStatus? status = null)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            IEnumerable<AgentJob> jobs;
            if (status.HasValue)
            {
                var allJobs = await _unitOfWork.AgentJobs.GetByStatusAsync(status.Value);
                jobs = allJobs.Where(j => j.UserId == userId).Skip(skip).Take(take);
            }
            else
            {
                jobs = await _unitOfWork.AgentJobs.GetByUserIdAsync(userId, skip, take);
            }

            return Ok(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving jobs");
            return this.CreateProblemDetails("Failed to retrieve jobs", 500);
        }
    }

    /// <summary>
    /// Get a specific job by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AgentJob>> GetJob(string id)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var job = await _unitOfWork.AgentJobs.GetByIdAsync(id);
            if (job == null || job.UserId != userId)
            {
                return NotFound();
            }

            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job {JobId}", id);
            return this.CreateProblemDetails("Failed to retrieve job", 500);
        }
    }

    /// <summary>
    /// Get recent jobs for the current user
    /// </summary>
    [HttpGet("recent")]
    public async Task<ActionResult<IEnumerable<AgentJob>>> GetRecentJobs([FromQuery] int count = 10)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var jobs = await _unitOfWork.AgentJobs.GetRecentJobsAsync(userId, count);
            return Ok(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent jobs");
            return this.CreateProblemDetails("Failed to retrieve recent jobs", 500);
        }
    }

    /// <summary>
    /// Get job statistics for the current user
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<JobStatsResponse>> GetJobStats()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var stats = await _unitOfWork.AgentJobs.GetJobStatsAsync(userId);
            var response = new JobStatsResponse
            {
                Total = stats.Total,
                Completed = stats.Completed,
                Failed = stats.Failed,
                Running = stats.Running,
                Queued = stats.Total - stats.Completed - stats.Failed - stats.Running
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job statistics");
            return this.CreateProblemDetails("Failed to retrieve job statistics", 500);
        }
    }

    /// <summary>
    /// Create a new background job
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AgentJob>> CreateJob([FromBody] CreateJobRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var job = new AgentJob
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Question = request.Question,
                ConnectionId = request.ConnectionId,
                Status = JobStatus.Queued,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.AgentJobs.AddAsync(job);
            await _unitOfWork.SaveChangesAsync();

            // TODO: Enqueue job with Hangfire
            _logger.LogInformation("Created job {JobId} for user {UserId}", job.Id, userId);

            return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating job");
            return this.CreateProblemDetails("Failed to create job", 500);
        }
    }

    /// <summary>
    /// Cancel a running job
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelJob(string id)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var job = await _unitOfWork.AgentJobs.GetByIdAsync(id);
            if (job == null || job.UserId != userId)
            {
                return NotFound();
            }

            if (job.Status != JobStatus.Queued && job.Status != JobStatus.Running)
            {
                return BadRequest("Job cannot be cancelled in its current state");
            }

            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await _unitOfWork.AgentJobs.UpdateAsync(job);
            await _unitOfWork.SaveChangesAsync();

            // TODO: Cancel Hangfire job if exists
            _logger.LogInformation("Cancelled job {JobId}", job.Id);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling job {JobId}", id);
            return this.CreateProblemDetails("Failed to cancel job", 500);
        }
    }
}

/// <summary>
/// Request model for creating a job
/// </summary>
public class CreateJobRequest
{
    public string Question { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
}

/// <summary>
/// Response model for job statistics
/// </summary>
public class JobStatsResponse
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Running { get; set; }
    public int Queued { get; set; }
}
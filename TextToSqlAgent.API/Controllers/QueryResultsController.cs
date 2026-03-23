using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for paginated query results (lazy loading)
/// </summary>
[ApiController]
[Route("api/query-results")]
[Authorize]
public class QueryResultsController : ControllerBase
{
    private readonly IQueryResultCache _resultCache;
    private readonly ILogger<QueryResultsController> _logger;

    public QueryResultsController(
        IQueryResultCache resultCache,
        ILogger<QueryResultsController> logger)
    {
        _resultCache = resultCache;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated query results
    /// </summary>
    /// <param name="resultId">Cached result ID</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of rows per page (default: 50, max: 200)</param>
    [HttpGet("{resultId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPage(
        string resultId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            // Validate parameters
            if (page < 1)
            {
                return BadRequest(new { error = "Page must be >= 1" });
            }

            if (pageSize < 1 || pageSize > 200)
            {
                return BadRequest(new { error = "PageSize must be between 1 and 200" });
            }

            _logger.LogInformation(
                "[QueryResults] Fetching page {Page} (size: {PageSize}) for result {ResultId}",
                page, pageSize, resultId);

            var paginatedResult = await _resultCache.GetPageAsync(
                resultId,
                page,
                pageSize,
                HttpContext.RequestAborted);

            if (paginatedResult == null)
            {
                _logger.LogWarning("[QueryResults] Result {ResultId} not found in cache", resultId);
                return NotFound(new
                {
                    error = "RESULT_NOT_FOUND",
                    message = "Query result not found or expired. Please re-run the query.",
                    resultId
                });
            }

            _logger.LogInformation(
                "[QueryResults] Returning page {Page}/{TotalPages} with {RowCount} rows",
                paginatedResult.CurrentPage,
                paginatedResult.TotalPages,
                paginatedResult.Rows.Count);

            return Ok(new
            {
                success = true,
                data = paginatedResult
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResults] Error fetching page for result {ResultId}", resultId);
            return StatusCode(500, new { error = "Failed to fetch query results" });
        }
    }

    /// <summary>
    /// Get full query result (for export)
    /// </summary>
    /// <param name="resultId">Cached result ID</param>
    [HttpGet("{resultId}/full")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFullResult(string resultId)
    {
        try
        {
            _logger.LogInformation("[QueryResults] Fetching full result for {ResultId}", resultId);

            var fullResult = await _resultCache.GetFullResultAsync(
                resultId,
                HttpContext.RequestAborted);

            if (fullResult == null)
            {
                return NotFound(new
                {
                    error = "RESULT_NOT_FOUND",
                    message = "Query result not found or expired",
                    resultId
                });
            }

            return Ok(new
            {
                success = true,
                data = fullResult
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResults] Error fetching full result {ResultId}", resultId);
            return StatusCode(500, new { error = "Failed to fetch full query result" });
        }
    }

    /// <summary>
    /// Delete cached query result
    /// </summary>
    /// <param name="resultId">Cached result ID</param>
    [HttpDelete("{resultId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteResult(string resultId)
    {
        try
        {
            await _resultCache.DeleteAsync(resultId, HttpContext.RequestAborted);
            _logger.LogInformation("[QueryResults] Deleted cached result {ResultId}", resultId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResults] Error deleting result {ResultId}", resultId);
            return StatusCode(500, new { error = "Failed to delete query result" });
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var stats = await _resultCache.GetStatisticsAsync(HttpContext.RequestAborted);
            return Ok(new
            {
                success = true,
                data = stats
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResults] Error fetching statistics");
            return StatusCode(500, new { error = "Failed to fetch cache statistics" });
        }
    }
}

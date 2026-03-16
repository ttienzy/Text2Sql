using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for managing messages within conversations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(
        IUnitOfWork unitOfWork,
        ILogger<MessagesController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Get messages for a conversation
    /// </summary>
    [HttpGet("conversation/{conversationId}")]
    public async Task<ActionResult<IEnumerable<Message>>> GetMessages(
        string conversationId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Verify user owns the conversation
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(conversationId, userId);
            if (conversation == null)
            {
                return NotFound("Conversation not found");
            }

            var messages = await _unitOfWork.Messages.GetByConversationIdAsync(conversationId, skip: offset, take: limit);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving messages for conversation {ConversationId}", conversationId);
            return this.CreateProblemDetails("Failed to retrieve messages", 500);
        }
    }

    /// <summary>
    /// Get a specific message by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Message>> GetMessage(string id)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var message = await _unitOfWork.Messages.GetByIdAsync(id);
            if (message == null)
            {
                return NotFound();
            }

            // Verify user owns the conversation
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(message.ConversationId, userId);
            if (conversation == null)
            {
                return NotFound();
            }

            return Ok(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving message {MessageId}", id);
            return this.CreateProblemDetails("Failed to retrieve message", 500);
        }
    }

    /// <summary>
    /// Create a new message in a conversation
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Message>> CreateMessage([FromBody] CreateMessageRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Verify user owns the conversation
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(request.ConversationId, userId);
            if (conversation == null)
            {
                return NotFound("Conversation not found");
            }

            var message = new Message
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = request.ConversationId,
                Role = request.Role,
                Content = request.Content,
                SqlQuery = request.SqlQuery,
                Results = request.Results,
                ErrorMessage = request.ErrorMessage,
                Explanation = request.Explanation,
                Model = request.Model,
                InputTokens = request.InputTokens,
                OutputTokens = request.OutputTokens,
                TotalTokens = request.TotalTokens,
                Cost = request.Cost,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(message);

            // Update conversation last active time
            conversation.LastActiveAt = DateTime.UtcNow;
            await _unitOfWork.Conversations.UpdateAsync(conversation);

            await _unitOfWork.SaveChangesAsync();

            return CreatedAtAction(nameof(GetMessage), new { id = message.Id }, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating message");
            return this.CreateProblemDetails("Failed to create message", 500);
        }
    }

    /// <summary>
    /// Update a message
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Message>> UpdateMessage(string id, [FromBody] UpdateMessageRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var message = await _unitOfWork.Messages.GetByIdAsync(id);
            if (message == null)
            {
                return NotFound();
            }

            // Verify user owns the conversation
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(message.ConversationId, userId);
            if (conversation == null)
            {
                return NotFound();
            }

            message.Content = request.Content;
            message.SqlQuery = request.SqlQuery;
            message.Results = request.Results;
            message.ErrorMessage = request.ErrorMessage;
            message.Explanation = request.Explanation;

            await _unitOfWork.Messages.UpdateAsync(message);
            await _unitOfWork.SaveChangesAsync();

            return Ok(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating message {MessageId}", id);
            return this.CreateProblemDetails("Failed to update message", 500);
        }
    }

    /// <summary>
    /// Get recent SQL queries for the current user
    /// </summary>
    [HttpGet("recent-queries")]
    public async Task<ActionResult<IEnumerable<object>>> GetRecentQueries([FromQuery] int limit = 5)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Get recent messages with SQL queries for this user
            var recentMessages = await _unitOfWork.Messages.GetRecentQueriesAsync(userId, limit);

            var result = recentMessages.Select(m => new
            {
                id = m.Id,
                sqlQuery = m.SqlQuery,
                content = m.Content?.Length > 50 ? m.Content.Substring(0, 50) + "..." : m.Content,
                createdAt = m.CreatedAt,
                conversationId = m.ConversationId
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent queries for user {UserId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            return this.CreateProblemDetails("Failed to retrieve recent queries", 500);
        }
    }

    /// <summary>
    /// Delete a message
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMessage(string id)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var message = await _unitOfWork.Messages.GetByIdAsync(id);
            if (message == null)
            {
                return NotFound();
            }

            // Verify user owns the conversation
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(message.ConversationId, userId);
            if (conversation == null)
            {
                return NotFound();
            }

            await _unitOfWork.Messages.DeleteAsync(message);
            await _unitOfWork.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message {MessageId}", id);
            return this.CreateProblemDetails("Failed to delete message", 500);
        }
    }
}

/// <summary>
/// Request model for creating a message
/// </summary>
public class CreateMessageRequest
{
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? SqlQuery { get; set; }
    public string? Results { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Explanation { get; set; }
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? Cost { get; set; }
}

/// <summary>
/// Request model for updating a message
/// </summary>
public class UpdateMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public string? SqlQuery { get; set; }
    public string? Results { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Explanation { get; set; }
}
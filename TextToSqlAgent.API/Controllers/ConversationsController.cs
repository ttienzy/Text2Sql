using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for managing conversations/chat sessions
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(
        IUnitOfWork unitOfWork,
        ILogger<ConversationsController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Get all conversations for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Conversation>>> GetConversations(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] bool includeArchived = false)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var conversations = await _unitOfWork.Conversations.GetByUserIdAsync(userId);

            // Apply filters on the client side
            var filteredConversations = conversations.AsEnumerable();

            if (!includeArchived)
            {
                filteredConversations = filteredConversations.Where(c => !c.IsArchived);
            }

            // Apply pagination
            filteredConversations = filteredConversations.Skip(skip).Take(take);

            return Ok(filteredConversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversations");
            return this.CreateProblemDetails("Failed to retrieve conversations", 500);
        }
    }

    /// <summary>
    /// Get a specific conversation by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Conversation>> GetConversation(string id)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return NotFound();
            }

            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversation {ConversationId}", id);
            return this.CreateProblemDetails("Failed to retrieve conversation", 500);
        }
    }

    /// <summary>
    /// Create a new conversation
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Conversation>> CreateConversation([FromBody] CreateConversationRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var conversation = new Conversation
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ConnectionId = request.ConnectionId,
                Title = request.Title,
                ContextJson = request.ContextJson,
                CreatedAt = DateTime.UtcNow,
                LastActiveAt = DateTime.UtcNow,
                IsArchived = false
            };

            await _unitOfWork.Conversations.AddAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            return CreatedAtAction(nameof(GetConversation), new { id = conversation.Id }, conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversation");
            return this.CreateProblemDetails("Failed to create conversation", 500);
        }
    }

    /// <summary>
    /// Update a conversation
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Conversation>> UpdateConversation(string id, [FromBody] UpdateConversationRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return NotFound();
            }

            conversation.Title = request.Title;
            conversation.ContextJson = request.ContextJson;
            conversation.LastActiveAt = DateTime.UtcNow;

            await _unitOfWork.Conversations.UpdateAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversation {ConversationId}", id);
            return this.CreateProblemDetails("Failed to update conversation", 500);
        }
    }

    /// <summary>
    /// Archive a conversation
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> ArchiveConversation(string id)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return NotFound();
            }

            conversation.IsArchived = true;
            await _unitOfWork.Conversations.UpdateAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving conversation {ConversationId}", id);
            return this.CreateProblemDetails("Failed to archive conversation", 500);
        }
    }

    /// <summary>
    /// Delete a conversation
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteConversation(string id)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return NotFound();
            }

            await _unitOfWork.Conversations.DeleteAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversation {ConversationId}", id);
            return this.CreateProblemDetails("Failed to delete conversation", 500);
        }
    }

    /// <summary>
    /// Get messages for a specific conversation (nested endpoint)
    /// </summary>
    [HttpGet("{id}/messages")]
    public async Task<ActionResult<IEnumerable<Message>>> GetConversationMessages(
        string id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Verify user owns the conversation
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return NotFound("Conversation not found");
            }

            var messages = await _unitOfWork.Messages.GetByConversationIdAsync(id, skip, take);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving messages for conversation {ConversationId}", id);
            return this.CreateProblemDetails("Failed to retrieve messages", 500);
        }
    }

    /// <summary>
    /// Send a message to a specific conversation (nested endpoint)
    /// </summary>
    [HttpPost("{id}/messages")]
    public async Task<ActionResult<Message>> SendMessageToConversation(
        string id,
        [FromBody] SendMessageRequest messageRequest)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Verify user owns the conversation
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return NotFound("Conversation not found");
            }

            // TODO: Implement message creation logic
            // This should integrate with the agent to process the question and generate SQL
            var message = new Message
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = id,
                Role = "user",
                Content = messageRequest.Question ?? "No question provided",
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(message);
            await _unitOfWork.SaveChangesAsync();

            return CreatedAtAction(nameof(GetConversationMessages), new { id }, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to conversation {ConversationId}", id);
            return this.CreateProblemDetails("Failed to send message", 500);
        }
    }
}

/// <summary>
/// Request model for creating a conversation
/// </summary>
public class CreateConversationRequest
{
    public string Title { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
}

/// <summary>
/// Request model for updating a conversation
/// </summary>
public class UpdateConversationRequest
{
    public string Title { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
}

/// <summary>
/// Request model for sending a message to a conversation
/// </summary>
public class SendMessageRequest
{
    public string Question { get; set; } = string.Empty;
}
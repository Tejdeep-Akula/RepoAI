using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using RepoAI.Api.Contracts;
using RepoAI.Application.Chat;
using RepoAI.Domain.Entities;

namespace RepoAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/conversations")]
[Tags("Chat conversations")]
public sealed class ChatConversationsController(
    ChatConversationService conversationService,
    ILogger<ChatConversationsController> logger) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [EndpointSummary("Create a saved chat for one repository snapshot")]
    [ProducesResponseType<ChatConversationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatConversationResponse>> Create(
        Guid projectId,
        CreateChatConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        if (request.SnapshotId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.SnapshotId), "A repository snapshot is required.");
            return ValidationProblem(ModelState);
        }

        if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title))
        {
            ModelState.AddModelError(nameof(request.Title), "The title cannot be blank.");
            return ValidationProblem(ModelState);
        }

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? "Repository questions"
            : request.Title.Trim();
        var conversation = await conversationService.CreateAsync(
            ownerId,
            projectId,
            request.SnapshotId,
            title,
            cancellationToken);

        if (conversation is null)
        {
            return NotFound();
        }

        var response = Map(conversation);
        return CreatedAtAction(
            nameof(Get),
            new { projectId, conversationId = conversation.Id },
            response);
    }

    [HttpGet]
    [EndpointSummary("List saved chats for the signed-in user's project")]
    [ProducesResponseType<IReadOnlyList<ChatConversationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChatConversationResponse>>> List(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var conversations = await conversationService.ListAsync(ownerId, projectId, cancellationToken);
        return Ok(conversations.Select(Map).ToArray());
    }

    [HttpGet("{conversationId:guid}")]
    [EndpointSummary("Reopen a saved chat and load its message history")]
    [ProducesResponseType<ChatConversationDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatConversationDetailResponse>> Get(
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            return Unauthorized();
        }

        var conversation = await conversationService.FindAsync(
            ownerId,
            projectId,
            conversationId,
            cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        var messages = await conversationService.ListMessagesAsync(
            ownerId,
            projectId,
            conversationId,
            cancellationToken);
        return Ok(new ChatConversationDetailResponse(
            Map(conversation),
            messages.Select(Map).ToArray()));
    }

    [HttpPost("{conversationId:guid}/messages/stream")]
    [Consumes("application/json")]
    [Produces("text/event-stream")]
    [EndpointSummary("Ask a repository question and stream the grounded answer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StreamMessage(
        Guid projectId,
        Guid conversationId,
        CreateChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var ownerId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 8_000)
        {
            ModelState.AddModelError(nameof(request.Content), "Enter a question of at most 8,000 characters.");
            return ValidationProblem(ModelState);
        }

        if (await conversationService.FindAsync(ownerId, projectId, conversationId, cancellationToken) is null)
        {
            return NotFound();
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await foreach (var item in conversationService.StreamAnswerAsync(
                ownerId, projectId, conversationId, request.Content, cancellationToken))
            {
                var json = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await Response.WriteAsync($"event: {item.Type}\ndata: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser disconnected or canceled its request; stop generation promptly.
        }
        catch (Exception exception)
        {
            var traceId = HttpContext.TraceIdentifier;
            logger.LogError(exception,
                "Chat streaming failed for conversation {ConversationId}, project {ProjectId}, trace {TraceId}",
                conversationId, projectId, traceId);
            var error = JsonSerializer.Serialize(
                new ChatStreamEvent("error", Code: "chat_generation_failed",
                    Error: $"The answer could not be generated. Try again. (Reference: {traceId})"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await Response.WriteAsync($"event: error\ndata: {error}\n\n");
            await Response.Body.FlushAsync();
        }

        return new EmptyResult();
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var subject = User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }

    private static ChatConversationResponse Map(ChatConversation conversation) => new(
        conversation.Id,
        conversation.ProjectId,
        conversation.SnapshotId,
        conversation.Title,
        conversation.CreatedAtUtc,
        conversation.UpdatedAtUtc);

    private static ChatMessageResponse Map(ChatMessage message)
    {
        IReadOnlyList<ChatCitationResponse> citations = [];
        if (!string.IsNullOrWhiteSpace(message.CitationsJson))
        {
            try
            {
                citations = JsonSerializer.Deserialize<List<ChatCitationResponse>>(
                    message.CitationsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            }
            catch (JsonException)
            {
                // Old or malformed stored citations should not prevent reopening the conversation.
            }
        }

        return new ChatMessageResponse(
            message.Id, message.Role, message.Content, message.Model, citations, message.CreatedAtUtc);
    }
}

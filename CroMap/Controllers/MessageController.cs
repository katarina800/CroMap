using CroMap.Models;
using CroMap.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CroMap.Controllers
{
  

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MessageController : ControllerBase
    {
        private readonly IMessageRepository _messageRepository;
        // Greške idu u zapisnik; klijent dobiva samo da nije uspjelo.
        private readonly ILogger<MessageController> _logger;

        public MessageController(IMessageRepository messageRepository, ILogger<MessageController> logger)
        {
            _messageRepository = messageRepository;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
                           ?? User.FindFirst("sub");

            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                throw new UnauthorizedAccessException("User not authenticated");

            return userId;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            

            if (request == null || string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { message = "Message content cannot be empty" });

            if (request.ReceiverId <= 0)
                return BadRequest(new { message = "Invalid receiver" });

            try
            {
                var senderId = GetCurrentUserId();
                var message = new Message
                {
                    SenderId = senderId,
                    ReceiverId = request.ReceiverId,
                    Content = request.Content,
                    IsRead = false,
                    SentAt = DateTime.UtcNow
                };

                await _messageRepository.SendMessageAsync(message);

                return Ok(new
                {
                    message = "Message sent successfully",
                    messageId = message.Id
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SendMessage error: {ex.Message}");
                // "detail" je nosio poruku iznimke ravno klijentu.
                _logger.LogError(ex, "Slanje poruke nije uspjelo.");
                return StatusCode(500, new { message = "Failed to send message" });
            }
        }

        // Popis razgovora s kojima korisnik STVARNO ima razmijenjene poruke.
        // Vraća zadnju poruku, vrijeme i broj nepročitanih u jednom upitu, pa
        // aplikacija više ne mora sama slagati popis iz pratitelja/praćenih.
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations()
        {
            var userId = GetCurrentUserId();
            var conversations = await _messageRepository.GetConversationsAsync(userId);
            return Ok(conversations);
        }

        [HttpGet("conversation/{userId}")]
        public async Task<IActionResult> GetConversation(int userId)
        {
            var currentUserId = GetCurrentUserId();
            var messages = await _messageRepository.GetConversationAsync(currentUserId, userId);
            return Ok(messages);
        }

        [HttpGet("my-messages")]
        public async Task<IActionResult> GetMyMessages()
        {
            var userId = GetCurrentUserId();
            var messages = await _messageRepository.GetUserMessagesAsync(userId);
            return Ok(messages);
        }

        [HttpGet("unread")]
        public async Task<IActionResult> GetUnreadMessages()
        {
            var userId = GetCurrentUserId();
            var messages = await _messageRepository.GetUnreadMessagesAsync(userId);
            return Ok(messages);
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetCurrentUserId();
            var count = await _messageRepository.GetUnreadCountAsync(userId);
            return Ok(new { unreadCount = count });
        }

        [HttpPut("read/{messageId}")]
        public async Task<IActionResult> MarkAsRead(int messageId)
        {
            var success = await _messageRepository.MarkAsReadAsync(messageId);

            if (!success)
                return NotFound(new { message = "Message not found" });

            return Ok(new { message = "Message marked as read" });
        }

        [HttpDelete("{messageId}")]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var userId = GetCurrentUserId();
            var success = await _messageRepository.DeleteMessageAsync(messageId, userId);

            if (!success)
                return NotFound(new { message = "Message not found or you don't have permission to delete it" });

            return Ok(new { message = "Message deleted successfully" });
        }
    }
}
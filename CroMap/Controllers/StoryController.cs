// Controllers/StoryController.cs - prošireni kontroler
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using CroMap.ModelsDto;
using CroMap.Repositories;
using CroMap.Data;
using Dapper;

namespace CroMap.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StoryController : ControllerBase
    {
        private readonly IStoryRepository _storyRepository;
        private readonly DatabaseConnection _dbConnection;
        // Greške idu u zapisnik; klijent dobiva samo da nije uspjelo.
        private readonly ILogger<StoryController> _logger;

        public StoryController(IStoryRepository storyRepository, DatabaseConnection dbConnection, ILogger<StoryController> logger)
        {
            _storyRepository = storyRepository;
            _dbConnection = dbConnection;
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

        [HttpPost("upload")]
        public async Task<IActionResult> UploadStory([FromBody] UploadStoryRequest request)
        {
            if (string.IsNullOrEmpty(request.MediaUrl) || string.IsNullOrEmpty(request.MediaType))
                return BadRequest(new { message = "MediaUrl and MediaType are required." });

            try
            {
                var userId = GetCurrentUserId();
                var storyId = await _storyRepository.CreateStoryAsync(userId, request.MediaUrl, request.MediaType);
                return Ok(new { message = "Story uploaded", storyId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Objava priče nije uspjela.");
                return StatusCode(500, new { message = "Could not save the story." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStories()
        {
            var userId = GetCurrentUserId();
            var stories = await _storyRepository.GetStoriesAsync(userId);
            return Ok(stories);
        }

        [HttpDelete("{storyId}")]
        public async Task<IActionResult> DeleteStory(int storyId)
        {
            var userId = GetCurrentUserId();
            var deleted = await _storyRepository.DeleteStoryAsync(storyId, userId);
            if (!deleted)
                return NotFound(new { message = "Story not found or not owned by user." });
            return Ok(new { message = "Story deleted" });
        }

        [HttpPost("mark-viewed/{storyId}")]
        public async Task<IActionResult> MarkStoryAsViewed(int storyId)
        {
            var userId = GetCurrentUserId();
            await _storyRepository.MarkAsViewedAsync(storyId, userId);
            return Ok(new { message = "Story marked as viewed" });
        }

        [HttpGet("viewers/{storyId}")]
        public async Task<IActionResult> GetStoryViewers(int storyId)
        {
            var viewers = await _storyRepository.GetStoryViewersAsync(storyId);
            Console.WriteLine($"Viewers for story {storyId}: {viewers.Count()}");
            return Ok(viewers);
        }

        [HttpPost("like/{storyId}")]
        public async Task<IActionResult> LikeStory(int storyId, [FromBody] LikeStoryRequest request)
        {
            var userId = GetCurrentUserId();
            await _storyRepository.LikeStoryAsync(storyId, userId, request?.ReactionType ?? "like");
            return Ok(new { message = "Story liked" });
        }

        [HttpDelete("like/{storyId}")]
        public async Task<IActionResult> UnlikeStory(int storyId)
        {
            var userId = GetCurrentUserId();
            await _storyRepository.UnlikeStoryAsync(storyId, userId);
            return Ok(new { message = "Story unliked" });
        }

        [HttpGet("likes/{storyId}")]
        public async Task<IActionResult> GetStoryLikes(int storyId)
        {
            var likes = await _storyRepository.GetStoryLikesAsync(storyId);
            return Ok(likes);
        }

        [HttpPost("comment/{storyId}")]
        public async Task<IActionResult> AddComment(int storyId, [FromBody] AddCommentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return BadRequest(new { message = "Comment text is required" });

            var userId = GetCurrentUserId();
            var comment = await _storyRepository.AddCommentAsync(storyId, userId, request.Text);
            return Ok(comment);
        }

        [HttpDelete("comment/{commentId}")]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var userId = GetCurrentUserId();
            var deleted = await _storyRepository.DeleteCommentAsync(commentId, userId);
            if (!deleted)
                return NotFound(new { message = "Comment not found or not owned by user." });
            return Ok(new { message = "Comment deleted" });
        }

        [HttpPost("comment/react/{commentId}")]
        public async Task<IActionResult> ReactToComment(int commentId, [FromBody] ReactToCommentRequest request)
        {
            var userId = GetCurrentUserId();
            await _storyRepository.ReactToCommentAsync(commentId, userId, request.ReactionType);
            return Ok(new { message = "Reaction added" });
        }

        [HttpGet("comments/{storyId}")]
        public async Task<IActionResult> GetComments(int storyId)
        {
            var comments = await _storyRepository.GetStoryCommentsAsync(storyId);
            return Ok(comments);
        }

        [HttpGet("user/{userId}/has-unviewed")]
        public async Task<IActionResult> HasUnviewedStories(int userId)
        {
            var currentUserId = GetCurrentUserId();
            var hasUnviewed = await _storyRepository.HasUnviewedStoriesAsync(userId, currentUserId);
            return Ok(new { hasUnviewedStory = hasUnviewed });
        }

        [HttpPost("test-add-viewer")]
        public async Task<IActionResult> TestAddViewer()
        {
            var userId = GetCurrentUserId();
            var storyId = 7; // Testni story

            var sql = @"
        INSERT INTO story_views (story_id, user_id, viewed_at)
        VALUES (@StoryId, @UserId, @ViewedAt)
        ON CONFLICT (story_id, user_id) DO NOTHING;
    ";

            using var connection = _dbConnection.CreateConnection();
            var rows = await connection.ExecuteAsync(sql, new
            {
                StoryId = storyId,
                UserId = userId,
                ViewedAt = DateTime.UtcNow
            });

            return Ok(new { rowsAffected = rows, storyId, userId });
        }

        [HttpGet("has-story/{userId}")]
        public async Task<IActionResult> HasActiveStory(int userId)
        {
            var hasStory = await _storyRepository.HasActiveStoryAsync(userId);
            return Ok(new { hasActiveStory = hasStory });
        }
    }

    public class LikeStoryRequest
    {
        public string? ReactionType { get; set; }
    }

    public class AddCommentRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class ReactToCommentRequest
    {
        public string ReactionType { get; set; } = string.Empty;
    }
}
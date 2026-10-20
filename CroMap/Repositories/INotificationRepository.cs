using CroMap.Models;

namespace CroMap.Repositories
{
    public interface INotificationRepository
    {
        Task<IEnumerable<NotificationDto>> GetForUserAsync(int userId, int limit, int offset);
        Task<int> GetUnreadCountAsync(int userId);
        Task<bool> MarkReadAsync(int notificationId, int userId);
        Task<int> MarkAllReadAsync(int userId);
        Task<bool> DeleteAsync(int notificationId, int userId);

        Task<NotificationPreferencesDto> GetPreferencesAsync(int userId);
        Task SavePreferencesAsync(int userId, NotificationPreferencesDto prefs);

        /// <summary>
        /// Napravi obavijest svima koje objava zanima: onima koji su uključili
        /// obavijesti i prate barem jednu od njezinih kategorija, uz uvjet da im
        /// je objava blizu (ili da prate autora, ili primaju sadržaj odasvud).
        /// </summary>
        /// <param name="latitude">Položaj objave; null kad ga nema.</param>
        /// <returns>Id-evi korisnika kojima je obavijest napravljena (za push).</returns>
        Task<IReadOnlyList<int>> FanOutNewActivityAsync(
            int actorUserId,
            int videoId,
            string title,
            string body,
            IEnumerable<string> categories,
            double? latitude = null,
            double? longitude = null);

        /// <summary>Isti krug ljudi, za one koji obavijest žele i e-poštom.</summary>
        Task<IEnumerable<NotificationEmailRecipient>> GetEmailRecipientsAsync(
            int actorUserId,
            IEnumerable<string> categories,
            double? latitude = null,
            double? longitude = null);
    }
}

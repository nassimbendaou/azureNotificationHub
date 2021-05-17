using BackPNS.Model;
using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackPNS.Service
{
    public class NotificationHubService : INotificationService
    {
        readonly NotificationHubClient _hub;
        readonly Dictionary<string, NotificationPlatform> _installationPlatform;
        readonly ILogger<NotificationHubService> _logger;

        public NotificationHubService(IOptions<NotificationHubOptions> options, ILogger<NotificationHubService> logger)
        {
            _logger = logger;
            _hub = NotificationHubClient.CreateClientFromConnectionString(
                options.Value.ConnectionString,
                options.Value.Name);

            _installationPlatform = new Dictionary<string, NotificationPlatform>
            {
                { nameof(NotificationPlatform.Apns).ToLower(), NotificationPlatform.Apns },
                { nameof(NotificationPlatform.Fcm).ToLower(), NotificationPlatform.Fcm }
            };
        }

        public async Task<bool> CreateOrUpdateInstallationAsync(DeviceInstallation deviceInstallation, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(deviceInstallation?.InstallationId) ||
                string.IsNullOrWhiteSpace(deviceInstallation?.Platform) ||
                string.IsNullOrWhiteSpace(deviceInstallation?.PushChannel))
                return false;

            var installation = new Installation()
            {
                InstallationId = deviceInstallation.InstallationId,
                PushChannel = deviceInstallation.PushChannel,
                Tags = deviceInstallation.Tags
            };

            if (_installationPlatform.TryGetValue(deviceInstallation.Platform, out var platform))
                installation.Platform = platform;
            else
                return false;

            try
            {
                await _hub.CreateOrUpdateInstallationAsync(installation);
            }
            catch
            {
                return false;
            }

            return true;
        }

        public Task<bool> CreateOrUpdateInstallationAsync(DeviceInstallation deviceInstallation)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> DeleteInstallationByIdAsync(string installationId)
        {
            if (string.IsNullOrWhiteSpace(installationId))
                return false;

            try
            {
                await _hub.DeleteInstallationAsync(installationId);
            }
            catch
            {
                return false;
            }

            return true;
        }


        public async Task<bool> RequestNotificationAsync(NotificationRequest notificationRequest)
        {
            if ((notificationRequest.Silent &&
                string.IsNullOrWhiteSpace(notificationRequest?.Action)) ||
                (!notificationRequest.Silent &&
                (string.IsNullOrWhiteSpace(notificationRequest?.Text)) ||
                string.IsNullOrWhiteSpace(notificationRequest?.Action)))
                return false;

            var androidPushTemplate = notificationRequest.Silent ?
                PushTemplates.Silent.Android :
                PushTemplates.Generic.Android;

            var iOSPushTemplate = notificationRequest.Silent ?
                PushTemplates.Silent.iOS :
                PushTemplates.Generic.iOS;

            var androidPayload = PrepareNotificationPayload(
                androidPushTemplate,
                notificationRequest.Text,
                notificationRequest.Action);

            var iOSPayload = PrepareNotificationPayload(
                iOSPushTemplate,
                notificationRequest.Text,
                notificationRequest.Action);

            try
            {
                if (notificationRequest.Tags.Length == 0)
                {
                    // This will broadcast to all users registered in the notification hub
                    await SendPlatformNotificationsAsync(androidPayload, iOSPayload);
                }
                else if (notificationRequest.Tags.Length <= 20)
                {
                    await SendPlatformNotificationsAsync(androidPayload, iOSPayload, notificationRequest.Tags);
                }
                else
                {
                    var notificationTasks = notificationRequest.Tags
                        .Select((value, index) => (value, index))
                        .GroupBy(g => g.index / 20, i => i.value)
                        .Select(tags => SendPlatformNotificationsAsync(androidPayload, iOSPayload, tags));

                    await Task.WhenAll(notificationTasks);
                }

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unexpected error sending notification");
                return false;
            }
        }

        string PrepareNotificationPayload(string template, string text, string action) => template
            .Replace("$(alertMessage)", text, StringComparison.InvariantCulture)
            .Replace("$(alertAction)", action, StringComparison.InvariantCulture);

        Task SendPlatformNotificationsAsync(string androidPayload, string iOSPayload)
        {
            var sendTasks = new Task[]
            {
                _hub.SendFcmNativeNotificationAsync(androidPayload),
                _hub.SendAppleNativeNotificationAsync(iOSPayload)
            };

            return Task.WhenAll(sendTasks);
        }

        Task SendPlatformNotificationsAsync(string androidPayload, string iOSPayload, IEnumerable<string> tags)
        {
            var sendTasks = new Task[]
            {
                _hub.SendFcmNativeNotificationAsync(androidPayload, tags),
                _hub.SendAppleNativeNotificationAsync(iOSPayload, tags)
            };

            return Task.WhenAll(sendTasks);
        }
    }
}

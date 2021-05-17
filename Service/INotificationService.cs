using BackPNS.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackPNS.Service
{
    public interface INotificationService
    {
        Task<bool> CreateOrUpdateInstallationAsync(DeviceInstallation deviceInstallation);
        Task<bool> DeleteInstallationByIdAsync(string installationId);
        Task<bool> RequestNotificationAsync(NotificationRequest notificationRequest);
    }
}

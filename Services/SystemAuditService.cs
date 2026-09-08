using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace agilicomsptoolkit.Services
{
    public interface ISystemAuditService
    {
        Task<List<HardwareItem>> GetHardwareDiagnosticsAsync();
    }

    public sealed class SystemAuditService : ISystemAuditService
    {
        public Task<List<HardwareItem>> GetHardwareDiagnosticsAsync()
        {
            return HardwareChecker.RunDiagnosticsAsync();
        }
    }
}

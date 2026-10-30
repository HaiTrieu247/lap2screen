using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>Whether Windows' part of Wi-Fi control (the four firewall rules and the Scheduled Task) is in place.</summary>
    internal static class WindowsSetupState
    {
        /// <summary>
        /// The Scheduled Task starts tunneld: <see cref="CoreDeviceRuntime.UsesTask"/> (installed copy in Program Files,
        /// administrator account). Otherwise every Wi-Fi session asks UAC.
        /// </summary>
        public static bool UseTask => CoreDeviceRuntime.UsesTask;

        public static async Task<bool> IsDoneAsync()
        {
            // a copy that never gets the task and rules (portable, or outside Program Files) has nothing to set up here:
            // its first Wi-Fi session adds the rules itself
            if (!CoreDeviceRuntime.TaskAllowed) return true;
            var result = await PowerShellRunner.RunAsync(CoreDeviceSetupScripts.Query());
            if (!result.Succeeded) return false;
            return CoreDeviceSetupScripts.ParseQuery(result.Output).IsComplete(UseTask);
        }
    }
}

using System.Windows.Forms;

namespace TidyUp
{
    /// <summary>
    /// Activation hook. Everything is unlocked for now.
    /// To sell the app later: set RequireActivation to true and implement the
    /// key check below (e.g. a signed key stored in %ProgramData%\TidyUp\license.key).
    /// </summary>
    static class License
    {
        static readonly bool RequireActivation = false;

        public static bool IsActivated()
        {
            if (!RequireActivation) return true;

            // TODO: read the saved key and verify its signature here.
            return false;
        }

        public static void ShowActivationRequired(IWin32Window owner)
        {
            MessageBox.Show(owner,
                "Please activate TidyUp to clean your disk.",
                "TidyUp", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

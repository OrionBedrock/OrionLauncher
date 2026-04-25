namespace OrionBE.Launcher.Services;

public static class InstanceFolderNameHelper
{
    public static string ToFolderName(string displayName)
    {
        var filtered = string.Concat(
            displayName.Trim().Where(static c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrEmpty(filtered))
        {
            filtered = "instance" + Guid.NewGuid().ToString("N")[..8];
        }

        return filtered;
    }
}

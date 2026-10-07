using BitFab.KW1281Test.Interface;

namespace BitFab.KW1281Test.Tests;

[TestClass]
public class LinuxSerialPermissionsTests
{
    [TestMethod]
    public void PermissionHint_UsesDeviceGroupAndExplainsLoginAndEnvironment()
    {
        foreach (string group in new[] { "dialout", "uucp" })
        {
            string text = string.Join('\n', LinuxSerialPermissions.Describe("/dev/ttyUSB0", group));
            Assert.IsTrue(text.Contains($"sudo usermod -aG '{group}' \"$USER\""));
            Assert.IsTrue(text.Contains("Log out and log back in"));
            Assert.IsTrue(text.Contains("without sudo"));
            Assert.IsTrue(text.Contains("KW1281TEST_PROFILE"));
            Assert.IsFalse(text.Contains("setfacl"));
        }
    }

    [TestMethod]
    public void PermissionHint_UsesDeviceAclWhenGroupIsUnknownOrRootAndQuotesPaths()
    {
        foreach (string? group in new[] { null, "root", "123", "unsafe; command" })
        {
            string text = string.Join('\n', LinuxSerialPermissions.Describe("/dev/serial/by-id/cable's port", group));
            Assert.IsTrue(text.Contains("sudo setfacl -m \"u:$USER:rw\" -- '/dev/serial/by-id/cable'\\''s port'"));
            Assert.IsFalse(text.Contains("usermod"));
            Assert.IsTrue(text.Contains("reconnecting the cable"));
        }
    }

    [TestMethod]
    public void LinuxOpen_ReportsNativePermissionDenialInsteadOfGenericIoFailure()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("Linux native open is required."); return; }
        string file = Path.GetTempFileName();
        var mode = File.GetUnixFileMode(file);
        try
        {
            File.SetUnixFileMode(file, UnixFileMode.None);
            try { using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite); Assert.Inconclusive("The current user can bypass file permissions."); }
            catch (UnauthorizedAccessException) { }
            var error = Assert.ThrowsExactly<UnauthorizedAccessException>(() => new LinuxInterface(file, 9600));
            Assert.IsTrue(error.Message.Contains("Access denied"));
            Assert.IsInstanceOfType<System.ComponentModel.Win32Exception>(error.InnerException);
            Assert.IsTrue(string.Join('\n', LinuxSerialPermissions.ForDevice(file)).Contains("sudo setfacl"));
        }
        finally
        {
            File.SetUnixFileMode(file, mode);
            File.Delete(file);
        }
    }
}

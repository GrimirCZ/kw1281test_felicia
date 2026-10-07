using BitFab.KW1281Test.Interface;
using System.Globalization;
using System.IO.Ports;
using System.Text.Json;

namespace BitFab.KW1281Test.Tests;

[TestClass]
public class SerialByteDumpTests
{
    [TestMethod]
    public void SessionInfo_PreservesArgumentBoundariesAndProfileSourceWithoutCreatingDataRecords()
    {
        string source = "C:\\profiles\\my \"car\"\n.json";
        string[] commandLine = ["kw1281test", "ecu", "Sensors", "engine", "--profile", source];
        string[] command = ["Sensors", "engine"];
        using var output = new StringWriter();
        using (var dump = new SerialByteDump(output))
            dump.WriteSessionInfo(commandLine, command, "/dev/serial/by-id/cable with spaces", 9600, 0x25, "custom-profile", source);
        string text = output.ToString();
        using var original = JsonDocument.Parse(Metadata(text, "Command line"));
        CollectionAssert.AreEqual(commandLine, original.RootElement.EnumerateArray().Select(x => x.GetString()).ToArray());
        using var selected = JsonDocument.Parse(Metadata(text, "Command"));
        CollectionAssert.AreEqual(command, selected.RootElement.EnumerateArray().Select(x => x.GetString()).ToArray());
        using var profileSource = JsonDocument.Parse(Metadata(text, "Profile source"));
        Assert.AreEqual(source, profileSource.RootElement.GetString());
        using var profile = JsonDocument.Parse(Metadata(text, "Profile"));
        Assert.AreEqual("custom-profile", profile.RootElement.GetString());
        using var port = JsonDocument.Parse(Metadata(text, "Port"));
        Assert.AreEqual("/dev/serial/by-id/cable with spaces", port.RootElement.GetString());
        Assert.AreEqual("9600", Metadata(text, "Initial baud rate"));
        Assert.AreEqual("0x25", Metadata(text, "Initial controller address"));
        Assert.AreEqual(0, Records(text).Length);

        using var generic = new StringWriter();
        using (var dump = new SerialByteDump(generic))
            dump.WriteSessionInfo(["kw1281test", "01", "ReadIdent"], ["ReadIdent"], "/dev/ttyUSB0", 9600, 1, null, null);
        Assert.AreEqual("none", Metadata(generic.ToString(), "Profile"));
        Assert.AreEqual("none", Metadata(generic.ToString(), "Profile source"));
    }

    [TestMethod]
    public void Options_OverrideEnvironmentAndPreserveProfileAndCommandArguments()
    {
        var selection = ByteDumpOptions.Extract(
            ["--dump", "capture with spaces.kwdump", "ecu", "Sensors", "engine", "--once", "--profile", "felicia-simos2p"], "environment.kwdump");
        Assert.AreEqual("capture with spaces.kwdump", selection.Path);
        var profile = VehicleProfile.ExtractOption(selection.Arguments, null);
        CollectionAssert.AreEqual(new[] { "ecu", "Sensors", "engine", "--once" }, profile.Arguments);
        Assert.AreEqual("felicia-simos2p", profile.Selector);
        Assert.AreEqual("environment.kwdump", ByteDumpOptions.Extract([], "environment.kwdump").Path);
        Assert.IsNull(ByteDumpOptions.Extract([], " ").Path);
        foreach (string[] invalid in new[] { new[] { "--dump" }, new[] { "--dump", " " },
            new[] { "--dump", "--profile", "car" }, new[] { "--dump", "a", "--dump", "b" } })
            Assert.ThrowsExactly<ArgumentException>(() => ByteDumpOptions.Extract(invalid, null));
    }

    [TestMethod]
    public void Capture_PreservesAllByteValuesIncludingProtocolHeadersAndDiscardedEchoes()
    {
        // A raw reply and its conversion header need to survive without any decoding or profile.
        byte[] incoming = [0x55, 0x06, 0x00, 0x02, 0x80, 0x20, 0x00, 0x03, 0x04, 0x02, 0xF4, 0x19, 0x03];
        var port = new FakeInterface(incoming);
        using var output = new StringWriter();
        using (var dump = new SerialByteDump(output))
        using (var capture = dump.Wrap(port, 9600))
        {
            var common = new KwpCommon(capture);
            foreach (byte value in Enumerable.Range(0, 256).Select(x => (byte)x))
                common.WriteByte(value); // KwpCommon reads and discards the echo, which must still be captured.
            foreach (byte value in incoming) Assert.AreEqual(value, common.ReadByte());
        }
        var records = Records(output.ToString());
        var bytes = records.Where(x => x[2] is "RX" or "TX").ToArray();
        Assert.AreEqual(512 + incoming.Length, bytes.Length);
        for (int i = 0; i < 256; i++)
        {
            Assert.AreEqual("TX", bytes[2 * i][2]);
            Assert.AreEqual("RX", bytes[2 * i + 1][2]);
            Assert.AreEqual(i.ToString("X2"), bytes[2 * i][3]);
            Assert.AreEqual(bytes[2 * i][3], bytes[2 * i + 1][3]);
        }
        CollectionAssert.AreEqual(incoming.Select(x => x.ToString("X2")).ToArray(), bytes.Skip(512).Select(x => x[3]).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(1, records.Length).Select(x => (long)x).ToArray(),
            records.Select(x => long.Parse(x[0], CultureInfo.InvariantCulture)).ToArray());
        long[] times = records.Select(x => long.Parse(x[1], CultureInfo.InvariantCulture)).ToArray();
        Assert.IsTrue(times.Zip(times.Skip(1), (a, b) => a <= b).All(x => x));
        Assert.IsTrue(port.Disposed);
    }

    [TestMethod]
    public void Capture_DelegatesSettingsAndRecordsWakeupControlsAndIoFailures()
    {
        var port = new FakeInterface([]);
        using var output = new StringWriter();
        using (var dump = new SerialByteDump(output))
        using (var capture = dump.Wrap(port, 10400))
        {
            Assert.AreEqual(1234, capture.DefaultTimeoutMilliseconds);
            capture.ReadTimeout = 200;
            capture.WriteTimeout = 300;
            Assert.AreEqual(200, port.ReadTimeout);
            Assert.AreEqual(300, port.WriteTimeout);
            capture.SetBaudRate(9600);
            capture.SetParity(Parity.Even);
            capture.SetBreak(true);
            capture.SetBreak(false);
            capture.SetDtr(true);
            capture.SetRts(false);
            capture.ClearReceiveBuffer();
            Assert.ThrowsExactly<TimeoutException>(() => capture.ReadByte());
            port.FailWrite = true;
            Assert.ThrowsExactly<IOException>(() => capture.WriteByteRaw(0xAA));
        }
        CollectionAssert.AreEqual(new[] { "BAUD 9600", "PARITY Even", "BREAK 1", "BREAK 0", "DTR 1", "RTS 0", "CLEAR_RX" }, port.Controls.ToArray());
        CollectionAssert.AreEqual(new[] { "BAUD 10400", "BAUD 9600", "PARITY Even", "BREAK 1", "BREAK 0", "DTR 1", "RTS 0", "CLEAR_RX ",
            "ERROR RX TimeoutException", "ERROR TX IOException", "CLOSE " },
            Records(output.ToString()).Select(x => $"{x[2]} {x[3]}").ToArray());
    }

    [TestMethod]
    public void CaptureFile_FlushesOnFailureAndAppendsAnotherSessionWithoutChangingEarlierData()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.kwdump");
        try
        {
            using (var dump = new SerialByteDump(path))
            using (var capture = dump.Wrap(new FakeInterface([0x02, 0xF4]), 9600))
            {
                dump.WriteSessionInfo(["kw1281test", "ecu", "Sensors", "all", "--once"],
                    ["Sensors", "all", "--once"], "/dev/ttyUSB0", 9600, 1, "felicia-simos2p", "felicia-simos2p");
                capture.ReadByte();
                capture.ReadByte();
                Assert.ThrowsExactly<TimeoutException>(() => capture.ReadByte());
            }
            string original = File.ReadAllText(path);
            Assert.IsTrue(original.StartsWith("# KW1281TEST byte dump v1\n") || original.StartsWith("# KW1281TEST byte dump v1\r\n"));
            Assert.IsTrue(original.Contains("\tRX\t02"));
            Assert.IsTrue(original.Contains("\tRX\tF4"));
            Assert.IsTrue(original.Contains("\tERROR\tRX TimeoutException"));
            Assert.IsTrue(original.Contains("\tCLOSE\t"));
            using (var dump = new SerialByteDump(path))
            using (var capture = dump.Wrap(new FakeInterface([0x55]), 10400))
            {
                dump.WriteSessionInfo(["kw1281test", "ecu", "GroupRead", "1"], ["GroupRead", "1"],
                    "/dev/ttyUSB0", 10400, 1, "custom-profile", "./my-car.json");
                Assert.AreEqual((byte)0x55, capture.ReadByte());
            }
            string combined = File.ReadAllText(path);
            Assert.IsTrue(combined.StartsWith(original, StringComparison.Ordinal));
            string nextSession = combined[original.Length..];
            Assert.IsTrue(nextSession.Contains("# KW1281TEST byte dump v1"));
            Assert.IsTrue(nextSession.Contains("# UTC start "));
            Assert.AreEqual("\"felicia-simos2p\"", Metadata(original, "Profile"));
            Assert.AreEqual("\"custom-profile\"", Metadata(nextSession, "Profile"));
            Assert.AreEqual("\"./my-car.json\"", Metadata(nextSession, "Profile source"));
            using var selected = JsonDocument.Parse(Metadata(nextSession, "Command"));
            CollectionAssert.AreEqual(new[] { "GroupRead", "1" }, selected.RootElement.EnumerateArray().Select(x => x.GetString()).ToArray());
            var records = Records(nextSession);
            Assert.AreEqual("1", records[0][0]);
            Assert.AreEqual("BAUD", records[0][2]);
            Assert.AreEqual("10400", records[0][3]);
            Assert.AreEqual("RX", records[1][2]);
            Assert.AreEqual("55", records[1][3]);
            Assert.AreEqual("CLOSE", records[2][2]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void CaptureFile_AppendsHeaderOnANewLineAfterAnInterruptedSession()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.kwdump");
        const string interrupted = "# KW1281TEST byte dump v1\n1\t42\tRX\t02";
        try
        {
            File.WriteAllText(path, interrupted);
            using (var dump = new SerialByteDump(path))
            using (var capture = dump.Wrap(new FakeInterface([0xF4]), 9600))
                capture.ReadByte();
            string combined = File.ReadAllText(path);
            Assert.IsTrue(combined.StartsWith(interrupted + Environment.NewLine + "# KW1281TEST byte dump v1", StringComparison.Ordinal));
            var records = Records(combined);
            Assert.AreEqual("02", records[0][3]);
            Assert.AreEqual("F4", records[2][3]);
        }
        finally { File.Delete(path); }
    }

    private static string[][] Records(string text) => text.Split('\n')
        .Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0 && !x.StartsWith('#')).Select(x => x.Split('\t')).ToArray();

    private static string Metadata(string text, string name)
    {
        string prefix = $"# {name}: ";
        return text.Split('\n').Select(x => x.TrimEnd('\r')).Single(x => x.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
    }

    private sealed class FakeInterface(byte[] incoming) : IInterface
    {
        private readonly Queue<byte> _incoming = new(incoming);
        private byte? _echo;
        public List<string> Controls { get; } = [];
        public bool Disposed { get; private set; }
        public bool FailWrite { get; set; }
        public int DefaultTimeoutMilliseconds => 1234;
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public byte ReadByte()
        {
            if (_echo is byte value) { _echo = null; return value; }
            if (_incoming.TryDequeue(out value)) return value;
            throw new TimeoutException();
        }
        public void WriteByteRaw(byte value) { if (FailWrite) throw new IOException(); _echo = value; }
        public void ClearReceiveBuffer() { Controls.Add("CLEAR_RX"); _incoming.Clear(); }
        public void SetBaudRate(int value) => Controls.Add($"BAUD {value}");
        public void SetParity(Parity value) => Controls.Add($"PARITY {value}");
        public void SetBreak(bool value) => Controls.Add($"BREAK {(value ? 1 : 0)}");
        public void SetDtr(bool value) => Controls.Add($"DTR {(value ? 1 : 0)}");
        public void SetRts(bool value) => Controls.Add($"RTS {(value ? 1 : 0)}");
        public void Dispose() => Disposed = true;
    }
}

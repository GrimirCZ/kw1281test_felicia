using System.Text;
using System.Text.Json;
using BitFab.KW1281Test.Blocks;

namespace BitFab.KW1281Test.Tests;

[TestClass]
[DoNotParallelize]
public class RawMeasurementTests
{
    private static ProfileUnit Felicia => VehicleProfile.Load("felicia-simos2p").FindUnit(1)!;

    // Console exports contain payloads, not conversion headers. These headers are protocol examples.
    private static byte[] TemperatureMap => Enumerable.Range(0, 17).Select(x => (byte)(x * 12)).ToArray();
    private static GroupReadResponseWithTextBlock Group1Header => Header(
        (0x80, 32, []), (0x8C, 48, TemperatureMap), (0x85, 5, []), (0x88, 255, []));
    private static GroupReadResponseWithTextBlock Group5Header => Header(
        (0x80, 32, []), (0x06, 100, []), (0x8C, 48, TemperatureMap), (0x8C, 48, TemperatureMap));

    [TestMethod]
    public void CapturedGroup1Series_DecodesEveryFieldAndTracksIdleSwitch()
    {
        using var captures = Captures();
        int samples = 0;
        foreach (var sample in captures.RootElement.GetProperty("group1Samples").EnumerateArray())
        {
            var values = sample.EnumerateArray().Select(x => x.GetByte()).ToArray();
            string output = DiagnosticFormatter.Group(Felicia, 1, Raw(values, Group1Header));
            Assert.IsTrue(output.Contains($"Engine speed: {values[0] * 32} rpm"), output);
            Assert.IsTrue(output.Contains("Coolant temperature:"), output);
            Assert.IsTrue(output.Contains("°C"), output);
            Assert.IsTrue(output.Contains("Lambda sensor voltage:"), output);
            Assert.IsTrue(output.Contains((values[3] & 16) != 0 ? "Idle switch closed" : "Idle switch open"), output);
            Assert.IsTrue(output.Contains("Coolant below 80°C"), output);
            Assert.IsFalse(output.Contains("conversion pending"), output);
            Assert.IsFalse(output.Contains("unavailable"), output);
            samples++;
        }
        Assert.AreEqual(198, samples);
    }

    [TestMethod]
    public void CapturedGroup5_UsesHeaderUnitsAndCanonicalSensorPositions()
    {
        var unit = Felicia;
        var raw = Raw([25, 135, 156, 100], Group5Header);
        string group = DiagnosticFormatter.Group(unit, 5, raw);
        Assert.AreEqual("Engine speed: 800 rpm | Battery voltage: 13.5 V | Coolant temperature: 69.0 °C | Intake air temperature: 27.0 °C", group);
        Assert.IsTrue(DiagnosticFormatter.Measurement(unit, unit.Select("engine.temp").Single(), raw).EndsWith("69.0 °C"));
        Assert.IsTrue(DiagnosticFormatter.Measurement(unit, unit.Select("electrical.voltage").Single(), raw).EndsWith("13.5 V"));
    }

    [TestMethod]
    public void CapturedPayloadsWithoutHeaders_KeepNamesAndDecodeKnownStatusesWithoutInventingUnits()
    {
        using var captures = Captures();
        var unit = Felicia;
        foreach (var item in captures.RootElement.GetProperty("groups").EnumerateArray())
        {
            byte group = item.GetProperty("group").GetByte();
            byte[] values = item.GetProperty("values").EnumerateArray().Select(x => x.GetByte()).ToArray();
            string output = DiagnosticFormatter.Group(unit, group, Raw(values));
            foreach (var field in unit.Groups[group.ToString()])
            {
                string label = field.Label ?? unit.GetMeasurements()[field.Measurement!].Label;
                Assert.IsTrue(output.Contains(label), output);
            }
            Assert.IsFalse(output.Contains("unavailable"), output);
        }
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 2, Raw([24, 78, 0, 4])).Contains("Engine operating state: Idle"));
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 19, Raw([25, 0, 0, 0])).Contains("A/C compressor state: off"));
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 20, Raw([25, 77, 156, 151])).Contains("Fault stored, Reduced dynamic range"));
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 5, Raw([25, 135, 156, 100])).Contains("Coolant temperature: 156 (conversion pending)"));
    }

    [TestMethod]
    public void Reader_RetainsNewAndCachedHeadersAcrossSweeps()
    {
        var unit = Felicia;
        var header = Group5Header;
        var responses = new Queue<Block>([header, Raw([25, 135, 156, 100]), Raw([26, 136, 157, 101])]);
        var requests = new List<List<byte>>();
        GroupReadResponseWithTextBlock? cached = null;
        var first = MeasurementReader.Read(5, requests.Add, responses.Dequeue, h => cached = h);
        Assert.AreSame(header, ((RawDataReadResponseBlock)first).MeasurementHeader);
        var second = MeasurementReader.Read(5, requests.Add, responses.Dequeue, header: cached);
        Assert.AreSame(header, ((RawDataReadResponseBlock)second).MeasurementHeader);
        Assert.AreEqual(3, requests.Count);
        var output = new List<string>();
        MeasurementReader.Sweep(unit, unit.Select("engine.rpm,engine.temp,electrical.voltage"), _ => second, output.Add);
        Assert.AreEqual(3, output.Count);
        Assert.IsTrue(output.Any(x => x.EndsWith("832 rpm")));
        Assert.IsTrue(output.Any(x => x.EndsWith("69.8 °C")));
        Assert.IsTrue(output.Any(x => x.EndsWith("13.6 V")));
    }

    [TestMethod]
    public void SensorsCommand_DecodesHeaderAndRawPayloadThroughTheDialog()
    {
        var unit = Felicia;
        var common = new RecordedCommon(Frame(9, []), Group5Header.Bytes.ToList(), Raw([25, 135, 156, 100]).Bytes.ToList());
        string output = CaptureConsole(() =>
        {
            var dialog = new KW1281Dialog(common, unit);
            dialog.Connect();
            dialog.ReadSensors(unit.Select("engine.rpm,engine.temp,electrical.voltage"), once: true);
        });
        Assert.IsTrue(output.Contains("engine.rpm (Engine speed): 800 rpm"), output);
        Assert.IsTrue(output.Contains("engine.temp (Coolant temperature): 69.0 °C"), output);
        Assert.IsTrue(output.Contains("electrical.voltage (Battery voltage): 13.5 V"), output);
        Assert.IsFalse(output.Contains("unavailable"), output);
        Assert.AreEqual(0, common.BytesRemaining);
    }

    [TestMethod]
    public void GroupReadCommand_UsesEveryHeaderFieldInsteadOfTreatingRpmAsATextIndex()
    {
        var common = new RecordedCommon(Frame(9, []), Group1Header.Bytes.ToList(), Raw([25, 143, 4, 147]).Bytes.ToList(), Frame(9, []));
        string output = CaptureConsole(() =>
        {
            var dialog = new KW1281Dialog(common, Felicia);
            dialog.Connect();
            dialog.GroupRead(1);
        });
        Assert.IsTrue(output.Contains("Group 001: Engine speed: 800 rpm"), output);
        Assert.IsTrue(output.Contains("Coolant temperature: 59.2 °C"), output);
        Assert.IsTrue(output.Contains("Lambda sensor voltage: 0.08 V"), output);
        Assert.IsTrue(output.Contains("Idle switch closed"), output);
        Assert.AreEqual(0, common.BytesRemaining);
    }

    [TestMethod]
    public void Header_TablesHandleInterpolationEndpointsAndDescendingValues()
    {
        var header = Header((0x8B, 10, TemperatureMap), (0x8C, 48, TemperatureMap),
            (0x93, 100, Enumerable.Range(0, 17).Select(x => (byte)(200 - x * 10)).ToArray()));
        Assert.IsTrue(header.TryGetValue(0, [255, 143, 0], out var rpm));
        Assert.AreEqual("1912 rpm", rpm);
        Assert.IsTrue(header.TryGetValue(1, [255, 143, 0], out var temperature));
        Assert.AreEqual("59.2 °C", temperature);
        Assert.IsTrue(header.TryGetValue(2, [255, 143, 15], out var descending));
        Assert.AreEqual("90.6 %", descending);
    }

    [TestMethod]
    public void Header_TextFieldsDecodeTheirOwnIndexesAndPreserveNumericNeighbors()
    {
        var strings = Encoding.ASCII.GetBytes("off\x03on\x03");
        var header = Header((0x80, 32, []), (0x8D, 0, strings), (0x89, 10, []), (0x8D, 0, strings));
        var values = new byte[] { 25, 1, 19, 0 };
        Assert.IsTrue(header.TryGetValue(0, values, out var rpm));
        Assert.AreEqual("800 rpm", rpm);
        Assert.IsTrue(header.TryGetValue(1, values, out var first));
        Assert.AreEqual("on", first);
        Assert.IsTrue(header.TryGetValue(2, values, out var injection));
        Assert.AreEqual("1.90 ms", injection);
        Assert.IsTrue(header.TryGetValue(3, values, out var second));
        Assert.AreEqual("off", second);
        Assert.IsFalse(header.TryGetValue(1, [25, 4, 19, 0], out _));
    }

    [TestMethod]
    public void InvalidHeadersAndPayloads_PreserveAllBytesAndAvoidFalseConversions()
    {
        var unit = Felicia;
        var shortHeader = Header((0x80, 32, []));
        var raw = Raw([25, 135, 156, 100], shortHeader);
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 5, raw).Contains("header does not match"));
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 5, raw).EndsWith(raw.ToString()));
        var invalidTable = Header((0x80, 32, []), (0x8C, 48, [0, 12]), (0x85, 5, []), (0x88, 255, []));
        string output = DiagnosticFormatter.Group(unit, 1, Raw([25, 143, 4, 147], invalidTable));
        Assert.IsTrue(output.Contains("Coolant temperature: 143 (ECU conversion not supported)"));
        Assert.IsTrue(output.Contains("Engine speed: 800 rpm"));
        Assert.IsFalse(output.Contains("59"));
        raw = Raw([25, 135, 156, 100, 123]);
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 5, raw).EndsWith(raw.ToString()));
        Assert.IsTrue(DiagnosticFormatter.Measurement(unit, unit.Select("engine.temp").Single(), raw).Contains("returned 5 values, expected 4"));
    }

    [TestMethod]
    public void ProfileBits_RespectHeaderMasksKeepUnknownBitsAndDoNotOverrideText()
    {
        var unit = Felicia;
        var header = Header((0x80, 32, []), (0x8C, 48, TemperatureMap), (0x85, 5, []), (0x88, 0x1F, []));
        string output = DiagnosticFormatter.Group(unit, 1, Raw([25, 143, 4, 255], header));
        Assert.IsFalse(output.Contains("Diagnostic fault affecting"));
        Assert.IsFalse(output.Contains("A/C compressor on"));
        Assert.IsTrue(output.Contains("Idle switch closed"));
        output = DiagnosticFormatter.Group(unit, 1, Raw([25, 143, 4, 0x40]));
        Assert.IsTrue(output.Contains("unknown flags 0x40"));
        header = Header((0x85, 5, []), (0x85, 5, []), (0x88, 15, []), (0x8D, 0, Encoding.ASCII.GetBytes("ADP OK\x03ADP RUN")));
        output = DiagnosticFormatter.Group(unit, 98, Raw([221, 210, 0, 0], header));
        Assert.IsTrue(output.Contains("Throttle adaptation status: ADP OK"));
        header = Header((0x80, 32, []), (0x87, 1, []), (0x87, 1, []), (0x87, 1, []));
        output = DiagnosticFormatter.Group(unit, 19, Raw([25, 0, 1, 0], header));
        Assert.IsTrue(output.Contains("A/C state: on"));
        Assert.IsTrue(output.Contains("A/C compressor state: off"));
    }

    [TestMethod]
    public void GroupRead_PreservesDisregardedAndUnknownFieldsAndLegacyBlockDisplay()
    {
        var unit = Felicia;
        var header = Header((0x80, 32, []), (0x87, 1, []), (0x86, 1, []), (0x88, 15, []));
        var raw = Raw([24, 78, 0, 4], header);
        string output = DiagnosticFormatter.Group(unit, 2, raw);
        Assert.IsTrue(output.Contains("Air mass [disregarded]: 78"));
        Assert.IsTrue(output.Contains("Vehicle speed [disregarded]: 0 km/h"));
        Assert.IsTrue(output.Contains("Engine operating state: Idle"));
        Assert.AreEqual("Raw Data: 024 078 000 004", raw.ToString());
    }

    [TestMethod]
    public void RawProfileFormats_ValidateConfigurationAndAppearWithCommentsInProfileInfo()
    {
        const string custom = """
        {"schemaVersion":1,"id":"custom","units":[{"address":"01",
        "measurements":{"engine":{"temp":{"label":"Temperature","unit":"°C","readFrom":{"group":1,"position":1},"raw":RAW_FORMAT}}},
        "groups":{"1":[{"position":1,"measurement":"engine.temp"}]},"rawGroupLengths":{"1":1}}]}
        """;
        var profile = VehicleProfile.Load("felicia-simos2p");
        Assert.IsTrue(profile.Describe().Contains("rawGroupLengths"));
        Assert.IsTrue(profile.Describe().Contains("Fixed group 000 conversion"));
        foreach (string json in new[] {
            "{\"scale\":1,\"bits\":[{\"mask\":1,\"set\":\"test\"}]}",
            "{\"bits\":[{\"mask\":3,\"set\":\"test\"}]}",
            "{\"bits\":[{\"mask\":1,\"set\":\"test\"},{\"mask\":1,\"set\":\"duplicate\"}]}",
            "{\"decimals\":7}", "{\"bits\":null}"
        })
        {
            Assert.Throws<Exception>(() => VehicleProfile.Parse(custom.Replace("RAW_FORMAT", json)));
        }
        var unit = VehicleProfile.Parse(custom.Replace("RAW_FORMAT", "{\"scale\":0.75,\"offset\":-48,\"decimals\":2,\"comment\":\"Custom conversion\"}")).FindUnit(1)!;
        Assert.AreEqual("Temperature: 69.00 °C", DiagnosticFormatter.Group(unit, 1, Raw([156])));
        Assert.IsTrue(DiagnosticFormatter.Measurement(unit, unit.Select("engine.temp").Single(), Raw([156])).EndsWith("69.00 °C"));
        Assert.ThrowsExactly<ArgumentException>(() => VehicleProfile.Parse(custom.Replace("RAW_FORMAT", "{}").Replace("\"1\":1", "\"1\":0")));
    }

    private static JsonDocument Captures() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "felicia-raw-groups.json")));

    private static RawDataReadResponseBlock Raw(byte[] values, GroupReadResponseWithTextBlock? header = null) =>
        new(Frame(0xF4, values)) { MeasurementHeader = header };

    private static GroupReadResponseWithTextBlock Header(params (byte Formula, byte Parameter, byte[] Table)[] fields) =>
        new(Frame(2, fields.SelectMany(x => new byte[] { x.Formula, x.Parameter, (byte)x.Table.Length }.Concat(x.Table)).ToArray()));

    private static List<byte> Frame(byte title, byte[] body) =>
        new byte[] { (byte)(body.Length + 3), 0, title }.Concat(body).Append((byte)3).ToList();

    private static string CaptureConsole(Action action)
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        try { Console.SetOut(output); action(); }
        finally { Console.SetOut(previous); }
        return output.ToString();
    }

    private sealed class RecordedCommon : IKwpCommon
    {
        private readonly Queue<byte> _bytes = new();
        public int BytesRemaining => _bytes.Count;
        public BitFab.KW1281Test.Interface.IInterface Interface => throw new NotSupportedException();

        public RecordedCommon(params List<byte>[] frames)
        {
            byte counter = 0;
            foreach (var frame in frames)
            {
                frame[1] = counter;
                foreach (byte value in frame) _bytes.Enqueue(value);
                counter += 2;
            }
        }

        public int WakeUp(byte controllerAddress, bool evenParity = false, bool failQuietly = false) => 1281;
        public byte ReadByte() => _bytes.Dequeue();
        public void WriteByte(byte value) { }
        public void ReadComplement(byte value) { }
    }
}

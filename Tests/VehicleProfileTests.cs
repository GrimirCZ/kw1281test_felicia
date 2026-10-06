using System.Text.Json.Nodes;
using BitFab.KW1281Test.Blocks;

namespace BitFab.KW1281Test.Tests;

[TestClass]
public class VehicleProfileTests
{
    private const string Custom = """
    {
      "schemaVersion": 1, "id": "custom", "comment": "Root comment",
      "settings": { "baudRate": 10400, "fieldComments": { "baudRate": "Rate comment" } },
      "units": [{
        "address": "0x01", "aliases": ["ecu"], "present": true, "baudRate": 9600,
        "measurements": {
          "engine": {
            "comment": "Category comment",
            "rpm": { "label": "RPM", "readFrom": { "group": 5, "position": 1 }, "comment": "Line one\nLine two" },
            "temp": { "label": "Temperature", "readFrom": { "group": 5, "position": 2 } },
            "abs": { "label": "ABS", "readFrom": { "group": 6, "position": 1 }, "applicability": "notFitted", "includeInSensors": false, "comment": "Absent field" }
          },
          "engineExtra": { "rpm": { "label": "Other RPM", "readFrom": { "group": 7, "position": 1 } } },
          "custom": { "deep": { "signal": { "label": "New signal", "readFrom": { "group": 8, "position": 1 } } } }
        },
        "groups": {
          "5": [{ "position": 1, "measurement": "engine.rpm" }, { "position": 2, "measurement": "engine.temp" }, { "position": 3, "label": "Ignored", "applicability": "disregard", "comment": "Ignored field comment" }],
          "6": [{ "position": 1, "measurement": "engine.abs" }],
          "7": [{ "position": 1, "measurement": "engineExtra.rpm" }],
          "8": [{ "position": 1, "measurement": "custom.deep.signal" }]
        },
        "dtcs": {
          "00522": { "component": "G62", "description": "Coolant sensor", "comment": "DTC comment", "variants": [
            { "subtypes": [30], "failure": "Open circuit", "checks": ["Inspect wiring"], "obdCode": "P0118", "obdMapping": "approximate analogue" },
            { "subtypes": [31], "failure": "Short to ground" }
          ] },
          "65535": { "component": "ECU", "description": "Internal fault", "variants": [] }
        }
      }]
    }
    """;

    [TestMethod]
    public void EmbeddedProfile_HasCanonicalMeasurementsAndPairedInjectorDetails()
    {
        var profile = VehicleProfile.Load("felicia-simos2p");
        Assert.AreEqual(2, profile.Units.Count);
        var unit = profile.FindUnit(1)!;
        Assert.IsGreaterThan(30, unit.GetMeasurements().Count);
        Assert.AreEqual(3, unit.Select("engine").Count(x => x.Path.StartsWith("engine.idle") || x.Path == "engine.rpm"));
        Assert.IsTrue(unit.Dtcs["01249"].Component.Contains("1 or 4"));
        Assert.IsTrue(unit.Dtcs["01250"].Component.Contains("2 or 3"));
        Assert.IsTrue(unit.Dtcs.ContainsKey("00624"));
        Assert.IsFalse(profile.FindUnit(0x25)!.Dtcs.ContainsKey("00522"));
        Assert.IsTrue(profile.Describe().Contains("Default connection rate"));
    }

    [TestMethod]
    public void Selection_TraversesArbitraryHierarchyAndDeduplicatesOverlaps()
    {
        var unit = VehicleProfile.Parse(Custom).FindUnit(1)!;
        CollectionAssert.AreEqual(new[] { "engine.rpm", "engine.temp" }, unit.Select("engine,engine.rpm").Select(x => x.Path).ToArray());
        Assert.AreEqual("custom.deep.signal", unit.Select("custom.deep").Single().Path);
        Assert.AreEqual(4, unit.Select("all").Count);
        Assert.AreEqual(2, unit.Select("engine.rpm,engine.temp").Count);
        Assert.ThrowsExactly<ArgumentException>(() => unit.Select("engine.abs"));
        Assert.ThrowsExactly<ArgumentException>(() => unit.Select("engine.r"));
        Assert.ThrowsExactly<ArgumentException>(() => unit.Select("engine.comment"));
        Assert.ThrowsExactly<ArgumentException>(() => unit.Select("engine,"));
    }

    [TestMethod]
    public void Settings_ResolveAliasesDefaultsAndExplicitOverrides()
    {
        var profile = VehicleProfile.Parse(Custom);
        Assert.AreEqual(1, profile.ResolveAddress("ECU"));
        Assert.AreEqual(0x25, profile.ResolveAddress("25"));
        Assert.AreEqual(9600, profile.ResolveBaud("auto", 1));
        Assert.AreEqual(10400, profile.ResolveBaud("auto", 0x25));
        Assert.AreEqual(4800, profile.ResolveBaud("4800", 1));
        Assert.ThrowsExactly<ArgumentException>(() => profile.ResolveAddress("missing"));
        Assert.ThrowsExactly<ArgumentException>(() => profile.ResolveBaud("0", 1));
    }

    [TestMethod]
    public void ProfileOption_UsesCliBeforeEnvironmentAndSupportsPaths()
    {
        var selection = VehicleProfile.ExtractOption(["ecu", "Sensors", "engine", "--profile", "./car.json"], "invalid-env");
        Assert.AreEqual("./car.json", selection.Selector);
        CollectionAssert.AreEqual(new[] { "ecu", "Sensors", "engine" }, selection.Arguments);
        Assert.AreEqual("felicia-simos2p", VehicleProfile.ExtractOption([], "felicia-simos2p").Selector);
        Assert.IsNull(VehicleProfile.ExtractOption([], " ").Selector);
        Assert.ThrowsExactly<ArgumentException>(() => VehicleProfile.ExtractOption(["--profile"], null));
        Assert.ThrowsExactly<ArgumentException>(() => VehicleProfile.ExtractOption(["--profile", "a", "--profile", "b"], null));
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, Custom);
            Assert.AreEqual("custom", VehicleProfile.Load(file).Id);
            Assert.ThrowsExactly<FileNotFoundException>(() => VehicleProfile.Load(file + ".missing"));
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void ConnectionEnvironment_ShortensInvocationWithoutChangingExplicitArguments()
    {
        var profile = VehicleProfile.Parse(Custom);
        CollectionAssert.AreEqual(new[] { "COM9", "4800", "ecu", "Sensors", "engine" },
            ConnectionArguments.Normalize(["ecu", "Sensors", "engine"], profile, "COM9", "4800"));
        CollectionAssert.AreEqual(new[] { "COM8", "auto", "ecu", "GroupRead", "5" },
            ConnectionArguments.Normalize(["COM8", "ecu", "GroupRead", "5"], profile, "COM9", null));
        CollectionAssert.AreEqual(new[] { "COM9", "9600", "ecu", "ReadIdent" },
            ConnectionArguments.Normalize(["9600", "ecu", "ReadIdent"], null, "COM9", null));
        var explicitArgs = new[] { "COM7", "10400", "01", "ReadIdent" };
        CollectionAssert.AreEqual(explicitArgs, ConnectionArguments.Normalize(explicitArgs, profile, "COM9", "4800"));
        Assert.ThrowsExactly<ArgumentException>(() => ConnectionArguments.Normalize(["ecu", "ReadIdent"], profile, null, null));
    }

    [TestMethod]
    public void ProfileInfo_ShowsCommentsForHierarchyPrimitiveFieldsAndExcludedValues()
    {
        string info = VehicleProfile.Parse(Custom).Describe();
        foreach (string text in new[] { "Root comment", "Category comment", "Rate comment", "Line one", "Line two", "Absent field", "Ignored field comment", "DTC comment" })
            Assert.IsTrue(info.Contains(text), text);
        Assert.IsTrue(info.Contains("engine.abs"));
        Assert.IsTrue(info.Contains("notFitted"));
    }

    [TestMethod]
    public void Validation_RejectsAmbiguousOrBrokenManifestsBeforeHardwareAccess()
    {
        foreach (Action<JsonNode> mutation in new Action<JsonNode>[] {
            x => x["schemaVersion"] = 2,
            x => x["settings"]!["baudRate"] = 0,
            x => x["settings"]!["fieldComments"]!["missing"] = "bad",
            x => x["units"]![0]!["aliases"] = new JsonArray("ecu", "ECU"),
            x => x["units"]![0]!["address"] = "0x80",
            x => x["units"]![0]!["measurements"]!["engine"]!["rpm"]!["readFrom"]!["position"] = 9,
            x => x["units"]![0]!["measurements"]!["engine"]!["rpm"]!["child"] = new JsonObject(),
            x => x["units"]![0]!["groups"]!["5"]![0]!["measurement"] = "missing",
            x => x["units"]![0]!["groups"]!["5"]![1]!["position"] = 1
        })
        {
            var node = JsonNode.Parse(Custom)!;
            mutation(node);
            Assert.Throws<Exception>(() => VehicleProfile.Parse(node.ToJsonString()));
        }
        Assert.ThrowsExactly<ArgumentException>(() => VehicleProfile.Parse(Custom.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1")));
    }

    [TestMethod]
    public void GroupRead_PreservesIgnoredAndUnknownFieldsWhileAddingLabels()
    {
        var unit = VehicleProfile.Parse(Custom).FindUnit(1)!;
        var block = Group(1, 10, 20, 5, 10, 110, 54, 0, 8, 99, 12, 34);
        string formatted = DiagnosticFormatter.Group(unit, 5, block);
        Assert.IsTrue(formatted.Contains("RPM: 40 rpm"));
        Assert.IsTrue(formatted.Contains("Temperature:"));
        Assert.IsTrue(formatted.Contains("Ignored [disregard]: 8"));
        Assert.IsTrue(formatted.Contains("(99 12 34)"));
        Assert.AreEqual("40 rpm | 10.0 °C | 8 | (99 12 34)", block.ToString());
    }

    [TestMethod]
    public void Faults_MatchVerifiedSubtypeAndPreserveUnknownStatusAndSentinelDistinction()
    {
        var unit = VehicleProfile.Parse(Custom).FindUnit(1)!;
        var lines = DiagnosticFormatter.Fault(unit, new FaultCode(522, 30 | 0x80)).ToArray();
        Assert.AreEqual("00522 - 30-10", lines[0]);
        Assert.IsTrue(lines.Any(x => x.Contains("Open circuit")));
        Assert.IsFalse(lines.Any(x => x.Contains("Short to ground")));
        Assert.IsTrue(DiagnosticFormatter.Fault(unit, new FaultCode(522, 27)).Any(x => x.Contains("Fault subtype needs verification")));
        Assert.AreEqual(0, DiagnosticFormatter.Fault(unit, FaultCode.None).Count());
        Assert.IsTrue(DiagnosticFormatter.Fault(unit, new FaultCode(65535, 0)).Any(x => x.Contains("Internal fault")));
        Assert.AreEqual("12345 - 01-00", DiagnosticFormatter.Fault(unit, new FaultCode(12345, 1)).Single());
        Assert.AreEqual("00522 - 30-10", DiagnosticFormatter.Fault(null, new FaultCode(522, 158)).Single());
    }

    [TestMethod]
    public void Sweep_ReadsSharedGroupOnceFiltersFieldsAndReportsUnsupportedGroups()
    {
        var unit = VehicleProfile.Parse(Custom).FindUnit(1)!;
        var requests = new List<byte>();
        var output = new List<string>();
        MeasurementReader.Sweep(unit.Select("engine,engine.rpm,custom"), group =>
        {
            requests.Add(group);
            return group == 5 ? Group(1, 10, 20, 5, 10, 110, 54, 0, 8) : new NakBlock([3, 0, 0x0A, 3]);
        }, output.Add);
        CollectionAssert.AreEqual(new byte[] { 5, 8 }, requests);
        Assert.AreEqual(3, output.Count);
        Assert.IsTrue(output.Any(x => x.Contains("custom.deep.signal: unavailable")));
        Assert.IsFalse(output.Any(x => x.Contains("Ignored")));
        Assert.IsTrue(output[0].Contains("T") && output[0].Contains("+00:00"));
    }

    [TestMethod]
    public void RawMeasurements_KeepBytesAndDoNotGuessNormalizedTextLayouts()
    {
        var unit = VehicleProfile.Load("felicia-simos2p").FindUnit(1)!;
        var raw = new RawDataReadResponseBlock([13, 0, 0xF4, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 3]);
        string pressure = DiagnosticFormatter.Measurement(unit.Select("intake.pressure").Single(), raw);
        Assert.IsTrue(pressure.EndsWith("4 [raw]"));
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 0, raw).Contains("Battery voltage: 2 [raw]"));
        Assert.IsTrue(DiagnosticFormatter.Measurement(unit.Select("engine.rpm").Single(), raw).Contains("unverified raw response layout"));
        Assert.AreEqual(raw.ToString(), DiagnosticFormatter.Group(unit, 5, raw));
    }

    [TestMethod]
    public void GroupFieldExclusionAndEmptyBranches_AreHandledByGenericSelection()
    {
        var node = JsonNode.Parse(Custom)!;
        node["units"]![0]!["groups"]!["5"]![0]!["includeInSensors"] = false;
        node["units"]![0]!["measurements"]!["empty"] = new JsonObject { ["comment"] = "Empty category" };
        var unit = VehicleProfile.Parse(node.ToJsonString()).FindUnit(1)!;
        Assert.AreEqual("engine.temp", unit.Select("engine").Single().Path);
        Assert.ThrowsExactly<ArgumentException>(() => unit.Select("engine.rpm"));
        var exception = Assert.ThrowsExactly<ArgumentException>(() => unit.Select("empty"));
        Assert.IsTrue(exception.Message.Contains("no enabled"));
        Assert.IsTrue(DiagnosticFormatter.Group(unit, 5, Group(1, 10, 20)).Contains("RPM: 40 rpm"));
    }

    [TestMethod]
    public void Reader_UsesMeasuringRequestsForRawAndNormalizedGroupsAndHandlesTextFrames()
    {
        var requests = new List<List<byte>>();
        var text = new GroupReadResponseWithTextBlock([3, 0, 2, 3]);
        var reading = Group(1, 10, 20);
        var responses = new Queue<Block>(new Block[] { text, reading });
        Assert.AreSame(reading, MeasurementReader.Read(5, requests.Add, responses.Dequeue));
        Assert.IsTrue(requests.All(x => x.SequenceEqual(new byte[] { 0x29, 5 })));
        requests.Clear();
        var raw = new RawDataReadResponseBlock([4, 0, 0xF4, 7, 3]);
        Assert.AreSame(raw, MeasurementReader.Read(0, requests.Add, () => raw));
        CollectionAssert.AreEqual(new byte[] { 0x12 }, requests.Single());
        requests.Clear();
        MeasurementReader.Sweep(VehicleProfile.Parse(Custom).FindUnit(1)!.Select("all"), _ => throw new AssertFailedException(), _ => { }, () => true);
    }

    private static GroupReadResponseBlock Group(params byte[] body) => new(new byte[] { (byte)(body.Length + 3), 0, 0xE7 }.Concat(body).Append((byte)3).ToList());
}

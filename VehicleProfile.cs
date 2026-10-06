using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BitFab.KW1281Test;

internal sealed class VehicleProfile
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public string Id { get; set; } = "";
    public Dictionary<string, JsonElement> Vehicle { get; set; } = [];
    public ProfileSettings Settings { get; set; } = new();
    [JsonRequired] public List<ProfileUnit> Units { get; set; } = [];

    private JsonElement _manifest;

    internal const string EnvironmentVariable = "KW1281TEST_PROFILE";

    internal static (string[] Arguments, string? Selector) ExtractOption(string[] args, string? environment)
    {
        var remaining = new List<string>();
        string? selector = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (selector != null || ++i == args.Length || string.IsNullOrWhiteSpace(args[i]))
                    throw new ArgumentException("Specify --profile once, followed by an identifier or JSON path.");
                selector = args[i];
            }
            else remaining.Add(args[i]);
        }
        return (remaining.ToArray(), selector ?? (string.IsNullOrWhiteSpace(environment) ? null : environment));
    }

    internal static VehicleProfile Load(string selector)
    {
        var assembly = typeof(VehicleProfile).Assembly;
        string resource = $"BitFab.KW1281Test.Profiles.{selector}.json";
        using var stream = assembly.GetManifestResourceStream(resource) ?? File.OpenRead(selector);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    internal static VehicleProfile Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        ValidateJson(document.RootElement, "profile");
        using var normalized = JsonDocument.Parse(RemoveAnnotations(document.RootElement));
        var profile = JsonSerializer.Deserialize(normalized.RootElement, ProfileJsonContext.Default.VehicleProfile)
            ?? throw new ArgumentException("Profile must be a JSON object.");
        profile._manifest = document.RootElement.Clone();
        profile.Validate();
        return profile;
    }

    private static byte[] RemoveAnnotations(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(element, writer);
        return stream.ToArray();

        static void Write(JsonElement value, Utf8JsonWriter writer)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name is "comment" or "fieldComments") continue;
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (var child in value.EnumerateArray()) Write(child, writer);
                writer.WriteEndArray();
            }
            else value.WriteTo(writer);
        }
    }

    private static void ValidateJson(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var child in element.EnumerateArray()) ValidateJson(child, $"{path}[{i++}]");
        }
        if (element.ValueKind != JsonValueKind.Object) return;
        var properties = element.EnumerateObject().ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!names.Add(property.Name)) throw new ArgumentException($"Duplicate property {path}.{property.Name}.");
            if (property.Name == "comment" && property.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"{path}.comment must be a string.");
            ValidateJson(property.Value, $"{path}.{property.Name}");
        }
        if (element.TryGetProperty("fieldComments", out var comments))
        {
            if (comments.ValueKind != JsonValueKind.Object) throw new ArgumentException($"{path}.fieldComments must be an object.");
            foreach (var comment in comments.EnumerateObject())
                if (comment.Name is "comment" or "fieldComments" || !names.Contains(comment.Name) || comment.Value.ValueKind != JsonValueKind.String)
                    throw new ArgumentException($"Invalid field comment {path}.{comment.Name}.");
        }
    }

    private void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Id) || Units == null || Units.Count == 0 || Settings == null)
            throw new ArgumentException("Profile requires schemaVersion 1, a nonempty id, settings and units.");
        if (Settings.BaudRate is <= 0) throw new ArgumentException("Profile baudRate must be positive.");
        var addresses = new HashSet<int>();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in Units)
        {
            if (unit == null || !addresses.Add(unit.NumericAddress) || unit.BaudRate is <= 0 || unit.Aliases == null || unit.Groups == null || unit.Measurements == null || unit.Dtcs == null)
                throw new ArgumentException("Units require distinct addresses, valid baud rates and non-null collections.");
            foreach (string alias in unit.Aliases)
                if (string.IsNullOrWhiteSpace(alias) || int.TryParse(alias.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? alias[2..] : alias, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) || !aliases.Add(alias))
                    throw new ArgumentException($"Invalid, numeric or duplicate unit alias: {alias}.");
            var measurements = unit.GetMeasurements();
            foreach (var (group, fields) in unit.Groups)
            {
                if (!byte.TryParse(group, NumberStyles.None, CultureInfo.InvariantCulture, out byte number) || group != number.ToString(CultureInfo.InvariantCulture))
                    throw new ArgumentException($"Invalid group number: {group}.");
                var positions = new HashSet<int>();
                if (fields == null) throw new ArgumentException($"Group {group} fields must be an array.");
                foreach (var field in fields)
                {
                    if (field == null || field.Position < 1 || !positions.Add(field.Position))
                        throw new ArgumentException($"Invalid or duplicate field position in group {group}.");
                    if (field.Measurement != null && !measurements.ContainsKey(field.Measurement))
                        throw new ArgumentException($"Unknown measurement reference: {field.Measurement}.");
                }
            }
            foreach (var (path, measurement) in measurements)
            {
                if (measurement.ReadFrom == null || measurement.ReadFrom.Position < 1 || string.IsNullOrWhiteSpace(measurement.Label) || measurement.States == null)
                    throw new ArgumentException($"Measurement {path} needs a label and readFrom.");
                var field = unit.FindField(measurement.ReadFrom.Group, measurement.ReadFrom.Position);
                if (field?.Measurement != path)
                    throw new ArgumentException($"Canonical readFrom for {path} must reference a group field with that measurement path.");
                ValidateApplicability(measurement.Applicability);
            }
            foreach (var field in unit.Groups.Values.SelectMany(x => x)) ValidateApplicability(field.Applicability);
            foreach (var (code, dtc) in unit.Dtcs)
            {
                if (code.Length != 5 || !int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number > 65535 || dtc == null || dtc.Variants == null)
                    throw new ArgumentException($"Invalid DTC: {code}.");
                var subtypes = new HashSet<int>();
                foreach (var variant in dtc.Variants)
                {
                    if (variant == null || variant.Subtypes == null || variant.Causes == null || variant.Checks == null) throw new ArgumentException($"Invalid variant for DTC {code}.");
                    foreach (int subtype in variant.Subtypes)
                        if (subtype is < 0 or > 127 || !subtypes.Add(subtype))
                            throw new ArgumentException($"Invalid or ambiguous subtype for DTC {code}: {subtype}.");
                }
            }
        }
    }

    private static void ValidateApplicability(string applicability)
    {
        if (applicability is not ("present" or "notFitted" or "disregard" or "unresolved"))
            throw new ArgumentException($"Unknown applicability: {applicability}.");
    }

    internal ProfileUnit? FindUnit(int address) => Units.FirstOrDefault(x => x.NumericAddress == address);

    internal int ResolveAddress(string value)
    {
        var unit = Units.FirstOrDefault(x => x.Aliases.Contains(value, StringComparer.OrdinalIgnoreCase));
        return unit?.NumericAddress ?? ParseAddress(value);
    }

    internal static int ParseAddress(string value)
    {
        // Preserve upstream hexadecimal address syntax, also allowing explicit 0x prefixes.
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int address) || address is < 0 or > 127)
            throw new ArgumentException($"Unknown unit alias or invalid hexadecimal address: {value}.");
        return address;
    }

    internal int ResolveBaud(string value, int address)
    {
        if (!value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int baud) || baud <= 0)
                throw new ArgumentException("Baud rate must be positive or auto with a profile default.");
            return baud;
        }
        return FindUnit(address)?.BaudRate ?? Settings.BaudRate
            ?? throw new ArgumentException("No profile baud rate is defined for auto.");
    }

    internal string Describe()
    {
        // Walk the complete manifest, so comments on fields/settings/states/DTCs are never lost.
        var element = _manifest;
        var lines = new List<string> { $"Profile: {Id}", "Measurement selectors (use any branch, leaf, or all):" };
        foreach (var unit in Units)
            foreach (var (path, measurement) in unit.GetMeasurements())
                lines.Add($"  {unit.Address} {path}: {measurement.Label} [{measurement.Applicability}]" +
                    (measurement.IncludeInSensors ? "" : " [excluded from Sensors]"));
        DescribeElement(element, "profile", lines);
        return string.Join(Environment.NewLine, lines);
    }

    private static void DescribeElement(JsonElement element, string path, List<string> lines, string? fieldComment = null)
    {
        if (element.ValueKind == JsonValueKind.Null) return;
        lines.Add($"{path}: {(element.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? "" : element.ToString())}");
        if (!string.IsNullOrWhiteSpace(fieldComment)) AddComment(lines, fieldComment);
        if (element.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var child in element.EnumerateArray()) DescribeElement(child, $"{path}[{i++}]", lines);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("comment", out var comment) && comment.ValueKind == JsonValueKind.String)
                AddComment(lines, comment.GetString());
            element.TryGetProperty("fieldComments", out var comments);
            foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (property.Name is "comment" or "fieldComments") continue;
                string? attached = comments.ValueKind == JsonValueKind.Object && comments.TryGetProperty(property.Name, out var c) ? c.GetString() : null;
                DescribeElement(property.Value, $"{path}.{property.Name}", lines, attached);
            }
        }
    }

    private static void AddComment(List<string> lines, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (string line in text.Replace("\r\n", "\n").Split('\n')) lines.Add($"  # {line}");
    }
}

internal sealed class ProfileSettings
{
    public int? BaudRate { get; set; }
}

internal sealed class ProfileUnit
{
    [JsonRequired] public string Address { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public bool Present { get; set; } = true;
    public int? BaudRate { get; set; }
    public Dictionary<string, JsonElement> Measurements { get; set; } = [];
    public Dictionary<string, List<GroupField>> Groups { get; set; } = [];
    public Dictionary<string, DtcDefinition> Dtcs { get; set; } = [];
    [JsonIgnore] public int NumericAddress => VehicleProfile.ParseAddress(Address);

    internal GroupField? FindField(int group, int position) =>
        Groups.GetValueOrDefault(group.ToString(CultureInfo.InvariantCulture))?.FirstOrDefault(x => x.Position == position);

    internal Dictionary<string, MeasurementDefinition> GetMeasurements()
    {
        var result = new Dictionary<string, MeasurementDefinition>(StringComparer.Ordinal);
        foreach (var (key, element) in Measurements)
        {
            if (key.Contains('.') || key is "comment" or "fieldComments") throw new ArgumentException($"Dots are not allowed in node names: {key}.");
            Walk(key, element, result);
        }
        return result;
    }

    private static void Walk(string path, JsonElement element, Dictionary<string, MeasurementDefinition> result)
    {
        string name = path.Split('.').Last();
        if (string.IsNullOrWhiteSpace(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || name == "all" || element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Invalid measurement node: {path}.");
        if (element.TryGetProperty("readFrom", out _))
        {
            var measurement = JsonSerializer.Deserialize(element, ProfileJsonContext.Default.MeasurementDefinition)
                ?? throw new ArgumentException($"Invalid measurement: {path}.");
            result.Add(path, measurement);
        }
        else
        {
            foreach (var child in element.EnumerateObject())
            {
                if (child.Name is "comment" or "fieldComments") continue;
                if (child.Name.Contains('.')) throw new ArgumentException($"Dots are not allowed in node names: {child.Name}.");
                Walk($"{path}.{child.Name}", child.Value, result);
            }
        }
    }

    private bool HasBranch(string path)
    {
        var parts = path.Split('.');
        if (!Measurements.TryGetValue(parts[0], out var node)) return false;
        foreach (string part in parts.Skip(1))
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node)) return false;
        }
        return node.ValueKind == JsonValueKind.Object && !node.TryGetProperty("readFrom", out _);
    }

    internal List<SelectedMeasurement> Select(string selectors)
    {
        var measurements = GetMeasurements();
        var result = new Dictionary<string, SelectedMeasurement>(StringComparer.Ordinal);
        foreach (string selector in selectors.Split(',', StringSplitOptions.TrimEntries))
        {
            var matching = measurements.Where(x => selector == "all" || x.Key == selector || x.Key.StartsWith(selector + ".", StringComparison.Ordinal)).ToArray();
            if (selector.Length == 0 || (matching.Length == 0 && selector != "all" && !HasBranch(selector)))
                throw new ArgumentException($"Unknown measurement selector '{selector}'. Available: all, {string.Join(", ", measurements.Keys)}.");
            foreach (var (path, measurement) in matching)
            {
                var field = FindField(measurement.ReadFrom!.Group, measurement.ReadFrom.Position)!;
                string applicability = field.Applicability != "present" ? field.Applicability : measurement.Applicability;
                if (!measurement.IncludeInSensors || !field.IncludeInSensors || applicability != "present")
                {
                    if (path == selector) throw new ArgumentException($"Measurement {path} is unavailable: {applicability} (excluded from Sensors).");
                    continue;
                }
                result.TryAdd(path, new SelectedMeasurement(path, measurement));
            }
        }
        if (result.Count == 0) throw new ArgumentException("Selection contains no enabled applicable measurements.");
        return result.Values.ToList();
    }
}

internal sealed class MeasurementDefinition
{
    [JsonRequired] public string Label { get; set; } = "";
    public string? Unit { get; set; }
    [JsonRequired] public GroupReference? ReadFrom { get; set; }
    public string Applicability { get; set; } = "present";
    public bool IncludeInSensors { get; set; } = true;
    public Dictionary<string, string> States { get; set; } = [];
}

internal sealed class GroupReference
{
    [JsonRequired] public byte Group { get; set; }
    [JsonRequired] public int Position { get; set; }
}

internal sealed class GroupField
{
    public bool IncludeInSensors { get; set; } = true;
    [JsonRequired] public int Position { get; set; }
    public string? Measurement { get; set; }
    public string? Label { get; set; }
    public string Applicability { get; set; } = "present";
}

internal sealed class DtcDefinition
{
    [JsonRequired] public string Component { get; set; } = "";
    [JsonRequired] public string Description { get; set; } = "";
    [JsonRequired] public List<DtcVariant> Variants { get; set; } = [];
}

internal sealed class DtcVariant
{
    public List<int> Subtypes { get; set; } = [];
    [JsonRequired] public string Failure { get; set; } = "";
    public List<string> Causes { get; set; } = [];
    public List<string> Checks { get; set; } = [];
    public string? ObdCode { get; set; }
    public string? ObdMapping { get; set; }
}

internal sealed record SelectedMeasurement(string Path, MeasurementDefinition Definition);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VehicleProfile))]
[JsonSerializable(typeof(MeasurementDefinition))]
internal partial class ProfileJsonContext : JsonSerializerContext { }

using BitFab.KW1281Test.Blocks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BitFab.KW1281Test;

internal static class DiagnosticFormatter
{
    internal static string Group(ProfileUnit unit, byte group, Block block)
    {
        var values = Values(block);
        if (values == null) return block.ToString() ?? "";
        if (block is RawDataReadResponseBlock raw && RawLayoutError(unit, group, raw) is string error)
            return $"{error}: {raw}";
        var measurements = unit.GetMeasurements();
        return string.Join(" | ", values.Select((value, index) =>
        {
            var field = unit.FindField(group, index + 1);
            var measurement = field?.Measurement == null ? null : measurements.GetValueOrDefault(field.Measurement);
            var label = field?.Label ?? measurement?.Label;
            var applicability = field?.Applicability != "present" ? field?.Applicability : measurement?.Applicability;
            var formatted = FormatValue(block, index, value, measurement, field);
            var annotation = applicability switch { "disregard" => " [disregarded]", "notFitted" => " [not fitted]", "unresolved" => " [unresolved]", _ => "" };
            return $"{label ?? $"Field {index + 1}"}{annotation}: {formatted}";
        }));
    }

    internal static List<string>? Values(Block block) => block switch
    {
        GroupReadResponseBlock reading => reading.SensorValues.Select(x => x.ToString()).ToList(),
        RawDataReadResponseBlock raw => raw.Body.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
        _ => null
    };

    internal static string Measurement(ProfileUnit unit, SelectedMeasurement selected, Block block)
    {
        byte group = selected.Definition.ReadFrom!.Group;
        if (block is RawDataReadResponseBlock raw && RawLayoutError(unit, group, raw) is string error)
            return $"{selected.Path} ({selected.Definition.Label}): unavailable ({error})";
        var values = Values(block);
        int index = selected.Definition.ReadFrom!.Position - 1;
        if (values == null || index >= values.Count)
            return $"{selected.Path} ({selected.Definition.Label}): unavailable (group {group:D3} returned no measurement for this field)";
        return $"{selected.Path} ({selected.Definition.Label}): {FormatValue(block, index, values[index], selected.Definition, unit.FindField(group, index + 1))}";
    }

    private static string? RawLayoutError(ProfileUnit unit, byte group, RawDataReadResponseBlock raw)
    {
        int? expected = unit.RawGroupLength(group);
        if (expected.HasValue && expected.Value != raw.Body.Count)
            return $"group {group:D3} returned {raw.Body.Count} values, expected {expected.Value}";
        if (raw.MeasurementHeader != null && !raw.MeasurementHeader.MatchesValueCount(raw.Body.Count))
            return $"group {group:D3} conversion header does not match its values";
        if (!expected.HasValue && raw.MeasurementHeader == null)
            return $"group {group:D3} raw layout is not defined";
        return null;
    }

    private static string FormatValue(Block block, int index, string value, MeasurementDefinition? definition, GroupField? field)
    {
        var format = field?.Raw ?? definition?.Raw;
        if (block is RawDataReadResponseBlock raw)
        {
            byte status = raw.Body[index];
            var header = raw.MeasurementHeader;
            bool isStatus = header == null || header.TryGetStatusValue(index, status, out status);
            if (isStatus && format?.Bits.Count > 0) return FormatBits(status, format);
            if (isStatus && definition != null && definition.States.TryGetValue(status.ToString(CultureInfo.InvariantCulture), out var state)) return state;
            if (header != null)
            {
                if (!header.TryGetValue(index, raw.Body, out var decoded)) return $"{value} (ECU conversion not supported)";
                return definition != null && definition.States.TryGetValue(decoded, out var description) ? description : decoded;
            }
            if (format?.Scale is double scale)
            {
                double result = raw.Body[index] * scale + format.Offset;
                string number = result.ToString($"F{format.Decimals}", CultureInfo.InvariantCulture);
                return number + (string.IsNullOrWhiteSpace(definition?.Unit) ? "" : " " + definition.Unit);
            }
            return value + (field?.Applicability is "disregard" or "notFitted" ? " [raw]" : " (conversion pending)");
        }
        if (block is GroupReadResponseBlock reading && reading.SensorValues[index].SensorID is 0x10 or 0x88)
        {
            byte status = (byte)(reading.SensorValues[index].A & reading.SensorValues[index].B);
            if (format?.Bits.Count > 0) return FormatBits(status, format);
            if (definition != null && definition.States.TryGetValue(status.ToString(CultureInfo.InvariantCulture), out var state)) return state;
        }
        if (definition != null && definition.States.TryGetValue(value, out var stateDescription)) return stateDescription;
        return value;
    }

    private static string FormatBits(byte value, RawValueDefinition definition)
    {
        var labels = new List<string>();
        int known = 0;
        foreach (var bit in definition.Bits)
        {
            known |= bit.Mask;
            string? label = (value & bit.Mask) != 0 ? bit.Set : bit.Clear;
            if (!string.IsNullOrWhiteSpace(label)) labels.Add(label);
        }
        int unknown = value & ~known;
        if (unknown != 0) labels.Add($"unknown flags 0x{unknown:X2}");
        return labels.Count > 0 ? string.Join(", ", labels) : definition.Zero ?? "No flags set";
    }

    internal static IEnumerable<string> Fault(ProfileUnit? unit, FaultCode fault)
    {
        // The protocol's exact no-fault marker is distinct from other 65535 statuses.
        if (fault.Equals(FaultCode.None)) yield break;
        yield return fault.ToString();
        if (unit == null || !unit.Dtcs.TryGetValue(fault.Dtc.ToString("D5", CultureInfo.InvariantCulture), out var definition))
            yield break;
        yield return $"  {definition.Component}: {definition.Description}";
        int subtype = fault.Status & 0x7F;
        var matching = definition.Variants.Where(x => x.Subtypes.Contains(subtype)).ToArray();
        if (matching.Length == 0)
            yield return "  Fault subtype needs verification. Possible failure modes:";
        foreach (var variant in matching.Length == 0 ? definition.Variants : matching.AsEnumerable())
        {
            yield return $"  {(matching.Length == 0 ? "Possible failure" : "Failure")}: {variant.Failure}";
            foreach (var cause in variant.Causes) yield return $"    Possible cause: {cause}";
            foreach (var check in variant.Checks) yield return $"    Check: {check}";
            if (!string.IsNullOrWhiteSpace(variant.ObdCode))
                yield return $"    OBD reference: {variant.ObdCode} ({variant.ObdMapping ?? "approximate equivalent for reference only"})";
        }
    }
}

/// <summary>One protocol transaction, shared by profile Sensors and ordinary GroupRead.</summary>
internal static class MeasurementReader
{
    internal static List<byte> Request(byte group, bool useBasicSetting = false) => group == 0
        ? [(byte)(useBasicSetting ? BlockTitle.BasicSettingRawDataRead : BlockTitle.RawDataRead)]
        : [(byte)(useBasicSetting ? BlockTitle.BasicSettingRead : BlockTitle.GroupRead), group];

    internal static Block Read(byte group, Action<List<byte>> send, Func<Block> receive, Action<GroupReadResponseWithTextBlock>? rememberText = null,
        bool useBasicSetting = false, GroupReadResponseWithTextBlock? header = null)
    {
        var request = Request(group, useBasicSetting);
        // Some controllers return a text description before the values on a subsequent request.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            send(new List<byte>(request));
            var block = receive();
            if (block is GroupReadResponseWithTextBlock text)
            {
                header = text;
                rememberText?.Invoke(text);
                continue;
            }
            if (block is RawDataReadResponseBlock raw && group != 0) raw.MeasurementHeader = header;
            return block;
        }
        throw new InvalidOperationException($"Group {group:D3} returned text repeatedly without measurement values.");
    }

    internal static void Sweep(ProfileUnit unit, IReadOnlyList<SelectedMeasurement> selected, Func<byte, Block> read, Action<string> output, Func<bool>? cancelled = null)
    {
        foreach (var group in selected.GroupBy(x => x.Definition.ReadFrom!.Group))
        {
            if (cancelled?.Invoke() == true) break;
            var block = read(group.Key);
            string timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            if (block is NakBlock)
            {
                foreach (var item in group) output($"{timestamp} {item.Path}: unavailable (group {group.Key:D3} not supported)");
                continue;
            }
            foreach (var item in group) output($"{timestamp} {DiagnosticFormatter.Measurement(unit, item, block)}");
        }
    }
}

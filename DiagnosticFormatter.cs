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
        if (block is RawDataReadResponseBlock && group != 0) return block.ToString() ?? "";
        if (values == null) return block.ToString() ?? "";
        var measurements = unit.GetMeasurements();
        return string.Join(" | ", values.Select((value, index) =>
        {
            var field = unit.FindField(group, index + 1);
            var measurement = field?.Measurement == null ? null : measurements.GetValueOrDefault(field.Measurement);
            var label = field?.Label ?? measurement?.Label;
            var applicability = field?.Applicability != "present" ? field?.Applicability : measurement?.Applicability;
            var formatted = FormatValue(value, measurement, block is RawDataReadResponseBlock);
            var annotation = applicability is null or "present" ? "" : $" [{applicability}]";
            return label == null ? formatted : $"{label}{annotation}: {formatted}";
        }));
    }

    internal static List<string>? Values(Block block) => block switch
    {
        GroupReadResponseBlock reading => reading.SensorValues.Select(x => x.ToString()).ToList(),
        RawDataReadResponseBlock raw => raw.Body.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
        _ => null
    };

    internal static string Measurement(SelectedMeasurement selected, Block block)
    {
        if (block is RawDataReadResponseBlock && selected.Definition.ReadFrom!.Group != 0)
            return $"{selected.Path}: unavailable (unverified raw response layout: {block})";
        var values = Values(block);
        int index = selected.Definition.ReadFrom!.Position - 1;
        if (values == null || index >= values.Count)
            return $"{selected.Path} ({selected.Definition.Label}): unavailable (unexpected response or missing field)";
        return $"{selected.Path} ({selected.Definition.Label}): {FormatValue(values[index], selected.Definition, block is RawDataReadResponseBlock)}";
    }

    private static string FormatValue(string value, MeasurementDefinition? definition, bool raw)
    {
        if (definition != null && definition.States.TryGetValue(value, out var state)) value += $" ({state})";
        return raw ? value + " [raw]" : value;
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
        bool useBasicSetting = false)
    {
        var request = Request(group, useBasicSetting);
        // Some controllers return a text description before the values on a subsequent request.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            send(new List<byte>(request));
            var block = receive();
            if (block is not GroupReadResponseWithTextBlock text) return block;
            rememberText?.Invoke(text);
        }
        throw new InvalidOperationException($"Group {group:D3} returned text repeatedly without measurement values.");
    }

    internal static void Sweep(IReadOnlyList<SelectedMeasurement> selected, Func<byte, Block> read, Action<string> output, Func<bool>? cancelled = null)
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
            foreach (var item in group) output($"{timestamp} {DiagnosticFormatter.Measurement(item, block)}");
        }
    }
}

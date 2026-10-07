using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BitFab.KW1281Test.Blocks
{
    internal class GroupReadResponseWithTextBlock : Block
    {
        public GroupReadResponseWithTextBlock(List<byte> bytes)
            : base(bytes)
        {
            var bodyBytes = new List<byte>(Body);
            while (bodyBytes.Count > 2)
            {
                var subBlockHeader = bodyBytes.Take(3).ToArray();
                bodyBytes = bodyBytes.Skip(3).ToList();

                int subBlockBodyLength = subBlockHeader[2];
                if (bodyBytes.Count < subBlockBodyLength)
                {
                    throw new InvalidOperationException(
                        $"{nameof(GroupReadResponseWithTextBlock)} body ({Utils.DumpBytes(Body)}) contains extra bytes after sub-blocks.");
                }

                var subBlock = new SubBlock
                {
                    BlockType = subBlockHeader[0],
                    Data = subBlockHeader[1],
                    Body = bodyBytes.Take(subBlockBodyLength).ToArray()
                };
                bodyBytes = bodyBytes.Skip(subBlockBodyLength).ToList();

                SubBlocks.Add(subBlock);
                
                if (subBlock.BlockType == 0x8D)
                {
                    var text = Encoding.ASCII.GetString(subBlock.Body, 0, subBlock.Body.Length);
                    _text = text.Split((char)0x03).ToList();
                }
            }

            if (bodyBytes.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(GroupReadResponseWithTextBlock)} body ({Utils.DumpBytes(Body)}) contains extra bytes after sub-blocks.");
            }
        }

        private readonly List<string> _text = new();

        internal bool MatchesValueCount(int count) => SubBlocks.Count == count;

        internal bool TryGetValue(int index, IReadOnlyList<byte> values, out string formatted)
        {
            formatted = "";
            if (!MatchesValueCount(values.Count) || index < 0 || index >= values.Count) return false;
            var descriptor = SubBlocks[index];
            byte value = values[index];
            if (descriptor.BlockType is 0x8B or 0x8C or 0x93)
            {
                // The ECU provides 17 map points at raw values 0, 16, ... 256.
                if (descriptor.Body.Length != 17) return false;
                int point = value / 16;
                double mapped = descriptor.Body[point] +
                    (descriptor.Body[point + 1] - descriptor.Body[point]) * (value % 16) / 16.0;
                double result = descriptor.BlockType == 0x8B ? mapped * descriptor.Data : mapped - descriptor.Data;
                string unit = descriptor.BlockType switch { 0x8B => "rpm", 0x8C => "°C", _ => "%" };
                formatted = result.ToString(descriptor.BlockType == 0x8B ? "F0" : "F1", CultureInfo.InvariantCulture) + " " + unit;
                return true;
            }
            if (descriptor.BlockType == 0x8D)
            {
                if (descriptor.Body.Length == 0) return false;
                var strings = Encoding.ASCII.GetString(descriptor.Body).Split((char)0x03);
                if (value >= strings.Length || strings[value].Length == 0) return false;
                formatted = strings[value];
                return true;
            }
            // Other descriptor formulas use their parameter as A and the following raw byte as B.
            if (descriptor.Body.Length != 0) return false;
            var sensor = new SensorValue(descriptor.BlockType, descriptor.Data, value);
            if (!sensor.HasKnownFormula) return false;
            formatted = sensor.ToString();
            return true;
        }

        internal bool TryGetStatusValue(int index, byte value, out byte status)
        {
            status = value;
            if (index < 0 || index >= SubBlocks.Count || SubBlocks[index].BlockType is not (0x10 or 0x88)) return false;
            status = (byte)(value & SubBlocks[index].Data);
            return true;
        }

        public string GetText(int i)
        {
            if (i >= 0 && i < _text.Count)
            {
                return $"\"{_text[i]}\"";
            }

            return i.ToString();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var subBlock in SubBlocks)
            {
                sb.Append(subBlock.ToString());
            }

            return sb.ToString();
        }

        readonly List<SubBlock> SubBlocks = new();

        class SubBlock
        {
            public byte BlockType { get; init; }

            public byte Data { get; init; }

            public byte[] Body { get; init; } = Array.Empty<byte>();

            public override string ToString()
            {
                switch(BlockType)
                {
                    case 0x8D:
                        return $"(${BlockType:X2} ${Data:X2} {Encoding.ASCII.GetString(Body, 0, Body.Length).Replace((char)0x03, '|')})";

                    default:
                        return $"(${BlockType:X2} ${Data:X2}{Utils.Dump(Body)})";
                }
            }
        }
    }
}

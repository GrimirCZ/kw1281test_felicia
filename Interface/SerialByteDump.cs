using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BitFab.KW1281Test.Interface;

/// <summary>Records serial traffic before protocol parsing, including reads used to discard echoes.</summary>
internal sealed class SerialByteDump : IDisposable
{
    private readonly TextWriter _writer;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly object _gate = new();
    private long _sequence;
    private long _lastFlush;

    internal SerialByteDump(string path) : this(OpenFile(path)) { }

    internal SerialByteDump(TextWriter writer)
    {
        _writer = writer;
        _writer.WriteLine("# KW1281TEST byte dump v1");
        _writer.WriteLine($"# UTC start {DateTimeOffset.UtcNow:O}");
        _writer.WriteLine("# sequence\telapsed_us\tkind\tvalue");
        _writer.Flush();
    }

    internal void WriteSessionInfo(IReadOnlyList<string> commandLine, IReadOnlyList<string> command,
        string port, int baudRate, int controllerAddress, string? profileId, string? profileSource)
    {
        lock (_gate)
        {
            _writer.WriteLine($"# Command line: {QuoteArguments(commandLine)}");
            _writer.WriteLine($"# Command: {QuoteArguments(command)}");
            _writer.WriteLine($"# Working directory: {Quote(Environment.CurrentDirectory)}");
            _writer.WriteLine($"# Port: {Quote(port)}");
            _writer.WriteLine(FormattableString.Invariant($"# Initial baud rate: {baudRate}"));
            _writer.WriteLine(FormattableString.Invariant($"# Initial controller address: 0x{controllerAddress:X2}"));
            _writer.WriteLine($"# Profile: {(profileId == null ? "none" : Quote(profileId))}");
            _writer.WriteLine($"# Profile source: {(profileSource == null ? "none" : Quote(profileSource))}");
            _writer.Flush();
        }
    }

    private static string Quote(string value) => $"\"{JsonEncodedText.Encode(value)}\"";
    private static string QuoteArguments(IReadOnlyList<string> args) => $"[{string.Join(", ", args.Select(Quote))}]";

    private static TextWriter OpenFile(string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream,
                new UTF8Encoding(false), 65536);
            // Separate sessions even when an interrupted capture left an unfinished line.
            if (stream.Length > 0) writer.WriteLine();
            return writer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Unable to append to byte dump '{path}': {ex.Message}", ex);
        }
    }

    internal IInterface Wrap(IInterface inner, int baudRate)
    {
        Record("BAUD", baudRate.ToString(CultureInfo.InvariantCulture));
        return new DumpInterface(inner, this);
    }

    private void Record(string kind, string value)
    {
        lock (_gate)
        {
            long elapsed = Stopwatch.GetElapsedTime(_started).Ticks / 10;
            _writer.WriteLine(FormattableString.Invariant($"{++_sequence}\t{elapsed}\t{kind}\t{value}"));
            // Buffer individual bytes and avoid periodic disk flushes during the bit-banged wakeup.
            if ((kind is "RX" or "TX" or "ERROR") && elapsed - _lastFlush >= 1_000_000)
            {
                _writer.Flush();
                _lastFlush = elapsed;
            }
        }
    }

    private void Flush() { lock (_gate) _writer.Flush(); }
    public void Dispose() { lock (_gate) _writer.Dispose(); }

    private sealed class DumpInterface(IInterface inner, SerialByteDump dump) : IInterface
    {
        public int DefaultTimeoutMilliseconds => inner.DefaultTimeoutMilliseconds;
        public int ReadTimeout { get => inner.ReadTimeout; set => inner.ReadTimeout = value; }
        public int WriteTimeout { get => inner.WriteTimeout; set => inner.WriteTimeout = value; }

        public byte ReadByte()
        {
            byte value;
            try { value = inner.ReadByte(); }
            catch (Exception ex) { dump.Record("ERROR", $"RX {ex.GetType().Name}"); throw; }
            dump.Record("RX", value.ToString("X2", CultureInfo.InvariantCulture));
            return value;
        }

        public void WriteByteRaw(byte value)
        {
            try { inner.WriteByteRaw(value); }
            catch (Exception ex) { dump.Record("ERROR", $"TX {ex.GetType().Name}"); throw; }
            dump.Record("TX", value.ToString("X2", CultureInfo.InvariantCulture));
        }

        public void ClearReceiveBuffer()
        {
            inner.ClearReceiveBuffer();
            dump.Record("CLEAR_RX", "");
            dump.Flush();
        }

        public void SetBaudRate(int baudRate) { inner.SetBaudRate(baudRate); dump.Record("BAUD", baudRate.ToString(CultureInfo.InvariantCulture)); }
        public void SetParity(Parity parity) { inner.SetParity(parity); dump.Record("PARITY", parity.ToString()); }
        public void SetBreak(bool on) { inner.SetBreak(on); dump.Record("BREAK", on ? "1" : "0"); }
        public void SetDtr(bool on) { inner.SetDtr(on); dump.Record("DTR", on ? "1" : "0"); }
        public void SetRts(bool on) { inner.SetRts(on); dump.Record("RTS", on ? "1" : "0"); }

        public void Dispose()
        {
            try { inner.Dispose(); }
            finally { dump.Record("CLOSE", ""); dump.Flush(); }
        }
    }
}

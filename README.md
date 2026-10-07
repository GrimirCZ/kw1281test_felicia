# kw1281test
VW KW1281 Protocol Test Tool

This tool can send some KW1281 (and a few KW2000) commands over a dumb serial->KKL or USB->KKL cable.
If you have a legacy Ross-Tech USB cable, you can probably use that cable by
installing the Virtual COM Port drivers: https://www.ross-tech.com/vag-com/usb/virtual-com-port.php
Functionality includes reading/writing the EEPROMs of VW MKIV Golf/Jetta/Beetle/Passat instrument clusters and Comfort Control Modules, reading and clearing fault codes, changing the software coding of modules, performing an actuator test of various modules and retrieving the SAFE code of the Delco Premium V radio.

The tool is written in C#, targetting .NET 10.0 and runs under Windows 10/11 (most serial ports), macOS and Linux (macOS/Linux need an FTDI serial port and D2xx drivers). It may also run under
Windows 10/11.

You can download a precompiled version for Windows, macOS and Linux from the Releases page: https://github.com/gmenounos/kw1281test/releases/

Otherwise, here's how to build it yourself:

##### Compiling the tool

1. You will need the .NET Core SDK,
which you can find here: https://dotnet.microsoft.com/download
(Click on the "Download .NET Core SDK" link and follow the instructions) or Microsoft Visual Studio
(free Community Edition here: https://visualstudio.microsoft.com/vs/community/)

2. Download the source code: https://github.com/gmenounos/kw1281test/archive/master.zip
and unzip it into a folder on your computer.

3. Open up a command prompt on your computer and go into the folder where you unzipped
the source code. Type `dotnet build` to build the tool.
Or, load up the project in Visual Studio and Ctrl-Shift-B.

4. You can run the tool by typing `dotnet run`

```
Usage: KW1281Test PORT BAUD ADDRESS COMMAND [args]
                
PORT = COM1|COM2|etc. (Windows)
    /dev/ttyXXXX (Linux)
    AABBCCDD (macOS/Linux FTDI cable serial number)
BAUD = 10400|9600|etc.
ADDRESS = Controller address, e.g. 1 (ECU), 17 (cluster), 46 (CCM), 56 (radio)
COMMAND =
    ActuatorTest
    AdaptationRead CHANNEL [LOGIN]
        CHANNEL = Channel number (0-99)
        LOGIN = Optional login (0-65535)
    AdaptationSave CHANNEL VALUE [LOGIN]
        CHANNEL = Channel number (0-99)
        VALUE = Channel value (0-65535)
        LOGIN = Optional login (0-65535)
    AdaptationTest CHANNEL VALUE [LOGIN]
        CHANNEL = Channel number (0-99)
        VALUE = Channel value (0-65535)
        LOGIN = Optional login (0-65535)
    AutoScan
    BasicSetting GROUP
        GROUP = Group number (0-255)
        (Group 0: Raw controller data)
    ClarionVWPremium4SafeCode
    ClearFaultCodes
    DelcoVWPremium5SafeCode
    DumpEdc15Eeprom [FILENAME]
        FILENAME = Optional filename
    DumpEeprom START LENGTH [FILENAME]
        START = Start address in decimal (e.g. 0) or hex (e.g. 0x0)
        LENGTH = Number of bytes in decimal (e.g. 2048) or hex (e.g. 0x800)
        FILENAME = Optional filename
    DumpEeprom FILENAME
        (For Airbag/Cluster address only) Dumps the whole EEPROM (size
         auto-detected from ReadIdent).
    DumpMarelliMem START LENGTH [FILENAME]
        START = Start address in decimal (e.g. 3072) or hex (e.g. 0xC00)
        LENGTH = Number of bytes in decimal (e.g. 1024) or hex (e.g. 0x400)
        FILENAME = Optional filename
    DumpMem START LENGTH [FILENAME]
        START = Start address in decimal (e.g. 8192) or hex (e.g. 0x2000)
        LENGTH = Number of bytes in decimal (e.g. 65536) or hex (e.g. 0x10000)
        FILENAME = Optional filename
    DumpRam START LENGTH [FILENAME]
        START = Start address in decimal (e.g. 8192) or hex (e.g. 0x2000)
        LENGTH = Number of bytes in decimal (e.g. 65536) or hex (e.g. 0x10000)
        FILENAME = Optional filename
    DumpRBxMem START LENGTH [FILENAME]
        START = Start address in decimal (e.g. 66560) or hex (e.g. 0x10400)
        LENGTH = Number of bytes in decimal (e.g. 1024) or hex (e.g. 0x400)
        FILENAME = Optional filename
    DumpRom START LENGTH [FILENAME]
        START = Start address in decimal (e.g. 8192) or hex (e.g. 0x2000)
        LENGTH = Number of bytes in decimal (e.g. 65536) or hex (e.g. 0x10000)
        FILENAME = Optional filename
    FindLogins LOGIN
        LOGIN = Known good login (0-65535)
    GetSKC
    GroupRead GROUP
        GROUP = Group number (0-255)
        (Group 0: Raw controller data)
    LoadEeprom START FILENAME
        START = Start address in decimal (e.g. 0) or hex (e.g. 0x0)
        FILENAME = Name of file containing binary data to load into EEPROM
    MapEeprom
    ReadFaultCodes
    ReadIdent
    ReadEeprom ADDRESS
        ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. 0x1109)
    ReadRAM ADDRESS
        ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. 0x1109)
    ReadROM ADDRESS
        ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. 0x1109)
    ReadSoftwareVersion
    Reset
    SetSoftwareCoding CODING WORKSHOP
        CODING = Software coding in decimal (e.g. 4361) or hex (e.g. 0x1109)
        WORKSHOP = Workshop code in decimal (e.g. 4361) or hex (e.g. 0x1109)
    ToggleRB4Mode
    WriteEdc15Eeprom ADDRESS1 VALUE1 [ADDRESS2 VALUE2 ... ADDRESSn VALUEn]
        ADDRESS = EEPROM address in decimal (0-511) or hex (0x00-0x1FF)
        VALUE = Value to be stored in decimal (0-255) or hex (0x00-0xFF)
    WriteEeprom ADDRESS VALUE
        ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. 0x1109)
        VALUE = Value in decimal (e.g. 138) or hex (e.g. 0x8A)
    WriteRAM ADDRESS VALUE
        ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. 0x1109)
        VALUE = Value in decimal (e.g. 138) or hex (e.g. 0x8A)
```

##### Credits
- Protocol Info: https://www.blafusel.de/obd/obd2_kw1281.html  
- VW Radio Reverse Engineering Info: https://github.com/mnaberez/vwradio  
- 6502bench SourceGen: https://6502bench.com/
- EDC15 flashing info and seed/key algorithm: https://github.com/fjvva/ecu-tool
- Contributions
    - [IJskonijn](https://github.com/IJskonijn)
    - [jpadie](https://github.com/jpadie)
    - [kerekt](https://github.com/kerekt)
    - [Olivier Fauchon](https://github.com/ofauchon)
    - [Jonathan Klamroth](https://github.com/jonnykl)
    - [Martin Sestak](https://github.com/poure-1)
    - [Dragonslab53](https://github.com/Dragonslab53)
    - [DiagProf](https://github.com/DiagProf)
    - [magna413](https://github.com/magna413)
## Optional vehicle profiles

Profiles add controller aliases, connection defaults, measurement names and DTC details. Existing numeric-address commands and value decoders remain available. The built-in `felicia-simos2p` profile targets a 1999 Felicia 1.3 MPI 50 kW with four injectors, A/C and immobilizer.

```sh
export KW1281TEST_PROFILE=felicia-simos2p
export KW1281TEST_PORT=/dev/ttyUSB0
export KW1281TEST_BAUD_RATE=9600

dotnet run -- ecu GroupRead 5
dotnet run -- ecu Sensors engine --once
dotnet run -- ecu Sensors engine.rpm,engine.temp,ac
dotnet run -- ecu ReadFaultCodes
dotnet run -- ProfileInfo
```

`--profile IDENTIFIER_OR_PATH` overrides `KW1281TEST_PROFILE`. Both accept an embedded identifier or a JSON file. Relative paths use the current directory. The tool checks the profile before opening the port. Without a profile, it uses the existing generic commands.

The example uses 9600 baud, confirmed on a Felicia ECU identified as `047906030C SIMOS 2P 7002`. The built-in profile defaults to 10400 baud. Keep the environment override for a controller that communicates at 9600.

Explicit positional connection arguments override environment defaults. Supported forms are `PORT BAUD ADDRESS COMMAND`, `PORT ADDRESS COMMAND` with a default baud, `BAUD ADDRESS COMMAND` with an environment port, and `ADDRESS COMMAND` with environment port and default baud. `BAUD=auto` uses the controller-specific profile rate, then the profile-wide rate. An omitted rate uses `KW1281TEST_BAUD_RATE`, then profile defaults. Addresses remain hexadecimal (`01`, `25`). Profile aliases such as `ecu`, `engine`, `immo` and `immobilizer` are case-insensitive. Use the full positional form if a port name is purely numeric.

```sh
dotnet run -- /dev/ttyUSB0 auto ecu GroupRead 5 --profile felicia-simos2p
dotnet run -- /dev/ttyUSB0 9600 25 ReadIdent --profile ./my-car.json
```

`GroupRead` displays every returned value, including fields marked disregard/not fitted and unknown fields. Names come from the profile's group and field definitions. Existing formulas determine the values and units. `Sensors` reads enabled measurements for the car. Select a category such as `engine` or `fuel.lambda`, or a single value such as `engine.rpm` or `engine.temp`. `all` selects the complete enabled tree. Use commas to select several paths. Each measurement appears once, and shared groups are read once per sweep. Each sample has a UTC timestamp. Groups are read one after another. Use `--once` for one sweep, or Q/Ctrl+C to stop continuous sampling. Sensors never performs basic settings.

Raw replies to numbered groups use the ECU's conversion header. The tool keeps each field's formula, coefficient and lookup table, then decodes the following bytes into values with units. `Sensors` keeps these headers between sweeps. Profile status definitions turn byte masks into labels such as `Idle`, `Idle switch closed`, `Sensor ready` and `Coolant below 80°C`. GroupRead shows one field per line in an interactive terminal and keeps each complete sample on one line in the log.

With a profile, `AutoScan` probes only controllers marked present. Explicit numeric-address access and generic no-profile AutoScan remain available. A controller marked present may still reject a command or fail to communicate.

`ReadFaultCodes` keeps the original code and status line and adds explanations from the profile. When a subtype is unverified, it lists possible failure modes. P-code references are approximate equivalents where indicated. See [the references and open questions](docs/felicia-simos2p-evidence.md) for details that still need testing.

### Serial byte dumps

Use `--dump FILE` to record serial traffic before protocol decoding. This works with every command and does not require a profile. It captures every byte read or successfully written through the serial interface, including wakeup replies, cable echoes, acknowledgements, conversion headers and raw measurement payloads.

```sh
dotnet run -- ecu Sensors all --once --dump ./sensors.kwdump
dotnet run -- ecu GroupRead 1 --dump ./group-1.kwdump
```

Set `KW1281TEST_DUMP` to a file path to enable the same capture through the environment. The command-line option overrides this variable. Relative paths use the current directory. Each run appends a new session to the file and preserves earlier captures. The file is created if it does not exist. `ProfileInfo` does not open the serial port or create a dump.

```sh
export KW1281TEST_DUMP=./felicia.kwdump
dotnet run -- ecu Sensors all --once
unset KW1281TEST_DUMP
```

Each session in the UTF-8 text file starts with a format version and UTC start time. Sequence numbers and elapsed times restart for each session. Each subsequent line has four tab-separated fields: sequence number, elapsed microseconds, event kind and value. `RX` and `TX` values are two-digit hexadecimal bytes. Received cable echoes are included as `RX` entries. `BAUD`, `PARITY`, `BREAK`, `DTR` and `RTS` record successful configuration changes. `ERROR` records failed reads and writes, and `CLOSE` marks disposal of the port. Elapsed times describe when the tool completed each operation, rather than electrical timing on the K-line.

The five-baud wakeup uses line breaks, so its bits appear as `BREAK` events rather than `TX` bytes. `CLEAR_RX` marks a receive-buffer purge. Bytes discarded by the driver during that purge are not available to the tool and cannot be captured. Writes are buffered and flushed during byte traffic about once per second, on a purge and when the port closes. Normal errors preserve the captured data, but forcibly terminating the process can lose the buffered tail. The usual `KW1281Test.log` remains available alongside the byte dump.

For conversion debugging, send the `.kwdump` file from `Sensors all --once` together with the normal log. That supplies the ECU's conversion headers as well as the measurement bytes missing from earlier console exports.

### Linux serial-port permissions

Run the tool as your own user. `sudo` commonly removes exported environment variables, including `KW1281TEST_PROFILE`, `KW1281TEST_PORT` and `KW1281TEST_BAUD_RATE`.

Connect the cable and check the device's group and your current group memberships:

```sh
ls -l /dev/ttyUSB0
id -nG
```

Serial devices usually belong to `dialout` on Debian and Ubuntu, or `uucp` on Arch Linux. The device must allow its group to read and write. For a device owned by `dialout`, add your user to that group:

```sh
sudo usermod -aG dialout "$USER"
```

Use `uucp` instead if that is the device's group. Log out and log back in, or reboot, so the new group membership takes effect. Open a shell as your own user and run `id -nG` again to confirm membership. Export the connection variables again in the new session, or add their exports to your shell's startup file.

Check profile selection without connecting to the car, then read the ECU identification without `sudo`:

```sh
dotnet run -- ProfileInfo
dotnet run -- ecu ReadIdent
```

`ProfileInfo` should print `Profile: felicia-simos2p`. For a published executable, replace `dotnet run --` with `./kw1281test`. If the device already grants your user read and write access, no group change is needed.

On a Linux access-denied error, the tool prints a permission fix command. When the device group grants read and write access, the command adds your user to that group. If the group cannot be used, it suggests temporary access with `setfacl`, which is provided by the `acl` package on Debian and Ubuntu. Temporary access applies to the current device and may need to be granted again after reconnecting the cable.

### Extending a profile

Start with [the built-in JSON](Profiles/felicia-simos2p.json) and [the schema](Profiles/vehicle-profile.schema.json). The loader also checks aliases, measurement references, duplicate subtypes and comment references. No additional package is needed, and the built-in profile is embedded in published executables.

Each controller has a nested `measurements` tree. A measurement supplies a label and its preferred group and field. Field positions start at 1. Entries in `groups` reference these paths when a measurement appears in more than one group:

```json
{
  "measurements": {
    "engine": {
      "comment": "Engine measurements",
      "rpm": {
        "label": "Engine speed",
        "readFrom": { "group": 5, "position": 1 },
        "comment": "Shown by both GroupRead and Sensors"
      }
    }
  },
  "groups": {
    "5": [{ "position": 1, "measurement": "engine.rpm" }]
  }
}
```

New categories and measurements become selectable without code changes. Names cannot contain dots. The names `all`, `comment` and `fieldComments` are reserved. Use `applicability` (`present`, `notFitted`, `disregard`, `unresolved`) and `includeInSensors` to control Sensors inclusion. GroupRead never filters returned fields. Numbered raw groups use the ECU conversion header for their values and units. A missing or unsupported conversion stays visible with the field name and an explanation.

Any definition object can contain `comment`. Primitive fields can be annotated through their containing object's `fieldComments`, for example `{"baudRate":10400,"fieldComments":{"baudRate":"Connection default"}}`. `ProfileInfo` prints all definitions and comments, including excluded fields. Comments are for the reader. Settings must have JSON values that the tool can read. Keep source references in separate documentation. A `states` map matches the existing decoder's exact output. Verify numeric DTC `subtypes` before adding them.

A controller can declare `rawGroupLengths`, for example `{"0":10,"5":4}`, to validate raw field counts. A measurement or group field can define `raw.scale`, `raw.offset` and `raw.decimals` for a fixed byte conversion. The result is `byte * scale + offset`, displayed with the measurement's `unit`. A group-field definition overrides its measurement's raw definition. The Felicia profile uses these fixed scales only for group 000, which has no conversion header. ECU conversions take priority whenever a header is available.

A measurement's `raw.bits` describes status masks. Each entry has a one-bit `mask`, text for `set`, and optional text for `clear`. `raw.zero` supplies text when no flags are displayed. Unknown bits remain visible. For example:

```json
{
  "raw": {
    "bits": [{ "mask": 4, "set": "Idle", "comment": "Operating-state bit" }],
    "zero": "No operating mode reported"
  }
}
```

Raw definitions and their comments also appear in ProfileInfo. The profile loader validates field counts, finite scales, decimal precision and distinct bit masks before opening the port.

# Felicia Simos 2P references and open questions

This profile is for a 1999 Felicia 1.3 MPI 50 kW hatchback with four injectors, A/C and an immobilizer. The car has no ABS, power steering or central locking. Select the profile by name or file path. Vehicle exports confirm communication at 9600 baud with ECU `047906030C SIMOS 2P 7002`. The updated decoder has been tested with recorded payloads and protocol examples. It still needs a run on the car.

## References

The main reference is the supplied **Simos_2P.pdf**, the Skoda Felicia workshop manual with the 05.99 and 11.99 supplements. A copy is available at <https://milos.wrp.cz/rock/felicia/Simos_2P.pdf>. Page numbers below refer to the printed pages. The measurement tables were checked against the PDF images as well as the extracted text.

The supplied **felicia_simos2p_vag_dtc_map.csv** contains 52 fault variants across 21 VAG codes. It also cites <https://www.transporterclub.cz/media/kunena/attachments/11974/Chybove-kdy-VW-koda-Octavia-Fabia.pdf>. That table and the Ross-Tech pages were blocked by the development environment's network policy. Their claims still need checking. The supplied workshop manual was used to check the CSV against this engine.

Value decoding uses the existing `Blocks/SensorValue.cs`, `Blocks/FaultCodesBlock.cs` and `KW1281Dialog.cs`. Upstream references include <https://www.blafusel.de/obd/obd2_kw1281.html#7> and <https://github.com/gmenounos/kw1281test>. These describe value encoding. Field names come from the engine's measurement tables. Header/body encoding was checked against [KLineKWP1281Lib](https://github.com/domnulvlad/KLineKWP1281Lib/blob/84f90a9a1dc28d8706187fdbf9144e60799e6afe/src/KLineKWP1281Lib.cpp), especially `readGroup`, `getMeasurementValueFromHeaderBody` and `getMeasurementTextFromHeaderBody`. That source was accessible in the development environment.

References are kept here. The profile JSON contains settings, diagnostic data and comments about how to use them.

## Measurement coverage

| Printed page | Groups | Contents |
| --- | --- | --- |
| 01-20 (11.99) | 000 | Ten raw fields. Pressure is field 4. Fixed scales come from the displayed raw and physical ranges. Idle adaptation conversion remains unresolved. |
| 01-21 | 001-006 | RPM, coolant temperature, lambda voltage, status, throttle angle and duty, injection duration, battery voltage, intake temperature and altitude correction. |
| 01-22 | 007-012 | Operating states, idle regulation, lambda regulation and adaptation, EVAP duty and status. |
| 01-23 | 015-017, 019-021 | Knock values for cylinder pairs, A/C and compressor states, lambda status and stored throttle positions. |
| 01-24 | 097-099 | Throttle and positioner endpoint voltages, adaptation states and lambda values. |
| 01-24 to 01-25 | Status words | Status masks and readable labels. The lambda mixture-bit direction remains unresolved. |

The manual omits groups 013, 014 and 018 from the repair overview, so the profile has no definitions for them. Fields marked “neuvažovat” (disregard) stay visible in GroupRead. Sensors excludes them. These include air-mass, air-consumption and vehicle-speed slots. A disregarded field does not by itself prove that a component is absent.

Each measurement has one preferred group and field for Sensors. This avoids reading repeated values when selecting a category or all. GroupRead still labels their other appearances. Numbered groups can return a conversion header (`0x02`) followed by four raw measurement bytes (`0xF4`). Each header entry supplies a formula, coefficient and optional data table. The tool applies each entry to the corresponding byte. Formulas `0x8B`, `0x8C` and `0x93` interpolate the ECU's 17-point tables for RPM, temperature and percentage. Formula `0x8D` selects a string from its own table. Numeric formulas use the existing sensor decoder and the added header formulas. Headers are retained between Sensors sweeps and reset when GroupRead changes group. A header never consumes a measurement byte as a general text index.

Group 000 has ten fixed raw fields and no conversion header. The profile defines its documented temperature, voltage, pressure, angle and injection scales. The 32-rpm fallback follows the manual's rounded idle range and should be compared with a numbered group on the car. Idle adaptation remains a named raw value pending conversion. Group 000 did not provide a usable MAP reading in the supplied Sensors export. The tool reports that field as unavailable instead of inferring a failed or absent pressure sensor.

Groups 015-017 report knock values for cylinder pairs 1/4 and 2/3. They cannot identify the individual cylinder. Four displayed amplitudes do not imply four separate knock sensors. Group 019 fields 3 and 4 use 0=off and 1=on. Operating state, adjustment conditions, throttle status, EVAP status, lambda status and adaptation state have JSON bit definitions. Header byte masks are applied before decoding status labels. Unknown flags remain visible. The manual gives conflicting mixture-bit directions for group 000 and groups 020/099, so that bit is labelled without assigning rich or lean. Group 097 field 2 may display the 5 V endpoint as 0 V. ProfileInfo includes that note.

Sensors reads measuring blocks, including groups 097 and 098. It does not start basic settings or throttle adaptation.

## Fault codes

* Pages 01-6 to 01-14 list faults, possible causes and checks. They usually do not give numeric subtypes. Built-in variants have empty `subtypes` arrays and are shown as possible failure modes. Custom profiles can supply verified subtype numbers.
* Fault 01249 calls for checking N30/N33 on cylinders 1/4. Fault 01250 calls for checking N31/N32 on cylinders 2/3. Pages 01-13 and 01-14 describe these pairs. The CSV's separate-cylinder descriptions and P0201/P0202 equivalents were removed for these outputs.
* The manual adds eight codes missing from the CSV: 00533, 00609, 00610, 00624, 00635, 01126, 01177 and 01242. A/C fault 00624 does not establish a separate diagnostic address for the A/C controller.
* The manual identifies the engine ECU as J361. This replaces the CSV's J382 identifier on code 65535.
* The reviewed manual does not establish G40/00515 or separate outputs 01251/01252. Those entries and 65535 remain available to explain an actually returned code, with their applicability marked unresolved. They do not add sensors to the profile.
* The P-code equivalents for 00537 were removed. The regulation direction needs checking before assigning a lean or rich interpretation.
* Other CSV P-codes are approximate equivalents for reference. The ECU reports VAG codes. Code 17978/P1570 is a manufacturer-specific reference.
* The exact no-fault marker is code 65535 with status 0x88. Other statuses for 65535 remain visible.
* Engine fault definitions apply to address 01. Address 25 uses generic immobilizer fault output until its definitions are documented.

## Still to check

1. Check the connection rate for other ECU variants and the immobilizer. The supplied engine exports use 9600 baud. A numeric CLI rate or KW1281TEST_BAUD_RATE overrides the profile default.
2. Verify group 000 support and compare its fixed scales with numbered groups. Complete the idle adaptation conversion.
3. Resolve the lambda mixture-bit direction and compare decoded status labels during operation.
4. Verify numeric fault subtypes, the remaining P-code equivalents and unresolved component applicability.
5. Test which documented groups the ECU supports. NAK responses are reported as unavailable.

The regression fixture contains 17 group payloads and all 198 Group 001 samples from the supplied exports. Those console exports do not include conversion headers. Tests combine the recorded payloads with explicit protocol-example headers, including different numeric formulas, tables and strings. They exercise both commands through the dialog, header caching, missing or malformed headers, status masks, field preservation and Linux permission errors. Example decoded temperatures in tests are not a reconstruction of the car's actual temperatures. A new capture from the updated tool is needed to verify the ECU-provided tables on the car. Run `Sensors all --once --dump ./sensors.kwdump` with the configured profile, port and baud rate to capture those headers together with the raw replies. Keep the normal log alongside the byte dump.

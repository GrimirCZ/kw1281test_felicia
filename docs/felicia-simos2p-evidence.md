# Felicia Simos 2P references and open questions

This profile is for a 1999 Felicia 1.3 MPI 50 kW hatchback with four injectors, A/C and an immobilizer. The car has no ABS, power steering or central locking. Select the profile by name or file path. It has not been tested on the car.

## References

The main reference is the supplied **Simos_2P.pdf**, the Skoda Felicia workshop manual with the 05.99 and 11.99 supplements. A copy is available at <https://milos.wrp.cz/rock/felicia/Simos_2P.pdf>. Page numbers below refer to the printed pages. The measurement tables were checked against the PDF images as well as the extracted text.

The supplied **felicia_simos2p_vag_dtc_map.csv** contains 52 fault variants across 21 VAG codes. It also cites <https://www.transporterclub.cz/media/kunena/attachments/11974/Chybove-kdy-VW-koda-Octavia-Fabia.pdf>. That table and the Ross-Tech pages were blocked by the development environment's network policy. Their claims still need checking. The supplied workshop manual was used to check the CSV against this engine.

Value decoding uses the existing `Blocks/SensorValue.cs`, `Blocks/FaultCodesBlock.cs` and `KW1281Dialog.cs`. Upstream references include <https://www.blafusel.de/obd/obd2_kw1281.html#7> and <https://github.com/gmenounos/kw1281test>. These describe value encoding. Field names come from the engine's measurement tables.

References are kept here. The profile JSON contains settings, diagnostic data and comments about how to use them.

## Measurement coverage

| Printed page | Groups | Contents |
| --- | --- | --- |
| 01-20 (11.99) | 000 | Ten raw fields. Pressure is field 4. Conversion formulas still need checking. |
| 01-21 | 001-006 | RPM, coolant temperature, lambda voltage, status, throttle angle and duty, injection duration, battery voltage, intake temperature and altitude correction. |
| 01-22 | 007-012 | Operating states, idle regulation, lambda regulation and adaptation, EVAP duty and status. |
| 01-23 | 015-017, 019-021 | Knock values for cylinder pairs, A/C and compressor states, lambda status and stored throttle positions. |
| 01-24 | 097-099 | Throttle and positioner endpoint voltages, adaptation states and lambda values. |
| 01-24 to 01-25 | Status words | Meanings of displayed status positions. Their correspondence to KW1281 bytes still needs testing. |

The manual omits groups 013, 014 and 018 from the repair overview, so the profile has no definitions for them. Fields marked “neuvažovat” (disregard) stay visible in GroupRead. Sensors excludes them. These include air-mass, air-consumption and vehicle-speed slots. A disregarded field does not by itself prove that a component is absent.

Each measurement has one preferred group and field for Sensors. This avoids reading repeated values when selecting a category or all. GroupRead still labels their other appearances. Group 000 displays all ten bytes as raw values. Raw or text responses to numbered groups keep their original display because their field layout is unverified.

Groups 015-017 report knock values for cylinder pairs 1/4 and 2/3. They cannot identify the individual cylinder. Four displayed amplitudes do not imply four separate knock sensors. Group 019 fields 3 and 4 use 0=off and 1=on when the existing decoder returns those values. Other status words remain raw. Group 097 field 2 may display the 5 V endpoint as 0 V. ProfileInfo includes that note.

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

1. Verify the 10400-baud default, especially for the immobilizer. A numeric CLI rate or KW1281TEST_BAUD_RATE overrides it.
2. Check conversion formulas for group 000 before displaying physical units.
3. Confirm the byte and bit layout of status fields before decoding them.
4. Verify numeric fault subtypes, the remaining P-code equivalents and unresolved component applicability.
5. Test which documented groups the ECU supports. NAK responses are reported as unavailable.

The automated tests cover software behavior. They do not verify communication with the car or its wire encodings.

# Performance Checker Design

## Goal

This tool is intended to collect evidence for intermittent application crashes and to provide a repeatable baseline for comparing an old PC with a replacement PC.

## Measurement policy

### One-time host snapshot

- Windows version / architecture
- CPU model, physical cores, logical processors, maximum clock
- GPU model, driver version, VRAM information
- Physical disk model, interface, media type, capacity, status
- Logical drive capacity and free space
- Total physical memory

### Periodic sample

Default interval: 60 seconds.

- CPU usage
- CPU package temperature
- CPU package power when exposed by the hardware monitor
- Top 3 processes by CPU usage
- Top process working-set memory
- System memory usage
- Network receive/send bytes per second
- GPU utilization
- GPU temperature
- GPU memory utilization when exposed
- GPU power when exposed
- Physical disk read/write throughput and busy percentage

## Important diagnostic extensions

The next implementation phase should collect Windows Event Log evidence because resource counters alone cannot establish the cause of a crash.

Priority event sources:

- `WHEA-Logger`
- `Application Error`
- `Application Hang`
- `Display`
- `nvlddmkm`
- `Disk`
- `Ntfs`
- `stornvme`
- `Kernel-Power`

For the Intel 13th/14th generation stability investigation, WHEA events should be treated as high-value evidence. The tool must not label a CPU as defective from temperature or utilization alone.

## YAML format

The generated file is `performance-log.yaml` beside the executable.

The first lines are comments explaining the schema to an AI. This is intentional: the entire file can be pasted into ChatGPT or another LLM for analysis without an additional schema document.

The document contains:

- `schemaVersion`
- `startedAt`
- `endedAt`
- `host`
- `samples`
- `summary`

Missing hardware sensor values are represented as YAML `null` rather than fabricated values.

## Controls

- `L`: launch a separate `notepad.exe` process for the current YAML
- `S`: write an immediate sample
- `Q`: stop monitoring
- `Ctrl+C`: stop monitoring

## Design principles

1. Never fabricate unavailable sensor values.
2. Preserve timestamps for correlation.
3. Keep the raw measurements machine-readable.
4. Add derived summary values without replacing raw samples.
5. Keep the same schema across old and new PCs.
6. Prefer Windows-native data for OS/process/network/disk counters and LibreHardwareMonitor for hardware sensors.
7. Run as administrator when hardware sensors require elevated access.

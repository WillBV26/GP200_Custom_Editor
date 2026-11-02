# GP200 Custom Editor

Independent Windows editor for the Valeton GP-200.
Supports **Stock** and **Custom** firmware profiles as Custom https://github.com/Goaltoday/GP-200-FIRMWARE-MODS
Choose on Setting/Device profile 
This project is not an official Valeton product and is not endorsed by the hardware manufacturer.
**Yes, it's vibecoded, but with a lot of human effort...**

<img width="1349" height="872" alt="image" src="https://github.com/user-attachments/assets/20e8a7c1-31bc-4ddb-8114-2d00b086231e" />



## Features

- Hecho para poder aprovechar al máximo el custom firmware.
- Live effect editing, parameters, bypass, insert-based block ordering, Undo/Redo and double-click reset.
- Preset library, Save and Save As, import/export, backup and verified device readback.
- Device connection prefers the main Valeton/GP-200 MIDI endpoint, falls back to a matching secondary endpoint or the first available MIDI port, and opens the active preset first; reading all 256 saved slots is on demand.
- Manage signal chain.
- Scenes A–D, BASE, recall/capture, pending changes, direct 4-Scene pedal write (`⚡ Escribir Preset y 4 Escenas en GP-200`), and physical SFU1 footswitch mapping.
- Native NAM loading to 32 stored slots, maximum one selected NAM block per preset, including bypassed blocks.
- NAM→CLO SnapTone conversion (`NamToCloBridgeService`) with Tone Match, optional corrective IR and Convert only. Automatically detects external converters and synthesizes byte-exact 8840-byte VTSI SnapTone `.clo` models.
- IR loading with user PC library scanner (`LocalToneLibraryService`) for `.wav`, `.nam`, and `.clo` assets.
- Integrated Community Tone Search (`ToneSearchService`) with 1-click links to TONE3000 (`https://www.tone3000.com/search?q=...`) and ToneHunt.
- Local DSP Spectral & Dynamic Audio Analyzer (`AudioToneAnalyzer`) extracting crest factor (Clean/Crunch/High-Gain), 6 spectral frequency bands, centroid, and BPM from audio files or live WASAPI guitar input. Reference audio stays local unless the user opts in and confirms a Gemini upload; video containers are never uploaded.
- Local BS.1770 momentary and gated-integrated LUFS meter with confirmed, backup-protected batch matching (Normal -16 LUFS / Lead -13 LUFS) and a restorable Stock-profile working slot.
- Mod Capability Scanner (`ModCapabilityScanner`) reporting negotiated USB extensions and possible static firmware markers; static signatures are heuristic only and do not prove a mod is installed or active.
- Real-provider AI preset generation/refinement with Gemini, Anthropic, or OpenRouter; no local synthetic fallback. Gemini defaults to `gemini-2.5-flash-lite`, and an explicitly selected model is never silently replaced. API keys may use environment variables (`GP200_AI_API_KEY`, `GEMINI_API_KEY`, `ANTHROPIC_API_KEY`, `OPENROUTER_API_KEY`) or optional Windows DPAPI-protected storage in the current user's LocalAppData.
- AI studio includes a persistent Generate/Refine action, scrollable tabs, readable selected-tab colors, local `.prst` opening for refinement with a source/result change summary, an optional AI-selected set size of one to four song presets, song/video URLs as web-search context, audio-file analysis with explicit Gemini upload consent, cited search links, Patch/Stomp/User/Scenes targets, selected set-list upload/export, CTRL editing, and batch volume matching. Provider model choices stay empty until the user validates the API key; the editor then queries that provider for account-accessible models. Prompt chips are examples, not bundled AI result presets; the real provider creates each proposal.
- Built-in Automated QA Self-Test Harness (`GP200 Replica.exe --self-test`) covering MIDI enumeration, dual-chain topology, DSP, loudness matching, encrypted key storage, and AI policy. Live Gemini requests and physical USB connections are skipped unless `GP200_RUN_LIVE_AI_TEST=1` or `GP200_RUN_LIVE_HARDWARE_TEST=1` are explicitly set.


**Firmware updates and factory reset are excluded**

## Images

The source includes AI images in order to not distribute all original images.
You can copy from the Valeton official images or create yours.

## Download.
You can build it from the code or download from release.

https://github.com/Goaltoday/GP200_Custom_Editor/releases/tag/0.1

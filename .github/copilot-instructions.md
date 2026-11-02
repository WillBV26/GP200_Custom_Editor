# GP-200 Community Editor project instructions

## Continue requested work

- For ongoing user-requested feature batches, keep a visible backlog of the ideas from the supplied conversation and what is already implemented. Finish and validate one coherent change, then immediately start the next independent actionable item in the same response; do not stop after a single feature or ask whether to continue.
- Before marking an item done, verify the requested behavior with the smallest meaningful test/build and update directly related documentation. Keep moving through the backlog until all actionable items are implemented or a concrete blocker requires user input.
- At each phase transition, briefly report progress in Spanish when the user is communicating in Spanish, alongside the next tool action; do not send progress-only messages.
- Never claim that a conversation, link, hardware behavior, or feature was verified unless it was actually inspected or tested. Track inaccessible sources as blockers and continue with other verified work.

## Hardware and data safety

- Do not write firmware, update firmware, reset the device, or invent undocumented GP-200 protocol capabilities.
- Keep pre-write backups and existing CLO integrity checks intact. Hardware writes must remain explicit and use the application's existing transaction and confirmation flows.
- Do not transmit user audio/video or preset data to external services unless the user explicitly selects and authorizes a concrete provider workflow. Prefer local processing.

## Project validation

- Build with `dotnet build Editor\\Gp200.Replica.csproj --no-restore`.
- Prefer focused tests or runtime smoke checks for behavior not covered by a test project; clean up temporary test artifacts afterwards.
- Follow existing WPF, localization, and C# patterns. Document protocol and model limitations rather than guessing.

# Elite Venue Manager (EVM)

A Dalamud plugin for running an FFXIV venue.

Open with `/venue` or `/evm`.

**Setup tab:** go inside your venue and press **Register this house**. Guest tracking, welcome tells and timed shouts only run while you're inside a registered house. Also here: your venue name, seconds between messages, and which chat channels `!order`, `!menu` and `!song` are read from.

- **Guests:** everyone inside right now (visit count, NEW for first-timers, how long they've been here) plus tonight's arrivals/departures log.
- **Welcome tells:** an automatic /tell when someone walks in. Separate first-visit and returning messages, a cooldown, and a "never message" list for staff.
- **Shouts:** game/venue ads shouted in rotation every N minutes (minimum 5).
- **Bar:** guests type `!order <drink>` (fuzzy-matched to your menu) or `!menu`. Orders queue for the bartender; **Served** sends your serve line. Mark drinks "86" when you run out.
- **Songs:** guests type `!song <name>`. Mark requests **Added to queue**, **Played** or **Skip**; **Copy** puts the song name on your clipboard.
- **Party Finder:** a listing timer that warns you in chat (with a sound) before your listing expires, plus a button to open Party Finder.

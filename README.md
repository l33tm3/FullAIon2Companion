<div align="center">

<img src="public/assets/aion2-logo.png" alt="AION 2" width="120">

# FullAIon2Companion · Daeva Companion

**A free overlay and companion app for AION 2: DPS meter, boss timers, soul hunting, minimap, market prices, rankings and much more, on top of your game.**

[![Download](https://img.shields.io/github/v/release/l33tm3/FullAIon2Companion?label=Download&logo=windows&color=2ea44f)](https://github.com/l33tm3/FullAIon2Companion/releases/latest)
[![VirusTotal](https://img.shields.io/badge/VirusTotal-VT__RESULT-VT__COLOR?logo=virustotal&logoColor=white)](https://www.virustotal.com/gui/file/18b23018c8e87a7b5669d28c52a974c16a9e97b96f3657ce869d3015d7e7ad17)
[![Donate with PayPal](https://img.shields.io/badge/Donate-PayPal-e36cb8?logo=paypal&logoColor=white)](https://paypal.me/martincito45)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Windows 10/11 64-bit](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078d6?logo=windows)

[**Download**](#download) · [**Features**](#features) · [**Donate**](#support-the-project) · [**Build from source**](#build-from-source) · [Website](https://governanceai.me)

<img src="docs/images/overlay-pulido.webp" alt="The overlay during a boss fight: boss card, DPS, group ranking, loot table and a hidden cube" width="900">

</div>

FullAIon2Companion reads the AION 2 traffic that already reaches your PC and shows it in an overlay on top of the game and in a panel with everything else. **It never touches the game: no memory reading, no injection, no modified files, no packets sent.** It only listens, like a network monitor.

**Everything is free, for everyone.** There is no subscription and no paywall: the PvP card, the minimap and every other feature work out of the box. If the app helps you, you can [support it with a donation](#support-the-project).

---

## Download

| | |
|---|---|
| **Latest version** | [**FullAIon2Companion 1.0.20**](https://github.com/l33tm3/FullAIon2Companion/releases/latest) (Windows 10/11, 64-bit, ~99 MB) |
| **SHA-256** | `18b23018c8e87a7b5669d28c52a974c16a9e97b96f3657ce869d3015d7e7ad17` |
| **VirusTotal** | [![VirusTotal](https://img.shields.io/badge/VirusTotal-VT__RESULT-VT__COLOR?logo=virustotal&logoColor=white)](https://www.virustotal.com/gui/file/18b23018c8e87a7b5669d28c52a974c16a9e97b96f3657ce869d3015d7e7ad17) — the report for this exact installer |
| **Also on** | [governanceai.me](https://governanceai.me/#home?download=1) |

**Requirements**

1. [Npcap](https://npcap.com/#download) — the packet capture driver (the same one Wireshark uses).
2. [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — already installed on most Windows 10/11 PCs.
3. Run the installer, then open AION 2 and the app. The overlay appears over the game; the panel opens from its gear icon.

> The installer is not signed with a publisher certificate yet, so Windows SmartScreen may warn you. Check the SHA-256 above and the VirusTotal report before running it. The app updates itself from inside: *News → Update now*.

---

## Features

### The overlay, always on top of your game
Your DPS, damage and healing, your group's ranking with each player's class, the target with its health and, beside it, the loot table of what you are hunting. Your Abyss rank and the next events sit in its header.

### Know whether you will beat the enrage
<img src="docs/images/boss-card.webp" alt="Boss card" width="420" align="right">

The boss card works out how long the kill takes at your current pace, when the boss enrages and how much health it will have left by then. A race bar tells you at a glance whether you make it. It has its own window under the game's boss bar, or lives inside the overlay.

<br clear="right">

### Boss alerts, with sound
When a boss starts a dangerous skill, a big warning appears in the middle of the screen and a strip in the overlay shows when it lands. And it speaks, like a raid add-on: a voice saying *"Watch out!"* in Spanish or English, or an alarm. You choose which enemies warn you: dungeon bosses, field bosses, elites or just your target.

<img src="docs/images/boss-alert.webp" alt="A field boss casting a skill: warning in the middle of the screen and in the overlay" width="900">

### Your buffs and cooldowns, in sight
<img src="docs/images/assistant.webp" alt="Combat assistant with a buff's card" width="420" align="right">

A small bar above the game's skill bar with your buffs and how long they last, and your skills cooling down underneath. Rest the mouse on an icon to see what it does at **your** skill level, like the game's own tooltip, without taking a single click away from the game.

<br clear="right">

### See who you are up against
<img src="docs/images/pvp-card.webp" alt="PvP card of another player" width="360" align="right">

Target another player and the overlay brings their official NCSOFT profile: class, level, legion, combat power and all their gear with enchantments, compared with yours.

<br clear="right">

### Every hit, broken down
The full fight in the panel: DPS and total damage, a per-skill breakdown with hits and crits, and the history of your fights to compare.

<img src="docs/images/panel-combat.webp" alt="DPS meter in the panel" width="900">

### 36 styles for your overlay
From the classic look to Cyberpunk, Gamer Girl, Synthwave or Terminal: every style changes the font, the colours, the pattern and the glow, and every overlay window follows it.

<img src="docs/images/estilos.webp" alt="Twelve of the 36 overlay styles" width="900">

<details>
<summary>More styles in action</summary>

<img src="docs/images/overlay-cyberpunk.webp" alt="Cyberpunk style" width="900">
<img src="docs/images/overlay-aion.webp" alt="Golden Aion style" width="900">
<img src="docs/images/overlay-momento.webp" alt="By the moment style, out of a fight" width="420">
<img src="docs/images/panel-styles.webp" alt="Styles section of the settings" width="900">

</details>

### A minimap over the game
<img src="docs/images/minimap.webp" alt="Minimap of Altgard" width="420" align="right">

The atlas of Atreia around your position while you play: creatures and bosses, hidden cubes, ores, plants and Od, your troop, and your own pins and routes. Move it and resize it wherever you like (Ctrl+Alt+N).

<br clear="right">

### The map of Atreia, inside the app
Portraits of every creature, search across every layer, *You are here* while you play, your pins and routes, and the upcoming events beside the map.

<img src="docs/images/panel-atlas.webp" alt="Map of Atreia in the panel" width="900">

### Soul hunting, creature by creature
Target a creature and the overlay tells you which pet its soul belongs to, how many souls you have, how many you still need for the next level and how many kills that takes at today's pace. Beside it, everything it drops, with the chance of each item.

<img src="docs/images/hunting.webp" alt="Soul hunting: pet card in the overlay and the creature's loot" width="900">

### Your pet collection, soul by soul
The progress of every pet with the souls it still needs and which creatures drop them, one click away from the map. Pin the ones you are hunting and they show under the overlay.

<img src="docs/images/panel-pets.webp" alt="Pets tab" width="900">

### Your server's Market, at hand
Prices of every item per server, with rises and drops, and your inventory valued. Whatever you loot shows what it is worth. *Market data: [aion2.exchange](https://aion2.exchange), used with permission.*

<img src="docs/images/panel-market.webp" alt="Market tab" width="900">

### Your place in the game's rankings
Abyss, bosses, expedition, awakening and arenas: your rank in your class and overall, how much you went up or down, and the hourly history.

<img src="docs/images/panel-ranking.webp" alt="Rankings tab" width="900">

### Get to every field boss in time
Field bosses of every map with their next spawn, alerts with sound and your favourites in the overlay's header.

<img src="docs/images/panel-fieldbosses.webp" alt="Field bosses tab" width="900">

### Never miss a hidden cube
When the server reveals a hidden cube nearby, the overlay gives you its direction and distance. The panel keeps every one, with its map and whether it was still there.

<img src="docs/images/panel-cubes.webp" alt="Hidden cubes tab" width="900">

### Your farming session, in numbers
Kills, souls and kinah per hour, the loot each creature gives you (learned while you play) and goals with a countdown.

<img src="docs/images/panel-farm.webp" alt="Hunting tab" width="900">

### And also

| Goals | Resources and Od | Events |
|---|---|---|
| <img src="docs/images/panel-goals.webp" alt="Goals" width="280"> | <img src="docs/images/panel-resources.webp" alt="Resources" width="280"> | <img src="docs/images/panel-events.webp" alt="Events" width="280"> |
| Kills, kinah or loot: set the goal and the app tells you whether you are on time. | Your in-game counters and the Od energy for dungeons. | Upcoming events for your region, with alerts and sound. |

The app speaks **English, Spanish and Portuguese** (*Settings → General*). Item and creature names come from the game's Spanish data.

---

## Support the project

FullAIon2Companion and the Daeva Companion website are free, and they will stay free. If they help you and you want to help keep them going, you can make a donation. It is voluntary and changes nothing in the app.

<a href="https://paypal.me/martincito45"><img src="https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif" alt="Donate with PayPal"></a>

[**paypal.me/martincito45**](https://paypal.me/martincito45) · Thank you!

---

## How it works

- **Passive capture.** [SharpPcap](https://github.com/dotpcap/sharpcap) and Npcap read the TCP traffic between AION 2 and its server (also through gaming VPNs on the loopback adapter). Frames are decoded with the protocol work of [Aion-DPS-Meter](https://github.com/SkeeveAN/Aion-DPS-Meter) by SkeeveTV (MIT, vendored in `desktop/vendor`).
- **Nothing is sent to the game** and its memory is never read. Encrypted traffic is not decrypted. Some things are therefore impossible by design (for example, a real-time GPS position while walking, or Market prices from the game itself).
- **Your data stays on your PC** (`%LOCALAPPDATA%\FullAIon2Companion`). Linking the app to a Daeva Companion account is optional and only syncs your progress (pets, plans) with the website.
- Public character profiles come from NCSOFT's official AION 2 website.

> Use it at your own risk. This is a fan project, not affiliated with or endorsed by NCSOFT.

---

## Build from source

### Repository layout

| Path | What it is |
|---|---|
| `desktop/FullAIon2Companion` | The desktop app: C# / WPF on .NET 10, the panel is HTML/JS in WebView2 (`ui/`), translations in `i18n/` |
| `desktop/vendor` | Aion-DPS-Meter (MIT): AION 2 protocol decoding, capture and skill data |
| `desktop/installer` | Inno Setup script of the installer |
| `src`, `public` | The [Daeva Companion](https://governanceai.me) website (Astro + TypeScript) and its game database (`public/data`, `public/assets`) |
| `public/api` | The website's PHP API (accounts, app pairing, releases) |
| `scripts` | Data import, image and build tools |
| `tests` | Website tests |

### Desktop app

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download), [Npcap](https://npcap.com) to run it, and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

```powershell
# Build and run
dotnet build desktop/FullAIon2Companion -c Release -o desktop/build
desktop/build/FullAIon2Companion.exe

# Installer (self-contained, written to desktop/release/<version>)
powershell -ExecutionPolicy Bypass -File scripts/build-companion.ps1 -Version 1.0.20 -Output desktop/release/1.0.20
```

The app has offline self-tests, for example `FullAIon2Companion.exe --license-test out.json`, `--verify-test out.json`, `--overlay-styles <folder> [en]` (draws the overlay in all 36 styles) or `--skill-card-test <folder>`.

### Website

Requirements: Node.js 20+.

```bash
npm install
npm run build   # static site in dist/
npm test
```

The PHP API (`public/api`) needs PHP 8 and MySQL. Its private settings (`account-config.json` with the database, mail and payment configuration) live outside the web root, in the folder named by `AION_PRIVATE_DIR` (by default `../aion2-private` next to the site), and are not part of this repository.

Some import scripts in `scripts/` read research sources that are not included here.

---

## Credits

- Game data, names and images: © NCSOFT. Used for reference in a free fan tool.
- [Aion-DPS-Meter](https://github.com/SkeeveAN/Aion-DPS-Meter) by SkeeveTV — protocol decoding, capture and skill data (MIT).
- Cartography of the Map of Atreia: [InteractiveMap.app](https://interactivemap.app/aion2/maps/es/altgard).
- Creature portraits: [aion2.th.gl](https://aion2.th.gl), used with its authors' permission.
- Market prices: [aion2.exchange](https://aion2.exchange), used with its authors' permission.
- [Leaflet](https://leafletjs.com), [Lucide](https://lucide.dev), [SharpPcap / PacketDotNet](https://github.com/dotpcap), [Microsoft WebView2](https://developer.microsoft.com/microsoft-edge/webview2/).

See [`desktop/THIRD-PARTY-NOTICES.txt`](desktop/THIRD-PARTY-NOTICES.txt) for the full notices.

## License

The original code of FullAIon2Companion and Daeva Companion is released under the [MIT License](LICENSE). Third-party code, data and images keep their own licenses and owners (see Credits). AION and AION 2 are trademarks of NCSOFT Corporation.

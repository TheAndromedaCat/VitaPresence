[![PS3 Rich Presence](https://i.imgur.com/koKT4by.png)](https://github.com/TheAndromedaCat/PS3-Rich-Presence-for-Discord)
# VitaPresence

Change your Discord Rich Presence to display your currently playing PS Vita game with automatic high-resolution cover art!

<img width="3264" height="1824" alt="VitaPresence" src="https://github.com/user-attachments/assets/cfc15434-8487-48cc-b995-13f9c44db5f1" />


Works with PS Vita games, LiveArea home screen, homebrew applications, and Adrenaline (including custom bubbles).

---

## ✨ Features

- **Automatic Multi-Tier Cover Resolution**:
  - 🥇 **Tier 1 (SteamGridDB)**: Title-based search prioritizing 1:1 square grid artwork (`1024x1024`, `512x512`) when a SteamGridDB API key is provided. Automatic title sanitization removes Vita edition suffixes (`: Playstation®Vita Edition`, `®`, `™`) for accurate matches.
  - 🥈 **Tier 2 (GameTDB)**: Direct system and region-mapped cover art database resolution (`https://art.gametdb.com/psv/cover/{REGION}/{TITLE_ID}.jpg`) with region fallbacks (`US`, `EN`, `JA`, `KO`, `ZH`).
  - 🥉 **Tier 3 (Discord Developer App Asset Fallback)**: Queries Discord Developer Portal application assets via CDN (`https://cdn.discordapp.com/app-assets/{client_id}/{asset_id}.png`), defaulting to App ID `1354524447746293901` and fallback asset key `"vita"`.
- **Thread-Safe In-Memory Caching**: Prevents redundant HTTP requests for quick, responsive presence updates.
- **GUI & CLI Apps**: Both Windows Forms GUI (`VitaPresence-GUI`) and Command Line Interface (`VitaPresence-CLI`) included.
- **Real-Time Source Feedback**: Displays cover art resolution source directly in the GUI status label.
- **IP to MAC Conversion**: Option to convert IP address to MAC address for seamless automatic reconnects.

---

## 🛠️ Setup & Installation

### 1. PS Vita Plugin
1. Copy `VitaPresence.skprx` to `ur0:tai/` or `ux0:tai/`.
2. Add the plugin under the `*KERNEL` section of your `config.txt`:
   ```ini
   *KERNEL
   ur0:tai/VitaPresence.skprx
   ```
3. Reboot your PS Vita or reload `config.txt` via taiHEN menu.

### 2. PC Application (GUI or CLI)
1. Run `VitaPresence-GUI.exe` or `VitaPresence-CLI.exe` on your Windows PC (must be on the same Wi-Fi network as your Vita).
2. Enter your Vita's **IP** or **MAC Address**.
3. (Optional) Enter your custom Discord **Client ID** (defaults to `1354524447746293901`).
4. (Optional) Enter your **SteamGridDB API Key** for automatic 1:1 square cover art grids.
5. Click **Connect**.

---

## 🏗️ Building from Source

- **PC Apps (GUI & CLI)**: Open `pc/VitaPresence.sln` in Visual Studio 2022 and build, or compile via MSBuild:
  ```cmd
  MSBuild pc/VitaPresence.sln
  ```
- **Vita Plugin**: Built using [VitaSDK](https://vitasdk.org/):
  ```bash
  cd plugin
  mkdir build && cd build
  cmake ..
  make
  ```

---

## 📜 Credits & Acknowledgments

- **[Electry](https://github.com/Electry/VitaPresence)** — Original creator of the VitaPresence project.
- **[OffeedTaiyoz](https://www.reddit.com/r/VitaPiracy/comments/1joe4hl/unofficial_vitapresence_update/)** — Second developer in line who laid down the foundation for the update.
- **[Sun-Research-University](https://github.com/Sun-Research-University)** — Idea and desktop application inspiration.

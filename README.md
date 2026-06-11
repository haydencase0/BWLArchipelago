# Before We Leave Archipelago Mod

A [BepInEx](https://github.com/BepInEx/BepInEx) mod that integrates Before We Leave with the [Archipelago](https://archipelago.gg) multiworld randomizer. Research technologies to send checks to the Archipelago server, and receive randomized technology unlocks from other players in your multiworld session.

---

## Requirements

- Before We Leave (Steam)
- [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) for Unity (x64)
- [Archipelago](https://archipelago.gg/downloads) 0.6.7 or later
- The Before We Leave APworld (included in this repository)

---

## Installation

### Step 1: Install BepInEx

1. Download [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) — make sure to get the **x64 Unity** version (`BepInEx_win_x64_5.4.x.zip`).
2. Extract the contents of the zip into your Before We Leave game folder. The game folder is typically:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\Before We Leave
   ```
3. Your game folder should now contain a `BepInEx` folder alongside `Before We Leave.exe`.
4. Launch the game once to let BepInEx initialize, then close it. A `BepInEx/plugins` folder will be created.

### Step 2: Install the Mod

1. Download `BWLArchipelago.dll` from the [releases page](https://github.com/YourName/BWLArchipelago/releases).
2. Copy `BWLArchipelago.dll` into your `BepInEx/plugins` folder:
   ```
   Before We Leave/BepInEx/plugins/BWLArchipelago.dll
   ```
3. Launch the game. A connection screen will appear on the main menu.

### Step 3: Install the APworld

1. Download `before_we_leave.apworld` from the [releases page](https://github.com/YourName/BWLArchipelago/releases).
2. Copy it into your Archipelago custom worlds folder:
   ```
   C:\ProgramData\Archipelago\custom_worlds\before_we_leave.apworld
   ```
   If the `custom_worlds` folder does not exist, create it.

---

## Setting Up a Multiworld

### Step 1: Create Your YAML

1. Download `before we leave.yaml` from this repository as a template.
2. Edit the following fields:
   - `name` — your player name as it will appear in the multiworld
   - `win_condition` — set your preferred win condition (see options below)
   - `planets_to_colonize` — only relevant for `colonize_planets` and `complete_game` win conditions
3. Submit your YAML to the Archipelago website or share it with your multiworld host.

### Win Condition Options

| Option | Value | Description |
|--------|-------|-------------|
| Launch Rocket | `launch_rocket: 50` | Launch your first rocket. Short game. |
| Colonize Planets | `colonize_planets: 50` | Colonize a set number of planets. Medium game. |
| Complete Game | `complete_game: 50` | See the end game scene. Long game. |

### Step 2: Generate the Multiworld

Generate your multiworld using the Archipelago website or local generator. Once generated, start the Archipelago server.

### Step 3: Connect In-Game

1. Launch Before We Leave.
2. The Archipelago Connection screen will appear on the main menu.
3. Enter your connection details:
   - **Server** — the Archipelago server address (e.g. `archipelago.gg` or a custom host)
   - **Port** — the port number provided by your multiworld host
   - **Slot Name** — your player name exactly as entered in your YAML
   - **Password** — leave blank if the server has no password
4. Click **Connect**. The mod will connect to the server and begin syncing.
5. Click **Play Offline** if you want to play without Archipelago for this session.

Your connection details are saved automatically and will pre-populate next time you launch.

---

## Progressive Technology Groups

The following technologies are grouped into progressive chains. You must receive earlier items in the chain before later ones are granted:

| Group | Technologies (in order) |
|-------|------------------------|
| Progressive Housing | House → School → Apartment |
| Progressive Mining | Mining → Metalwork → Glass → Laser |
| Progressive Elevator | Elevator → SpaceElevator |
| Progressive Power | Repair → Power → OilPower → CleanPower |
| Progressive Happiness | Pump → Music → MeetingSquare → RoadDecoration |
| Progressive Food | Gardening → Cooking → Farming → Baking |
| Progressive Upgrades | Tinkering → Automation → Filtering |
| Progressive Rocket | Fuel → Space |
| Progressive Shipping | Shipping → AdvancedShipping → Airships |

---

## Troubleshooting

**The connection screen doesn't appear**
- Make sure BepInEx is installed correctly and the mod DLL is in the `plugins` folder.
- Check `BepInEx/LogOutput.log` for any errors on startup.

**Connection fails**
- Double check your server address, port, and slot name.
- Make sure the Archipelago server is running.
- Check that your slot name exactly matches what was used in your YAML (case sensitive).

**A technology wasn't granted**
- Check `BepInEx/LogOutput.log` for any warnings about unknown technology names.
- Some technologies may have name mismatches between the APworld and the game. Report these as issues on the GitHub page or on Discord.

---

## Building From Source

Building From Source

Prerequisites


Visual Studio 2022 or JetBrains Rider
.NET Framework 4.7.2
Before We Leave installed via Steam
BepInEx 5.4.x installed in the Before We Leave game folder


Steps


1. Clone this repository.
2. Open BWLArchipelago.csproj in your IDE.
3. The project uses NuGet for Archipelago.MultiClient.Net (version 6.7.1) — NuGet should restore this automatically on build.
4. Update the HintPath values in the .csproj file if your Steam library is not at the default location (C:\Program Files(x86)\Steam\steamapps\common\Before We Leave). The following references point to local DLLs:
   - 0Harmony20.dll — from BepInEx\core\
   - Assembly-CSharp.dll — from Before We Leave_Data\Managed\
   - BepInEx.dll — from BepInEx\core\
   - BepInEx.Harmony.dll — from BepInEx\core\
   - UnityEngine.dll — from Before We Leave_Data\Managed\
   - UnityEngine.CoreModule.dll — from Before We Leave_Data\Managed\
   - UnityEngine.IMGUIModule.dll — from Before We Leave_Data\Managed\
   - UnityEngine.UI.dll — from Before We Leave_Data\Managed\
   - Unity.TextMeshPro.dll — from Before We Leave_Data\Managed\
   - MonoMod.RuntimeDetour.dll — from BepInEx\core\
   - MonoMod.Utils.dll — from BepInEx\core\
5. Build in Release configuration. The output DLL will be in bin/Release.

---

## Contributing

Bug reports and pull requests are welcome. Please open an issue on GitHub or message me on Discord.
